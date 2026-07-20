using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The spinner-field engine behind <see cref="NumericUpDown"/> and <see cref="DomainUpDown"/>: a
/// hosted native <see cref="TextBox"/> fills the field, and the owner-drawn surface paints the
/// themed up/down button column at the right edge. Clicking a button steps once; holding it
/// autorepeats (500 ms initial delay, then every 50 ms); the Up/Down keys step as well.
/// </summary>
/// <remarks>
/// Keys typed inside the hosted native editor are not observable from the core (the peer exposes
/// text changes, not key events), so a typed edit has no Enter-key moment to commit at. Instead, a
/// pending edit is committed — parsed by <see cref="ValidateEditText"/>, which clamps or
/// reverts — at the honest points available: before any step (buttons, keys, autorepeat, exactly
/// like the classic control validates before stepping) and when the surface loses focus. Subclasses
/// may add further commit points, as <see cref="NumericUpDown"/> does on <c>Value</c> reads.
/// </remarks>
public abstract class UpDownBase : OwnerDrawnControl
{
    /// <summary>The number of stacked lines forming a spinner arrow glyph.</summary>
    private const int _ArrowRows = 3;

    private readonly TextBox _editor;
    private AutoRepeat? _autoRepeat;
    private bool _updatingEditor;
    private int _pressedDirection; // +1 up button held, -1 down button held, 0 none

    /// <summary>Creates the spinner shell and its hosted editor.</summary>
    protected UpDownBase()
    {
        _editor = new() { TabStop = false };
        _editor.TextChanged += this.OnEditorTextChanged;
        this.Controls.Add(_editor);
    }

    /// <summary>The content of the hosted editor. Assigning counts as a pending user edit, committed
    /// at the next commit point.</summary>
    public override string Text
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    /// <summary>Whether the editor holds a user edit that has not been committed yet.</summary>
    protected bool UserEdit { get; private set; }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Steps the value one increment up.</summary>
    public abstract void UpButton();

    /// <summary>Steps the value one increment down.</summary>
    public abstract void DownButton();

    /// <summary>Rewrites the editor from the current value, clearing any pending edit.</summary>
    protected abstract void UpdateEditText();

    /// <summary>Commits the editor's text into the value: parse and clamp, or revert when invalid.</summary>
    protected abstract void ValidateEditText();

    /// <summary>Commits a pending user edit, if any — the shared entry to every commit point.</summary>
    private protected void CommitEdit()
    {
        if (this.UserEdit)
            this.ValidateEditText();
    }

    /// <summary>Writes programmatic text into the editor without flagging a user edit.</summary>
    protected void SetEditorText(string text)
    {
        _updatingEditor = true;
        _editor.Text = text;
        _updatingEditor = false;
        this.UserEdit = false;
    }

    /// <summary>The width of the spinner-button column at the right edge of the field.</summary>
    private int ButtonWidth => this.Theme.ScrollBarSize + 1;

    /// <summary>The upper (increment) spinner button.</summary>
    private Rectangle UpButtonRect => new(this.Width - this.ButtonWidth, 0, this.ButtonWidth, this.Height / 2);

    /// <summary>The lower (decrement) spinner button.</summary>
    private Rectangle DownButtonRect
    {
        get
        {
            var top = this.Height / 2;
            return new(this.Width - this.ButtonWidth, top, this.ButtonWidth, this.Height - top);
        }
    }

    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        _editor.Bounds = new(0, 0, Math.Max(0, this.Width - this.ButtonWidth), this.Height);
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _pressedDirection = 0;
        _autoRepeat?.Dispose();
        _autoRepeat = null;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        var up = this.UpButtonRect;
        var down = this.DownButtonRect;
        if (_pressedDirection > 0)
            g.FillRectangle(theme.Accent, up);
        else if (_pressedDirection < 0)
            g.FillRectangle(theme.Accent, down);

        var restingColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        DrawArrow(g, _pressedDirection > 0 ? theme.SelectionText : restingColor, up, pointsUp: true);
        DrawArrow(g, _pressedDirection < 0 ? theme.SelectionText : restingColor, down, pointsUp: false);

        // Seams between the field, the buttons, and around the whole control.
        g.DrawLine(theme.Border, up.X, 0, up.X, height - 1);
        g.DrawLine(theme.Border, up.X, down.Y, width - 1, down.Y);
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (this.UpButtonRect.Contains(e.Location))
            this.PressButton(+1);
        else if (this.DownButtonRect.Contains(e.Location))
            this.PressButton(-1);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => this.ReleaseButton();

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e) => this.ReleaseButton();

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                this.UpButton();
                e.Handled = true;
                break;

            case Keys.Down:
                this.DownButton();
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        this.CommitEdit();
    }

    /// <summary>Presses a spinner button: steps once and arms the autorepeat.</summary>
    private void PressButton(int direction)
    {
        _pressedDirection = direction;
        this.StepPressedButton();
        this.Invalidate();

        var backend = this.Backend;
        if (backend is null)
            return;

        _autoRepeat ??= new(this.StepPressedButton);
        _autoRepeat.Start(backend);
    }

    /// <summary>Releases a pressed spinner button and stops its autorepeat.</summary>
    private void ReleaseButton()
    {
        if (_pressedDirection == 0)
            return;

        _pressedDirection = 0;
        _autoRepeat?.Stop();
        this.Invalidate();
    }

    /// <summary>Steps once in the pressed button's direction; the autorepeat tick action.</summary>
    private void StepPressedButton()
    {
        if (_pressedDirection > 0)
            this.UpButton();
        else if (_pressedDirection < 0)
            this.DownButton();
    }

    /// <summary>Tracks editor changes: everything not written by <see cref="SetEditorText"/> becomes
    /// a pending user edit awaiting the next commit point.</summary>
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_updatingEditor)
            this.UserEdit = true;

        this.OnTextChanged(EventArgs.Empty);
    }

    /// <summary>Draws a small spinner triangle centered in <paramref name="rect"/>.</summary>
    private static void DrawArrow(IGraphics g, Color color, Rectangle rect, bool pointsUp)
    {
        var centerX = rect.X + rect.Width / 2;
        var top = rect.Y + (rect.Height - _ArrowRows) / 2;
        for (var i = 0; i < _ArrowRows; ++i)
        {
            var half = pointsUp ? i : _ArrowRows - 1 - i;
            g.DrawLine(color, centerX - half, top + i, centerX + half, top + i);
        }
    }
}
