using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

internal static class GrayPng
{
    public static void Write(string path, int width, int height, byte[] pixels)
    {
        if (pixels.Length != checked(width * height)) throw new InvalidDataException("Invalid grayscale layer size.");
        using var output = File.Create(path);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            for (int row = 0; row < height; row++) { deflate.WriteByte(0); deflate.Write(pixels, row * width, width); }
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream output, string name, byte[] bytes)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, bytes.Length);
        output.Write(word);
        byte[] kind = Encoding.ASCII.GetBytes(name);
        output.Write(kind); output.Write(bytes);
        uint crc = uint.MaxValue;
        foreach (byte value in kind.Concat(bytes)) {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
    }
}
