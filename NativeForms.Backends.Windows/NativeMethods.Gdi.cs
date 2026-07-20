using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The GDI/USER32 drawing and owner-draw surface of the backend: the entry points, structs and
/// constants the canvas peer, the native theme and the software graphics context need. Kept in a
/// separate partial so the plain windowing surface (<c>NativeMethods.cs</c>) stays focused on the
/// message loop. Everything is source-generated P/Invoke over blittable handles so the layer remains
/// trim- and AOT-safe.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // --- Additional window messages ---

    /// <summary>Sent when part of a window's client area must be painted.</summary>
    internal const uint WM_PAINT = 0x000F;

    /// <summary>Sent when the window background must be erased (we suppress it to avoid flicker).</summary>
    internal const uint WM_ERASEBKGND = 0x0014;

    /// <summary>Left mouse button pressed.</summary>
    internal const uint WM_LBUTTONDOWN = 0x0201;

    /// <summary>Left mouse button released.</summary>
    internal const uint WM_LBUTTONUP = 0x0202;

    /// <summary>Right mouse button pressed.</summary>
    internal const uint WM_RBUTTONDOWN = 0x0204;

    /// <summary>Right mouse button released.</summary>
    internal const uint WM_RBUTTONUP = 0x0205;

    /// <summary>Middle mouse button pressed.</summary>
    internal const uint WM_MBUTTONDOWN = 0x0207;

    /// <summary>Middle mouse button released.</summary>
    internal const uint WM_MBUTTONUP = 0x0208;

    /// <summary>The pointer moved over the client area.</summary>
    internal const uint WM_MOUSEMOVE = 0x0200;

    /// <summary>The mouse wheel turned.</summary>
    internal const uint WM_MOUSEWHEEL = 0x020A;

    /// <summary>Shift is held — <c>wParam</c> key-state flag of mouse messages.</summary>
    internal const uint MK_SHIFT = 0x0004;

    /// <summary>Control is held — <c>wParam</c> key-state flag of mouse messages.</summary>
    internal const uint MK_CONTROL = 0x0008;

    /// <summary>The pointer left the client area (requested via <see cref="TrackMouseEvent"/>).</summary>
    internal const uint WM_MOUSELEAVE = 0x02A3;

    /// <summary>A non-system key was pressed.</summary>
    internal const uint WM_KEYDOWN = 0x0100;

    /// <summary>A non-system key was released.</summary>
    internal const uint WM_KEYUP = 0x0101;

    /// <summary>A character was produced by a key press.</summary>
    internal const uint WM_CHAR = 0x0102;

    /// <summary>A key was pressed while Alt is held (or F10) — the mnemonic path.</summary>
    internal const uint WM_SYSKEYDOWN = 0x0104;

    /// <summary>A key was released while Alt is held.</summary>
    internal const uint WM_SYSKEYUP = 0x0105;

    /// <summary>The window gained keyboard focus.</summary>
    internal const uint WM_SETFOCUS = 0x0007;

    /// <summary>The window lost keyboard focus.</summary>
    internal const uint WM_KILLFOCUS = 0x0008;

    // --- System colors (GetSysColor indices) ---

    /// <summary>Text color on a window/field.</summary>
    internal const int COLOR_WINDOWTEXT = 8;

    /// <summary>Active window border.</summary>
    internal const int COLOR_ACTIVEBORDER = 10;

    /// <summary>Selection/highlight background.</summary>
    internal const int COLOR_HIGHLIGHT = 13;

    /// <summary>Selection/highlight text.</summary>
    internal const int COLOR_HIGHLIGHTTEXT = 14;

    /// <summary>Face of a 3-D control (same value as <c>COLOR_3DFACE</c>).</summary>
    internal const int COLOR_BTNFACE = 15;

    /// <summary>Shadow edge of a 3-D control.</summary>
    internal const int COLOR_3DSHADOW = 16;

    /// <summary>Greyed (disabled) text.</summary>
    internal const int COLOR_GRAYTEXT = 17;

    // --- System metrics (GetSystemMetrics indices) ---

    /// <summary>Width of a vertical scroll bar, in pixels.</summary>
    internal const int SM_CXVSCROLL = 0;

    /// <summary>Width of the primary screen, in pixels.</summary>
    internal const int SM_CXSCREEN = 0;

    /// <summary>Height of the primary screen, in pixels.</summary>
    internal const int SM_CYSCREEN = 1;

    // --- Device caps (GetDeviceCaps indices) ---

    /// <summary>Logical pixels per inch along the screen height (the vertical DPI).</summary>
    internal const int LOGPIXELSY = 90;

    // --- SystemParametersInfo actions ---

    /// <summary>Retrieves the non-client metrics (fonts and sizes of window frame elements).</summary>
    internal const uint SPI_GETNONCLIENTMETRICS = 0x0029;

    // --- DrawText format flags ---

    /// <summary>Align text to the left edge.</summary>
    internal const uint DT_LEFT = 0x00000000;

    /// <summary>Center text horizontally.</summary>
    internal const uint DT_CENTER = 0x00000001;

    /// <summary>Align text to the right edge.</summary>
    internal const uint DT_RIGHT = 0x00000002;

    /// <summary>Align text to the top edge (single line).</summary>
    internal const uint DT_TOP = 0x00000000;

    /// <summary>Center text vertically (single line only).</summary>
    internal const uint DT_VCENTER = 0x00000004;

    /// <summary>Align text to the bottom edge (single line only).</summary>
    internal const uint DT_BOTTOM = 0x00000008;

    /// <summary>Break text across multiple lines at word boundaries.</summary>
    internal const uint DT_WORDBREAK = 0x00000010;

    /// <summary>Lay the text out on a single line.</summary>
    internal const uint DT_SINGLELINE = 0x00000020;

    /// <summary>Do not interpret <c>&amp;</c> as an accelerator prefix.</summary>
    internal const uint DT_NOPREFIX = 0x00000800;

    /// <summary>Measure the text instead of drawing it (fills the rect with the required extent).</summary>
    internal const uint DT_CALCRECT = 0x00000400;

    // --- Background modes (SetBkMode) ---

    /// <summary>Leave the background untouched when drawing text.</summary>
    internal const int TRANSPARENT = 1;

    // --- Pen styles ---

    /// <summary>A solid pen.</summary>
    internal const int PS_SOLID = 0;

    // --- Stock objects (GetStockObject) ---

    /// <summary>The hollow (no-fill) stock brush.</summary>
    internal const int NULL_BRUSH = 5;

    /// <summary>The empty (no-draw) stock pen.</summary>
    internal const int NULL_PEN = 8;

    // --- DIB usage / compression ---

    /// <summary>The DIB color table holds literal RGB values.</summary>
    internal const uint DIB_RGB_COLORS = 0;

    /// <summary>An uncompressed RGB bitmap.</summary>
    internal const uint BI_RGB = 0;

    // --- TrackMouseEvent flags ---

    /// <summary>Post <c>WM_MOUSELEAVE</c> when the pointer leaves the tracked window.</summary>
    internal const uint TME_LEAVE = 0x00000002;

    // --- AlphaBlend blend function ---

    /// <summary>Source-over compositing operation.</summary>
    internal const byte AC_SRC_OVER = 0x00;

    /// <summary>The source bitmap carries a per-pixel alpha channel.</summary>
    internal const byte AC_SRC_ALPHA = 0x01;

    // --- Font weights / charset ---

    /// <summary>Normal font weight.</summary>
    internal const int FW_NORMAL = 400;

    /// <summary>Bold font weight.</summary>
    internal const int FW_BOLD = 700;

    /// <summary>Let the mapper choose a charset based on the face name.</summary>
    internal const uint DEFAULT_CHARSET = 1;

    // --- Virtual key codes for modifier queries ---

    /// <summary>The Shift key.</summary>
    internal const int VK_SHIFT = 0x10;

    /// <summary>The Control key.</summary>
    internal const int VK_CONTROL = 0x11;

    /// <summary>The Alt (menu) key.</summary>
    internal const int VK_MENU = 0x12;

    // --- GetWindowLongPtr / SetWindowLongPtr offsets ---

    /// <summary>The window-style (<c>WS_*</c>) long, read/written via <see cref="GetWindowLongPtrW"/>.</summary>
    internal const int GWL_STYLE = -16;

    /// <summary>The extended window-style (<c>WS_EX_*</c>) long.</summary>
    internal const int GWL_EXSTYLE = -20;

    /// <summary>The paint information filled in by <see cref="BeginPaint"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PAINTSTRUCT
    {
        /// <summary>The display device context to paint on.</summary>
        public nint hdc;

        /// <summary>Whether the background must be erased.</summary>
        public int fErase;

        /// <summary>The rectangle, in client coordinates, that needs painting.</summary>
        public RECT rcPaint;

        /// <summary>Reserved; used internally by the system.</summary>
        public int fRestore;

        /// <summary>Reserved; used internally by the system.</summary>
        public int fIncUpdate;

        /// <summary>Reserved; used internally by the system.</summary>
        public fixed byte rgbReserved[32];
    }

    /// <summary>Parameters for <see cref="TrackMouseEvent"/> — requests <c>WM_MOUSELEAVE</c> tracking.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TRACKMOUSEEVENT
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint cbSize;

        /// <summary>The services requested (see <c>TME_*</c>).</summary>
        public uint dwFlags;

        /// <summary>The window to track.</summary>
        public nint hwndTrack;

        /// <summary>The hover time-out, in milliseconds (unused for leave tracking).</summary>
        public uint dwHoverTime;
    }

    /// <summary>A width/height pair, as filled in by <see cref="GetTextExtentPoint32W"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        /// <summary>The horizontal extent.</summary>
        public int cx;

        /// <summary>The vertical extent.</summary>
        public int cy;
    }

    /// <summary>A device-independent-bitmap header describing pixel layout for <see cref="CreateDIBSection"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint biSize;

        /// <summary>Bitmap width, in pixels.</summary>
        public int biWidth;

        /// <summary>Bitmap height; negative for a top-down (origin at top-left) DIB.</summary>
        public int biHeight;

        /// <summary>Number of color planes (always 1).</summary>
        public ushort biPlanes;

        /// <summary>Bits per pixel.</summary>
        public ushort biBitCount;

        /// <summary>Compression scheme (see <c>BI_RGB</c>).</summary>
        public uint biCompression;

        /// <summary>Image size, in bytes (may be 0 for <c>BI_RGB</c>).</summary>
        public uint biSizeImage;

        /// <summary>Horizontal resolution, in pixels per meter.</summary>
        public int biXPelsPerMeter;

        /// <summary>Vertical resolution, in pixels per meter.</summary>
        public int biYPelsPerMeter;

        /// <summary>Number of color-table entries used (0 for a 32bpp DIB).</summary>
        public uint biClrUsed;

        /// <summary>Number of important color-table entries (0 = all).</summary>
        public uint biClrImportant;
    }

    /// <summary>
    /// A DIB header plus its (unused for 32bpp) leading color-table entry, matching the layout
    /// <see cref="CreateDIBSection"/> expects for a <c>BITMAPINFO*</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        /// <summary>The pixel-format header.</summary>
        public BITMAPINFOHEADER bmiHeader;

        /// <summary>First color-table entry (present for layout only; unused for a 32bpp DIB).</summary>
        public uint bmiColors0;
    }

    /// <summary>The controlling per-pixel-alpha blend used by <see cref="AlphaBlend"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BLENDFUNCTION
    {
        /// <summary>The blend operation (see <c>AC_SRC_OVER</c>).</summary>
        public byte BlendOp;

        /// <summary>Reserved; must be 0.</summary>
        public byte BlendFlags;

        /// <summary>The constant alpha applied to the whole source, 0..255.</summary>
        public byte SourceConstantAlpha;

        /// <summary>The per-pixel alpha format (see <c>AC_SRC_ALPHA</c>).</summary>
        public byte AlphaFormat;
    }

    /// <summary>A logical font description, as carried by <see cref="NONCLIENTMETRICSW"/>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LOGFONTW
    {
        /// <summary>Character cell/character height; negative requests a point-derived character height.</summary>
        public int lfHeight;

        /// <summary>Average character width (0 = mapper default).</summary>
        public int lfWidth;

        /// <summary>Angle of the escapement vector, in tenths of a degree.</summary>
        public int lfEscapement;

        /// <summary>Angle of each character's baseline, in tenths of a degree.</summary>
        public int lfOrientation;

        /// <summary>Font weight, 0..1000 (see <c>FW_*</c>).</summary>
        public int lfWeight;

        /// <summary>Non-zero for italic.</summary>
        public byte lfItalic;

        /// <summary>Non-zero for underlined.</summary>
        public byte lfUnderline;

        /// <summary>Non-zero for strike-out.</summary>
        public byte lfStrikeOut;

        /// <summary>The character set.</summary>
        public byte lfCharSet;

        /// <summary>The output precision.</summary>
        public byte lfOutPrecision;

        /// <summary>The clipping precision.</summary>
        public byte lfClipPrecision;

        /// <summary>The output quality.</summary>
        public byte lfQuality;

        /// <summary>The pitch and family.</summary>
        public byte lfPitchAndFamily;

        /// <summary>The typeface name, null-terminated, up to 32 UTF-16 code units.</summary>
        public fixed char lfFaceName[32];
    }

    /// <summary>The system's non-client metrics, including the UI fonts, from <c>SPI_GETNONCLIENTMETRICS</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NONCLIENTMETRICSW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint cbSize;

        /// <summary>Border width, in pixels.</summary>
        public int iBorderWidth;

        /// <summary>Scroll-bar width, in pixels.</summary>
        public int iScrollWidth;

        /// <summary>Scroll-bar height, in pixels.</summary>
        public int iScrollHeight;

        /// <summary>Caption-button width, in pixels.</summary>
        public int iCaptionWidth;

        /// <summary>Caption-button height, in pixels.</summary>
        public int iCaptionHeight;

        /// <summary>The caption font.</summary>
        public LOGFONTW lfCaptionFont;

        /// <summary>Small-caption-button width, in pixels.</summary>
        public int iSmCaptionWidth;

        /// <summary>Small-caption-button height, in pixels.</summary>
        public int iSmCaptionHeight;

        /// <summary>The small-caption font.</summary>
        public LOGFONTW lfSmCaptionFont;

        /// <summary>Menu-bar-button width, in pixels.</summary>
        public int iMenuWidth;

        /// <summary>Menu-bar-button height, in pixels.</summary>
        public int iMenuHeight;

        /// <summary>The menu font.</summary>
        public LOGFONTW lfMenuFont;

        /// <summary>The status-bar font.</summary>
        public LOGFONTW lfStatusFont;

        /// <summary>The message-box font (the toolkit's default UI font).</summary>
        public LOGFONTW lfMessageFont;

        /// <summary>Padded-border width, in pixels (Vista and later).</summary>
        public int iPaddedBorderWidth;
    }

    // --- USER32: painting, focus, input, metrics ---

    /// <summary>Prepares a window for painting and returns a device context for its update region.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    /// <summary>Marks the end of painting begun by <see cref="BeginPaint"/>.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPaint(nint hWnd, in PAINTSTRUCT lpPaint);

    /// <summary>Adds a rectangle to a window's update region (pass a null pointer for the whole window).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InvalidateRect(nint hWnd, RECT* lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    /// <summary>Fills a rectangle with the given brush (the brush edges are not painted).</summary>
    [LibraryImport("user32.dll")]
    internal static partial int FillRect(nint hDC, in RECT lprc, nint hbr);

    /// <summary>Sets the keyboard focus to the given window.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint SetFocus(nint hWnd);

    /// <summary>Retrieves the current color of a display element (see <c>COLOR_*</c>).</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetSysColor(int nIndex);

    /// <summary>Retrieves a system metric or configuration setting (see <c>SM_*</c>).</summary>
    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    /// <summary>Retrieves the user's double-click interval in milliseconds.</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    /// <summary>Draws formatted text inside a rectangle (see <c>DT_*</c>).</summary>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    /// <summary>Requests notification (<c>WM_MOUSELEAVE</c>) when the pointer leaves a window.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    /// <summary>Returns the up/down and toggle state of a virtual key (high bit set = down).</summary>
    [LibraryImport("user32.dll")]
    internal static partial short GetKeyState(int nVirtKey);

    /// <summary>Queries or sets a system-wide parameter, here the non-client metrics.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref NONCLIENTMETRICSW pvParam, uint fWinIni);

    /// <summary>Retrieves a display device context for the given window (0 = the whole screen).</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hWnd);

    /// <summary>Releases a device context obtained from <see cref="GetDC"/>.</summary>
    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hWnd, nint hDC);

    /// <summary>Reads a window-style long (see <c>GWL_STYLE</c>).</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    /// <summary>Writes a window-style long (see <c>GWL_STYLE</c>).</summary>
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    // --- GDI32: brushes, pens, fonts, DCs, bitmaps, clipping ---

    /// <summary>Creates a solid brush of the given <c>COLORREF</c>.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateSolidBrush(uint color);

    /// <summary>Creates a cosmetic pen of the given style, width and <c>COLORREF</c>.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint CreatePen(int iStyle, int cWidth, uint color);

    /// <summary>Creates a logical font from its attributes and face name.</summary>
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFontW(
        int cHeight,
        int cWidth,
        int cEscapement,
        int cOrientation,
        int cWeight,
        uint bItalic,
        uint bUnderline,
        uint bStrikeOut,
        uint iCharSet,
        uint iOutPrecision,
        uint iClipPrecision,
        uint iQuality,
        uint iPitchAndFamily,
        string pszFaceName);

    /// <summary>Selects an object (pen, brush, font, bitmap) into a DC and returns the previous one.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint h);

    /// <summary>Deletes a logical GDI object, freeing its resources.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint ho);

    /// <summary>Draws a rectangle outline with the current pen and fills it with the current brush.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Rectangle(nint hdc, int left, int top, int right, int bottom);

    /// <summary>Draws an ellipse outline with the current pen and fills it with the current brush.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Ellipse(nint hdc, int left, int top, int right, int bottom);

    /// <summary>Retrieves a handle to one of the predefined stock pens, brushes or fonts (see <c>NULL_*</c>).</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint GetStockObject(int i);

    /// <summary>Moves the current position to the given point, optionally returning the previous one.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveToEx(nint hdc, int x, int y, nint lppt);

    /// <summary>Draws a line from the current position up to (but not including) the given point.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LineTo(nint hdc, int x, int y);

    /// <summary>Sets the text color of a DC and returns the previous color.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial uint SetTextColor(nint hdc, uint color);

    /// <summary>Sets the background mix mode (see <c>TRANSPARENT</c>) and returns the previous one.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial int SetBkMode(nint hdc, int mode);

    /// <summary>Sets the background fill color of a DC and returns the previous color.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial uint SetBkColor(nint hdc, uint color);

    /// <summary>Returns the shared system brush for a <c>COLOR_*</c> index (never delete it).</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint GetSysColorBrush(int nIndex);

    /// <summary>Activates a cursor and returns the previous one.</summary>
    [LibraryImport("user32.dll")]
    internal static partial nint SetCursor(nint hCursor);

    /// <summary>Measures the extent of a single-line string in the DC's current font.</summary>
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTextExtentPoint32W(nint hdc, string lpString, int c, out SIZE lpSize);

    /// <summary>Creates a memory device context compatible with the given DC.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    /// <summary>Deletes a device context created with <see cref="CreateCompatibleDC"/>.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    /// <summary>Creates a DIB section and returns a pointer to its pixel bits in <paramref name="ppvBits"/>.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateDIBSection(
        nint hdc,
        in BITMAPINFO pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset);

    /// <summary>Retrieves a device-specific capability (see <c>LOGPIXELSY</c>).</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial int GetDeviceCaps(nint hdc, int index);

    /// <summary>Saves the DC's state on its stack and returns an id for <see cref="RestoreDC"/>.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial int SaveDC(nint hdc);

    /// <summary>Restores a DC state saved by <see cref="SaveDC"/> (pass -1 for the most recent).</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RestoreDC(nint hdc, int nSavedDC);

    /// <summary>Intersects the DC's clip region with a rectangle.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial int IntersectClipRect(nint hdc, int left, int top, int right, int bottom);

    // --- Theme-change messages and high contrast ---

    /// <summary>A system color changed (light/dark palette switch included).</summary>
    internal const uint WM_SYSCOLORCHANGE = 0x0015;

    /// <summary>A system-wide setting changed (<c>SystemParametersInfo</c>, "ImmersiveColorSet", …).</summary>
    internal const uint WM_SETTINGCHANGE = 0x001A;

    /// <summary>The visual-styles theme changed.</summary>
    internal const uint WM_THEMECHANGED = 0x031A;

    /// <summary>Retrieves the high-contrast accessibility parameters.</summary>
    internal const uint SPI_GETHIGHCONTRAST = 0x0042;

    /// <summary>The high-contrast feature is on (<see cref="HIGHCONTRASTW.dwFlags"/>).</summary>
    internal const uint HCF_HIGHCONTRASTON = 0x00000001;

    /// <summary>The high-contrast accessibility parameters, from <see cref="SPI_GETHIGHCONTRAST"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HIGHCONTRASTW
    {
        /// <summary>Size of this structure, in bytes.</summary>
        public uint cbSize;

        /// <summary>The high-contrast flags (<see cref="HCF_HIGHCONTRASTON"/> among them).</summary>
        public uint dwFlags;

        /// <summary>The name of the default color scheme (unused by the toolkit).</summary>
        public nint lpszDefaultScheme;
    }

    /// <summary>Reads the high-contrast parameters (the <see cref="HIGHCONTRASTW"/> overload).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, ref HIGHCONTRASTW pvParam, uint fWinIni);

    /// <summary>Returns the system DPI (96 at 100% scale). Present since Windows 10 1607.</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForSystem();

    // --- Double buffering ---

    /// <summary>Copies the source rectangle unchanged (<see cref="BitBlt"/> raster operation).</summary>
    internal const uint SRCCOPY = 0x00CC0020;

    /// <summary>Creates a bitmap compatible with the device context, for the off-screen buffer.</summary>
    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    /// <summary>Block-transfers pixels between device contexts.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

    /// <summary>Draws a rectangle with rounded corners, filled with the current brush and outlined
    /// with the current pen; <paramref name="width"/>/<paramref name="height"/> are the ellipse axes
    /// of the corner rounding.</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);

    // --- KERNEL32 ---

    /// <summary>Multiplies two 32-bit values then divides by a third, rounding to the nearest integer.</summary>
    [LibraryImport("kernel32.dll")]
    internal static partial int MulDiv(int nNumber, int nNumerator, int nDenominator);

    // --- MSIMG32 ---

    /// <summary>Alpha-blends a source rectangle onto a destination, honoring per-pixel alpha.</summary>
    [LibraryImport("msimg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AlphaBlend(
        nint hdcDest,
        int xoriginDest,
        int yoriginDest,
        int wDest,
        int hDest,
        nint hdcSrc,
        int xoriginSrc,
        int yoriginSrc,
        int wSrc,
        int hSrc,
        BLENDFUNCTION ftn);
}
