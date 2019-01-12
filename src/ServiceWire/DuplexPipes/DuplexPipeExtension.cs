using System;
using System.Buffers.Text;
using System.Text;
using System.Threading.Tasks;
using ServiceWire.ArrayPoolOwners;

namespace ServiceWire.DuplexPipes
{
	public static class DuplexPipeExtension
	{
		private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

		public static ValueTask WriteInt(this DuplexPipe duplexPipe, int value)
		{
			byte[] bytes = BitConverter.GetBytes(value);
			return duplexPipe.WriteAsync(bytes);
		}

		public static ValueTask WriteString(this DuplexPipe duplexPipe, string value)
		{
			int length = Utf8.GetByteCount(value);

			uint num;
			for (num = (uint)length; num >= 128U; num >>= 7)
				duplexPipe.WriteAsync(new Memory<byte>(new[] { (byte)(num | 128U) }));
			duplexPipe.WriteAsync(new Memory<byte>(new []{(byte)num}));

			using (var memoryOwner = ArrayPoolOwner<byte>.Rent(length))
			{
#if NET462 || NETCOREAPP2_0
				Utf8.GetBytes(value, 0, value.Length, memoryOwner.Array, 0);
#else
				Utf8.GetBytes(value, memoryOwner.Array);
#endif
				return duplexPipe.WriteAsync(memoryOwner.Memory);
			}
		}

		public static int ReadIntAsync(this DuplexPipe duplexPipe)
		{
			const int length = 4;
			ReadOnlyMemory<byte> memory = duplexPipe.ReadBytesAsync(length);

			Utf8Parser.TryParse(memory.Span, out int value, out int bytesConsumed);//todo
#if NET462 || NETCOREAPP2_0
			int i = BitConverter.ToInt32(memory.ToArray(), 0);
#else
			int i = BitConverter.ToInt32(memory.Span);
#endif
			return i;
		}

		public static bool ReadBooleanAsync(this DuplexPipe duplexPipe)
		{
			ReadOnlyMemory<byte> memory = duplexPipe.ReadBytesAsync(1);
			//Utf8Parser.TryParse(memory.Span, out bool value, out int bytesConsumed);//todo

			bool value = memory.Span[0] > (byte)0;
			return value;
		}

		public static byte[] ReadAsync(this DuplexPipe duplexPipe, int count)
		{
			ReadOnlyMemory<byte> memory = duplexPipe.ReadBytesAsync(count);
			byte[] destination = memory.ToArray();
			//Buffer.BlockCopy(memoryOwner.Array, 0, destination, 0, count);
			//memoryOwner.Dispose();
			return destination;
		}
		//TODO Create dictionary with functions for write and read value like Read<bool>
	}
}