using System.Buffers.Binary;
using System.IO.Compression;

namespace Hawkynt.NativeForms.Tests.Fakes;

/// <summary>
/// A minimal PNG/ICO writer for round-trip fixtures — it lives in the test project on purpose, so
/// the shipped library carries a decoder only. It produces spec-valid files: real zlib framing
/// (header + Adler32), real chunk CRCs, any of the five scanline filters applied forward, and ICO
/// containers around PNG or classic BMP entries.
/// </summary>
internal static class TestImageEncoder
{
    /// <summary>Encodes RGBA pixels (row-major ARGB ints) as an 8-bit color-type-6 PNG, filtering every row with <paramref name="filter"/>.</summary>
    public static byte[] EncodeRgba(int width, int height, int[] argb, byte filter = 0)
    {
        var samples = new byte[width * height * 4];
        for (var i = 0; i < argb.Length; ++i)
        {
            var pixel = argb[i];
            samples[i * 4] = (byte)(pixel >> 16);
            samples[(i * 4) + 1] = (byte)(pixel >> 8);
            samples[(i * 4) + 2] = (byte)pixel;
            samples[(i * 4) + 3] = (byte)((uint)pixel >> 24);
        }

        return EncodePng(width, height, 6, samples, filter);
    }

    /// <summary>Encodes RGB pixels (alpha ignored) as an 8-bit color-type-2 PNG.</summary>
    public static byte[] EncodeRgb(int width, int height, int[] argb, byte filter = 0)
    {
        var samples = new byte[width * height * 3];
        for (var i = 0; i < argb.Length; ++i)
        {
            var pixel = argb[i];
            samples[i * 3] = (byte)(pixel >> 16);
            samples[(i * 3) + 1] = (byte)(pixel >> 8);
            samples[(i * 3) + 2] = (byte)pixel;
        }

        return EncodePng(width, height, 2, samples, filter);
    }

    /// <summary>Encodes one grayscale sample per pixel as an 8-bit color-type-0 PNG.</summary>
    public static byte[] EncodeGrayscale(int width, int height, byte[] gray, byte filter = 0)
        => EncodePng(width, height, 0, gray, filter);

    /// <summary>Encodes gray+alpha sample pairs as an 8-bit color-type-4 PNG.</summary>
    public static byte[] EncodeGrayscaleAlpha(int width, int height, byte[] grayAlpha, byte filter = 0)
        => EncodePng(width, height, 4, grayAlpha, filter);

    /// <summary>Encodes palette indices plus an RGB-triplet palette as an 8-bit color-type-3 PNG.</summary>
    public static byte[] EncodePalette(int width, int height, byte[] indices, byte[] palette, byte filter = 0)
        => EncodePng(width, height, 3, indices, filter, palette);

    /// <summary>Encodes raw unfiltered scanline samples as a PNG, applying <paramref name="filter"/> forward on every row.</summary>
    public static byte[] EncodePng(int width, int height, byte colorType, byte[] samples, byte filter, byte[]? palette = null)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var stride = width * channels;

        // Filter forward: each output row is the filter-type byte followed by the filtered bytes.
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; ++y)
        {
            var rowOut = (y * (stride + 1)) + 1;
            raw[rowOut - 1] = filter;
            for (var i = 0; i < stride; ++i)
            {
                int value = samples[(y * stride) + i];
                var left = i >= channels ? samples[(y * stride) + i - channels] : 0;
                var up = y > 0 ? samples[((y - 1) * stride) + i] : 0;
                var upLeft = y > 0 && i >= channels ? samples[((y - 1) * stride) + i - channels] : 0;
                raw[rowOut + i] = filter switch
                {
                    1 => (byte)(value - left),
                    2 => (byte)(value - up),
                    3 => (byte)(value - ((left + up) >> 1)),
                    4 => (byte)(value - Paeth(left, up, upLeft)),
                    _ => (byte)value,
                };
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bit depth
        header[9] = colorType;
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // not interlaced
        WriteChunk(output, "IHDR", header);

        if (palette is not null)
            WriteChunk(output, "PLTE", palette);

        WriteChunk(output, "IDAT", ZlibCompress(raw));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Builds an ICO container around pre-encoded entries (PNG bytes or classic BMP blocks).</summary>
    public static byte[] EncodeIco(params (int Width, int Height, byte[] Data)[] entries)
    {
        using var output = new MemoryStream();
        Span<byte> word = stackalloc byte[4];
        output.Write([0, 0, 1, 0]); // reserved + type 1 (icon)
        BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)entries.Length);
        output.Write(word[..2]);

        var offset = 6 + (entries.Length * 16);
        foreach (var (width, height, data) in entries)
        {
            output.WriteByte((byte)(width == 256 ? 0 : width));
            output.WriteByte((byte)(height == 256 ? 0 : height));
            output.Write([0, 0, 1, 0, 32, 0]); // colors, reserved, planes 1, bpp 32 (advisory)
            BinaryPrimitives.WriteInt32LittleEndian(word, data.Length);
            output.Write(word);
            BinaryPrimitives.WriteInt32LittleEndian(word, offset);
            output.Write(word);
            offset += data.Length;
        }

        foreach (var (_, _, data) in entries)
            output.Write(data);

        return output.ToArray();
    }

    /// <summary>Builds a 32-bit BI_RGB ICO bitmap entry (bottom-up BGRA; the alpha channel carries transparency).</summary>
    public static byte[] EncodeIcoBmp32(int width, int height, int[] argb)
    {
        var output = new byte[40 + (width * height * 4)];
        WriteBitmapHeader(output, width, height, 32);
        for (var y = 0; y < height; ++y)
        {
            var row = 40 + ((height - 1 - y) * width * 4);
            for (var x = 0; x < width; ++x)
            {
                var pixel = argb[(y * width) + x];
                output[row + (x * 4)] = (byte)pixel;
                output[row + (x * 4) + 1] = (byte)(pixel >> 8);
                output[row + (x * 4) + 2] = (byte)(pixel >> 16);
                output[row + (x * 4) + 3] = (byte)((uint)pixel >> 24);
            }
        }

        return output;
    }

    /// <summary>Builds a 24-bit BI_RGB ICO bitmap entry (bottom-up padded BGR) whose AND mask marks
    /// every fully transparent input pixel (alpha 0).</summary>
    public static byte[] EncodeIcoBmp24(int width, int height, int[] argb)
    {
        var xorStride = ((width * 3) + 3) & ~3;
        var maskStride = ((width + 31) / 32) * 4;
        var output = new byte[40 + (xorStride * height) + (maskStride * height)];
        WriteBitmapHeader(output, width, height, 24);
        for (var y = 0; y < height; ++y)
        {
            var row = 40 + ((height - 1 - y) * xorStride);
            var maskRow = 40 + (xorStride * height) + ((height - 1 - y) * maskStride);
            for (var x = 0; x < width; ++x)
            {
                var pixel = argb[(y * width) + x];
                output[row + (x * 3)] = (byte)pixel;
                output[row + (x * 3) + 1] = (byte)(pixel >> 8);
                output[row + (x * 3) + 2] = (byte)(pixel >> 16);
                if ((uint)pixel >> 24 == 0)
                    output[maskRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        return output;
    }

    private static void WriteBitmapHeader(byte[] output, int width, int height, ushort bitCount)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output, 40);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), height * 2); // XOR + AND
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(12), 1); // planes
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(14), bitCount);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16), 0); // BI_RGB
    }

    /// <summary>Wraps raw bytes in a zlib stream: 2-byte header, deflate body, Adler32 trailer.</summary>
    private static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new MemoryStream();
        output.Write([0x78, 0x9C]);
        using (var deflater = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflater.Write(raw);

        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
        output.Write(adler);
        return output.ToArray();
    }

    private static void WriteChunk(MemoryStream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);

        var typeAndData = new byte[4 + data.Length];
        for (var i = 0; i < 4; ++i)
            typeAndData[i] = (byte)type[i];
        data.CopyTo(typeAndData.AsSpan(4));
        output.Write(typeAndData);

        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(typeAndData));
        output.Write(word);
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; ++bit)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }

        return ~crc;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        return distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft ? left
            : distanceUp <= distanceUpLeft ? up
            : upLeft;
    }
}
