using System.Drawing;

namespace Hawkynt.NativeForms.Drawing;

/// <summary>
/// Colour-space conversions for the <see cref="ColorPicker"/> mixer — HSV, HSL and CMYK to and from
/// <see cref="Color"/>, plus hex parsing and formatting. Pure value-type math with no allocation,
/// reflection or LINQ, so it is safe on the paint path and under trimming/NativeAOT.
/// </summary>
internal static class ColorMath {
  /// <summary>Builds a colour from HSV: <paramref name="h"/> in [0,360), <paramref name="s"/> and
  /// <paramref name="v"/> in [0,1], with an optional alpha byte.</summary>
  internal static Color HsvToColor(double h, double s, double v, byte a = 255) {
    h = ((h % 360) + 360) % 360;
    s = Clamp01(s);
    v = Clamp01(v);
    var c = v * s;
    var x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
    var m = v - c;
    double r, g, b;
    if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; } else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }

    return Color.FromArgb(a, Byte((r + m) * 255), Byte((g + m) * 255), Byte((b + m) * 255));
  }

  /// <summary>Decomposes a colour into HSV: hue in [0,360), saturation and value in [0,1].</summary>
  internal static void ColorToHsv(Color color, out double h, out double s, out double v) {
    double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
    var max = Math.Max(r, Math.Max(g, b));
    var min = Math.Min(r, Math.Min(g, b));
    var d = max - min;
    h = Hue(r, g, b, max, d);
    s = max == 0 ? 0 : d / max;
    v = max;
  }

  /// <summary>Builds a colour from HSL: hue in [0,360), saturation and lightness in [0,1].</summary>
  internal static Color HslToColor(double h, double s, double l, byte a = 255) {
    h = ((h % 360) + 360) % 360;
    s = Clamp01(s);
    l = Clamp01(l);
    var c = (1 - Math.Abs((2 * l) - 1)) * s;
    var x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
    var m = l - (c / 2);
    double r, g, b;
    if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; } else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }

    return Color.FromArgb(a, Byte((r + m) * 255), Byte((g + m) * 255), Byte((b + m) * 255));
  }

  /// <summary>Decomposes a colour into HSL: hue in [0,360), saturation and lightness in [0,1].</summary>
  internal static void ColorToHsl(Color color, out double h, out double s, out double l) {
    double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
    var max = Math.Max(r, Math.Max(g, b));
    var min = Math.Min(r, Math.Min(g, b));
    var d = max - min;
    h = Hue(r, g, b, max, d);
    l = (max + min) / 2;
    s = d == 0 ? 0 : d / (1 - Math.Abs((2 * l) - 1));
  }

  /// <summary>Decomposes a colour into CMYK, each channel in [0,1].</summary>
  internal static void ColorToCmyk(Color color, out double c, out double m, out double y, out double k) {
    double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
    k = 1 - Math.Max(r, Math.Max(g, b));
    if (k >= 1) {
      c = m = y = 0;
      return;
    }

    c = (1 - r - k) / (1 - k);
    m = (1 - g - k) / (1 - k);
    y = (1 - b - k) / (1 - k);
  }

  /// <summary>Builds a colour from CMYK, each channel in [0,1].</summary>
  internal static Color CmykToColor(double c, double m, double y, double k, byte a = 255) {
    c = Clamp01(c); m = Clamp01(m); y = Clamp01(y); k = Clamp01(k);
    return Color.FromArgb(a, Byte(255 * (1 - c) * (1 - k)), Byte(255 * (1 - m) * (1 - k)), Byte(255 * (1 - y) * (1 - k)));
  }

  /// <summary>Formats a colour as <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when <paramref name="withAlpha"/>, uppercased.</summary>
  internal static string ToHex(Color color, bool withAlpha)
      => withAlpha
          ? $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}"
          : $"#{color.R:X2}{color.G:X2}{color.B:X2}";

  /// <summary>Parses <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c> (the <c>#</c> optional).</summary>
  internal static bool TryParseHex(ReadOnlySpan<char> text, out Color color) {
    color = Color.Black;
    text = text.Trim();
    if (text.Length > 0 && text[0] == '#')
      text = text[1..];

    switch (text.Length) {
      case 3:
        return TryNibbles(text, 1, out color, hasAlpha: false);
      case 4:
        return TryNibbles(text, 1, out color, hasAlpha: true);
      case 6:
        return TryNibbles(text, 2, out color, hasAlpha: false);
      case 8:
        return TryNibbles(text, 2, out color, hasAlpha: true);
      default:
        return false;
    }
  }

  private static bool TryNibbles(ReadOnlySpan<char> text, int width, out Color color, bool hasAlpha) {
    color = Color.Black;
    if (!Component(text, 0, width, out var r)
        || !Component(text, width, width, out var g)
        || !Component(text, 2 * width, width, out var b))
      return false;

    var a = 255;
    if (hasAlpha && !Component(text, 3 * width, width, out a))
      return false;

    color = Color.FromArgb(a, r, g, b);
    return true;
  }

  private static bool Component(ReadOnlySpan<char> text, int start, int width, out int value) {
    value = 0;
    for (var i = 0; i < width; ++i) {
      var digit = HexDigit(text[start + i]);
      if (digit < 0)
        return false;

      value = (value * 16) + digit;
    }

    // A single hex digit expands to two (0xF → 0xFF), matching CSS short-hex.
    if (width == 1)
      value = (value * 16) + value;

    return true;
  }

  private static int HexDigit(char c) => c switch {
    >= '0' and <= '9' => c - '0',
    >= 'a' and <= 'f' => c - 'a' + 10,
    >= 'A' and <= 'F' => c - 'A' + 10,
    _ => -1,
  };

  private static double Hue(double r, double g, double b, double max, double d) {
    if (d == 0)
      return 0;

    double h;
    if (max == r)
      h = 60 * ((((g - b) / d) % 6 + 6) % 6);
    else if (max == g)
      h = 60 * (((b - r) / d) + 2);
    else
      h = 60 * (((r - g) / d) + 4);

    return (h % 360 + 360) % 360;
  }

  private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

  private static int Byte(double x) => (int)Math.Round(x < 0 ? 0 : x > 255 ? 255 : x);
}
