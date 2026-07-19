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

    /// <summary>Creates an unrealized top-level window peer.</summary>
    IWindowPeer CreateWindow();

    /// <summary>Creates an unrealized push-button peer.</summary>
    IButtonPeer CreateButton();

    /// <summary>Creates an unrealized static-text peer.</summary>
    ILabelPeer CreateLabel();

    /// <summary>Creates an unrealized text-input peer.</summary>
    ITextBoxPeer CreateTextBox();

    /// <summary>Creates an unrealized owner-draw canvas peer (the surface all custom controls use).</summary>
    ICanvasPeer CreateCanvas();

    /// <summary>Creates a hidden light-dismiss popup surface peer (drop-downs, menus, tooltips).</summary>
    IPopupPeer CreatePopup();

    /// <summary>Creates a native image from 32-bit ARGB pixels (row-major, length = width * height).</summary>
    IImage CreateImage(int width, int height, ReadOnlySpan<int> argb);

    /// <summary>Creates a stopped UI-thread timer peer.</summary>
    ITimerPeer CreateTimer();

    /// <summary>
    /// Measures the pixel size <paramref name="text"/> would occupy in <paramref name="font"/>, without
    /// needing a paint surface — the seam auto-sizing controls use before and between paints. Uses the
    /// same native text engine as <see cref="IGraphics.MeasureText"/>, so both agree.
    /// </summary>
    Size MeasureText(string text, Font font);

    /// <summary>
    /// Enters the platform message loop and blocks until the main window closes or <see cref="Quit"/>
    /// is called. Must be invoked on the thread that created the widgets.
    /// </summary>
    void Run(IWindowPeer mainWindow);

    /// <summary>Requests that the running message loop exit.</summary>
    void Quit();
}
