using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace ServiceWire.DuplexPipes
{
    public class FakeDuplexPipe : IDuplexPipe
    {
        private byte[] _arrayPool;
        private int _position;
        private int _readPosition;
        private int _length;

        public PipeReader Input => null;

        public PipeWriter Output => null;

        public FakeDuplexPipe()
        {
            _arrayPool = ArrayPool<byte>.Shared.Rent(100);
            _length = 100;
        }

        public ValueTask AppendToBuffer(ReadOnlyMemory<byte> memory)
        {
            if (memory.Length + _position > _length)
            {
                _length = memory.Length + _position + 1024;
                byte[] bytes = ArrayPool<byte>.Shared.Rent(_length);
                Buffer.BlockCopy(_arrayPool, 0, bytes, 0, _arrayPool.Length);
                ArrayPool<byte>.Shared.Return(_arrayPool);
                _arrayPool = bytes;

                Buffer.BlockCopy(memory.ToArray(), 0, _arrayPool, _position, memory.Length);

            }
            else
            {
                Buffer.BlockCopy(memory.ToArray(), 0, _arrayPool, _position, memory.Length);
            }

            _position = memory.Length + _position;

            return default;
        }

        public ReadOnlyMemory<byte> ReadAsync()
        {
            return _arrayPool.AsMemory().Slice(0, _position);
        }

        public ReadOnlyMemory<byte> ReadAsync(int count)
        {
            var memory = _arrayPool.AsMemory().Slice(_readPosition, count);
            _readPosition += count;
            return memory;
        }

        public ValueTask Write(in ReadOnlyMemory<byte> payload)
        {
            AppendToBuffer(payload);
            return default;
        }

        public ValueTask Write(in ReadOnlySpan<byte> payload)
        {
            if (_arrayPool == null)
            {
                _length = payload.Length;
                _position = payload.Length;
                _arrayPool = ArrayPool<byte>.Shared.Rent(_length);
                payload.CopyTo(_arrayPool);
            }
            else
            {
                AppendToBuffer(payload.ToArray());
            }

            return default;
        }

        public void Dispose()
        {
            if (_arrayPool != null)
                ArrayPool<byte>.Shared.Return(_arrayPool);
        }
    }
}