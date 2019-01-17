using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ServiceWire.ArrayPoolOwners;

namespace ServiceWire.DuplexPipes
{
    public class DuplexPipe : IDisposable, IDuplexPipe
    {
        private readonly Stream _stream;
		private Pipe _readPipe;
        private Pipe _writePipe;
        private Task _execute;

        private ReadOnlySequence<byte> cachedSequence = ReadOnlySequence<byte>.Empty;

        public DuplexPipe(Stream stream)
        {
            this._stream = stream;
		}

        public PipeReader Input => _readPipe.Reader;

        public PipeWriter Output => _writePipe.Writer;

		public void Start()
        {
            PipeOptions receivePipeOptions = PipeOptions.Default;
            PipeOptions sendPipeOptions = PipeOptions.Default;
            _readPipe = new Pipe(receivePipeOptions);
            _writePipe = new Pipe(sendPipeOptions);

            _execute = ExecuteAsync();
        }

       public ReadOnlyMemory<byte> ReadAsync()
        {
            async Task<ReadResult> Read()
            {
                if (!Input.TryRead(out var readResultInternal))
                {
                    var readResultTask = Input.ReadAsync();
                    readResultInternal = readResultTask.IsCompleted ? readResultTask.Result : await readResultTask;
                }
                return readResultInternal;
            }

            while (true)
            {
                var result = Read().GetAwaiter().GetResult();

                var buffer = result.Buffer;
                SequenceMarshal.TryGetReadOnlyMemory(buffer, out ReadOnlyMemory<byte> memory);
                Input.AdvanceTo(buffer.End);

                return memory;
            }
        }

        public ReadOnlyMemory<byte> ReadAsync(int count)
		{
            async Task<ReadResult> Read()
            {
                ReadResult readResult;

                if (!Input.TryRead(out readResult))
                {
                    var readResultTask = Input.ReadAsync();
                    readResult = readResultTask.IsCompleted ? readResultTask.Result : await readResultTask;
                }
                
                return readResult;
		    }

			while (cachedSequence.Length < count)
            {
                var result = Read().GetAwaiter().GetResult();

                if (result.Buffer.Length >= 0)
                {
                    var builder = new SequenceBuilder<byte>();
                    builder.Append(cachedSequence);
                    builder.Append(result.Buffer);
                    var buffer = builder.Build();
                    cachedSequence = buffer;

                    Input.AdvanceTo(result.Buffer.End);
                }
                else
                {
                    Input.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }
            }
            var position = cachedSequence.GetPosition(count);
            var chank = cachedSequence.Slice(0, position);
            
            cachedSequence = cachedSequence.Slice(position);
            return chank.ToArray();

        }

        public ValueTask AppendToBuffer(ReadOnlyMemory<byte> memory)
        {
            var builder = new SequenceBuilder<byte>();
            builder.Append(cachedSequence);
            builder.Append(new Segment<byte>(memory));
            cachedSequence = builder.Build();

            return default;
        }

		public ValueTask Write(in ReadOnlyMemory<byte> payload)
        {
            return Write(payload.Span);
        }

        public ValueTask Write(in ReadOnlySpan<byte> payload)
        {
            async ValueTask AwaitFlushAndRelease()
            {
                var flushTask = _writePipe.Writer.Flush();
                if (!flushTask.IsCompleted)
                {
                    await flushTask;
                }
            }

            _writePipe.Writer.Write(payload);
            return AwaitFlushAndRelease();
        }

		private async Task ExecuteAsync()
        {
            Exception exception = null;
            try
            {
                var receiveTask = Receive();
                var sendTask = Send();
                if (await Task.WhenAny(receiveTask, sendTask) == sendTask)
                {
                    _stream.Close();
                    await receiveTask;
                }

                exception = await sendTask;

                _stream.Dispose();
            }
            catch (Exception e)
            {
                exception = e;
            }
			finally
            {
                Output.Complete(exception);
            }
        }

        private async Task Receive()
        {
            Exception exception = null;
            var writer = _readPipe.Writer;
            try
            {
                while (true)
                {
                    int read;
                    using (var memoryOwner = ArrayPoolOwner<byte>.Rent(256))
                    {
                        read = await _stream.ReadAsync(memoryOwner.Array, 0, memoryOwner.Array.Length).ConfigureAwait(false);
                        await writer.WriteAsync(new ReadOnlyMemory<byte>(memoryOwner.Array, 0, read)).ConfigureAwait(false);
                    }

                    if (read == 0)
                        break;

                    var flush = await writer.FlushAsync().ConfigureAwait(false);
                    if (flush.IsCanceled || flush.IsCompleted) break;
                }

            }
            catch (Exception e)
            {
                exception = e;
            }

            writer.Complete(exception);
        }

        private async Task<Exception> Send()
        {
            Exception exception = null;
            var reader = _writePipe.Reader;
            try
            {
                while (true)
                {
					var result = await reader.ReadAsync();
                    if (!result.IsCompleted)
                    {
                        await _stream.FlushAsync().ConfigureAwait(false);
                    }
                    if (result.IsCanceled) break;

                    var isCompleted = result.IsCompleted;

					var buffer = result.Buffer;
                    if (!buffer.IsEmpty)
                    {
                        await WriteBuffer(_stream, buffer);
                    }

                    reader.AdvanceTo(buffer.End);
                    if (isCompleted) break;
                }
            }
            catch (Exception e)
            {
                exception = e;
            }

            return exception;
        }

        private static Task WriteBuffer(Stream target, in ReadOnlySequence<byte> data)
        {
            async ValueTask WriteBufferAwaited(Stream ttarget, ReadOnlySequence<byte> ddata)
            {
                foreach (var segment in ddata)
                {
                    await ttarget.WriteAsync(segment);
                }
            }

            var writeTask = data.IsSingleSegment ? target.WriteAsync(data.First) : WriteBufferAwaited(target, data);

            return writeTask.IsCompletedSuccessfully ? Task.CompletedTask : writeTask.AsTask();

        }

		public void Stop()
        {
            Output.Complete();
		}
        public void Dispose()
        {
            Output.Complete();
            _execute.GetAwaiter().GetResult();
        }
    }

    internal static class Extension
	{
		internal static Task PipelinesFireAndForget(this Task task)
        {
            return task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        }

        internal static ValueTask<bool> Flush(this PipeWriter writer)
		{
			bool GetResult(FlushResult flush)
				=> !(flush.IsCanceled || flush.IsCompleted);

			async ValueTask<bool> Awaited(ValueTask<FlushResult> incomplete)
				=> GetResult(await incomplete);

			var flushTask = writer.FlushAsync();

			return flushTask.IsCompletedSuccessfully
				? new ValueTask<bool>(GetResult(flushTask.Result))
				: Awaited(flushTask);
		}

#if NET462 || NETCOREAPP2_0
		public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer)
		{
			using (ICustomMemoryOwner<byte> memoryOwner = buffer.Lease())
			{
				return new ValueTask(stream.WriteAsync(memoryOwner.Array, 0, buffer.Length));
			}
		}
#endif
	}

    public class SequenceBuilder<T>
    {
        private Segment<T> _head;
        private Segment<T> _tail;

        public void Append(Segment<T> segment)
        {
            if (segment.Memory.Length == 0)
                return;

            if (_head == null)
            {
                segment.SetIndex(0);
                _head = _tail = segment;
            }
            else
            {

                Segment<T> newTail = segment;
                Segment<T> oldTail = _tail;
                oldTail.SetNext(newTail);
                newTail.SetIndex(oldTail.RunningIndex + oldTail.Memory.Length);
                _tail = newTail;
            }
        }

        public void Append(in ReadOnlySequence<T> sequence)
        {
            foreach (var readOnlyMemory in sequence)
            {
                Append(new Segment<T>(readOnlyMemory));
            }
        }

        public ReadOnlySequence<T> Build()
        {
            Segment<T> head = _head;
            Segment<T> tail = _tail;
            _head = _tail = null;

            if (head == null)
                return ReadOnlySequence<T>.Empty;
            return new ReadOnlySequence<T>(head, 0, tail, tail.Memory.Length);
        }
    }

    public sealed class Segment<T> : ReadOnlySequenceSegment<T>
    {
        public Segment(in ReadOnlyMemory<T> memory)
        {
            Memory = memory;
        }

        public void SetIndex(long value) => RunningIndex = value;
        public void SetNext(ReadOnlySequenceSegment<T> value) => Next = value;
    }
}