using System;
using System.IO;
using System.IO.Compression;

namespace OpenNestCoop.GameSync;

internal static class MapSnapshotCodec
{
    internal const int MaxCompressed = 200000;
    internal const int MaxDecoded = 4000000;
    internal static byte[] Encode(byte[] data)
    {
        if (data == null || data.Length > MaxDecoded) throw new InvalidDataException("Map snapshot exceeds limit");
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(data, 0, data.Length);
        var packed = output.ToArray();
        if (packed.Length > MaxCompressed) throw new InvalidDataException("Compressed map exceeds transport limit");
        return packed;
    }
    internal static byte[] Decode(byte[] data)
    {
        if (data == null || data.Length > MaxCompressed) throw new InvalidDataException("Compressed map exceeds limit");
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var block = new byte[8192]; int n;
        while ((n = gzip.Read(block, 0, block.Length)) != 0)
        {
            if (output.Length + n > MaxDecoded) throw new InvalidDataException("Decoded map exceeds limit");
            output.Write(block, 0, n);
        }
        return output.ToArray();
    }
}
