namespace Messaging.Core.Tests.Compression;

using Messaging.Core.Compression;
using System.Text;

public sealed class GZipPayloadCompressorTests
{
    private readonly GZipPayloadCompressor _sut = new();

    [Fact]
    public void Compress_ThenDecompress_ReturnsOriginal()
    {
        var original = Encoding.UTF8.GetBytes("Hello, Azure Service Bus Messaging Library!");
        var compressed   = _sut.Compress(original);
        var decompressed = _sut.Decompress(compressed.Span);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public void Compress_LargePayload_SmallerThanOriginal()
    {
        var original   = Encoding.UTF8.GetBytes(new string('A', 10_000));
        var compressed = _sut.Compress(original);
        Assert.True(compressed.Length < original.Length);
    }

    [Fact]
    public void Compress_EmptyInput_RoundTrips()
    {
        var compressed   = _sut.Compress(ReadOnlySpan<byte>.Empty);
        var decompressed = _sut.Decompress(compressed.Span);
        Assert.Empty(decompressed.ToArray());
    }
}
