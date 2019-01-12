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
	public class DuplexPipe : IDisposable
	{
		private readonly Stream _stream;
		private readonly Pipe _readPipe;
		private readonly Pipe _writePipe;

		private volatile bool _receiveAborted;
		private volatile bool _isSendComplite;

		private ReadOnlySequence<byte> cachedData = ReadOnlySequence<byte>.Empty;

		public DuplexPipe(NetworkStream stream)
		{
			_stream = stream;
			PipeOptions receivePipeOptions = PipeOptions.Default;
			_readPipe = new Pipe(receivePipeOptions);
			receivePipeOptions.ReaderScheduler.Schedule(
				obj => ((DuplexPipe)obj).CopyFromStreamToPipe().PipelinesFireAndForget(), this);

			PipeOptions sendPipeOptions = PipeOptions.Default;
			_writePipe = new Pipe(sendPipeOptions);
			sendPipeOptions.WriterScheduler.Schedule(
				obj => ((DuplexPipe)obj).CopyFromWritePipeToStream().PipelinesFireAndForget(), this);
		}

		public PipeReader Input => _readPipe.Reader;

		public PipeWriter Output => _writePipe.Writer;

		public ReadOnlyMemory<byte> ReadBytesAsync(int count)
		{
			async ValueTask<ReadResult> Read()
			{
				if (!Input.TryRead(out var readResultInternal))
				{
					var readResultTask = Input.ReadAsync();
					readResultInternal = readResultTask.IsCompleted ? readResultTask.Result : await readResultTask;
				}
				return readResultInternal;
			}

			if (cachedData.Length < count)
			{
				ReadResult readResult = Read().Result;
				cachedData = readResult.Buffer;
				Input.AdvanceTo(cachedData.End);
			}

			ReadOnlyMemory<byte> memory;
			if (cachedData.First.Length >= count)
			{
				memory = cachedData.First.Slice(0, count);
			}
			else
			{
				ReadOnlySequence<byte> sequence = cachedData.Slice(0, count);
				SequenceMarshal.TryGetReadOnlyMemory(sequence, out memory);
			}

			cachedData = cachedData.Slice(count);
			return memory;
		}

		public void Dispose()
		{
			const int connectTimeoutMs = 100;
			_receiveAborted = true;
			_writePipe.Writer.Complete();
			while (!_isSendComplite)
			{
				if (!SpinWait.SpinUntil(() => _isSendComplite, connectTimeoutMs))
				{
					throw new TimeoutException("Unable to connect within " + connectTimeoutMs + "ms");
				}
			}
			_stream.Dispose();
		}

		public ValueTask WriteAsync(ReadOnlyMemory<byte> payload)
		{
			async ValueTask AwaitFlushAndRelease(ValueTask<FlushResult> flush)
			{
				await flush;
			}

			var write = _writePipe.Writer.WriteAsync(payload);
			if (write.IsCompletedSuccessfully) return default;
			return AwaitFlushAndRelease(write);
		}

		private async Task CopyFromStreamToPipe()
		{
			Exception ex = null;
			var writer = _readPipe.Writer;
			try
			{
				while (true)
				{

#if NET462 || NETCOREAPP2_0
					const int bufferSize = 4096;
					var buffer = new byte[bufferSize];
					int read = await _stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
					await writer.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, read)).ConfigureAwait(false);
#else
					var memory = writer.GetMemory(1);
					int read = await _stream.ReadAsync(memory).ConfigureAwait(false);
					writer.Advance(read);
#endif

					if (read <= 0)
						break;
					var flush = await writer.FlushAsync().ConfigureAwait(false);
					if (flush.IsCanceled || flush.IsCompleted) break;
				}
			}
			catch (Exception e)
			{
				if (!_receiveAborted)
					ex = e;
			}
			writer.Complete(ex);
		}

		private async Task CopyFromWritePipeToStream()
		{
			Exception error = null;
			var reader = _writePipe.Reader;
			try
			{
				while (true)
				{
					var pending = reader.ReadAsync();
					if (!pending.IsCompleted)
					{
						await _stream.FlushAsync().ConfigureAwait(false);
					}

					var result = await pending;
					ReadOnlySequence<byte> buffer;
					do
					{
						buffer = result.Buffer;
						if (!buffer.IsEmpty)
						{
							await WriteBuffer(_stream, buffer).ConfigureAwait(false);
						}

						reader.AdvanceTo(buffer.End);
					} while (!(buffer.IsEmpty && result.IsCompleted)
					         && reader.TryRead(out result));

					if (result.IsCanceled) break;
					if (buffer.IsEmpty && result.IsCompleted)
						break;
				}
			}
			catch (Exception ex)
			{
				if (!_receiveAborted)
					error = ex;
			}

			_isSendComplite = true;
			reader.Complete(error);
		}

		private static Task WriteBuffer(Stream target, in ReadOnlySequence<byte> data)
		{
			async Task WriteBufferAwaited(Stream ttarget, ReadOnlySequence<byte> ddata)
			{
				foreach (var segment in ddata)
				{
					await ttarget.WriteAsync(segment);
				}
			}

			if (data.IsSingleSegment)
			{
				var vt = target.WriteAsync(data.First);
				return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
			}

			return WriteBufferAwaited(target, data);
		}
	}

#if NET462 || NETCOREAPP2_0
	internal static class Extension
	{
		public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer)
		{
			using (ICustomMemoryOwner<byte> memoryOwner = buffer.Lease())
			{
				return new ValueTask(stream.WriteAsync(memoryOwner.Array, 0, buffer.Length));
			}
		}
	}
#endif


	internal static class Ext
	{
		internal static void PipelinesFireAndForget(this Task task)
			=> task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

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
	}
}