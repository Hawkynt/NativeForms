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
/// ICO/CUR — directory parsing plus per-entry decoding of embedded PNGs and classic <c>BI_RGB</c>
/// bitmaps: 32-bit BGRA (its own alpha channel) and 24-bit BGR with the 1-bit AND mask supplying
/// transparency; a CUR is an ICO with directory type 2.
/// BMP — uncompressed <c>BI_RGB</c> at 8-bit (palette), 24-bit and 32-bit, bottom-up or top-down.
/// PCX — RLE, 8-bit indexed (VGA trailer palette) or 24-bit (three 8-bit planes).
/// GIF (87a/89a) — LZW image data composited onto the logical screen with per-frame delay, disposal
/// and transparency, and the NETSCAPE loop count, producing a multi-frame <see cref="DecodedImage"/>.
/// ANI (RIFF/ACON) — the <c>fram</c> list of ICO/CUR frames with the <c>anih</c> rate and optional
/// <c>rate</c>/<c>seq</c> chunks, looping forever.
/// The <see cref="Decode(System.ReadOnlySpan{byte})"/> entry point sniffs the format from the leading
/// bytes and returns a <see cref="DecodedImage"/> (one frame for the stills, several for GIF/ANI).
/// </summary>
public static class ImageDecoder
{
    /// <summary>The eight-byte PNG file signature.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Sniffs the format from the leading bytes and decodes into a <see cref="DecodedImage"/> — one
    /// frame for the still formats (PNG, BMP, PCX, ICO, CUR), several for the animated ones (GIF, ANI).
    /// </summary>
    /// <exception cref="FormatException">The bytes are not a recognized format or use an unsupported feature.</exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
            return Still(DecodePng(data));

        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D) // "BM"
            return Still(DecodeBmp(data));

        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F') // "GIF"
            return DecodeGif(data);

        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'A' && data[9] == (byte)'C' && data[10] == (byte)'O' && data[11] == (byte)'N') // RIFF..ACON
            return DecodeAni(data);

        if (data.Length >= 4 && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0)
        {
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            if (kind == 1)
                return Still(DecodeIco(data));
            if (kind == 2)
                return Still(DecodeCur(data));
        }

        if (data.Length >= 4 && data[0] == 0x0A && data[2] == 1) // PCX: manufacturer 0x0A, encoding 1 (RLE)
            return Still(DecodePcx(data));

        throw new FormatException("Unrecognized image format.");
    }

    /// <summary>Wraps a single decoded frame as a still <see cref="DecodedImage"/>.</summary>
    private static DecodedImage Still((int Width, int Height, int[] Argb) decoded)
        => new(decoded.Width, decoded.Height, [new ImageFrame(decoded.Argb, 0)]);

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

        return DecodeIconDirectory(data, preferredSize, out _, out _);
    }

    /// <summary>
    /// Decodes a CUR cursor container. Structurally an ICO with directory type 2 (its planes/bitcount
    /// fields carry the hotspot instead), so the image pixels decode the same way; the hotspot is not
    /// returned since the pixels are what a surface blits.
    /// </summary>
    /// <exception cref="FormatException">The data is not a CUR or the chosen entry is unsupported.</exception>
    public static (int Width, int Height, int[] Argb) DecodeCur(ReadOnlySpan<byte> data, int preferredSize = 0)
    {
        if (data.Length < 6
            || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != 2)
            throw new FormatException("Not a CUR: the header is missing or not a cursor container.");

        return DecodeIconDirectory(data, preferredSize, out _, out _);
    }

    /// <summary>
    /// Decodes bytes into a pointer image: an ICO/CUR (the CUR's hotspot comes back), an ANI (its first
    /// frame), or any still image (hotspot 0,0). The hotspot is the pixel the click aligns to.
    /// </summary>
    /// <exception cref="FormatException">The bytes are not a recognized image.</exception>
    public static (int Width, int Height, int[] Argb, int HotspotX, int HotspotY) DecodeCursor(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12 && Tag(data[..4], "RIFF") && Tag(data[8..12], "ACON"))
            return DecodeCursor(FirstAniIcon(data));

        if (data.Length >= 4 && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0)
        {
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            if (kind is 1 or 2)
            {
                var (width, height, argb) = DecodeIconDirectory(data, 0, out var hotspotX, out var hotspotY);
                return kind == 2 ? (width, height, argb, hotspotX, hotspotY) : (width, height, argb, 0, 0);
            }
        }

        var image = Decode(data);
        return (image.Width, image.Height, image.Frames[0].Argb, 0, 0);
    }

    /// <summary>The bytes of the first icon frame of an ANI, for use as a static cursor.</summary>
    private static byte[] FirstAniIcon(ReadOnlySpan<byte> data)
    {
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 4)..]);
            pos += 8;
            if (size < 0 || pos + size > data.Length)
                break;

            if (Tag(id, "LIST") && size >= 4 && Tag(data.Slice(pos, 4), "fram"))
            {
                var inner = pos + 4;
                if (inner + 8 <= pos + size)
                {
                    var innerSize = BinaryPrimitives.ReadInt32LittleEndian(data[(inner + 4)..]);
                    if (innerSize > 0 && inner + 8 + innerSize <= data.Length)
                        return data.Slice(inner + 8, innerSize).ToArray();
                }
            }

            pos += size + (size & 1);
        }

        throw new FormatException("Not an ANI: no icon frame was found.");
    }

    /// <summary>Picks the best-matching entry from an ICO/CUR directory and decodes its pixels; the
    /// entry's hotspot (meaningful only for a CUR) comes back through the out parameters.</summary>
    private static (int Width, int Height, int[] Argb) DecodeIconDirectory(ReadOnlySpan<byte> data, int preferredSize, out int hotspotX, out int hotspotY)
    {
        hotspotX = 0;
        hotspotY = 0;
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
        hotspotX = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 4)..]); // CUR: wPlanes field holds hotspot X
        hotspotY = BinaryPrimitives.ReadUInt16LittleEndian(data[(record + 6)..]); // CUR: wBitCount field holds hotspot Y
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

    /// <summary>Decodes an uncompressed (<c>BI_RGB</c>) BMP — 8-bit palette, 24-bit BGR or 32-bit BGRX,
    /// bottom-up or (negative height) top-down — into row-major 32-bit ARGB.</summary>
    /// <exception cref="FormatException">The data is not a BMP or uses an unsupported feature.</exception>
    public static (int Width, int Height, int[] Argb) DecodeBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 54 || data[0] != 0x42 || data[1] != 0x4D)
            throw new FormatException("Not a BMP: the file header is missing.");

        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        var compression = BinaryPrimitives.ReadInt32LittleEndian(data[30..]);

        if (headerSize < 40 || width <= 0 || rawHeight == 0)
            throw new FormatException("Not a BMP: the header dimensions are invalid.");
        if (compression != 0)
            throw new FormatException($"Unsupported BMP: compression {compression} (only BI_RGB is decoded).");
        if (bitCount is not (8 or 24 or 32))
            throw new FormatException($"Unsupported BMP: {bitCount} bpp (only 8-, 24- and 32-bit BI_RGB is decoded).");

        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);

        ReadOnlySpan<byte> palette = default;
        if (bitCount == 8)
        {
            var clrUsed = BinaryPrimitives.ReadInt32LittleEndian(data[46..]);
            var entries = clrUsed > 0 ? clrUsed : 256;
            var paletteStart = 14 + headerSize;
            if (paletteStart + (entries * 4) > data.Length)
                throw new FormatException("Not a BMP: the colour table is truncated.");

            palette = data.Slice(paletteStart, entries * 4); // RGBQUAD: B, G, R, reserved
        }

        var bytesPerPixel = bitCount / 8;
        var stride = ((width * bytesPerPixel) + 3) & ~3;
        if (pixelOffset <= 0 || pixelOffset + ((long)stride * height) > data.Length)
            throw new FormatException("Not a BMP: the pixel block overruns the file.");

        var argb = new int[width * height];
        for (var y = 0; y < height; ++y)
        {
            var row = data.Slice(pixelOffset + ((topDown ? y : height - 1 - y) * stride), width * bytesPerPixel);
            var target = y * width;
            for (var x = 0; x < width; ++x)
            {
                if (bitCount == 8)
                {
                    var entry = row[x] * 4;
                    argb[target + x] = entry + 2 < palette.Length ? Argb(0xFF, palette[entry + 2], palette[entry + 1], palette[entry]) : 0;
                }
                else
                {
                    var source = x * bytesPerPixel; // BI_RGB 32-bit's 4th byte is unused, so treat both as opaque BGR
                    argb[target + x] = Argb(0xFF, row[source + 2], row[source + 1], row[source]);
                }
            }
        }

        return (width, height, argb);
    }

    /// <summary>Decodes an RLE PCX — 8-bit palette (one plane, VGA trailer palette) or 24-bit (three
    /// 8-bit planes) — into row-major 32-bit ARGB.</summary>
    /// <exception cref="FormatException">The data is not a PCX or uses an unsupported feature.</exception>
    public static (int Width, int Height, int[] Argb) DecodePcx(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128 || data[0] != 0x0A || data[2] != 1)
            throw new FormatException("Not a PCX: the header is missing or not RLE-encoded.");

        int bitsPerPlane = data[3];
        int xmin = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int ymin = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int xmax = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int ymax = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        int planes = data[65];
        int bytesPerLine = BinaryPrimitives.ReadUInt16LittleEndian(data[66..]);

        var width = (xmax - xmin) + 1;
        var height = (ymax - ymin) + 1;
        if (width <= 0 || height <= 0)
            throw new FormatException("Not a PCX: the window dimensions are invalid.");
        if (bitsPerPlane != 8 || (planes != 1 && planes != 3))
            throw new FormatException($"Unsupported PCX: {bitsPerPlane} bits × {planes} planes (only 8-bit ×1 or ×3 is decoded).");

        var perScan = planes * bytesPerLine;
        var scanlines = new byte[height * perScan];
        var pos = 128;
        var outIndex = 0;
        while (outIndex < scanlines.Length && pos < data.Length)
        {
            var control = data[pos++];
            if ((control & 0xC0) == 0xC0)
            {
                var count = control & 0x3F;
                if (pos >= data.Length)
                    break;

                var value = data[pos++];
                for (var i = 0; i < count && outIndex < scanlines.Length; ++i)
                    scanlines[outIndex++] = value;
            }
            else
            {
                scanlines[outIndex++] = control;
            }
        }

        var argb = new int[width * height];
        if (planes == 3)
        {
            for (var y = 0; y < height; ++y)
            {
                var line = y * perScan;
                var target = y * width;
                for (var x = 0; x < width; ++x)
                    argb[target + x] = Argb(0xFF, scanlines[line + x], scanlines[line + bytesPerLine + x], scanlines[line + (2 * bytesPerLine) + x]);
            }

            return (width, height, argb);
        }

        // 8-bit indexed: the 256-colour VGA palette trails the file, flagged by a 0x0C marker.
        ReadOnlySpan<byte> vga = default;
        if (data.Length >= 769 && data[^769] == 0x0C)
            vga = data[^768..];

        for (var y = 0; y < height; ++y)
        {
            var line = y * perScan;
            var target = y * width;
            for (var x = 0; x < width; ++x)
            {
                int index = scanlines[line + x];
                argb[target + x] = vga.IsEmpty
                    ? Argb(0xFF, index, index, index)
                    : Argb(0xFF, vga[index * 3], vga[(index * 3) + 1], vga[(index * 3) + 2]);
            }
        }

        return (width, height, argb);
    }

    /// <summary>Decodes a GIF (87a/89a) — LZW image data, per-frame graphic control (delay, disposal,
    /// transparency) composited onto the logical screen, and the NETSCAPE loop count — into frames.</summary>
    /// <exception cref="FormatException">The data is not a GIF or is truncated.</exception>
    public static DecodedImage DecodeGif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13 || data[0] != (byte)'G' || data[1] != (byte)'I' || data[2] != (byte)'F')
            throw new FormatException("Not a GIF: the signature is missing.");

        int screenWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int screenHeight = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (screenWidth <= 0 || screenHeight <= 0)
            throw new FormatException("Not a GIF: the logical screen size is invalid.");

        var screenPacked = data[10];
        var globalTableSize = (screenPacked & 0x80) != 0 ? 2 << (screenPacked & 7) : 0;
        var pos = 13;
        ReadOnlySpan<byte> globalTable = default;
        if (globalTableSize > 0)
        {
            if (pos + (globalTableSize * 3) > data.Length)
                throw new FormatException("Not a GIF: the global colour table is truncated.");

            globalTable = data.Slice(pos, globalTableSize * 3);
            pos += globalTableSize * 3;
        }

        var frames = new List<ImageFrame>();
        var canvas = new int[screenWidth * screenHeight];
        var loopCount = 1;
        var delayCentis = 0;
        var transparentIndex = -1;
        var disposal = 0;

        while (pos < data.Length)
        {
            var block = data[pos++];
            if (block == 0x3B) // trailer
                break;

            if (block == 0x2C) // image descriptor
            {
                if (pos + 9 > data.Length)
                    throw new FormatException("Not a GIF: an image descriptor is truncated.");

                int left = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
                int top = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 2)..]);
                int frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 4)..]);
                int frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 6)..]);
                var localPacked = data[pos + 8];
                pos += 9;

                var interlaced = (localPacked & 0x40) != 0;
                var localTableSize = (localPacked & 0x80) != 0 ? 2 << (localPacked & 7) : 0;
                var table = globalTable;
                if (localTableSize > 0)
                {
                    if (pos + (localTableSize * 3) > data.Length)
                        throw new FormatException("Not a GIF: a local colour table is truncated.");

                    table = data.Slice(pos, localTableSize * 3);
                    pos += localTableSize * 3;
                }

                if (pos >= data.Length)
                    throw new FormatException("Not a GIF: image data is missing.");

                var minCodeSize = data[pos++];
                var lzw = GatherSubBlocks(data, ref pos);
                var indices = GifLzwDecode(lzw, minCodeSize, frameWidth * frameHeight);

                var saved = disposal == 3 ? (int[])canvas.Clone() : null;
                PaintGifFrame(canvas, screenWidth, screenHeight, indices, table, left, top, frameWidth, frameHeight, interlaced, transparentIndex);
                frames.Add(new ImageFrame((int[])canvas.Clone(), delayCentis > 0 ? delayCentis * 10 : 100));

                // The disposal readies the canvas the NEXT frame draws onto.
                if (disposal == 2)
                    ClearArea(canvas, screenWidth, screenHeight, left, top, frameWidth, frameHeight);
                else if (disposal == 3 && saved is not null)
                    Array.Copy(saved, canvas, canvas.Length);

                delayCentis = 0;
                transparentIndex = -1;
                disposal = 0;
            }
            else if (block == 0x21) // extension
            {
                if (pos >= data.Length)
                    break;

                var label = data[pos++];
                if (label == 0xF9) // graphic control
                {
                    var size = data[pos++];
                    if (size >= 4 && pos + 4 <= data.Length)
                    {
                        var controlPacked = data[pos];
                        disposal = (controlPacked >> 2) & 7;
                        delayCentis = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 1)..]);
                        transparentIndex = (controlPacked & 1) != 0 ? data[pos + 3] : -1;
                    }

                    pos += size;
                    SkipSubBlocks(data, ref pos);
                }
                else if (label == 0xFF) // application (NETSCAPE loop count)
                {
                    var size = data[pos++];
                    var app = size > 0 && pos + size <= data.Length ? data.Slice(pos, size) : default;
                    pos += size;
                    var isNetscape = app.Length >= 3 && app[0] == (byte)'N' && app[1] == (byte)'E' && app[2] == (byte)'T';
                    while (pos < data.Length)
                    {
                        var subLen = data[pos++];
                        if (subLen == 0)
                            break;

                        if (isNetscape && subLen >= 3 && pos + 3 <= data.Length && data[pos] == 1)
                            loopCount = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 1)..]);

                        pos += subLen;
                    }
                }
                else
                {
                    SkipSubBlocks(data, ref pos);
                }
            }
            else
            {
                break;
            }
        }

        if (frames.Count == 0)
            throw new FormatException("Not a GIF: no image frames were found.");

        return new DecodedImage(screenWidth, screenHeight, frames, frames.Count > 1 ? loopCount : 1);
    }

    /// <summary>Decodes an ANI (RIFF/ACON) cursor animation: the <c>fram</c> list of ICO/CUR frames,
    /// the <c>anih</c> default rate, and the optional <c>rate</c>/<c>seq</c> chunks, into frames that
    /// loop forever.</summary>
    /// <exception cref="FormatException">The data is not an ANI or is truncated.</exception>
    public static DecodedImage DecodeAni(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            throw new FormatException("Not an ANI: the RIFF header is truncated.");

        var icons = new List<(int Width, int Height, int[] Argb)>();
        int stepCount = 0, defaultRate = 10;
        int[]? rates = null;
        int[]? sequence = null;

        var pos = 12; // past "RIFF", size, "ACON"
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 4)..]);
            pos += 8;
            if (size < 0 || pos + size > data.Length)
                break;

            var chunk = data.Slice(pos, size);
            if (Tag(id, "anih") && size >= 36)
            {
                stepCount = BinaryPrimitives.ReadInt32LittleEndian(chunk[8..]);   // nSteps
                defaultRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[32..]); // jiffies (1/60 s)
            }
            else if (Tag(id, "rate"))
            {
                rates = new int[size / 4];
                for (var i = 0; i < rates.Length; ++i)
                    rates[i] = BinaryPrimitives.ReadInt32LittleEndian(chunk[(i * 4)..]);
            }
            else if (Tag(id, "seq "))
            {
                sequence = new int[size / 4];
                for (var i = 0; i < sequence.Length; ++i)
                    sequence[i] = BinaryPrimitives.ReadInt32LittleEndian(chunk[(i * 4)..]);
            }
            else if (Tag(id, "LIST") && size >= 4 && Tag(chunk[..4], "fram"))
            {
                var inner = 4;
                while (inner + 8 <= chunk.Length)
                {
                    var innerSize = BinaryPrimitives.ReadInt32LittleEndian(chunk[(inner + 4)..]);
                    if (innerSize < 0 || inner + 8 + innerSize > chunk.Length)
                        break;

                    var iconBytes = chunk.Slice(inner + 8, innerSize);
                    var kind = iconBytes.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(iconBytes[2..]) : 0;
                    icons.Add(kind == 2 ? DecodeCur(iconBytes) : DecodeIco(iconBytes));
                    inner += 8 + innerSize + (innerSize & 1);
                }
            }

            pos += size + (size & 1); // RIFF chunks are word-aligned
        }

        if (icons.Count == 0)
            throw new FormatException("Not an ANI: no icon frames were found.");

        var steps = stepCount > 0 ? stepCount : icons.Count;
        var (width, height, _) = icons[0];
        var frames = new List<ImageFrame>(steps);
        for (var step = 0; step < steps; ++step)
        {
            var index = sequence is not null && step < sequence.Length ? sequence[step] : step;
            if (index < 0 || index >= icons.Count)
                index = 0;

            var rate = rates is not null && step < rates.Length ? rates[step] : defaultRate;
            var delayMs = Math.Max(1, (rate * 1000) / 60);
            var pixels = icons[index].Argb.Length == width * height ? icons[index].Argb : new int[width * height];
            frames.Add(new ImageFrame(pixels, delayMs));
        }

        return new DecodedImage(width, height, frames, 0); // cursor animations loop forever
    }

    private static bool Tag(ReadOnlySpan<byte> four, string tag)
        => four.Length >= 4 && four[0] == (byte)tag[0] && four[1] == (byte)tag[1] && four[2] == (byte)tag[2] && four[3] == (byte)tag[3];

    /// <summary>Concatenates a GIF's length-prefixed data sub-blocks up to the zero terminator.</summary>
    private static byte[] GatherSubBlocks(ReadOnlySpan<byte> data, ref int pos)
    {
        var buffer = new List<byte>();
        while (pos < data.Length)
        {
            var length = data[pos++];
            if (length == 0)
                break;

            if (pos + length > data.Length)
                length = (byte)(data.Length - pos);

            for (var i = 0; i < length; ++i)
                buffer.Add(data[pos + i]);

            pos += length;
        }

        return buffer.ToArray();
    }

    /// <summary>Skips a GIF sub-block chain up to (and past) the zero terminator.</summary>
    private static void SkipSubBlocks(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length)
        {
            var length = data[pos++];
            if (length == 0)
                break;

            pos += length;
        }
    }

    /// <summary>Inflates one GIF image's LZW-compressed colour indices.</summary>
    private static int[] GifLzwDecode(ReadOnlySpan<byte> input, int minCodeSize, int pixelCount)
    {
        const int MaxCodes = 4096;
        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;
        var available = clearCode + 2;
        var codeSize = minCodeSize + 1;
        var codeMask = (1 << codeSize) - 1;

        var prefix = new int[MaxCodes];
        var suffix = new int[MaxCodes];
        var pixelStack = new int[MaxCodes + 1];
        for (var code = 0; code < clearCode; ++code)
            suffix[code] = code;

        var output = new int[pixelCount];
        var oldCode = -1;
        var first = 0;
        var top = 0;
        var datum = 0;
        var bits = 0;
        var inPos = 0;

        for (var pixel = 0; pixel < pixelCount;)
        {
            if (top == 0)
            {
                while (bits < codeSize)
                {
                    if (inPos >= input.Length)
                        return output; // truncated: the rest stays transparent index 0

                    datum |= input[inPos++] << bits;
                    bits += 8;
                }

                var code = datum & codeMask;
                datum >>= codeSize;
                bits -= codeSize;

                if (code == endCode)
                    return output;

                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    codeMask = (1 << codeSize) - 1;
                    available = clearCode + 2;
                    oldCode = -1;
                    continue;
                }

                if (oldCode == -1)
                {
                    pixelStack[top++] = suffix[code];
                    oldCode = code;
                    first = code;
                }
                else
                {
                    var inCode = code;
                    if (code >= available)
                    {
                        pixelStack[top++] = first;
                        code = oldCode;
                    }

                    while (code >= clearCode)
                    {
                        pixelStack[top++] = suffix[code];
                        code = prefix[code];
                    }

                    first = suffix[code];
                    pixelStack[top++] = first;
                    if (available < MaxCodes)
                    {
                        prefix[available] = oldCode;
                        suffix[available] = first;
                        ++available;
                        if ((available & codeMask) == 0 && available < MaxCodes)
                        {
                            ++codeSize;
                            codeMask += available;
                        }
                    }

                    oldCode = inCode;
                }
            }

            output[pixel++] = pixelStack[--top];
        }

        return output;
    }

    /// <summary>Composites one GIF frame's indices onto the logical-screen canvas, honouring interlace
    /// and the transparent colour index.</summary>
    private static void PaintGifFrame(int[] canvas, int screenWidth, int screenHeight, int[] indices, ReadOnlySpan<byte> table, int left, int top, int frameWidth, int frameHeight, bool interlaced, int transparentIndex)
    {
        var tableCount = table.Length / 3;
        for (var row = 0; row < frameHeight; ++row)
        {
            var targetRow = top + (interlaced ? InterlacedRow(row, frameHeight) : row);
            if (targetRow < 0 || targetRow >= screenHeight)
                continue;

            for (var col = 0; col < frameWidth; ++col)
            {
                var index = indices[(row * frameWidth) + col];
                if (index == transparentIndex)
                    continue;

                var targetCol = left + col;
                if (targetCol < 0 || targetCol >= screenWidth)
                    continue;

                var entry = index * 3;
                canvas[(targetRow * screenWidth) + targetCol] = index < tableCount
                    ? Argb(0xFF, table[entry], table[entry + 1], table[entry + 2])
                    : 0;
            }
        }
    }

    /// <summary>Maps an interlaced GIF's source row to its screen row across the four passes.</summary>
    private static int InterlacedRow(int row, int height)
    {
        var pass1 = (height + 7) / 8;
        var pass2 = (height + 3) / 8;
        var pass3 = (height + 1) / 4;
        if (row < pass1)
            return row * 8;

        row -= pass1;
        if (row < pass2)
            return 4 + (row * 8);

        row -= pass2;
        if (row < pass3)
            return 2 + (row * 4);

        row -= pass3;
        return 1 + (row * 2);
    }

    /// <summary>Clears a rectangle of the canvas to transparent (GIF disposal method 2).</summary>
    private static void ClearArea(int[] canvas, int screenWidth, int screenHeight, int left, int top, int width, int height)
    {
        for (var row = 0; row < height; ++row)
        {
            var targetRow = top + row;
            if (targetRow < 0 || targetRow >= screenHeight)
                continue;

            for (var col = 0; col < width; ++col)
            {
                var targetCol = left + col;
                if (targetCol >= 0 && targetCol < screenWidth)
                    canvas[(targetRow * screenWidth) + targetCol] = 0;
            }
        }
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
