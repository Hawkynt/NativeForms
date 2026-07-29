using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// The macOS (Cocoa/AppKit) backend. This is currently a wired-but-unimplemented placeholder: it
/// reports support on macOS and fails with an explicit, actionable message rather than pretending to
/// draw. Implementing it — <c>NSApplication</c>, <c>NSWindow</c>, <c>NSButton</c>, <c>NSTextField</c>
/// via <c>objc_msgSend</c> P/Invoke — is tracked in <c>docs/PRD.md</c>.
/// </summary>
public sealed class CocoaBackend : IPlatformBackend
{
    private const string _NotImplemented =
        "The NativeForms Cocoa (macOS) backend is not implemented yet — see docs/PRD.md for status. "
        + "Until then, run on Windows (Win32) or Linux (GTK).";

    /// <inheritdoc/>
    public string Name => "Cocoa";

    /// <inheritdoc/>
    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <inheritdoc/>
    public ITheme Theme => DefaultTheme.Instance;

    /// <inheritdoc/>
    /// <remarks>Never raised: the placeholder serves the static fallback theme only.</remarks>
    public event EventHandler? ThemeChanged { add { } remove { } }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>[[NSScreen mainScreen] backingScaleFactor]</c>: 2.0 on a Retina display, 1.0 otherwise. Also
    /// the first thing in this backend to go through Objective-C messaging, which makes it the check
    /// that the messaging layer works on a real machine — a wrong <c>objc_msgSend</c> signature does not
    /// fail, it returns plausible nonsense, so the first use of it wants to be something whose right
    /// answer is known.
    /// </remarks>
    public double GetDpiScale()
    {
        if (!CocoaRuntime.Available)
            return 1.0;

        var screen = CocoaRuntime.SendToClass("NSScreen", "mainScreen");
        if (screen == 0)
            return 1.0;

        var scale = CocoaRuntime.SendDouble(screen, CocoaRuntime.sel_registerName("backingScaleFactor"));
        return scale is > 0 and < 16 ? scale : 1.0;
    }

    /// <inheritdoc/>
    public IWindowPeer CreateWindow() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ICanvasPeer CreateCanvas() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IPopupPeer CreatePopup(IWindowPeer? owner) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => new CocoaImage(width, height, argb);

    /// <inheritdoc/>
    public ITimerPeer CreateTimer() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public INotifyIconPeer CreateNotifyIcon() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Size GetScreenSize()
    {
        var display = CocoaNative.CGMainDisplayID();
        return new((int)CocoaNative.CGDisplayPixelsWide(display), (int)CocoaNative.CGDisplayPixelsHigh(display));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CoreText, so the measurement is the one the platform would lay the text out with rather than a
    /// guess from character counts. A refusal falls back to the shared metric estimate instead of
    /// reporting zero, which would quietly collapse every layout that measures.
    /// </remarks>
    public Size MeasureText(string text, Font font)
    {
        if (text.Length == 0)
            return Size.Empty;

        return CocoaNative.TryMeasure(text, font.Family, font.SizeInPoints, out var width, out var height)
            ? new((int)Math.Ceiling(width), (int)Math.Ceiling(height))
            : new((int)Math.Ceiling(text.Length * font.SizeInPoints * 0.6), (int)Math.Ceiling(font.SizeInPoints * 1.3));
    }

    /// <inheritdoc/>
    public IButtonPeer CreateButton() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ILabelPeer CreateLabel() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ITextBoxPeer CreateTextBox() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null)
        => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public string[]? ShowFileDialog(in FileDialogOptions options) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Color? ShowColorDialog(Color color) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Font? ShowFontDialog(Font font) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IRichTextBoxPeer CreateRichTextBox() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void SetClipboardText(string text) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public string? GetClipboardText() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void Post(Action action) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void Run(IWindowPeer mainWindow) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void Quit() { }
}
