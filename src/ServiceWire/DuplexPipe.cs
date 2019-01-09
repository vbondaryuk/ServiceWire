using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceWire
{
	public class DuplexPipe
	{
		private Stream _stream;
		private Pipe _readPipe;
		private Pipe _writePipe;

		public PipeReader Input => _readPipe.Reader;
		public PipeWriter Output => _writePipe.Writer;

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

		public ValueTask WriteAsync(ReadOnlyMemory<byte> payload)
		{
			async ValueTask AwaitFlushAndRelease(ValueTask<FlushResult> flush)
			{
				await flush;
			}

			var write = Output.WriteAsync(payload);
			if (write.IsCompletedSuccessfully) return default;
			return AwaitFlushAndRelease(write);
		}

		private async Task CopyFromStreamToPipe()
		{
			Exception exception = null;
			var writer = _readPipe.Writer;
			try
			{
				while (true)
				{
#if NET462 || NETCOREAPP2_0
					const int BufferSize = 4096;
					var buffer = new byte[BufferSize];
					int read = await _stream.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false);
#else
					var memory = writer.GetMemory(1);
					int read = await _stream.ReadAsync(memory).ConfigureAwait(false);
#endif
					if (read <= 0) break;
					writer.Advance(read);
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

		private async Task CopyFromWritePipeToStream()
		{
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
						if (buffer.IsEmpty) continue;
						await WriteBuffer(_stream, buffer, this.GetType().Name);
						reader.AdvanceTo(buffer.End);
					} while (!(buffer.IsEmpty && result.IsCompleted) && reader.TryRead(out result));

					if (result.IsCanceled) break;
					if (buffer.IsEmpty && result.IsCompleted) break;
				}
			}
			catch (Exception e)
			{
				reader.Complete(e);
			}
		}

		private static Task WriteBuffer(Stream target, in ReadOnlySequence<byte> data, string name)
		{
			async Task WriteBufferAwaited(Stream ttarget, ReadOnlySequence<byte> ddata, string nname)
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

			return WriteBufferAwaited(target, data, name);
		}
	}

	public static class DuplexPipeExtension
	{
		internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);

		public static ValueTask WriteInt(this DuplexPipe duplexPipe, int value)
		{
			byte[] bytes = BitConverter.GetBytes(value);
			return duplexPipe.WriteAsync(bytes);
		}

		public static ValueTask WriteString(this DuplexPipe duplexPipe, string value)
		{
			int length = Utf8.GetMaxByteCount(value.Length);

			uint num;
			for (num = (uint)length; num >= 128U; num >>= 7)
				duplexPipe.WriteAsync(new Memory<byte>(new[] { (byte)(num | 128U) }));
			duplexPipe.WriteAsync(new Memory<byte>(new []{(byte)num}));

			using (var memoryOwner = ArrayPoolOwner<byte>.Rent(length))
			{
#if NET462 || NETCOREAPP2_0
				Utf8.GetBytes(value, 0, value.Length - 1, memoryOwner.Array, 0);
#else
				Utf8.GetBytes(value, memoryOwner.Array);
#endif
				return duplexPipe.WriteAsync(memoryOwner.Memory);
			}
		}

		public static async ValueTask<bool> ReadBooleanAsync(this DuplexPipe duplexPipe)
		{
			var reader = duplexPipe.Input;
			if (!reader.TryRead(out var readResult))
				readResult = await reader.ReadAsync().ConfigureAwait(false);
			var buffer = readResult.Buffer;
			ReadOnlyMemory<byte> memory = buffer.First.Slice(0,1);
			reader.AdvanceTo(buffer.GetPosition(1));

			//Utf8Parser.TryParse(memory.Span, out bool value, out int bytesConsumed);//todo

			return memory.Span[0] > (byte)0;
		}
		//TODO Create dictionary with functions for write and read value like Read<bool>
		public static async ValueTask<ReadOnlyMemory<byte>> ReadAsync(this DuplexPipe duplexPipe, int count)
		{
			var reader = duplexPipe.Input;
			if (!reader.TryRead(out var readResult))
				readResult = await reader.ReadAsync().ConfigureAwait(false);
			var buffer = readResult.Buffer;

			ReadOnlySequence<byte> sequence = buffer.Slice(0, count);
			ReadOnlyMemory<byte> memory = ReadOnlyMemory<byte>.Empty;
			memory = sequence.IsSingleSegment ? sequence.First.Slice(0) : sequence.ToArray();
			reader.AdvanceTo(sequence.End);

			return memory;
		}
	}

#if NET462 || NETCOREAPP2_0
	internal static class Extension
	{ 
		public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer)
		{
//			if (MemoryMarshal.TryGetArray<byte>(buffer, out ArraySegment<byte> segment))
//				return new ValueTask(stream.WriteAsync(segment.Array, segment.Offset, segment.Count));

			using (ICustomMemoryOwner<byte> memoryOwner = buffer.Lease())
			{
				return new ValueTask(stream.WriteAsync(memoryOwner.Array, 0, buffer.Length));
			}
		}
	}

#endif
	static class Ext
	{
		internal static void PipelinesFireAndForget(this Task task)
			=> task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

		internal static ValueTask<bool> Flush(this PipeWriter writer)
		{
			bool GetResult(FlushResult flush)
				// tell the calling code whether any more messages
				// should be written
				=> !(flush.IsCanceled || flush.IsCompleted);

			async ValueTask<bool> Awaited(ValueTask<FlushResult> incomplete)
				=> GetResult(await incomplete);

			// apply back-pressure etc
			var flushTask = writer.FlushAsync();

			return flushTask.IsCompletedSuccessfully
				? new ValueTask<bool>(GetResult(flushTask.Result))
				: Awaited(flushTask);
		}

		public static ICustomMemoryOwner<T> Lease<T>(
			this ReadOnlySequence<T> source)
		{
			if (source.IsEmpty) return SimpleMemoryOwner<T>.Empty;

			int len = checked((int)source.Length);
			var arr = ArrayPool<T>.Shared.Rent(len);
			source.CopyTo(arr);
			return new ArrayPoolOwner<T>(arr, len);
		}

		public static ICustomMemoryOwner<T> Lease<T>(
			this ReadOnlyMemory<T> source)
		{
			if (source.IsEmpty) return SimpleMemoryOwner<T>.Empty;

			T[] array = ArrayPool<T>.Shared.Rent(source.Length);
			source.Span.CopyTo(array);
			return new ArrayPoolOwner<T>(array, source.Length);

		}

		public static ICustomMemoryOwner<T> Lease<T>(this T[] source, int length = -1)
		{
			if (source == null) return null;
			if (length < 0) length = source.Length;
			else if (length > source.Length) throw new ArgumentOutOfRangeException(nameof(length));
			return length == 0 ? SimpleMemoryOwner<T>.Empty : new ArrayPoolOwner<T>(source, length);
		}
	}


	internal sealed class ArrayPoolOwner<T> : ICustomMemoryOwner<T>
	{
		private readonly int _length;
		private T[] _oversized;

		internal ArrayPoolOwner(T[] oversized, int length)
		{
			_length = length;
			_oversized = oversized;
		}

		public static ICustomMemoryOwner<T> Rent(int length)
		{
			T[] bytes = ArrayPool<T>.Shared.Rent(length);
			return new ArrayPoolOwner<T>(bytes, length);
		}
		public Memory<T> Memory => new Memory<T>(GetArray(), 0, _length);

		public T[] Array => GetArray();

		public T[] GetArray() =>
			Interlocked.CompareExchange(ref _oversized, null, null)
			?? throw new ObjectDisposedException(ToString());

		public void Dispose()
		{
			var arr = Interlocked.Exchange(ref _oversized, null);
			if (arr != null) ArrayPool<T>.Shared.Return(arr);
		}
	}

	internal sealed class SimpleMemoryOwner<T> : ICustomMemoryOwner<T>
	{
		public static ICustomMemoryOwner<T> Empty { get; } = new SimpleMemoryOwner<T>(System.Array.Empty<T>());

		public SimpleMemoryOwner(Memory<T> memory) => Memory = memory;

		public Memory<T> Memory { get; }

		public T[] Array => Memory.ToArray();

		public void Dispose() { }
	}

	internal interface ICustomMemoryOwner<T> : IMemoryOwner<T>
	{
		T[] Array { get; }
	}
}