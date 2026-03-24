namespace Messaging.Core.Compression;

public interface IPayloadCompressor
{
    ReadOnlyMemory<byte> Compress(ReadOnlySpan<byte> input);
    ReadOnlyMemory<byte> Decompress(ReadOnlySpan<byte> input);
}
