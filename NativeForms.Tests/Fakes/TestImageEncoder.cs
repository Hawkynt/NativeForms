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
    public static byte[] EncodeIco(params (int Width, int Height, byte[] Data)[] entries) => EncodeIconContainer(1, 1, 32, entries);

    /// <summary>Builds a CUR container (directory type 2), hotspot at the top-left.</summary>
    public static byte[] EncodeCur(params (int Width, int Height, byte[] Data)[] entries) => EncodeIconContainer(2, 0, 0, entries);

    /// <summary>Builds a CUR container with an explicit hotspot (stored in the planes/bitcount fields).</summary>
    public static byte[] EncodeCurWithHotspot(int hotspotX, int hotspotY, params (int Width, int Height, byte[] Data)[] entries)
        => EncodeIconContainer(2, (ushort)hotspotX, (ushort)hotspotY, entries);

    private static byte[] EncodeIconContainer(ushort type, ushort field1, ushort field2, (int Width, int Height, byte[] Data)[] entries)
    {
        using var output = new MemoryStream();
        Span<byte> word = stackalloc byte[4];
        output.Write([0, 0, (byte)type, 0]); // reserved + directory type (1 = icon, 2 = cursor)
        BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)entries.Length);
        output.Write(word[..2]);

        var offset = 6 + (entries.Length * 16);
        foreach (var (width, height, data) in entries)
        {
            output.WriteByte((byte)(width == 256 ? 0 : width));
            output.WriteByte((byte)(height == 256 ? 0 : height));
            output.WriteByte(0); // colour count
            output.WriteByte(0); // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(word, field1); // planes (ICO) or hotspot X (CUR)
            output.Write(word[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(word, field2); // bit count (ICO) or hotspot Y (CUR)
            output.Write(word[..2]);
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

    /// <summary>Builds a 24-bit BI_RGB BMP (bottom-up padded BGR).</summary>
    public static byte[] EncodeBmp24(int width, int height, int[] argb)
    {
        var stride = ((width * 3) + 3) & ~3;
        const int pixelOffset = 54;
        var output = new byte[pixelOffset + (stride * height)];
        output[0] = 0x42;
        output[1] = 0x4D;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2), output.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(10), pixelOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(22), height); // positive → bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(30), 0); // BI_RGB
        for (var y = 0; y < height; ++y)
        {
            var row = pixelOffset + ((height - 1 - y) * stride);
            for (var x = 0; x < width; ++x)
            {
                var pixel = argb[(y * width) + x];
                output[row + (x * 3)] = (byte)pixel;
                output[row + (x * 3) + 1] = (byte)(pixel >> 8);
                output[row + (x * 3) + 2] = (byte)(pixel >> 16);
            }
        }

        return output;
    }

    /// <summary>Builds an 8-bit RLE PCX (one plane) with a 256-colour VGA trailer palette.</summary>
    public static byte[] EncodePcx8(int width, int height, byte[] indices, byte[] palette768)
    {
        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[128];
        header.Clear();
        header[0] = 0x0A;
        header[1] = 5;
        header[2] = 1;
        header[3] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)(width - 1));  // xmax
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], (ushort)(height - 1)); // ymax
        header[65] = 1; // planes
        BinaryPrimitives.WriteUInt16LittleEndian(header[66..], (ushort)width);
        output.Write(header);

        for (var y = 0; y < height; ++y)
        {
            var x = 0;
            while (x < width)
            {
                var value = indices[(y * width) + x];
                var run = 1;
                while (x + run < width && run < 63 && indices[(y * width) + x + run] == value)
                    ++run;

                if (run > 1 || (value & 0xC0) == 0xC0)
                {
                    output.WriteByte((byte)(0xC0 | run));
                    output.WriteByte(value);
                }
                else
                {
                    output.WriteByte(value);
                }

                x += run;
            }
        }

        output.WriteByte(0x0C);
        output.Write(palette768, 0, 768);
        return output.ToArray();
    }

    /// <summary>Builds a GIF89a whose frames are LZW-encoded with a clear code before every pixel — no
    /// real compression, but spec-valid bytes the decoder inflates exactly.</summary>
    public static byte[] EncodeGif(int width, int height, byte[][] frames, int[] delaysCentis, byte[] paletteRgb, int loopCount = 0, int transparentIndex = -1)
    {
        var colors = paletteRgb.Length / 3;
        var bits = 2;
        while ((1 << bits) < colors)
            ++bits;

        using var ms = new MemoryStream();
        Span<byte> w = stackalloc byte[2];
        ms.Write("GIF89a"u8);
        BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)width);
        ms.Write(w);
        BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)height);
        ms.Write(w);
        ms.WriteByte((byte)(0x80 | ((bits - 1) << 4) | (bits - 1))); // GCT flag + sizes
        ms.WriteByte(0);
        ms.WriteByte(0);
        var gct = new byte[(1 << bits) * 3];
        Array.Copy(paletteRgb, gct, Math.Min(paletteRgb.Length, gct.Length));
        ms.Write(gct);

        if (frames.Length > 1)
        {
            ms.Write([0x21, 0xFF, 11]);
            ms.Write("NETSCAPE2.0"u8);
            ms.Write([0x03, 0x01]);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)loopCount);
            ms.Write(w);
            ms.WriteByte(0);
        }

        for (var f = 0; f < frames.Length; ++f)
        {
            ms.Write([0x21, 0xF9, 4]);
            ms.WriteByte((byte)(transparentIndex >= 0 ? 1 : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)delaysCentis[f]);
            ms.Write(w);
            ms.WriteByte((byte)(transparentIndex >= 0 ? transparentIndex : 0));
            ms.WriteByte(0);

            ms.WriteByte(0x2C);
            ms.Write([0, 0, 0, 0]); // left, top
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)width);
            ms.Write(w);
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)height);
            ms.Write(w);
            ms.WriteByte(0); // no LCT, no interlace
            ms.WriteByte((byte)bits); // LZW minimum code size

            var clear = 1 << bits;
            var codes = new List<int>();
            foreach (var index in frames[f])
            {
                codes.Add(clear);
                codes.Add(index);
            }

            codes.Add(clear + 1); // end code
            var packed = PackCodes(codes, bits + 1);
            var offset = 0;
            while (offset < packed.Length)
            {
                var chunk = Math.Min(255, packed.Length - offset);
                ms.WriteByte((byte)chunk);
                ms.Write(packed, offset, chunk);
                offset += chunk;
            }

            ms.WriteByte(0); // block terminator
        }

        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    private static byte[] PackCodes(List<int> codes, int codeSize)
    {
        using var ms = new MemoryStream();
        var buffer = 0;
        var bits = 0;
        foreach (var code in codes)
        {
            buffer |= code << bits;
            bits += codeSize;
            while (bits >= 8)
            {
                ms.WriteByte((byte)(buffer & 0xFF));
                buffer >>= 8;
                bits -= 8;
            }
        }

        if (bits > 0)
            ms.WriteByte((byte)(buffer & 0xFF));

        return ms.ToArray();
    }

    /// <summary>Builds an ANI (RIFF/ACON) from pre-encoded ICO frames, with an optional rate/sequence.</summary>
    public static byte[] EncodeAni(byte[][] iconFrames, int[]? rates = null, int[]? sequence = null, int defaultRate = 6)
    {
        using var body = new MemoryStream();
        var anih = new byte[36];
        BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(0), 36);
        BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(4), iconFrames.Length); // nFrames
        BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(8), sequence?.Length ?? iconFrames.Length); // nSteps
        BinaryPrimitives.WriteInt32LittleEndian(anih.AsSpan(32), defaultRate);
        WriteRiffChunk(body, "anih", anih);

        if (rates is not null)
        {
            var raw = new byte[rates.Length * 4];
            for (var i = 0; i < rates.Length; ++i)
                BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(i * 4), rates[i]);

            WriteRiffChunk(body, "rate", raw);
        }

        if (sequence is not null)
        {
            var raw = new byte[sequence.Length * 4];
            for (var i = 0; i < sequence.Length; ++i)
                BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(i * 4), sequence[i]);

            WriteRiffChunk(body, "seq ", raw);
        }

        using var fram = new MemoryStream();
        fram.Write("fram"u8);
        foreach (var icon in iconFrames)
            WriteRiffChunk(fram, "icon", icon);

        WriteRiffChunk(body, "LIST", fram.ToArray());

        using var output = new MemoryStream();
        var bodyBytes = body.ToArray();
        output.Write("RIFF"u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, 4 + bodyBytes.Length);
        output.Write(size);
        output.Write("ACON"u8);
        output.Write(bodyBytes);
        return output.ToArray();
    }

    private static void WriteRiffChunk(MemoryStream ms, string id, byte[] data)
    {
        for (var i = 0; i < 4; ++i)
            ms.WriteByte((byte)id[i]);

        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, data.Length);
        ms.Write(size);
        ms.Write(data);
        if ((data.Length & 1) != 0)
            ms.WriteByte(0); // word-align
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
