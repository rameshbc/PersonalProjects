namespace Messaging.Core.Compression;

using System.IO.Compression;

internal sealed class GZipPayloadCompressor : IPayloadCompressor
{
    public ReadOnlyMemory<byte> Compress(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(input);
        return output.ToArray();
    }

    public ReadOnlyMemory<byte> Decompress(ReadOnlySpan<byte> input)
    {
        using var inputStream = new MemoryStream(input.ToArray());
        using var output = new MemoryStream();
        using (var gz = new GZipStream(inputStream, CompressionMode.Decompress, leaveOpen: true))
            gz.CopyTo(output);
        return output.ToArray();
    }
}
