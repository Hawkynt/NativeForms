using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// Per-control hover text, the moral equivalent of <c>System.Windows.Forms.ToolTip</c>. Register a
/// text per control via <see cref="SetToolTip"/>; when the pointer rests on an owner-drawn control
/// for <see cref="InitialDelay"/> milliseconds a small themed popup appears near the cursor, and it
/// hides again when the pointer leaves, a button goes down, or <see cref="AutoPopDelay"/> elapses.
/// </summary>
/// <remarks>
/// Owner-drawn controls are observed through their canvas mouse pipeline. Native-widget controls
/// (Button, TextBox …) register their text, but showing it needs either hover events on their peers
/// or the platform tooltip API — tracked in <c>docs/PRD.md</c> §7.6.
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

    private OwnerDrawnControl? _hoverControl;
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

    /// <summary>Whether the tip popup is currently visible.</summary>
    public bool Active => _shown;

    /// <summary>
    /// Registers (or, with an empty text, removes) the hover text for a control. Owner-drawn
    /// controls are hooked immediately; the registration itself is backend-free and may happen long
    /// before the control is realized.
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
            if (control is OwnerDrawnControl ownerDrawn)
                this.Unhook(ownerDrawn);

            return;
        }

        _texts[control] = text;
        if (hooked || control is not OwnerDrawnControl target)
            return;

        target.CanvasMouseMove += this.OnControlMouseMove;
        target.CanvasMouseLeave += this.OnControlMouseLeave;
        target.CanvasMouseDown += this.OnControlMouseDown;
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
        _hoverControl = null;
        if (!_shown)
            return;

        _shown = false;
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
        _backend = null;
        foreach (var control in _texts.Keys)
            if (control is OwnerDrawnControl ownerDrawn)
                this.Unhook(ownerDrawn);

        _texts.Clear();
    }

    /// <summary>Pointer movement over a registered control: (re)arms the show delay, or re-anchors nothing while shown.</summary>
    private void OnControlMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not OwnerDrawnControl control || control.Backend is null)
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

        this.ShowPopup(backend, control.PointToScreen(new(_hoverPoint.X, _hoverPoint.Y + control.LogicalToDevice(CursorOffset))), text);

        _autoPopPhase = true;
        var timer = this.TimerFor(backend);
        timer.Interval = this.AutoPopDelay;
        timer.Start();
    }

    /// <summary>Shows (creating on first use) the popup with the given text at a screen position.</summary>
    private void ShowPopup(IPlatformBackend backend, Point screenLocation, string text)
    {
        var popup = _popup;
        if (popup is null || !ReferenceEquals(_backend, backend))
        {
            _popup?.Dispose();
            _backend = backend;
            _popup = popup = backend.CreatePopup();
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
        var theme = _backend?.Theme;
        if (theme is null)
            return;

        var g = e.Graphics;
        var textSize = g.MeasureText(_shownText, theme.DefaultFont);
        var size = new Size(textSize.Width + (2 * TextPadding), textSize.Height + (2 * TextPadding));
        g.FillRectangle(theme.FieldBackground, new(0, 0, size.Width, size.Height));
        g.DrawRectangle(theme.Border, new(0, 0, size.Width - 1, size.Height - 1));
        g.DrawText(_shownText, theme.DefaultFont, theme.ControlText, new(TextPadding, TextPadding, textSize.Width, textSize.Height));
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

    /// <summary>Detaches the mouse observers from a control whose registration was removed.</summary>
    private void Unhook(OwnerDrawnControl control)
    {
        control.CanvasMouseMove -= this.OnControlMouseMove;
        control.CanvasMouseLeave -= this.OnControlMouseLeave;
        control.CanvasMouseDown -= this.OnControlMouseDown;
        if (ReferenceEquals(_hoverControl, control))
            this.Hide();
    }
}
