using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Per-control hover text, the moral equivalent of <c>System.Windows.Forms.ToolTip</c>. Register a
/// text per control via <see cref="SetToolTip"/>; when the pointer rests on a control for
/// <see cref="InitialDelay"/> milliseconds a small themed popup appears near the cursor, and it
/// hides again when the pointer leaves, a button goes down, or <see cref="AutoPopDelay"/> elapses.
/// </summary>
/// <remarks>
/// Every control is observed the same way, so the tip, its delays and <see cref="Active"/> behave
/// identically whether the target owns a native widget or paints itself. Owner-drawn controls are
/// watched through their canvas mouse pipeline, which also lets a press dismiss the tip; native
/// widgets are watched through their peer's pointer channel
/// (<see cref="Backends.IControlPeer.PointerMove"/>), which every backend delivers — GTK via
/// <c>motion-notify-event</c>, Win32 via a subclassed <c>WM_MOUSEMOVE</c>. Registering a tip on a
/// control therefore always has an effect; the one behavioral difference is that a native widget's
/// tip is dismissed by the pointer leaving rather than by a button press.
/// </remarks>
public sealed class ToolTip : Component
{
    /// <summary>The offset from the cursor to the popup's top-left corner, in logical pixels —
    /// scaled through <see cref="Control.LogicalToDevice(int)"/> when the tip is shown.</summary>
    internal const int CursorOffset = 18;

    /// <summary>The padding between the popup border and its text.</summary>
    internal const int TextPadding = 4;

    private readonly Dictionary<Control, string> _texts = [];
    private Timer? _timer;
    private IPopupPeer? _popup;
    private IPlatformBackend? _backend;

    /// <summary>The window the cached surface was created for. One tip component can serve controls
    /// on several forms, and a surface belongs to exactly one of them, so a tip that moves to another
    /// form needs a new surface rather than one still anchored to the form it left.</summary>
    private IWindowPeer? _popupOwner;

    private Control? _hoverControl;
    private Point _hoverPoint;
    private string _shownText = string.Empty;
    private bool _shown;
    private bool _autoPopPhase;

    /// <summary>Milliseconds the pointer must rest on a control before its tip appears. Defaults to 500.</summary>
    public int InitialDelay
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 500;

    /// <summary>Milliseconds a visible tip stays up before hiding on its own. Defaults to 5000.</summary>
    public int AutoPopDelay
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 5000;

    /// <summary>
    /// Whether a tip is currently up. For an owner-drawn control this is the toolkit popup's own
    /// visibility. For a native widget it means the platform tooltip has been raised for it and not
    /// yet taken down — the final say on painting the tip belongs to the platform, so treat this as
    /// "a tip was raised" rather than a promise about pixels.
    /// </summary>
    public bool Active => _shown;

    /// <summary>
    /// Registers (or, with an empty text, removes) the hover text for a control. Every control is
    /// hooked immediately — owner-drawn ones through their canvas, native-widget ones through their
    /// peer's pointer channel — so a registration never silently does nothing. The registration
    /// itself is backend-free and may happen long before the control is realized.
    /// </summary>
    public void SetToolTip(Control control, string? text)
    {
        ArgumentNullException.ThrowIfNull(control);
        var hooked = _texts.ContainsKey(control);
        if (string.IsNullOrEmpty(text))
        {
            if (!hooked)
                return;

            _texts.Remove(control);
            this.Unhook(control);
            return;
        }

        _texts[control] = text;
        if (hooked)
            return;

        if (control is OwnerDrawnControl target)
        {
            target.CanvasMouseMove += this.OnControlMouseMove;
            target.CanvasMouseLeave += this.OnControlMouseLeave;
            target.CanvasMouseDown += this.OnControlMouseDown;
            return;
        }

        control.PointerMove += this.OnControlMouseMove;
        control.PointerLeave += this.OnControlMouseLeave;
    }

    /// <summary>The registered hover text for <paramref name="control"/>, or an empty string.</summary>
    public string GetToolTip(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _texts.TryGetValue(control, out var text) ? text : string.Empty;
    }

    /// <summary>Hides the tip and stops any pending delay.</summary>
    public void Hide()
    {
        _timer?.Stop();
        var control = _hoverControl;
        _hoverControl = null;
        if (!_shown)
            return;

        _shown = false;
        if (control is not null and not OwnerDrawnControl)
            control.Peer?.ShowToolTip(null);

        _popup?.Hide();
    }

    /// <summary>Hides the tip, detaches every observed control and releases the native popup and timer.</summary>
    protected override void Dispose(bool disposing)
    {
        this.Hide();
        _timer?.Dispose();
        _timer = null;
        _popup?.Dispose();
        _popup = null;
        _popupOwner = null;
        _backend = null;
        foreach (var control in _texts.Keys)
            this.Unhook(control);

        _texts.Clear();
    }

    /// <summary>Pointer movement over a registered control: (re)arms the show delay, or re-anchors nothing while shown.</summary>
    private void OnControlMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control || control.Backend is null)
            return;

        _hoverControl = control;
        _hoverPoint = e.Location;
        if (_shown)
            return;

        _autoPopPhase = false;
        var timer = this.TimerFor(control.Backend);
        timer.Stop();
        timer.Interval = this.InitialDelay;
        timer.Start();
    }

    /// <summary>Pointer left the control: the tip (and any pending delay) goes away.</summary>
    private void OnControlMouseLeave(object? sender, EventArgs e) => this.Hide();

    /// <summary>A button went down on the control: the tip goes away.</summary>
    private void OnControlMouseDown(object? sender, MouseEventArgs e) => this.Hide();

    /// <summary>The delay elapsed: show the tip, or hide it again after the auto-pop phase.</summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (_autoPopPhase)
        {
            this.Hide();
            return;
        }

        var control = _hoverControl;
        if (control?.Backend is not { } backend || !_texts.TryGetValue(control, out var text))
            return;

        // A native widget gets the platform's own tip; only an owner-drawn surface, which has no
        // platform tip of its own, is worth floating a toolkit popup for. See ShowNativeTip.
        if (control is OwnerDrawnControl)
            this.ShowPopup(backend, control, control.PointToScreen(new(_hoverPoint.X, _hoverPoint.Y + control.LogicalToDevice(CursorOffset))), text);
        else if (!this.ShowNativeTip(control, text))
            return;

        _autoPopPhase = true;
        var timer = this.TimerFor(backend);
        timer.Interval = this.AutoPopDelay;
        timer.Start();
    }

    /// <summary>
    /// Asks a native widget's peer to raise the platform tooltip, reporting whether a peer was there
    /// to ask. A widget that has a platform tip gets it, so the tip carries the desktop's own shape,
    /// shadow and animation; only a surface with no tip of its own is worth floating a toolkit popup
    /// for (see <see cref="ShowPopup"/>, which shows that popup passively).
    /// </summary>
    private bool ShowNativeTip(Control control, string text)
    {
        if (control.Peer is not { } peer)
            return false;

        peer.ShowToolTip(text);
        _shownText = text;
        _shown = true;
        return true;
    }

    /// <summary>Shows (creating on first use) the popup with the given text at a screen position.</summary>
    /// <param name="owner">The control being tipped; its form owns the surface, so the tip is
    /// anchored to that window and does not make it look inactive while it floats.</param>
    private void ShowPopup(IPlatformBackend backend, Control owner, Point screenLocation, string text)
    {
        var popup = _popup;
        var ownerWindow = owner.OwnerWindowPeer;
        if (popup is null || !ReferenceEquals(_backend, backend) || !ReferenceEquals(_popupOwner, ownerWindow))
        {
            _popup?.Dispose();
            _backend = backend;
            _popupOwner = ownerWindow;
            _popup = popup = backend.CreatePopup(ownerWindow);

            // A tip is passive: it must never take the grab that arms light dismiss, or the next
            // click would be spent closing the tip instead of reaching the control it was aimed at.
            // The tip is taken down by the pointer leaving, by a press on the control, or by the
            // auto-pop delay — never by a grab it holds over the rest of the application.
            popup.LightDismiss = false;
            popup.Paint += this.OnPopupPaint;
            popup.Dismissed += (_, _) => _shown = false;
        }

        _shownText = text;
        var theme = backend.Theme;
        var textSize = backend.MeasureText(text, theme.DefaultFont);
        var size = new Size(textSize.Width + (2 * TextPadding), textSize.Height + (2 * TextPadding));
        _shown = true;
        popup.ShowAt(screenLocation, size);
    }

    /// <summary>Paints the tip: themed background, border and the registered text.</summary>
    private void OnPopupPaint(object? sender, PaintEventArgs e)
    {
        if (_backend?.Theme is { } theme)
            PaintTip(e.Graphics, theme, _shownText);
    }

    /// <summary>The popup size a tip text needs: the measured text plus border padding — shared with
    /// hosts that float their own tips (a grid's per-cell tooltips).</summary>
    internal static Size MeasureTip(IPlatformBackend backend, string text)
    {
        var textSize = backend.MeasureText(text, backend.Theme.DefaultFont);
        return new(textSize.Width + (2 * TextPadding), textSize.Height + (2 * TextPadding));
    }

    /// <summary>Paints a tip surface — themed background, border and text — shared with hosts that
    /// float their own tips.</summary>
    internal static void PaintTip(IGraphics g, ITheme theme, string text)
    {
        var textSize = g.MeasureText(text, theme.DefaultFont);
        var size = new Size(textSize.Width + (2 * TextPadding), textSize.Height + (2 * TextPadding));
        g.FillRectangle(theme.FieldBackground, new(0, 0, size.Width, size.Height));
        g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
        g.DrawText(text, theme.DefaultFont, theme.ControlText, new(TextPadding, TextPadding, textSize.Width, textSize.Height));
    }

    /// <summary>The shared delay timer, created against the first backend that needs it.</summary>
    private Timer TimerFor(IPlatformBackend backend)
    {
        var timer = _timer;
        if (timer is null)
        {
            _timer = timer = new(backend);
            timer.Tick += this.OnTimerTick;
        }

        return timer;
    }

    /// <summary>Detaches the pointer observers from a control whose registration was removed,
    /// mirroring whichever channel <see cref="SetToolTip"/> hooked it through.</summary>
    private void Unhook(Control control)
    {
        if (control is OwnerDrawnControl ownerDrawn)
        {
            ownerDrawn.CanvasMouseMove -= this.OnControlMouseMove;
            ownerDrawn.CanvasMouseLeave -= this.OnControlMouseLeave;
            ownerDrawn.CanvasMouseDown -= this.OnControlMouseDown;
        }
        else
        {
            control.PointerMove -= this.OnControlMouseMove;
            control.PointerLeave -= this.OnControlMouseLeave;
        }

        if (ReferenceEquals(_hoverControl, control))
            this.Hide();
    }
}
