using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// A platform toolkit binding. One implementation exists per windowing system (Win32, GTK, Cocoa …);
/// each is a thin, reflection-free P/Invoke layer so the whole stack stays trim- and AOT-friendly.
/// The core never talks to a native API directly — it creates peers through this factory and drives
/// the platform event loop through <see cref="Run"/>.
/// </summary>
public interface IPlatformBackend
{
    /// <summary>A short, stable identifier used in diagnostics (for example <c>"Win32"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Whether this backend can run on the current OS. The registry uses it to pick a backend; a
    /// backend compiled into an app for another platform simply reports <see langword="false"/>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>The native theme (colors, font, metrics) owner-drawn controls paint with.</summary>
    ITheme Theme { get; }

    /// <summary>
    /// Raised after the desktop theme changes (light/dark switch, accent or system-color change,
    /// high-contrast toggle). By the time it fires, <see cref="Theme"/> already serves the fresh
    /// values; realized owner-drawn controls re-read it and repaint.
    /// </summary>
    event EventHandler? ThemeChanged;

    /// <summary>
    /// The ratio of device pixels to logical (96-DPI) pixels on the primary monitor — 1.0 at 100%,
    /// 1.5 at 150%, and so on. The groundwork for per-monitor DPI awareness: callers map logical
    /// lengths through it (see <see cref="Control.LogicalToDevice(int)"/>); per-monitor rescale on
    /// window move is tracked separately in <c>docs/PRD.md</c> §8.
    /// </summary>
    double GetDpiScale();

    /// <summary>Creates an unrealized top-level window peer.</summary>
    IWindowPeer CreateWindow();

    /// <summary>Creates an unrealized push-button peer.</summary>
    IButtonPeer CreateButton();

    /// <summary>Creates an unrealized static-text peer.</summary>
    ILabelPeer CreateLabel();

    /// <summary>Creates an unrealized text-input peer.</summary>
    ITextBoxPeer CreateTextBox();

    /// <summary>Creates an unrealized rich-text-editor peer.</summary>
    IRichTextBoxPeer CreateRichTextBox();

    /// <summary>Creates an unrealized owner-draw canvas peer (the surface all custom controls use).</summary>
    ICanvasPeer CreateCanvas();

    /// <summary>Creates a hidden light-dismiss popup surface peer (drop-downs, menus, tooltips).</summary>
    IPopupPeer CreatePopup();

    /// <summary>Creates a native image from 32-bit ARGB pixels (row-major, length = width * height).</summary>
    IImage CreateImage(int width, int height, ReadOnlySpan<int> argb);

    /// <summary>Creates a stopped UI-thread timer peer.</summary>
    ITimerPeer CreateTimer();

    /// <summary>
    /// Creates a hidden tray/status-area icon peer. Backends whose platform has no supported tray
    /// surface throw <see cref="NotSupportedException"/> — honestly, at creation time — rather than
    /// silently dropping the icon.
    /// </summary>
    INotifyIconPeer CreateNotifyIcon();

    /// <summary>
    /// The pixel size of the primary screen. The core uses it to place forms whose
    /// <see cref="Form.StartPosition"/> asks for centering — the policy stays platform-agnostic and
    /// the peers only ever see the resulting bounds.
    /// </summary>
    Size GetScreenSize();

    /// <summary>
    /// Measures the pixel size <paramref name="text"/> would occupy in <paramref name="font"/>, without
    /// needing a paint surface — the seam auto-sizing controls use before and between paints. Uses the
    /// same native text engine as <see cref="IGraphics.MeasureText"/>, so both agree.
    /// </summary>
    Size MeasureText(string text, Font font);

    /// <summary>
    /// Shows the platform's native message box, application-modal, and blocks until the user picks a
    /// button. Returns which button was pressed.
    /// </summary>
    DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon);

    /// <summary>
    /// Shows the platform's native file, save or folder dialog, application-modal. Returns the chosen
    /// absolute path(s) — one element unless <see cref="FileDialogOptions.Multiselect"/> — or
    /// <see langword="null"/> when the user cancelled.
    /// </summary>
    string[]? ShowFileDialog(in FileDialogOptions options);

    /// <summary>
    /// Shows the platform's native color picker preselecting <paramref name="color"/>,
    /// application-modal. Returns the chosen color, or <see langword="null"/> when cancelled.
    /// </summary>
    Color? ShowColorDialog(Color color);

    /// <summary>
    /// Shows the platform's native font picker preselecting <paramref name="font"/>,
    /// application-modal. Returns the chosen font, or <see langword="null"/> when cancelled.
    /// </summary>
    Font? ShowFontDialog(Font font);

    /// <summary>
    /// Places plain text on the system clipboard, replacing its current content. The seam behind copy
    /// gestures (a grid's Ctrl+C); reading the clipboard and non-text formats are not part of the
    /// contract.
    /// </summary>
    void SetClipboardText(string text);

    /// <summary>
    /// Queues <paramref name="action"/> for execution on the UI thread the message loop runs on and
    /// returns immediately. Safe to call from any thread; the loop executes queued work in posting
    /// order. The seam behind <see cref="Control.BeginInvoke"/> and the toolkit's
    /// <see cref="System.Threading.SynchronizationContext"/>.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Enters the platform message loop and blocks until the main window closes or <see cref="Quit"/>
    /// is called. Must be invoked on the thread that created the widgets.
    /// </summary>
    void Run(IWindowPeer mainWindow);

    /// <summary>Requests that the running message loop exit.</summary>
    void Quit();
}
