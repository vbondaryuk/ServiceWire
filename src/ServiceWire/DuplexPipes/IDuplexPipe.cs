using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace ServiceWire.DuplexPipes
{
    public interface IDuplexPipe : IDisposable
    {
        PipeReader Input { get; }

        PipeWriter Output { get; }

        ValueTask AppendToBuffer(ReadOnlyMemory<byte> memory);

        ReadOnlyMemory<byte> ReadAsync();

        ReadOnlyMemory<byte> ReadAsync(int count);

        ValueTask Write(in ReadOnlyMemory<byte> payload);

        ValueTask Write(in ReadOnlySpan<byte> payload);
    }
}