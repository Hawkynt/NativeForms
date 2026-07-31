using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn check box painted in the native theme (themed box, accent checkmark, themed text).
/// Toggles on click or Space and raises <see cref="CheckedChanged"/>.
/// </summary>
public class CheckBox : OwnerDrawnControl
{
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.CheckButton;

    private const int _TextGap = 6;

    private ICheckBoxPeer? _native;

    /// <summary>Whether the backend offered a widget the last time we asked; <see langword="null"/> until
    /// we have. Cached so a property change can tell whether it would actually flip the rendering path,
    /// instead of rebuilding a canvas peer that was never going to be promoted anyway.</summary>
    private bool? _nativeOffered;


    /// <summary>Whether the control is currently backed by a real platform check box.</summary>
    public override bool IsNativeWidget => _native is not null;

    /// <summary>
    /// Whether the configured properties stay inside what a platform check box can express. An
    /// <see cref="Image"/> is the one thing neither Win32's <c>BS_AUTOCHECKBOX</c> nor
    /// <c>GtkCheckButton</c> renders beside the caption the way this control does, so a box with one
    /// stays owner-drawn.
    /// </summary>
    private bool IsNativeEligible => this.Image is null;

    /// <summary>Which path this control would take if it were realized right now — the gate, the
    /// preference, and whether the backend has ever offered us a widget (optimistic until it is asked).</summary>
    private bool WouldBeNative
        => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible && (_nativeOffered ?? true);

    /// <summary>
    /// Whether the box is checked. <see cref="CheckState.Indeterminate"/> reads as
    /// <see langword="true"/> — the Windows Forms rule — so an application that only asks the boolean
    /// question never mistakes "mixed" for "off". Assigning projects back onto the two plain states.
    /// </summary>
    public bool Checked
    {
        get => this.CheckState is not CheckState.Unchecked;
        set => this.CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    /// <summary>
    /// The three-valued state. Setting it raises <see cref="CheckStateChanged"/>, and
    /// <see cref="CheckedChanged"/> as well whenever the change moved <see cref="Checked"/> — a box
    /// going from checked to indeterminate has changed state without changing that answer.
    /// </summary>
    public CheckState CheckState
    {
        get => field;
        set
        {
            if (field == value)
                return;

            var wasChecked = this.Checked;
            field = value;
            _native?.SetCheckState(value); // silent: the peer must not re-raise what we are about to raise
            this.Invalidate();
            this.OnCheckStateChanged(EventArgs.Empty);
            if (this.Checked != wasChecked)
                this.OnCheckedChanged(EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether clicking cycles through <see cref="CheckState.Indeterminate"/> as well. Off by default,
    /// so a plain box keeps toggling between two states; the third can still be assigned directly, which
    /// is how a box that summarises a set shows the set disagreeing without inviting the user to pick
    /// "mixed" by hand.
    /// </summary>
    public bool ThreeState
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _native?.SetThreeState(value);
        }
    }

    /// <summary>
    /// An optional icon rendered between the check square and the caption through the shared content
    /// layout; the text shifts right to make room.
    /// </summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.UpdateImageAnimation();

            // An image is outside what a platform check box can draw, so gaining or losing one moves the
            // control between the native widget and the canvas rather than silently dropping the image.
            if (this.IsNativeWidget != this.WouldBeNative)
                this.RerealizePeer();

            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    private protected override IImage? AnimatedImageSlot => this.Image;

    /// <inheritdoc/>
    /// <remarks>
    /// The promotion point (PRD §12): when the app prefers native widgets, the gate passes and the backend
    /// offers one, the box becomes a real platform check box; otherwise it falls back to the owner-drawn
    /// canvas. The public surface is identical either way — the only observable difference is
    /// <see cref="IsNativeWidget"/>.
    /// </remarks>
    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        if ((this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible)
        {
            var offered = backend.CreateCheckBox();
            _nativeOffered = offered is not null;
            if (offered is { } peer)
            {
                _native = peer;
                peer.SetThreeState(this.ThreeState); // before the state: the widget must be able to hold it
                peer.SetCheckState(this.CheckState);
                peer.CheckedChanged += this.OnNativeCheckedChanged;
                return peer;
            }
        }

        return base.CreatePeer(backend);
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        if (_native is { } peer)
        {
            peer.CheckedChanged -= this.OnNativeCheckedChanged;
            _native = null;
        }

        base.OnUnrealized();
    }

    /// <summary>The widget toggled itself; mirror it into the managed state and raise the public event
    /// exactly once, so a handler cannot tell which path it is on.</summary>
    private void OnNativeCheckedChanged(object? sender, EventArgs e)
    {
        if (_native is { } peer)
            this.CheckState = peer.GetCheckState();
    }

    /// <summary>Raised when <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Raised when <see cref="CheckState"/> changes, including the moves between checked and
    /// indeterminate that leave <see cref="Checked"/> alone.</summary>
    public event EventHandler? CheckStateChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Raises <see cref="CheckedChanged"/>.</summary>
    protected virtual void OnCheckedChanged(EventArgs e) => this.CheckedChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="CheckStateChanged"/>.</summary>
    protected virtual void OnCheckStateChanged(EventArgs e) => this.CheckStateChanged?.Invoke(this, e);

    /// <summary>Toggles the checked state and raises <see cref="Control.Click"/>.</summary>
    protected void Toggle() => this.OnClick(EventArgs.Empty);

    /// <summary>Advances <see cref="CheckState"/>, then raises <see cref="Control.Click"/> — the
    /// Windows Forms order (<see cref="CheckedChanged"/> first), shared by mouse, Space and
    /// <see cref="Control.PerformClick"/>.</summary>
    /// <remarks>
    /// A two-state box flips; a <see cref="ThreeState"/> one walks unchecked → checked → indeterminate
    /// → unchecked. A box left indeterminate without <see cref="ThreeState"/> clears on the next click
    /// rather than sticking, which is the only way out an application that assigned the state directly
    /// leaves the user.
    /// </remarks>
    protected override void OnClick(EventArgs e)
    {
        this.CheckState = this.CheckState switch
        {
            CheckState.Unchecked => CheckState.Checked,
            CheckState.Checked when this.ThreeState => CheckState.Indeterminate,
            _ => CheckState.Unchecked,
        };

        base.OnClick(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitTest.ClientContains(this, e.Location))
            this.OnClick(EventArgs.Empty);
    }

    /// <summary>Space toggles on the key <em>release</em>, like the Windows Forms button base — a
    /// held key must not auto-repeat the toggle.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Space)
            return;

        this.OnClick(EventArgs.Empty);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var font = this.Font;
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        // Right-to-left mirrors the whole face: check square at the right edge, content anchored
        // toward it, image on the text's trailing (right) side.
        var rtl = this.IsRightToLeft;
        var client = this.DisplayRectangle;
        var boxTop = client.Y + Math.Max(0, (client.Height - GlyphRenderer.CheckBoxSize) / 2);
        var box = new Rectangle(client.X, boxTop, GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
        var content = new Rectangle(box.Right + _TextGap, client.Y, client.Right - box.Right - _TextGap, client.Height);
        var alignment = ContentAlignment.MiddleLeft;
        if (rtl)
        {
            box = RtlLayout.Mirror(box, this.Width);
            content = RtlLayout.Mirror(content, this.Width);
            alignment = RtlLayout.Mirror(alignment);
        }

        GlyphRenderer.DrawCheckBox(g, theme, box, this.CheckState);

        var textColor = this.Enabled ? this.ForeColor : theme.DisabledText;
        if (this.Image is { } image)
        {
            ContentLayout.Arrange(
                content,
                new Size(image.Width, image.Height),
                g.MeasureText(this.Text, font),
                rtl ? TextImageRelation.TextBeforeImage : TextImageRelation.ImageBeforeText,
                alignment,
                out var imageRect,
                out var textRect);
            g.DrawImage(this.CurrentFrameOf(image)!, imageRect);
            g.DrawText(this.Text, font, textColor, textRect, alignment);
        }
        else
            g.DrawText(this.Text, font, textColor, content, alignment);
    }
}
