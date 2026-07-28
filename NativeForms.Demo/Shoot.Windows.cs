using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Writes a PNG of a top-level window on Win32, from inside the process that owns it — the Windows
/// counterpart of <see cref="Capture"/>, which asks GTK widgets to paint themselves.
/// </summary>
/// <remarks>
/// <para>
/// The reason for capturing in-process is the same on both platforms and stated once in
/// <see cref="Capture"/>: an external screenshot tool is not reliably available where this has to
/// run. On a CI runner there is no desktop session to point one at, and under wine there is neither
/// a compositor portal nor, usually, an ImageMagick built with its X11 delegate.
/// </para>
/// <para>
/// The route matters and the documented one is a trap. <c>PrintWindow</c> is what reaches child
/// controls on Windows, but wine returns success from it having drawn nothing — so every route is
/// tried and the first that yields more than a single flat colour wins, with the winner named in the
/// log. A capture that claims to have worked and is blank is worse than no capture: it turns a
/// missing feature into a passing check.
/// </para>
/// </remarks>
internal static unsafe partial class ShootWindows
{
    private const uint _PwRenderFullContent = 0x00000002;
    private const uint _SrcCopy = 0x00CC0020;
    private const uint _DibRgbColors = 0;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, BITMAPINFO* info, uint usage, out byte* bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int w, int h, nint source, int sx, int sy, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER header; public uint colour; }

    /// <summary>
    /// Captures the window with the given title to a PNG, returning its size, or <see langword="null"/>
    /// when no route produced pixels.
    /// </summary>
    public static Size? Window(string windowTitle, string path)
    {
        var hwnd = FindWindowW(null, windowTitle);
        if (hwnd == 0 || !GetClientRect(hwnd, out var client))
            return null;

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
            return null;

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var info = new BITMAPINFO
        {
            header = new()
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = height,        // positive: bottom-up, which is how a BMP stores rows
                biPlanes = 1,
                biBitCount = 32,
            },
        };

        var dib = CreateDIBSection(screen, &info, _DibRgbColors, out var bits, 0, 0);
        if (dib == 0)
        {
            DeleteDC(memory);
            ReleaseDC(0, screen);
            return null;
        }

        var previous = SelectObject(memory, dib);
        var length = width * height * 4;
        var best = (byte[]?)null;
        var bestScore = 0;
        var how = (string?)null;

        // Every route is scored rather than merely tried, because "produced pixels" is too weak a
        // test to separate them. Under wine PrintWindow reports success and paints the top few rows
        // of one label — enough variation to pass a not-flat check and nothing anyone would call a
        // screenshot. Detail wins instead, and the winner is named in the log.
        new Span<byte>(bits, length).Clear();
        if (PrintWindow(hwnd, memory, _PwRenderFullContent))
            Keep(bits, length, "PrintWindow", ref best, ref bestScore, ref how);

        new Span<byte>(bits, length).Clear();
        var windowDc = GetDC(hwnd);
        if (BitBlt(memory, 0, 0, width, height, windowDc, 0, 0, _SrcCopy))
            Keep(bits, length, "BitBlt", ref best, ref bestScore, ref how);

        ReleaseDC(hwnd, windowDc);

        new Span<byte>(bits, length).Clear();
        if (PrintWindow(hwnd, memory, 0))
            Keep(bits, length, "PrintWindow(client)", ref best, ref bestScore, ref how);

        Size? result = null;
        if (best is not null)
        {
            WritePng(path, width, height, best);
            Console.WriteLine($"      capture route: {how} (detail {bestScore})");
            result = new(width, height);
        }

        SelectObject(memory, previous);
        DeleteObject(dib);
        DeleteDC(memory);
        ReleaseDC(0, screen);
        return result;
    }

    /// <summary>Keeps a route's output when it carries more detail than the best one so far.</summary>
    private static void Keep(byte* bits, int length, string route, ref byte[]? best, ref int bestScore, ref string? how)
    {
        var score = Detail(bits, length);
        if (score <= bestScore)
            return;

        best ??= new byte[length];
        new ReadOnlySpan<byte>(bits, length).CopyTo(best);
        bestScore = score;
        how = route;
    }

    /// <summary>
    /// How much detail a surface carries: the number of pixels differing from the one before them.
    /// Flat fills score zero, a half-drawn window scores a little, a real render scores a lot — which
    /// is the only distinction that matters when picking between routes that all claim success.
    /// </summary>
    private static int Detail(byte* bits, int length)
    {
        var changes = 0;
        for (var i = 4; i < length; i += 4)
            if (*(uint*)(bits + i) != *(uint*)(bits + i - 4))
                ++changes;

        return changes;
    }

    /// <summary>
    /// Writes bottom-up BGRA rows as a PNG, using the toolkit's own encoder-free path: a zlib stream
    /// of unfiltered scanlines, which <see cref="Drawing.ImageDecoder"/> reads straight back.
    /// </summary>
    private static void WritePng(string path, int width, int height, byte[] pixels)
    {
        fixed (byte* bits = pixels)
        {
        var raw = new byte[height * ((width * 3) + 1)];
        var at = 0;
        for (var y = height - 1; y >= 0; --y)   // stored bottom-up, written top-down
        {
            raw[at++] = 0;                       // filter type 0: none
            var row = bits + (y * width * 4);
            for (var x = 0; x < width; ++x)
            {
                raw[at++] = row[(x * 4) + 2];    // B G R A -> R G B
                raw[at++] = row[(x * 4) + 1];
                raw[at++] = row[x * 4];
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(file, "IHDR", Ihdr(width, height));
        WriteChunk(file, "IDAT", Zlib(raw));
        WriteChunk(file, "IEND", []);
        }
    }

    private static byte[] Ihdr(int width, int height)
    {
        var header = new byte[13];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(width));
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(height));
        header[8] = 8;    // bit depth
        header[9] = 2;    // colour type: truecolour
        return header;
    }

    /// <summary>Wraps deflated data in the two-byte zlib header and Adler-32 trailer a PNG wants.</summary>
    private static byte[] Zlib(byte[] raw)
    {
        using var buffer = new MemoryStream();
        buffer.WriteByte(0x78);
        buffer.WriteByte(0x01);
        using (var deflate = new System.IO.Compression.DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        uint a = 1, b = 0;
        foreach (var value in raw)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        var adler = (b << 16) | a;
        buffer.WriteByte((byte)(adler >> 24));
        buffer.WriteByte((byte)(adler >> 16));
        buffer.WriteByte((byte)(adler >> 8));
        buffer.WriteByte((byte)adler);
        return buffer.ToArray();
    }

    private static void WriteChunk(Stream file, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);

        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; ++i)
            typed[i] = (byte)type[i];
        data.CopyTo(typed, 4);
        file.Write(typed);

        Span<byte> crc = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        file.Write(crc);
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

        return crc ^ 0xFFFFFFFFu;
    }
}
