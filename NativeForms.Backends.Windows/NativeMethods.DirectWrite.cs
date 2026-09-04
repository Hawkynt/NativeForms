using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Direct2D and DirectWrite surface behind colour-glyph text (PRD §13).
/// </summary>
/// <remarks>
/// COM, reached without COM interop: only the two factory entry points are imported, and every interface
/// method is called by indexing the object's vtable through a <c>delegate* unmanaged</c>. That keeps the
/// rules of §2 — no <c>[ComImport]</c>, no <c>Marshal.GetObjectForIUnknown</c>, no runtime type system —
/// and costs nothing per call. The price is that the slot numbers below are load-bearing: each one is
/// counted from the interface's own declaration order, inherited methods first, and is written next to
/// the method it names so a reader can check it against the SDK header without counting.
/// </remarks>
internal static unsafe partial class NativeMethods {
  // --- Factory entry points ----------------------------------------------------------------------

  /// <summary>Creates the Direct2D factory. <c>D2D1_FACTORY_TYPE_SINGLE_THREADED</c> is 0.</summary>
  [LibraryImport("d2d1.dll")]
  internal static partial int D2D1CreateFactory(int factoryType, in Guid riid, nint factoryOptions, out nint factory);

  /// <summary>Creates the DirectWrite factory. <c>DWRITE_FACTORY_TYPE_SHARED</c> is 0.</summary>
  [LibraryImport("dwrite.dll")]
  internal static partial int DWriteCreateFactory(int factoryType, in Guid iid, out nint factory);

  /// <summary><c>IID_ID2D1Factory</c>.</summary>
  internal static readonly Guid IID_ID2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");

  /// <summary><c>IID_IDWriteFactory</c>.</summary>
  internal static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

  // --- Structures --------------------------------------------------------------------------------

  /// <summary>How a render target stores and blends pixels.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct D2D1_PIXEL_FORMAT {
    /// <summary>A <c>DXGI_FORMAT</c>; 87 is <c>B8G8R8A8_UNORM</c>, 0 lets Direct2D choose.</summary>
    internal uint format;

    /// <summary>A <c>D2D1_ALPHA_MODE</c>: 0 unknown, 1 premultiplied, 2 straight, 3 ignore.</summary>
    internal uint alphaMode;
  }

  /// <summary>The properties a render target is created with.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct D2D1_RENDER_TARGET_PROPERTIES {
    /// <summary>A <c>D2D1_RENDER_TARGET_TYPE</c>; 0 is "whatever is available".</summary>
    internal uint type;

    /// <summary>The pixel format.</summary>
    internal D2D1_PIXEL_FORMAT pixelFormat;

    /// <summary>Horizontal DPI; 0 means the desktop's.</summary>
    internal float dpiX;

    /// <summary>Vertical DPI; 0 means the desktop's.</summary>
    internal float dpiY;

    /// <summary>A <c>D2D1_RENDER_TARGET_USAGE</c>; 2 is <c>GDI_COMPATIBLE</c>.</summary>
    internal uint usage;

    /// <summary>The minimum Direct3D feature level; 0 is "default".</summary>
    internal uint minLevel;
  }

  /// <summary>A rectangle in device-independent pixels.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct D2D1_RECT_F {
    /// <summary>The left edge.</summary>
    internal float left;

    /// <summary>The top edge.</summary>
    internal float top;

    /// <summary>The right edge.</summary>
    internal float right;

    /// <summary>The bottom edge.</summary>
    internal float bottom;
  }

  /// <summary>A colour in straight alpha, each channel 0..1.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct D2D1_COLOR_F {
    /// <summary>Red.</summary>
    internal float r;

    /// <summary>Green.</summary>
    internal float g;

    /// <summary>Blue.</summary>
    internal float b;

    /// <summary>Alpha.</summary>
    internal float a;
  }

  /// <summary>The size a laid-out text block occupies. Only the first members are read here, but the
  /// whole structure has to be present so the callee writes inside our buffer.</summary>
  [StructLayout(LayoutKind.Sequential)]
  internal struct DWRITE_TEXT_METRICS {
    /// <summary>The left edge of the formatted text relative to the layout box.</summary>
    internal float left;

    /// <summary>The top edge of the formatted text relative to the layout box.</summary>
    internal float top;

    /// <summary>The width of the formatted text, ignoring trailing whitespace.</summary>
    internal float width;

    /// <summary>The width including trailing whitespace.</summary>
    internal float widthIncludingTrailingWhitespace;

    /// <summary>The height of the formatted text.</summary>
    internal float height;

    /// <summary>The width of the layout box.</summary>
    internal float layoutWidth;

    /// <summary>The height of the layout box.</summary>
    internal float layoutHeight;

    /// <summary>The maximum reordering count of any line.</summary>
    internal uint maxBidiReorderingDepth;

    /// <summary>The number of lines.</summary>
    internal uint lineCount;
  }

  // --- Constants ---------------------------------------------------------------------------------

  /// <summary>Render colour glyphs in colour rather than as their monochrome fallback.</summary>
  internal const uint D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 0x00000004;

  /// <summary>Snap glyph positions to pixels, as GDI does — keeps mixed runs from drifting.</summary>
  internal const uint DWRITE_MEASURING_MODE_GDI_CLASSIC = 1;

  /// <summary>Normal font weight (400).</summary>
  internal const uint DWRITE_FONT_WEIGHT_NORMAL = 400;

  /// <summary>Bold font weight (700).</summary>
  internal const uint DWRITE_FONT_WEIGHT_BOLD = 700;

  /// <summary>Upright font style.</summary>
  internal const uint DWRITE_FONT_STYLE_NORMAL = 0;

  /// <summary>Italic font style.</summary>
  internal const uint DWRITE_FONT_STYLE_ITALIC = 2;

  /// <summary>Normal font stretch.</summary>
  internal const uint DWRITE_FONT_STRETCH_NORMAL = 5;

  /// <summary>Text alignment: leading edge.</summary>
  internal const uint DWRITE_TEXT_ALIGNMENT_LEADING = 0;

  /// <summary>Text alignment: trailing edge.</summary>
  internal const uint DWRITE_TEXT_ALIGNMENT_TRAILING = 1;

  /// <summary>Text alignment: centred.</summary>
  internal const uint DWRITE_TEXT_ALIGNMENT_CENTER = 2;

  /// <summary>Paragraph alignment: top (near).</summary>
  internal const uint DWRITE_PARAGRAPH_ALIGNMENT_NEAR = 0;

  /// <summary>Paragraph alignment: bottom (far).</summary>
  internal const uint DWRITE_PARAGRAPH_ALIGNMENT_FAR = 1;

  /// <summary>Paragraph alignment: centred.</summary>
  internal const uint DWRITE_PARAGRAPH_ALIGNMENT_CENTER = 2;

  /// <summary>Word wrapping: off, so a single line is measured as one.</summary>
  internal const uint DWRITE_WORD_WRAPPING_NO_WRAP = 1;

  // --- Vtable calls ------------------------------------------------------------------------------
  //
  // Every one of these reads the object's vtable pointer, indexes the slot named in the comment, and
  // calls through it with the COM `this` as the first argument.

  /// <summary>The <paramref name="slot"/>-th entry of <paramref name="instance"/>'s vtable.</summary>
  private static void* Slot(nint instance, int slot) => ((void**)*(void**)instance)[slot];

  /// <summary><c>IUnknown::Release</c> — slot 2.</summary>
  internal static void Release(nint instance) {
    if (instance != 0)
      ((delegate* unmanaged<nint, uint>)Slot(instance, 2))(instance);
  }

  /// <summary><c>ID2D1Factory::CreateDCRenderTarget</c> — slot 16 (3 IUnknown + 13 of its own).</summary>
  internal static int CreateDCRenderTarget(nint factory, in D2D1_RENDER_TARGET_PROPERTIES properties, out nint target) {
    fixed (D2D1_RENDER_TARGET_PROPERTIES* p = &properties)
    fixed (nint* t = &target)
      return ((delegate* unmanaged<nint, D2D1_RENDER_TARGET_PROPERTIES*, nint*, int>)Slot(factory, 16))(factory, p, t);
  }

  /// <summary><c>ID2D1DCRenderTarget::BindDC</c> — slot 57, the one method it adds to ID2D1RenderTarget.</summary>
  internal static int BindDC(nint target, nint hdc, in RECT subRect) {
    fixed (RECT* r = &subRect)
      return ((delegate* unmanaged<nint, nint, RECT*, int>)Slot(target, 57))(target, hdc, r);
  }

  /// <summary><c>ID2D1RenderTarget::CreateSolidColorBrush</c> — slot 8.</summary>
  internal static int CreateSolidColorBrush(nint target, in D2D1_COLOR_F color, out nint brush) {
    fixed (D2D1_COLOR_F* c = &color)
    fixed (nint* b = &brush)
      return ((delegate* unmanaged<nint, D2D1_COLOR_F*, nint, nint*, int>)Slot(target, 8))(target, c, 0, b);
  }

  /// <summary><c>ID2D1RenderTarget::DrawText</c> — slot 27.</summary>
  internal static void D2DDrawText(
      nint target,
      char* text,
      uint length,
      nint textFormat,
      in D2D1_RECT_F layoutRect,
      nint brush,
      uint options,
      uint measuringMode) {
    fixed (D2D1_RECT_F* r = &layoutRect)
      ((delegate* unmanaged<nint, char*, uint, nint, D2D1_RECT_F*, nint, uint, uint, void>)Slot(target, 27))(
          target, text, length, textFormat, r, brush, options, measuringMode);
  }

  /// <summary><c>ID2D1RenderTarget::BeginDraw</c> — slot 48.</summary>
  internal static void BeginDraw(nint target)
      => ((delegate* unmanaged<nint, void>)Slot(target, 48))(target);

  /// <summary><c>ID2D1RenderTarget::EndDraw</c> — slot 49.</summary>
  internal static int EndDraw(nint target)
      => ((delegate* unmanaged<nint, nint, nint, int>)Slot(target, 49))(target, 0, 0);

  /// <summary><c>IDWriteFactory::CreateTextFormat</c> — slot 15.</summary>
  internal static int CreateTextFormat(
      nint factory,
      string fontFamily,
      uint weight,
      uint style,
      uint stretch,
      float size,
      string locale,
      out nint format) {
    fixed (char* family = fontFamily)
    fixed (char* loc = locale)
    fixed (nint* f = &format)
      return ((delegate* unmanaged<nint, char*, nint, uint, uint, uint, float, char*, nint*, int>)Slot(factory, 15))(
          factory, family, 0, weight, style, stretch, size, loc, f);
  }

  /// <summary><c>IDWriteFactory::CreateTextLayout</c> — slot 18.</summary>
  internal static int CreateTextLayout(
      nint factory,
      char* text,
      uint length,
      nint format,
      float maxWidth,
      float maxHeight,
      out nint layout) {
    fixed (nint* l = &layout)
      return ((delegate* unmanaged<nint, char*, uint, nint, float, float, nint*, int>)Slot(factory, 18))(
          factory, text, length, format, maxWidth, maxHeight, l);
  }

  /// <summary>
  /// <c>IDWriteTextLayout::GetMetrics</c> — slot 60: 3 IUnknown, 25 IDWriteTextFormat, then its own
  /// thirty-third method.
  /// </summary>
  internal static int GetTextMetrics(nint layout, out DWRITE_TEXT_METRICS metrics) {
    fixed (DWRITE_TEXT_METRICS* m = &metrics)
      return ((delegate* unmanaged<nint, DWRITE_TEXT_METRICS*, int>)Slot(layout, 60))(layout, m);
  }

  /// <summary><c>IDWriteTextFormat::SetTextAlignment</c> — slot 3.</summary>
  internal static int SetTextAlignment(nint format, uint alignment)
      => ((delegate* unmanaged<nint, uint, int>)Slot(format, 3))(format, alignment);

  /// <summary><c>IDWriteTextFormat::SetParagraphAlignment</c> — slot 4.</summary>
  internal static int SetParagraphAlignment(nint format, uint alignment)
      => ((delegate* unmanaged<nint, uint, int>)Slot(format, 4))(format, alignment);

  /// <summary><c>IDWriteTextFormat::SetWordWrapping</c> — slot 5.</summary>
  internal static int SetWordWrapping(nint format, uint wrapping)
      => ((delegate* unmanaged<nint, uint, int>)Slot(format, 5))(format, wrapping);
}
