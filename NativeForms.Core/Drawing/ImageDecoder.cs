using System.Buffers.Binary;
using System.IO.Compression;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// A small pure-managed image decoder, so icons ship as ordinary <c>.png</c>/<c>.ico</c> bytes
/// without dragging a native codec or an imaging library into the footprint (PRD §8). Deliberately a
/// subset, honest about its edges:
/// PNG — 8-bit-per-channel non-interlaced images in grayscale, grayscale+alpha, RGB, RGBA or
/// palette form, all five scanline filters; the zlib stream is inflated with
/// <see cref="DeflateStream"/> (header and Adler32 trailer skipped), and chunk CRCs are not
/// verified — the input is the application's own embedded resource, not hostile data. <c>tRNS</c>
/// palette transparency and 16-bit channels are not supported.
/// ICO — directory parsing plus per-entry decoding of embedded PNGs and classic <c>BI_RGB</c>
/// bitmaps: 32-bit BGRA (its own alpha channel) and 24-bit BGR with the 1-bit AND mask supplying
/// transparency.
/// </summary>
public static class ImageDecoder
{
    /// <summary>The eight-byte PNG file signature.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Decodes a PNG (see the class summary for the supported subset) into row-major 32-bit ARGB
    /// pixels.
    /// </summary>
    /// <exception cref="FormatException">The data is not a PNG or uses an unsupported feature.</exception>
    public static (int Width, int Height, int[] Argb) DecodePng(ReadOnlySpan<byte> data)
    {
        if (data.Length < PngSignature.Length + 25 || !data.StartsWith(PngSignature))
            throw new FormatException("Not a PNG: the file signature is missing.");

        // --- IHDR: always the first chunk. ---
        var offset = PngSignature.Length;
        ReadChunkHeader(data, ref offset, out var headerLength, out var headerType);
        if (headerType != 0x49484452u || headerLength != 13) // "IHDR"
            throw new FormatException("Not a PNG: the IHDR chunk is missing or malformed.");

        var width = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]);
        var bitDepth = data[offset + 8];
        var colorType = data[offset + 9];
        var interlace = data[offset + 12];
        offset += 13 + 4; // data + CRC

        if (width <= 0 || height <= 0)
            throw new FormatException("Not a PNG: the IHDR dimensions are invalid.");
        if (bitDepth != 8)
            throw new FormatException($"Unsupported PNG: bit depth {bitDepth} (only 8-bit channels are decoded).");
        if (interlace != 0)
            throw new FormatException("Unsupported PNG: interlaced images are not decoded.");

        var channels = colorType switch
        {
            0 => 1, // grayscale
            2 => 3, // RGB
            3 => 1, // palette index
            4 => 2, // grayscale + alpha
            6 => 4, // RGBA
            _ => throw new FormatException($"Unsupported PNG: color type {colorType}."),
        };

        // --- Remaining chunks: collect the palette and the IDAT segments. ---
        ReadOnlySpan<byte> palette = default;
        var compressedLength = 0;
        var scan = offset;
        while (scan < data.Length - 4)
        {
            ReadChunkHeader(data, ref scan, out var length, out var type);
            if (scan + length + 4 > data.Length)
                throw new FormatException("Not a PNG: a chunk overruns the file.");

            switch (type)
            {
                case 0x504C5445u: // "PLTE"
                    palette = data.Slice(scan, length);
                    break;
                case 0x49444154u: // "IDAT"
                    compressedLength += length;
                    break;
            }

            scan += length + 4;
            if (type == 0x49454E44u) // "IEND"
                break;
        }

        if (compressedLength < 3)
            throw new FormatException("Not a PNG: no image data (IDAT) found.");
        if (colorType == 3 && (palette.IsEmpty || palette.Length % 3 != 0))
            throw new FormatException("Not a PNG: the palette (PLTE) is missing or malformed.");

        // --- Concatenate the IDAT payloads and inflate, skipping the 2-byte zlib header. ---
        var compressed = new byte[compressedLength];
        var copied = 0;
        scan = offset;
        while (scan < data.Length - 4)
        {
            ReadChunkHeader(data, ref scan, out var length, out var type);
            if (type == 0x49444154u)
            {
                data.Slice(scan, length).CopyTo(compressed.AsSpan(copied));
                copied += length;
            }

            scan += length + 4;
            if (type == 0x49454E44u)
                break;
        }

        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        try
        {
            using var inflater = new DeflateStream(new MemoryStream(compressed, 2, compressed.Length - 2, writable: false), CompressionMode.Decompress);
            inflater.ReadExactly(raw);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            throw new FormatException("Not a PNG: the image data does not inflate to the expected size.", exception);
        }

        Unfilter(raw, height, stride, channels);

        // --- Expand the unfiltered scanlines into ARGB. ---
        var argb = new int[width * height];
        for (var y = 0; y < height; ++y)
        {
            var row = raw.AsSpan((y * (stride + 1)) + 1, stride);
            var target = y * width;
            for (var x = 0; x < width; ++x)
            {
                var source = x * channels;
                argb[target + x] = colorType switch
                {
                    0 => Argb(0xFF, row[source], row[source], row[source]),
                    2 => Argb(0xFF, row[source], row[source + 1], row[source + 2]),
                    3 => PaletteArgb(palette, row[source]),
                    4 => Argb(row[source + 1], row[source], row[source], row[source]),
                    _ => Argb(row[source + 3], row[source], row[source + 1], row[source + 2]),
                };
            }
        }

        return (width, height, argb);
    }

    /// <summary>
    /// Decodes an ICO container, picking the entry whose width is closest to
    /// <paramref name="preferredSize"/> (larger wins ties; 0 or less selects the largest entry), and
    /// returns its pixels as row-major 32-bit ARGB.
    /// </summary>
    /// <exception cref="FormatException">The data is not an ICO or the chosen entry is unsupported.</exception>
    public static (int Width, int Height, int[] Argb) DecodeIco(ReadOnlySpan<byte> data, int preferredSize = 0)
    {
        if (data.Length < 6
            || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != 1)
            throw new FormatException("Not an ICO: the header is missing or not an icon container.");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (count == 0 || data.Length < 6 + (count * 16))
            throw new FormatException("Not an ICO: the entry directory is empty or truncated.");

        // --- Pick the best-matching entry from the 16-byte directory records. ---
        var bestIndex = -1;
        var bestWidth = -1;
        for (var i = 0; i < count; ++i)
        {
            var entry = 6 + (i * 16);
            int entryWidth = data[entry];
            if (entryWidth == 0)
                entryWidth = 256;

            var better = bestIndex < 0
                || (preferredSize <= 0
                    ? entryWidth > bestWidth
                    : Math.Abs(entryWidth - preferredSize) < Math.Abs(bestWidth - preferredSize)
                      || (Math.Abs(entryWidth - preferredSize) == Math.Abs(bestWidth - preferredSize) && entryWidth > bestWidth));
            if (!better)
                continue;

            bestIndex = i;
            bestWidth = entryWidth;
        }

        var record = 6 + (bestIndex * 16);
        var byteCount = BinaryPrimitives.ReadInt32LittleEndian(data[(record + 8)..]);
        var byteOffset = BinaryPrimitives.ReadInt32LittleEndian(data[(record + 12)..]);
        if (byteCount <= 0 || byteOffset <= 0 || (long)byteOffset + byteCount > data.Length)
            throw new FormatException("Not an ICO: the chosen entry overruns the file.");

        var image = data.Slice(byteOffset, byteCount);
        return image.StartsWith(PngSignature) ? DecodePng(image) : DecodeIcoBitmap(image);
    }

    /// <summary>Decodes a classic ICO bitmap entry: a <c>BITMAPINFOHEADER</c> followed by the
    /// bottom-up XOR pixel block and (for 24-bit) the 1-bit AND transparency mask.</summary>
    private static (int Width, int Height, int[] Argb) DecodeIcoBitmap(ReadOnlySpan<byte> data)
    {
        if (data.Length < 40)
            throw new FormatException("Not an ICO bitmap: the BITMAPINFOHEADER is truncated.");

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var doubleHeight = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);

        var height = doubleHeight / 2; // the header height covers XOR + AND blocks
        if (headerSize < 40 || width <= 0 || height <= 0)
            throw new FormatException("Not an ICO bitmap: the header dimensions are invalid.");
        if (compression != 0)
            throw new FormatException($"Unsupported ICO bitmap: compression {compression} (only BI_RGB is decoded).");
        if (bitCount is not (24 or 32))
            throw new FormatException($"Unsupported ICO bitmap: {bitCount} bpp (only 24- and 32-bit entries are decoded).");

        var bytesPerPixel = bitCount / 8;
        var xorStride = ((width * bytesPerPixel) + 3) & ~3; // rows pad to 32-bit boundaries
        var xorStart = headerSize;
        if (xorStart + (long)xorStride * height > data.Length)
            throw new FormatException("Not an ICO bitmap: the pixel block overruns the entry.");

        var argb = new int[width * height];
        for (var y = 0; y < height; ++y)
        {
            var row = data.Slice(xorStart + ((height - 1 - y) * xorStride), width * bytesPerPixel); // bottom-up
            var target = y * width;
            for (var x = 0; x < width; ++x)
            {
                var source = x * bytesPerPixel;
                argb[target + x] = bitCount == 32
                    ? Argb(row[source + 3], row[source + 2], row[source + 1], row[source])
                    : Argb(0xFF, row[source + 2], row[source + 1], row[source]);
            }
        }

        // 24-bit entries carry transparency in the AND mask: a set bit means "transparent here".
        if (bitCount == 24)
        {
            var maskStride = ((width + 31) / 32) * 4; // 1 bpp, rows pad to 32-bit boundaries
            var maskStart = xorStart + (xorStride * height);
            if (maskStart + (long)maskStride * height > data.Length)
                throw new FormatException("Not an ICO bitmap: the AND mask overruns the entry.");

            for (var y = 0; y < height; ++y)
            {
                var row = data.Slice(maskStart + ((height - 1 - y) * maskStride), maskStride);
                var target = y * width;
                for (var x = 0; x < width; ++x)
                    if ((row[x >> 3] & (0x80 >> (x & 7))) != 0)
                        argb[target + x] = 0;
            }
        }

        return (width, height, argb);
    }

    /// <summary>Reads a chunk's big-endian length and four-byte type, advancing <paramref name="offset"/> past both.</summary>
    private static void ReadChunkHeader(ReadOnlySpan<byte> data, ref int offset, out int length, out uint type)
    {
        if (offset + 8 > data.Length)
            throw new FormatException("Not a PNG: a chunk header is truncated.");

        length = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
        type = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
        if (length < 0)
            throw new FormatException("Not a PNG: a chunk declares a negative length.");

        offset += 8;
    }

    /// <summary>Reverses the per-scanline filters in place: each row starts with its filter-type byte
    /// (None, Sub, Up, Average, Paeth) followed by the filtered bytes.</summary>
    private static void Unfilter(byte[] raw, int height, int stride, int bytesPerPixel)
    {
        for (var y = 0; y < height; ++y)
        {
            var rowStart = y * (stride + 1);
            var filter = raw[rowStart];
            var row = rowStart + 1;
            var previous = row - (stride + 1);
            switch (filter)
            {
                case 0: // None
                    break;

                case 1: // Sub
                    for (var i = bytesPerPixel; i < stride; ++i)
                        raw[row + i] = (byte)(raw[row + i] + raw[row + i - bytesPerPixel]);
                    break;

                case 2: // Up
                    if (y > 0)
                        for (var i = 0; i < stride; ++i)
                            raw[row + i] = (byte)(raw[row + i] + raw[previous + i]);
                    break;

                case 3: // Average
                    for (var i = 0; i < stride; ++i)
                    {
                        var left = i >= bytesPerPixel ? raw[row + i - bytesPerPixel] : 0;
                        var up = y > 0 ? raw[previous + i] : 0;
                        raw[row + i] = (byte)(raw[row + i] + ((left + up) >> 1));
                    }

                    break;

                case 4: // Paeth
                    for (var i = 0; i < stride; ++i)
                    {
                        var left = i >= bytesPerPixel ? raw[row + i - bytesPerPixel] : 0;
                        var up = y > 0 ? raw[previous + i] : 0;
                        var upLeft = y > 0 && i >= bytesPerPixel ? raw[previous + i - bytesPerPixel] : 0;
                        raw[row + i] = (byte)(raw[row + i] + Paeth(left, up, upLeft));
                    }

                    break;

                default:
                    throw new FormatException($"Not a PNG: unknown scanline filter {filter}.");
            }
        }
    }

    /// <summary>The Paeth predictor: whichever neighbor is closest to <c>left + up − upLeft</c>.</summary>
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

    /// <summary>Looks a palette index up as an opaque ARGB pixel.</summary>
    private static int PaletteArgb(ReadOnlySpan<byte> palette, int index)
    {
        var entry = index * 3;
        if (entry + 3 > palette.Length)
            throw new FormatException("Not a PNG: a pixel indexes past the palette.");

        return Argb(0xFF, palette[entry], palette[entry + 1], palette[entry + 2]);
    }

    /// <summary>Packs channels into one 32-bit ARGB pixel.</summary>
    private static int Argb(int alpha, int red, int green, int blue)
        => (alpha << 24) | (red << 16) | (green << 8) | blue;
}
