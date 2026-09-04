namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The platform's own hover text, which on this desktop is a property of the view rather than a
/// window somebody puts up.
/// </summary>
/// <remarks>
/// There is no "show a tip now" on AppKit. A view carries a <c>toolTip</c> and the platform draws it
/// when it judges the pointer has rested long enough, so what the toolkit's show-now seam can do here
/// is hand the text over and let the desktop decide the moment. That is late for the hover that asked
/// — the toolkit's own delay has already run — and right for every hover after it, which is worth
/// more than a control that never shows a tip at all.
/// </remarks>
internal static class CocoaToolTip {
  /// <summary>Puts a hover text on a view, or takes the one it has away.</summary>
  internal static void Apply(nint view, string? text) {
    if (view == 0)
      return;

    if (string.IsNullOrEmpty(text)) {
      CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setToolTip:"), 0);
      return;
    }

    var value = CocoaRuntime.NSString(text);
    if (value == 0)
      return;

    CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setToolTip:"), value);
    CocoaNative.CFRelease(value);
  }
}
