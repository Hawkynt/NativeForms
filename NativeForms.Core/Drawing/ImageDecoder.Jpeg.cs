namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// The JPEG half of <see cref="ImageDecoder"/>: baseline and progressive DCT at 8-bit precision, in
/// grayscale or three components, with any sampling factors and restart intervals (PRD §14).
/// </summary>
/// <remarks>
/// <para>
/// Decoding is one forward pass over the markers. Tables accumulate as they are met and each scan is
/// entropy-decoded where it is found, which is what a progressive file needs anyway — its scans are
/// interleaved with the <c>DHT</c> segments that serve them, so a parse-everything-then-decode split
/// would have to keep every table version alive to get the same answer.
/// </para>
/// <para>
/// Coefficients live in one flat <see cref="short"/> array per component rather than an object per
/// 8×8 block: a 12-megapixel photograph is about 200,000 blocks, and an object apiece is 200,000
/// allocations and headers to pay for a fixed-size buffer. The inverse DCT keeps its workspace on the
/// stack for the same reason, so the whole transform allocates nothing per block.
/// </para>
/// <para>
/// Not decoded, and rejected rather than approximated: arithmetic coding, lossless and hierarchical
/// modes, 12-bit samples, and the four-component CMYK/YCCK files an image setter produces. Each throws
/// a <see cref="FormatException"/> naming what it found.
/// </para>
/// </remarks>
public static partial class ImageDecoder
{
    /// <summary>Zigzag index → natural row-major index (ITU-T T.81 figure A.6).</summary>
    private static ReadOnlySpan<byte> JpegZigZag =>
    [
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>
    /// Decodes a JPEG (see the class summary for the supported subset) into row-major 32-bit ARGB
    /// pixels, fully opaque.
    /// </summary>
    /// <exception cref="FormatException">The data is not a JPEG or uses an unsupported feature.</exception>
    public static (int Width, int Height, int[] Argb) DecodeJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new FormatException("Not a JPEG: the file signature is missing.");

        var quantization = new int[4][];
        var dcTables = new JpegHuffman[4];
        var acTables = new JpegHuffman[4];
        JpegComponent[]? components = null;
        var width = 0;
        var height = 0;
        var maxH = 1;
        var maxV = 1;
        var mcuColumns = 0;
        var mcuRows = 0;
        var progressive = false;
        var restartInterval = 0;
        var adobeTransform = -1;

        var position = 2;
        while (position < data.Length - 1)
        {
            if (data[position] != 0xFF)
            {
                ++position;
                continue;
            }

            // Fill bytes may pad the gap before any marker.
            while (position < data.Length - 1 && data[position + 1] == 0xFF)
                ++position;

            if (position >= data.Length - 1)
                break;

            var marker = data[position + 1];
            position += 2;

            if (marker == 0xD9) // EOI
                break;

            if (marker == 0xD8 || marker == 0x00 || marker is >= 0xD0 and <= 0xD7) // SOI, stuffing, RST
                continue;

            if (position + 1 >= data.Length)
                break;

            var length = (data[position] << 8) | data[position + 1];
            if (length < 2 || position + length > data.Length)
                throw new FormatException("Truncated JPEG marker segment.");

            var body = data.Slice(position + 2, length - 2);
            switch (marker)
            {
                case 0xC0: // SOF0, baseline
                case 0xC1: // SOF1, extended sequential — same entropy coding as baseline
                case 0xC2: // SOF2, progressive
                    progressive = marker == 0xC2;
                    components = ReadJpegFrame(body, out width, out height, out maxH, out maxV, out mcuColumns, out mcuRows);
                    break;

                case 0xC4: // DHT
                    ReadJpegHuffmanTables(body, dcTables, acTables);
                    break;

                case 0xDB: // DQT
                    ReadJpegQuantizationTables(body, quantization);
                    break;

                case 0xDD: // DRI
                    if (body.Length < 2)
                        throw new FormatException("Truncated JPEG restart interval.");
                    restartInterval = (body[0] << 8) | body[1];
                    break;

                case 0xEE: // APP14, where Adobe records whether it applied a colour transform
                    if (body.Length >= 12 && body[0] == (byte)'A' && body[1] == (byte)'d' && body[2] == (byte)'o'
                        && body[3] == (byte)'b' && body[4] == (byte)'e')
                        adobeTransform = body[11];
                    break;

                case 0xDA: // SOS
                    if (components is null)
                        throw new FormatException("Not a JPEG we can read: a scan arrives before the frame header.");

                    DecodeJpegScan(
                        data,
                        position + length,
                        body,
                        components,
                        dcTables,
                        acTables,
                        mcuColumns,
                        mcuRows,
                        progressive,
                        restartInterval);

                    position = JpegEntropyEnd(data, position + length);
                    continue;

                default:
                    // Every other SOF is a mode this decoder does not implement; say which rather than
                    // producing a picture of noise. C4 is DHT, C8 is reserved and CC is arithmetic
                    // conditioning, none of them frame headers.
                    if (marker is >= 0xC3 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                        throw new FormatException(
                            $"Unsupported JPEG: marker 0xFF{marker:X2} selects arithmetic, lossless or hierarchical coding, and only baseline and progressive DCT are decoded.");
                    break;
            }

            position += length;
        }

        if (components is null)
            throw new FormatException("Not a JPEG we can read: the frame header is missing.");

        var planes = new byte[components.Length][];
        for (var i = 0; i < components.Length; ++i)
        {
            var component = components[i];
            var table = quantization[component.QuantizationTable]
                ?? throw new FormatException($"JPEG component {component.Id} names quantization table {component.QuantizationTable}, which was never defined.");

            var stride = component.BlocksWide * 8;
            var plane = new byte[stride * component.BlocksHigh * 8];
            for (var blockY = 0; blockY < component.BlocksHigh; ++blockY)
                for (var blockX = 0; blockX < component.BlocksWide; ++blockX)
                    InverseJpegDct(
                        component.Coefficients,
                        (blockY * component.BlocksWide + blockX) * 64,
                        table,
                        plane,
                        blockY * 8 * stride + blockX * 8,
                        stride);

            planes[i] = plane;
        }

        // Three components are YCbCr unless the file says otherwise: Adobe's transform byte reads 0 for
        // untransformed data, and the component ids spell RGB in files that carry no APP14 at all.
        var untransformed = components.Length == 3
            && (adobeTransform == 0
                || (components[0].Id == 'R' && components[1].Id == 'G' && components[2].Id == 'B'));

        return (width, height, JpegPlanesToArgb(components, planes, width, height, maxH, maxV, untransformed));
    }

    /// <summary>Reads a <c>SOF</c> segment and allocates the coefficient storage each component needs.</summary>
    private static JpegComponent[] ReadJpegFrame(
        ReadOnlySpan<byte> body,
        out int width,
        out int height,
        out int maxH,
        out int maxV,
        out int mcuColumns,
        out int mcuRows)
    {
        if (body.Length < 6)
            throw new FormatException("Truncated JPEG frame header.");

        if (body[0] != 8)
            throw new FormatException($"Unsupported JPEG: {body[0]}-bit samples, where only 8-bit precision is decoded.");

        height = (body[1] << 8) | body[2];
        width = (body[3] << 8) | body[4];
        if (width <= 0 || height <= 0)
            throw new FormatException("JPEG frame has zero dimensions.");

        var count = body[5];
        if (count is not (1 or 3))
            throw new FormatException($"Unsupported JPEG: {count} components, where only grayscale and three-component images are decoded.");

        if (body.Length < 6 + count * 3)
            throw new FormatException("Truncated JPEG frame header.");

        var components = new JpegComponent[count];
        maxH = 1;
        maxV = 1;
        for (var i = 0; i < count; ++i)
        {
            var at = 6 + i * 3;
            var sampling = body[at + 1];
            var component = new JpegComponent
            {
                Id = body[at],
                H = sampling >> 4,
                V = sampling & 0x0F,
                QuantizationTable = body[at + 2],
            };

            if (component.H is < 1 or > 4 || component.V is < 1 or > 4)
                throw new FormatException("Invalid JPEG sampling factors.");
            if (component.QuantizationTable > 3)
                throw new FormatException("Invalid JPEG quantization table id.");

            components[i] = component;
            maxH = Math.Max(maxH, component.H);
            maxV = Math.Max(maxV, component.V);
        }

        mcuColumns = (width + maxH * 8 - 1) / (maxH * 8);
        mcuRows = (height + maxV * 8 - 1) / (maxV * 8);

        foreach (var component in components)
        {
            // Storage covers the whole MCU grid, because an interleaved scan writes blocks past the
            // image edge to fill its last MCU.
            component.BlocksWide = mcuColumns * component.H;
            component.BlocksHigh = mcuRows * component.V;

            // A scan carrying one component alone walks that component's own block grid instead, which
            // is the smaller of the two whenever the image does not fill its last MCU (T.81 A.2.4).
            component.ScanBlocksWide = ((width * component.H + maxH - 1) / maxH + 7) / 8;
            component.ScanBlocksHigh = ((height * component.V + maxV - 1) / maxV + 7) / 8;

            component.Coefficients = new short[component.BlocksWide * component.BlocksHigh * 64];
        }

        return components;
    }

    /// <summary>Reads the one or more tables a <c>DQT</c> segment carries, in zigzag order.</summary>
    private static void ReadJpegQuantizationTables(ReadOnlySpan<byte> body, int[]?[] tables)
    {
        var at = 0;
        while (at < body.Length)
        {
            var wide = body[at] >> 4 != 0; // 16-bit entries rather than 8-bit
            var id = body[at] & 0x0F;
            ++at;

            if (id > 3)
                throw new FormatException("Invalid JPEG quantization table id.");
            if (at + (wide ? 128 : 64) > body.Length)
                throw new FormatException("Truncated JPEG quantization table.");

            var values = new int[64];
            for (var i = 0; i < 64; ++i)
                values[i] = wide ? (body[at + i * 2] << 8) | body[at + i * 2 + 1] : body[at + i];

            at += wide ? 128 : 64;
            tables[id] = values;
        }
    }

    /// <summary>Reads the one or more tables a <c>DHT</c> segment carries into their DC or AC slot.</summary>
    private static void ReadJpegHuffmanTables(ReadOnlySpan<byte> body, JpegHuffman?[] dcTables, JpegHuffman?[] acTables)
    {
        var at = 0;
        while (at < body.Length)
        {
            var alternating = body[at] >> 4 != 0; // AC rather than DC
            var id = body[at] & 0x0F;
            ++at;

            if (id > 3 || at + 16 > body.Length)
                throw new FormatException("Invalid JPEG Huffman table.");

            var counts = body.Slice(at, 16);
            var total = 0;
            foreach (var codes in counts)
                total += codes;

            at += 16;
            if (at + total > body.Length)
                throw new FormatException("Truncated JPEG Huffman table.");

            (alternating ? acTables : dcTables)[id] = JpegHuffman.Build(counts, body.Slice(at, total));
            at += total;
        }
    }

    /// <summary>Reads a <c>SOS</c> header and entropy-decodes the scan that follows it.</summary>
    private static void DecodeJpegScan(
        ReadOnlySpan<byte> data,
        int entropyStart,
        ReadOnlySpan<byte> header,
        JpegComponent[] components,
        JpegHuffman?[] dcTables,
        JpegHuffman?[] acTables,
        int mcuColumns,
        int mcuRows,
        bool progressive,
        int restartInterval)
    {
        if (header.Length < 1)
            throw new FormatException("Truncated JPEG scan header.");

        var count = header[0];
        if (count is < 1 or > 4 || header.Length < 1 + count * 2 + 3)
            throw new FormatException("Truncated JPEG scan header.");

        Span<JpegScanComponent> scan = stackalloc JpegScanComponent[count];
        for (var i = 0; i < count; ++i)
        {
            var id = header[1 + i * 2];
            var tables = header[2 + i * 2];

            var index = -1;
            for (var c = 0; c < components.Length; ++c)
                if (components[c].Id == id)
                    index = c;

            if (index < 0)
                throw new FormatException($"JPEG scan names component {id}, which the frame header never declared.");
            if ((tables >> 4) > 3 || (tables & 0x0F) > 3)
                throw new FormatException("Invalid JPEG Huffman table id in a scan header.");

            scan[i] = new(index, tables >> 4, tables & 0x0F);
        }

        var at = 1 + count * 2;
        var spectralStart = header[at];
        var spectralEnd = header[at + 1];
        var approximationHigh = header[at + 2] >> 4;
        var approximationLow = header[at + 2] & 0x0F;

        if (spectralEnd > 63 || spectralStart > spectralEnd)
            throw new FormatException("Invalid JPEG spectral selection.");

        var reader = new JpegBits(data, entropyStart);

        if (!progressive)
        {
            DecodeJpegBaselineScan(ref reader, components, scan, dcTables, acTables, mcuColumns, mcuRows, restartInterval);
            return;
        }

        if (spectralStart == 0)
        {
            DecodeJpegProgressiveDc(ref reader, components, scan, dcTables, mcuColumns, mcuRows, restartInterval, approximationHigh, approximationLow);
            return;
        }

        // Spectral selection past DC is only ever coded one component at a time (T.81 G.1.2.2).
        if (scan.Length != 1)
            throw new FormatException("Invalid JPEG: an AC progressive scan carries more than one component.");

        var component = components[scan[0].Index];
        var table = acTables[scan[0].AcTable]
            ?? throw new FormatException("JPEG scan names an AC Huffman table that was never defined.");

        if (approximationHigh == 0)
            DecodeJpegProgressiveAcFirst(ref reader, component, table, spectralStart, spectralEnd, approximationLow, restartInterval);
        else
            DecodeJpegProgressiveAcRefine(ref reader, component, table, spectralStart, spectralEnd, approximationLow, restartInterval);
    }

    /// <summary>Decodes a baseline scan, interleaved over MCUs or over one component's own blocks.</summary>
    private static void DecodeJpegBaselineScan(
        ref JpegBits reader,
        JpegComponent[] components,
        scoped ReadOnlySpan<JpegScanComponent> scan,
        JpegHuffman?[] dcTables,
        JpegHuffman?[] acTables,
        int mcuColumns,
        int mcuRows,
        int restartInterval)
    {
        foreach (var component in components)
            component.DcPrediction = 0;

        if (scan.Length == 1)
        {
            var single = components[scan[0].Index];
            var dcTable = dcTables[scan[0].DcTable] ?? throw new FormatException("JPEG scan names a DC Huffman table that was never defined.");
            var acTable = acTables[scan[0].AcTable] ?? throw new FormatException("JPEG scan names an AC Huffman table that was never defined.");

            var block = 0;
            for (var y = 0; y < single.ScanBlocksHigh; ++y)
                for (var x = 0; x < single.ScanBlocksWide; ++x, ++block)
                {
                    if (restartInterval > 0 && block > 0 && block % restartInterval == 0)
                    {
                        reader.Restart();
                        single.DcPrediction = 0;
                    }

                    DecodeJpegBaselineBlock(ref reader, single, dcTable, acTable, x, y);
                }

            return;
        }

        var mcu = 0;
        for (var row = 0; row < mcuRows; ++row)
            for (var column = 0; column < mcuColumns; ++column, ++mcu)
            {
                if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
                {
                    reader.Restart();
                    foreach (var component in components)
                        component.DcPrediction = 0;
                }

                foreach (var entry in scan)
                {
                    var component = components[entry.Index];
                    var dcTable = dcTables[entry.DcTable] ?? throw new FormatException("JPEG scan names a DC Huffman table that was never defined.");
                    var acTable = acTables[entry.AcTable] ?? throw new FormatException("JPEG scan names an AC Huffman table that was never defined.");

                    for (var v = 0; v < component.V; ++v)
                        for (var h = 0; h < component.H; ++h)
                            DecodeJpegBaselineBlock(
                                ref reader,
                                component,
                                dcTable,
                                acTable,
                                column * component.H + h,
                                row * component.V + v);
                }
            }
    }

    /// <summary>Decodes one 8×8 block's DC difference and AC run/size pairs.</summary>
    private static void DecodeJpegBaselineBlock(
        ref JpegBits reader,
        JpegComponent component,
        JpegHuffman dcTable,
        JpegHuffman acTable,
        int blockX,
        int blockY)
    {
        if (blockX >= component.BlocksWide || blockY >= component.BlocksHigh)
            return;

        var offset = (blockY * component.BlocksWide + blockX) * 64;

        var category = reader.DecodeHuffman(dcTable);
        if (category != 0)
            component.DcPrediction += reader.Receive(category);

        component.Coefficients[offset] = (short)component.DcPrediction;
        DecodeJpegAcCoefficients(ref reader, acTable, component.Coefficients, offset);
    }

    /// <summary>Decodes coefficients 1..63 of one block as run/size pairs terminated by an end-of-block.</summary>
    private static void DecodeJpegAcCoefficients(ref JpegBits reader, JpegHuffman table, short[] coefficients, int offset)
    {
        for (var k = 1; k <= 63;)
        {
            var symbol = reader.DecodeHuffman(table);
            var run = symbol >> 4;
            var size = symbol & 0x0F;

            if (size == 0)
            {
                if (run != 15)
                    break; // end of block: everything left is zero

                k += 16; // ZRL, sixteen zeroes
                continue;
            }

            k += run;
            if (k > 63)
                break;

            coefficients[offset + k] = (short)reader.Receive(size);
            ++k;
        }
    }

    /// <summary>Decodes a progressive DC scan, either its first pass or one refinement bit per block.</summary>
    private static void DecodeJpegProgressiveDc(
        ref JpegBits reader,
        JpegComponent[] components,
        scoped ReadOnlySpan<JpegScanComponent> scan,
        JpegHuffman?[] dcTables,
        int mcuColumns,
        int mcuRows,
        int restartInterval,
        int approximationHigh,
        int approximationLow)
    {
        foreach (var component in components)
            component.DcPrediction = 0;

        if (scan.Length == 1)
        {
            var single = components[scan[0].Index];
            var block = 0;
            for (var y = 0; y < single.ScanBlocksHigh; ++y)
                for (var x = 0; x < single.ScanBlocksWide; ++x, ++block)
                {
                    if (restartInterval > 0 && block > 0 && block % restartInterval == 0)
                    {
                        reader.Restart();
                        single.DcPrediction = 0;
                    }

                    DecodeJpegProgressiveDcBlock(ref reader, single, dcTables, scan[0], x, y, approximationHigh, approximationLow);
                }

            return;
        }

        var mcu = 0;
        for (var row = 0; row < mcuRows; ++row)
            for (var column = 0; column < mcuColumns; ++column, ++mcu)
            {
                if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
                {
                    reader.Restart();
                    foreach (var component in components)
                        component.DcPrediction = 0;
                }

                foreach (var entry in scan)
                {
                    var component = components[entry.Index];
                    for (var v = 0; v < component.V; ++v)
                        for (var h = 0; h < component.H; ++h)
                            DecodeJpegProgressiveDcBlock(
                                ref reader,
                                component,
                                dcTables,
                                entry,
                                column * component.H + h,
                                row * component.V + v,
                                approximationHigh,
                                approximationLow);
                }
            }
    }

    /// <summary>Writes or refines one block's DC coefficient.</summary>
    private static void DecodeJpegProgressiveDcBlock(
        ref JpegBits reader,
        JpegComponent component,
        JpegHuffman?[] dcTables,
        JpegScanComponent entry,
        int blockX,
        int blockY,
        int approximationHigh,
        int approximationLow)
    {
        if (blockX >= component.BlocksWide || blockY >= component.BlocksHigh)
            return;

        var offset = (blockY * component.BlocksWide + blockX) * 64;

        if (approximationHigh != 0)
        {
            // A refinement pass carries exactly one more bit of the value, and no Huffman coding at all.
            component.Coefficients[offset] |= (short)(reader.ReadBit() << approximationLow);
            return;
        }

        var table = dcTables[entry.DcTable] ?? throw new FormatException("JPEG scan names a DC Huffman table that was never defined.");
        var category = reader.DecodeHuffman(table);
        if (category != 0)
            component.DcPrediction += reader.Receive(category);

        component.Coefficients[offset] = (short)(component.DcPrediction << approximationLow);
    }

    /// <summary>Decodes the first pass of a progressive AC band, which introduces the coefficients.</summary>
    private static void DecodeJpegProgressiveAcFirst(
        ref JpegBits reader,
        JpegComponent component,
        JpegHuffman table,
        int spectralStart,
        int spectralEnd,
        int approximationLow,
        int restartInterval)
    {
        var coefficients = component.Coefficients;
        var endOfBandRun = 0;
        var block = 0;

        for (var y = 0; y < component.ScanBlocksHigh; ++y)
            for (var x = 0; x < component.ScanBlocksWide; ++x, ++block)
            {
                if (restartInterval > 0 && block > 0 && block % restartInterval == 0)
                {
                    reader.Restart();
                    endOfBandRun = 0;
                }

                if (endOfBandRun > 0)
                {
                    --endOfBandRun;
                    continue;
                }

                var offset = (y * component.BlocksWide + x) * 64;
                for (var k = spectralStart; k <= spectralEnd;)
                {
                    var symbol = reader.DecodeHuffman(table);
                    var run = symbol >> 4;
                    var size = symbol & 0x0F;

                    if (size == 0)
                    {
                        if (run == 15)
                        {
                            k += 16; // ZRL
                            continue;
                        }

                        // An end-of-band run covers 2^run blocks plus the appended bits, this one included.
                        endOfBandRun = (1 << run) - 1;
                        if (run > 0)
                            endOfBandRun += reader.ReadBits(run);

                        break;
                    }

                    k += run;
                    if (k > spectralEnd)
                        break;

                    coefficients[offset + k] = (short)(reader.Receive(size) << approximationLow);
                    ++k;
                }
            }
    }

    /// <summary>
    /// Decodes a refinement pass of a progressive AC band, which appends one bit to coefficients that
    /// are already non-zero and introduces new ones at ±1 in this pass's place value.
    /// </summary>
    private static void DecodeJpegProgressiveAcRefine(
        ref JpegBits reader,
        JpegComponent component,
        JpegHuffman table,
        int spectralStart,
        int spectralEnd,
        int approximationLow,
        int restartInterval)
    {
        var coefficients = component.Coefficients;
        var positive = 1 << approximationLow;
        var negative = -1 << approximationLow;
        var endOfBandRun = 0;
        var block = 0;

        for (var y = 0; y < component.ScanBlocksHigh; ++y)
            for (var x = 0; x < component.ScanBlocksWide; ++x, ++block)
            {
                if (restartInterval > 0 && block > 0 && block % restartInterval == 0)
                {
                    reader.Restart();
                    endOfBandRun = 0;
                }

                var offset = (y * component.BlocksWide + x) * 64;
                var k = spectralStart;

                if (endOfBandRun == 0)
                    while (k <= spectralEnd)
                    {
                        var symbol = reader.DecodeHuffman(table);
                        var run = symbol >> 4;
                        var size = symbol & 0x0F;
                        var introduced = 0;

                        if (size == 0)
                        {
                            if (run != 15)
                            {
                                // Unlike the first pass, this run counts the current block as it ends,
                                // below, so it is not pre-decremented here.
                                endOfBandRun = 1 << run;
                                if (run > 0)
                                    endOfBandRun += reader.ReadBits(run);

                                break;
                            }

                            // ZRL: skip sixteen zero-valued coefficients, refining any non-zero ones met.
                        }
                        else
                            introduced = reader.ReadBit() != 0 ? positive : negative;

                        // Walk forward over `run` zeroes, appending a correction bit to every non-zero
                        // coefficient passed on the way — the refinement bits are interleaved with the run.
                        while (k <= spectralEnd)
                        {
                            if (coefficients[offset + k] != 0)
                                RefineJpegCoefficient(ref reader, coefficients, offset + k, positive, negative);
                            else if (run == 0)
                                break;
                            else
                                --run;

                            ++k;
                        }

                        if (introduced != 0 && k <= spectralEnd)
                            coefficients[offset + k] = (short)introduced;

                        ++k;
                    }

                if (endOfBandRun <= 0)
                    continue;

                // Inside an end-of-band run nothing new appears; only the coefficients that are already
                // non-zero take a correction bit.
                for (; k <= spectralEnd; ++k)
                    if (coefficients[offset + k] != 0)
                        RefineJpegCoefficient(ref reader, coefficients, offset + k, positive, negative);

                --endOfBandRun;
            }
    }

    /// <summary>Appends one correction bit to an already non-zero coefficient, growing its magnitude.</summary>
    private static void RefineJpegCoefficient(ref JpegBits reader, short[] coefficients, int index, int positive, int negative)
    {
        if (reader.ReadBit() == 0)
            return;

        var value = coefficients[index];
        if ((value & positive) != 0)
            return; // this place value is already set

        coefficients[index] = (short)(value + (value >= 0 ? positive : negative));
    }

    /// <summary>
    /// Finds where a scan's entropy-coded data ends, which is the first marker that is neither a
    /// stuffed zero nor a restart.
    /// </summary>
    private static int JpegEntropyEnd(ReadOnlySpan<byte> data, int start)
    {
        var position = start;
        while (position < data.Length - 1)
        {
            if (data[position] != 0xFF)
            {
                ++position;
                continue;
            }

            var next = data[position + 1];
            if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            if (next == 0xFF)
            {
                ++position; // fill byte
                continue;
            }

            return position;
        }

        return data.Length;
    }

    /// <summary>
    /// Dequantizes one block, transforms it back to the spatial domain and writes the level-shifted,
    /// clamped samples into a component plane.
    /// </summary>
    /// <remarks>
    /// The AAN integer transform of libjpeg's <c>jidctint.c</c>: two passes of eight one-dimensional
    /// transforms in 13-bit fixed point, with the all-zero-AC shortcut that makes flat blocks — most of
    /// them, in a photograph — cost one multiply instead of a full butterfly.
    /// </remarks>
    private static void InverseJpegDct(short[] coefficients, int offset, int[] quantization, byte[] plane, int planeOffset, int stride)
    {
        const int constantBits = 13;
        const int passBits = 2;
        const int fix0_298631336 = 2446;
        const int fix0_390180644 = 3196;
        const int fix0_541196100 = 4433;
        const int fix0_765366865 = 6270;
        const int fix0_899976223 = 7373;
        const int fix1_175875602 = 9633;
        const int fix1_501321110 = 12299;
        const int fix1_847759065 = 15137;
        const int fix1_961570560 = 16069;
        const int fix2_053119869 = 16819;
        const int fix2_562915447 = 20995;
        const int fix3_072711026 = 25172;

        Span<int> workspace = stackalloc int[64];
        for (var i = 0; i < 64; ++i)
            workspace[JpegZigZag[i]] = coefficients[offset + i] * quantization[i];

        // Pass one: the columns.
        for (var column = 0; column < 8; ++column)
        {
            if (workspace[column + 8] == 0 && workspace[column + 16] == 0 && workspace[column + 24] == 0
                && workspace[column + 32] == 0 && workspace[column + 40] == 0 && workspace[column + 48] == 0
                && workspace[column + 56] == 0)
            {
                var flat = workspace[column] << passBits;
                for (var row = 0; row < 8; ++row)
                    workspace[column + row * 8] = flat;

                continue;
            }

            var z2 = workspace[column + 16];
            var z3 = workspace[column + 48];
            var z1 = (z2 + z3) * fix0_541196100;
            var tmp2 = z1 - z3 * fix1_847759065;
            var tmp3 = z1 + z2 * fix0_765366865;

            z2 = workspace[column];
            z3 = workspace[column + 32];
            var tmp0 = (z2 + z3) << constantBits;
            var tmp1 = (z2 - z3) << constantBits;

            var tmp10 = tmp0 + tmp3;
            var tmp13 = tmp0 - tmp3;
            var tmp11 = tmp1 + tmp2;
            var tmp12 = tmp1 - tmp2;

            tmp0 = workspace[column + 56];
            tmp1 = workspace[column + 40];
            tmp2 = workspace[column + 24];
            tmp3 = workspace[column + 8];

            z1 = tmp0 + tmp3;
            z2 = tmp1 + tmp2;
            z3 = tmp0 + tmp2;
            var z4 = tmp1 + tmp3;
            var z5 = (z3 + z4) * fix1_175875602;

            tmp0 *= fix0_298631336;
            tmp1 *= fix2_053119869;
            tmp2 *= fix3_072711026;
            tmp3 *= fix1_501321110;
            z1 *= -fix0_899976223;
            z2 *= -fix2_562915447;
            z3 = -z3 * fix1_961570560 + z5;
            z4 = -z4 * fix0_390180644 + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;

            const int columnShift = constantBits - passBits;
            const int columnRound = 1 << (columnShift - 1);

            workspace[column] = (tmp10 + tmp3 + columnRound) >> columnShift;
            workspace[column + 56] = (tmp10 - tmp3 + columnRound) >> columnShift;
            workspace[column + 8] = (tmp11 + tmp2 + columnRound) >> columnShift;
            workspace[column + 48] = (tmp11 - tmp2 + columnRound) >> columnShift;
            workspace[column + 16] = (tmp12 + tmp1 + columnRound) >> columnShift;
            workspace[column + 40] = (tmp12 - tmp1 + columnRound) >> columnShift;
            workspace[column + 24] = (tmp13 + tmp0 + columnRound) >> columnShift;
            workspace[column + 32] = (tmp13 - tmp0 + columnRound) >> columnShift;
        }

        // Pass two: the rows, straight out to clamped bytes.
        const int rowShift = constantBits + passBits + 3;
        const int rowRound = 1 << (rowShift - 1);

        for (var row = 0; row < 8; ++row)
        {
            var source = row * 8;
            var target = planeOffset + row * stride;

            if (workspace[source + 1] == 0 && workspace[source + 2] == 0 && workspace[source + 3] == 0
                && workspace[source + 4] == 0 && workspace[source + 5] == 0 && workspace[source + 6] == 0
                && workspace[source + 7] == 0)
            {
                var flat = JpegSample((workspace[source] + (1 << (passBits + 2))) >> (passBits + 3));
                for (var column = 0; column < 8; ++column)
                    plane[target + column] = flat;

                continue;
            }

            var z2 = workspace[source + 2];
            var z3 = workspace[source + 6];
            var z1 = (z2 + z3) * fix0_541196100;
            var tmp2 = z1 - z3 * fix1_847759065;
            var tmp3 = z1 + z2 * fix0_765366865;

            z2 = workspace[source];
            z3 = workspace[source + 4];
            var tmp0 = (z2 + z3) << constantBits;
            var tmp1 = (z2 - z3) << constantBits;

            var tmp10 = tmp0 + tmp3;
            var tmp13 = tmp0 - tmp3;
            var tmp11 = tmp1 + tmp2;
            var tmp12 = tmp1 - tmp2;

            tmp0 = workspace[source + 7];
            tmp1 = workspace[source + 5];
            tmp2 = workspace[source + 3];
            tmp3 = workspace[source + 1];

            z1 = tmp0 + tmp3;
            z2 = tmp1 + tmp2;
            z3 = tmp0 + tmp2;
            var z4 = tmp1 + tmp3;
            var z5 = (z3 + z4) * fix1_175875602;

            tmp0 *= fix0_298631336;
            tmp1 *= fix2_053119869;
            tmp2 *= fix3_072711026;
            tmp3 *= fix1_501321110;
            z1 *= -fix0_899976223;
            z2 *= -fix2_562915447;
            z3 = -z3 * fix1_961570560 + z5;
            z4 = -z4 * fix0_390180644 + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;

            plane[target] = JpegSample((tmp10 + tmp3 + rowRound) >> rowShift);
            plane[target + 7] = JpegSample((tmp10 - tmp3 + rowRound) >> rowShift);
            plane[target + 1] = JpegSample((tmp11 + tmp2 + rowRound) >> rowShift);
            plane[target + 6] = JpegSample((tmp11 - tmp2 + rowRound) >> rowShift);
            plane[target + 2] = JpegSample((tmp12 + tmp1 + rowRound) >> rowShift);
            plane[target + 5] = JpegSample((tmp12 - tmp1 + rowRound) >> rowShift);
            plane[target + 3] = JpegSample((tmp13 + tmp0 + rowRound) >> rowShift);
            plane[target + 4] = JpegSample((tmp13 - tmp0 + rowRound) >> rowShift);
        }
    }

    /// <summary>Undoes the encoder's level shift and clamps one transformed sample into a byte.</summary>
    private static byte JpegSample(int value) => JpegClamp(value + 128);

    /// <summary>Clamps a channel value into a byte.</summary>
    private static byte JpegClamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    /// <summary>
    /// Samples the component planes at every output pixel and converts to ARGB, upsampling any
    /// subsampled component by taking the sample its own grid covers.
    /// </summary>
    private static int[] JpegPlanesToArgb(
        JpegComponent[] components,
        byte[][] planes,
        int width,
        int height,
        int maxH,
        int maxV,
        bool untransformed)
    {
        var argb = new int[width * height];

        if (components.Length == 1)
        {
            var stride = components[0].BlocksWide * 8;
            var plane = planes[0];
            for (var y = 0; y < height; ++y)
            {
                var source = y * stride;
                var target = y * width;
                for (var x = 0; x < width; ++x)
                {
                    int gray = plane[source + x];
                    argb[target + x] = Argb(0xFF, gray, gray, gray);
                }
            }

            return argb;
        }

        var first = components[0];
        var second = components[1];
        var third = components[2];
        var firstStride = first.BlocksWide * 8;
        var secondStride = second.BlocksWide * 8;
        var thirdStride = third.BlocksWide * 8;

        for (var y = 0; y < height; ++y)
        {
            var firstRow = y * first.V / maxV * firstStride;
            var secondRow = y * second.V / maxV * secondStride;
            var thirdRow = y * third.V / maxV * thirdStride;
            var target = y * width;

            for (var x = 0; x < width; ++x)
            {
                int a = planes[0][firstRow + x * first.H / maxH];
                int b = planes[1][secondRow + x * second.H / maxH];
                int c = planes[2][thirdRow + x * third.H / maxH];

                if (untransformed)
                {
                    argb[target + x] = Argb(0xFF, a, b, c);
                    continue;
                }

                // ITU-R BT.601, in 16-bit fixed point.
                var blueDifference = b - 128;
                var redDifference = c - 128;
                argb[target + x] = Argb(
                    0xFF,
                    JpegClamp(a + ((91881 * redDifference + 32768) >> 16)),
                    JpegClamp(a - ((22554 * blueDifference + 46802 * redDifference + 32768) >> 16)),
                    JpegClamp(a + ((116130 * blueDifference + 32768) >> 16)));
            }
        }

        return argb;
    }

    /// <summary>One component of a frame, and the coefficients decoded for it.</summary>
    private sealed class JpegComponent
    {
        /// <summary>The component's identifier, matched by a scan header against this frame.</summary>
        public byte Id { get; init; }

        /// <summary>The horizontal sampling factor, relative to the frame's largest.</summary>
        public int H { get; init; }

        /// <summary>The vertical sampling factor, relative to the frame's largest.</summary>
        public int V { get; init; }

        /// <summary>Which of the four quantization tables dequantizes this component.</summary>
        public int QuantizationTable { get; init; }

        /// <summary>The allocated block grid, which covers whole MCUs.</summary>
        public int BlocksWide { get; set; }

        /// <inheritdoc cref="BlocksWide"/>
        public int BlocksHigh { get; set; }

        /// <summary>The block grid a scan carrying this component alone walks (T.81 A.2.4).</summary>
        public int ScanBlocksWide { get; set; }

        /// <inheritdoc cref="ScanBlocksWide"/>
        public int ScanBlocksHigh { get; set; }

        /// <summary>Every block's 64 coefficients in zigzag order, laid end to end row-major.</summary>
        public short[] Coefficients { get; set; } = [];

        /// <summary>The running DC predictor, which a restart marker resets.</summary>
        public int DcPrediction { get; set; }
    }

    /// <summary>One entry of a scan header: which component, and the tables it is coded with.</summary>
    private readonly record struct JpegScanComponent(int Index, int DcTable, int AcTable);

    /// <summary>A Huffman table in decode form: the canonical code bounds plus an eight-bit fast path.</summary>
    private sealed class JpegHuffman
    {
        /// <summary>The largest code of each length, or -1 where no code has that length.</summary>
        private readonly int[] _maxCode = new int[17];

        /// <summary>What to add to a code of each length to index <see cref="_values"/>.</summary>
        private readonly int[] _valueOffset = new int[17];

        /// <summary>Symbols ordered by code length, as the table declares them.</summary>
        private byte[] _values = [];

        /// <summary>
        /// Symbol and length packed as <c>(symbol &lt;&lt; 4) | length</c> for every eight-bit prefix,
        /// zero where no code of eight bits or fewer matches.
        /// </summary>
        private readonly int[] _lookup = new int[256];

        /// <summary>Builds the decode tables from a segment's code counts and symbols (T.81 figure F.15).</summary>
        public static JpegHuffman Build(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            var table = new JpegHuffman { _values = values.ToArray() };

            var code = 0;
            var index = 0;
            for (var length = 1; length <= 16; ++length)
            {
                var count = counts[length - 1];
                if (count == 0)
                    table._maxCode[length] = -1;
                else
                {
                    table._valueOffset[length] = index - code;
                    table._maxCode[length] = code + count - 1;
                    index += count;
                    code += count;
                }

                code <<= 1;
            }

            // Most symbols are coded in eight bits or fewer, so every byte carrying such a code answers
            // in one indexed read instead of a walk down the length table.
            code = 0;
            index = 0;
            for (var length = 1; length <= 16; ++length)
            {
                for (var i = 0; i < counts[length - 1]; ++i, ++index, ++code)
                {
                    if (length > 8 || index >= table._values.Length)
                        continue;

                    var prefix = code << (8 - length);
                    for (var fill = 1 << (8 - length); fill > 0; --fill)
                        table._lookup[prefix + fill - 1] = (table._values[index] << 4) | length;
                }

                code <<= 1;
            }

            return table;
        }

        /// <summary>The eight-bit fast-path entry for a prefix, or zero when the code is longer.</summary>
        public int Lookup(int prefix) => _lookup[prefix];

        /// <summary>The symbol a code of the given length decodes to, or zero when the table has none.</summary>
        public int Resolve(int code, int length)
        {
            var index = code + _valueOffset[length];
            return (uint)index < (uint)_values.Length ? _values[index] : 0;
        }

        /// <summary>Whether a code of the given length is within that length's range.</summary>
        public bool Fits(int code, int length) => code <= _maxCode[length];
    }

    /// <summary>
    /// An MSB-first bit reader over entropy-coded data, which unstuffs the zero byte that follows a
    /// literal <c>0xFF</c> sample and stops at the marker that ends the scan.
    /// </summary>
    private ref struct JpegBits(ReadOnlySpan<byte> data, int position)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position = position;
        private int _buffer;
        private int _count;

        /// <summary>Reads the next <paramref name="count"/> bits as an unsigned value.</summary>
        public int ReadBits(int count)
        {
            while (_count < count)
            {
                _buffer = (_buffer << 8) | this.NextByte();
                _count += 8;
            }

            _count -= count;
            return (_buffer >> _count) & ((1 << count) - 1);
        }

        /// <summary>Reads one bit.</summary>
        public int ReadBit() => this.ReadBits(1);

        /// <summary>Reads a signed value of the given bit length — T.81's EXTEND.</summary>
        public int Receive(int length)
        {
            if (length == 0)
                return 0;

            var value = this.ReadBits(length);
            return value < 1 << (length - 1) ? value + (-1 << length) + 1 : value;
        }

        /// <summary>Decodes one Huffman symbol, by the eight-bit lookup where it can and bit by bit otherwise.</summary>
        public int DecodeHuffman(JpegHuffman table)
        {
            // Filling to eight bits can cross the marker that ends the scan, which leaves the buffer
            // holding real bits and the reader latched. Decoding has to go on from what is buffered:
            // giving up here would discard the last symbol of the scan, and in an interleaved DC scan
            // that is a whole block's differential — the error then rides the predictor to the edge of
            // the image. Past the real bits the reader pads with zeroes, and every consumer loop is
            // bounded by its block or coefficient count, so nothing spins.
            while (_count < 8)
            {
                _buffer = (_buffer << 8) | this.NextByte();
                _count += 8;
            }

            if (_count >= 8)
            {
                var entry = table.Lookup((_buffer >> (_count - 8)) & 0xFF);
                if ((entry & 0x0F) != 0)
                {
                    _count -= entry & 0x0F;
                    return entry >> 4;
                }
            }

            var code = this.ReadBit();
            for (var length = 1; length <= 16; ++length)
            {
                if (table.Fits(code, length))
                    return table.Resolve(code, length);

                code = (code << 1) | this.ReadBit();
            }

            return 0;
        }

        /// <summary>
        /// Realigns on the byte boundary and consumes the restart marker that separates two intervals,
        /// reporting whether one was there.
        /// </summary>
        /// <remarks>
        /// Stepping over the marker is the point. The reader stops on it and pads with zeroes from there,
        /// so an interval boundary that is never consumed leaves every interval after the first decoding
        /// as nothing at all.
        /// </remarks>
        public bool Restart()
        {
            _count = 0;
            _buffer = 0;

            while (_position < _data.Length - 1 && _data[_position] == 0xFF && _data[_position + 1] == 0xFF)
                ++_position;

            if (_position + 1 >= _data.Length || _data[_position] != 0xFF || _data[_position + 1] is < 0xD0 or > 0xD7)
                return false;

            _position += 2;
            return true;
        }

        /// <summary>
        /// The next byte of entropy data, unstuffing as it goes. A <c>0xFF</c> that is not followed by
        /// the stuffed zero starts a marker, which ends the scan; the reader stops on it and pads with
        /// zeroes from there, so the marker survives for <see cref="Restart"/> to step over.
        /// </summary>
        private byte NextByte()
        {
            if (_position >= _data.Length)
                return 0;

            var value = _data[_position++];
            if (value != 0xFF)
                return value;

            if (_position < _data.Length && _data[_position] == 0x00)
            {
                ++_position;
                return 0xFF;
            }

            --_position;
            return 0;
        }
    }
}
