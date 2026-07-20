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
    public double GetDpiScale() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IWindowPeer CreateWindow() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ICanvasPeer CreateCanvas() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IPopupPeer CreatePopup() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb)
        => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ITimerPeer CreateTimer() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public INotifyIconPeer CreateNotifyIcon() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Size GetScreenSize() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public Size MeasureText(string text, Font font) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public IButtonPeer CreateButton() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ILabelPeer CreateLabel() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public ITextBoxPeer CreateTextBox() => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
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
    public void Post(Action action) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void Run(IWindowPeer mainWindow) => throw new PlatformNotSupportedException(_NotImplemented);

    /// <inheritdoc/>
    public void Quit() { }
}
