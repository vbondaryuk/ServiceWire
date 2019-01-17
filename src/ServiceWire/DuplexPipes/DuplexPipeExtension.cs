using System;
using System.Buffers.Text;
using System.Text;
using ServiceWire.ArrayPoolOwners;

namespace ServiceWire.DuplexPipes
{
	public static class DuplexPipeExtension
	{
		private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

		public static void Write(this IDuplexPipe duplexPipe, int value)
        {
			byte[] bytes = BitConverter.GetBytes(value);
			duplexPipe.Write((ReadOnlySpan<byte>) bytes);
		}
        public static void Write(this IDuplexPipe duplexPipe, byte value)
        {
            Span<byte> bytes = stackalloc byte[1];
            bytes[0] = value;
            duplexPipe.Write(bytes);
        }

		public static void Write(this IDuplexPipe duplexPipe, bool value)
        {
            byte[] destination = BitConverter.GetBytes(value);
            duplexPipe.Write((ReadOnlySpan<byte>) destination);
        }

        public static void Write(this IDuplexPipe duplexPipe, string value)
		{
            //            var bytes = Utf8.GetBytes(value.ToCharArray());
            //            duplexPipe.Write(bytes.Length);
            //            duplexPipe.Write(new ReadOnlySpan<byte>(bytes));
            //            return;
            int length = Utf8.GetByteCount(value);
            duplexPipe.Write(length);

            if (length > 100)
            {
                var bytes = new byte[length];

                Utf8.GetBytes(value, 0, value.Length, bytes, 0);
                duplexPipe.Write((ReadOnlyMemory<byte>)bytes);
            }
            else
            {
                using (var memoryOwner = ArrayPoolOwner<byte>.Rent(length))
                {
                    Utf8.GetBytes(value, 0, value.Length, memoryOwner.Array, 0);
                    duplexPipe.Write(memoryOwner.Memory.Slice(0, length));
                    //#if NET462 || NETCOREAPP2_0
                    //				Utf8.GetBytes(value, 0, value.Length, memoryOwner.Array, 0);
                    //#else
                    //              Utf8.GetBytes(value, memoryOwner.Array);
                    //#endif
                }
            }

        }

        public static void Write(this IDuplexPipe duplexPipe, params char[] chars)
        {
            int count = Utf8.GetByteCount(chars);
            var bytes = Utf8.GetBytes(chars);
            duplexPipe.Write(count);
            duplexPipe.Write((ReadOnlySpan<byte>) bytes);
        }

		public static void Write(this IDuplexPipe duplexPipe, decimal value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);
            
            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
        }

        public static void Write(this IDuplexPipe duplexPipe, double value)
        {
			Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
		}

        public static void Write(this IDuplexPipe duplexPipe, float value)
        {
			Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
		}

        public static void Write(this IDuplexPipe duplexPipe, long value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
        }
        
        public static void Write(this IDuplexPipe duplexPipe, sbyte value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
		}

        public static void Write(this IDuplexPipe duplexPipe, short value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
        }

        public static void Write(this IDuplexPipe duplexPipe, uint value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
        }
        public static void Write(this IDuplexPipe duplexPipe, ulong value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
        }

        public static void Write(this IDuplexPipe duplexPipe, ushort value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
		}

        public static void Write(this IDuplexPipe duplexPipe, Guid value)
        {
            Span<byte> bytes = stackalloc byte[50];
            Utf8Formatter.TryFormat(value, bytes, out int written);

            duplexPipe.Write(written);
            duplexPipe.Write(bytes.Slice(0, written));
        }

		public static decimal ReadDecimal(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out decimal value, out _);

            return value;
		}

		public static double ReadDouble(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out double value, out _);

            return value;
        }

		public static float ReadFloat(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out float value, out _);

            return value;
		}

        public static long ReadLong(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out long value, out _);

            return value;
		}

        public static sbyte ReadSByte(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out sbyte value, out _);

            return value;
		}

        public static short ReadShort(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out short value, out _);

            return value;
		}

        public static uint ReadUInt(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out uint value, out _);

            return value;
		}

        public static ulong ReadULong(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out ulong value, out _);

            return value;
		}

        public static ushort ReadUShort(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out ushort value, out _);

            return value;
        }

        public static Guid ReadGuid(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            Utf8Parser.TryParse(memory.Span, out Guid value, out _);

            return value;
        }

		public static string ReadString(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
//            var chars = Utf8.GetChars(memory.ToArray());
//            return new string(chars);

#if NET462 || NETCOREAPP2_0
			var value = Utf8.GetString(memory.Span.ToArray());
#else
            var value = Utf8.GetString(memory.Span);
#endif
			return value;
        }

		public static int ReadInt(this IDuplexPipe duplexPipe)
		{
			const int length = 4;
			ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
#if NET462 || NETCOREAPP2_0
			int value = BitConverter.ToInt32(memory.ToArray(), 0);
#else
			int value = BitConverter.ToInt32(memory.Span);
#endif
			return value;
		}

		public static bool ReadBool(this IDuplexPipe duplexPipe)
		{
			ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(1);
			bool value = memory.Span[0] > 0;

			return value;
		}

		public static byte[] Read(this IDuplexPipe duplexPipe, int count)
		{
			ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(count);
			byte[] destination = memory.ToArray();

			return destination;
		}

        public static byte ReadByte(this IDuplexPipe duplexPipe)
        {
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(1);

            return memory.Span[0];
        }

        public static char ReadChar(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            var chars = Utf8.GetChars(memory.Span.ToArray());

            return chars[0];
        }

		public static char[] ReadCharArray(this IDuplexPipe duplexPipe)
        {
            int length = duplexPipe.ReadInt();
            ReadOnlyMemory<byte> memory = duplexPipe.ReadAsync(length);
            var chars = Utf8.GetChars(memory.Span.ToArray());

            return chars;
        }
		//TODO Create dictionary with functions for write and read value like Read<bool>
	}
}