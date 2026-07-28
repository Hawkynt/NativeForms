using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn radio button painted in the native theme (themed ring, accent dot, themed text).
/// Selecting it — by click or Space — checks it, unchecks its sibling radio buttons in the same parent
/// and raises <see cref="Control.Click"/>. Clearing <see cref="Checked"/> directly is permitted.
/// </summary>
public class RadioButton : OwnerDrawnControl
{
    private const int _CircleSize = 14;
    private const int _TextGap = 6;

    /// <summary>Whether this button is the selected one in its group.</summary>
    public bool Checked
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _native?.SetChecked(value);
            if (value)
                this.UncheckSiblings();

            this.Invalidate();
            this.OnCheckedChanged(EventArgs.Empty);
        }
    }

    /// <summary>
    /// An optional icon rendered between the ring and the caption through the shared content layout;
    /// the text shifts right to make room.
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
            if (this.IsNativeWidget != this.WouldBeNative)
                this.RerealizePeer();

            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    private protected override IImage? AnimatedImageSlot => this.Image;

    /// <summary>Raised when <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    private IRadioButtonPeer? _native;
    private bool? _nativeOffered;


    /// <summary>Whether this button is currently rendered by a real platform widget.</summary>
    public override bool IsNativeWidget => _native is not null;

    /// <summary>
    /// Whether the current property values are all expressible by a platform radio button. An
    /// <see cref="Image"/> is not: neither platform's radio draws one beside the caption.
    /// </summary>
    private bool IsNativeEligible => this.Image is null;

    /// <summary>What <see cref="IsNativeWidget"/> would be if the peer were built right now.</summary>
    private bool WouldBeNative
        => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible && (_nativeOffered ?? true);

    /// <inheritdoc/>
    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
    {
        if ((this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible)
        {
            var offered = backend.CreateRadioButton();
            _nativeOffered = offered is not null;
            if (offered is { } peer)
            {
                _native = peer;
                peer.SetChecked(this.Checked);
                peer.CheckedChanged += this.OnNativeCheckedChanged;
                return peer;
            }
        }

        return base.CreatePeer(backend);
    }

    /// <summary>
    /// Adopts a click the widget reported. The peers are non-automatic, so the widget has not changed
    /// its own state yet and this runs the same <see cref="OnClick"/> path as the owner-drawn button —
    /// which checks it, unchecks the siblings and raises <see cref="Control.Click"/>.
    /// </summary>
    private void OnNativeCheckedChanged(object? sender, EventArgs e) => this.OnClick(EventArgs.Empty);

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

    /// <summary>Raises <see cref="CheckedChanged"/>.</summary>
    protected virtual void OnCheckedChanged(EventArgs e) => this.CheckedChanged?.Invoke(this, e);

    /// <summary>Selects this button (checking it) and raises <see cref="Control.Click"/>.</summary>
    protected void Select() => this.OnClick(EventArgs.Empty);

    /// <summary>Checks the button, then raises <see cref="Control.Click"/> — the Windows Forms
    /// order (<see cref="CheckedChanged"/> first), shared by mouse, Space and
    /// <see cref="Control.PerformClick"/>.</summary>
    protected override void OnClick(EventArgs e)
    {
        this.Checked = true;
        base.OnClick(e);
    }

    /// <summary>Clears the checked state of the sibling radio buttons in the same parent.</summary>
    private void UncheckSiblings()
    {
        var parent = this.Parent;
        if (parent is null)
            return;

        var siblings = parent.Controls;
        for (var i = 0; i < siblings.Count; ++i)
            if (siblings[i] is RadioButton sibling && !ReferenceEquals(sibling, this))
                sibling.Checked = false;
    }

    /// <summary>
    /// Focus arriving while no mouse press is in flight checks the button, like tabbing onto a
    /// Windows Forms radio selects it. The pipeline records the press before click-to-focus runs,
    /// so <see cref="OwnerDrawnControl.IsMousePressInFlight"/> is the honest keyboard gate — a
    /// click defers selection to the click path and selects exactly once.
    /// </summary>
    /// <remarks>
    /// Focus handed back by a peer swap is not the user arriving, so it selects nothing: crossing the
    /// promotion gate (PRD §12) must leave the group's selection exactly where it was, which matters
    /// most in a group whose members do not all take the same rendering path.
    /// </remarks>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (!IsRestoringFocus && !this.IsMousePressInFlight && !this.Checked)
            this.Checked = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitTest.ClientContains(this, e.Location))
            this.OnClick(EventArgs.Empty);
    }

    /// <summary>Space selects on the key <em>release</em>, like the Windows Forms button base — a
    /// held key must not auto-repeat the click.</summary>
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

        // Right-to-left mirrors the whole face: ring at the right edge, content anchored toward it,
        // image on the text's trailing (right) side.
        var rtl = this.IsRightToLeft;
        var client = this.DisplayRectangle;
        var circleTop = client.Y + Math.Max(0, (client.Height - _CircleSize) / 2);
        var circle = new Rectangle(client.X, circleTop, _CircleSize, _CircleSize);
        var content = new Rectangle(circle.Right + _TextGap, client.Y, client.Right - circle.Right - _TextGap, client.Height);
        var alignment = ContentAlignment.MiddleLeft;
        if (rtl)
        {
            circle = RtlLayout.Mirror(circle, this.Width);
            content = RtlLayout.Mirror(content, this.Width);
            alignment = RtlLayout.Mirror(alignment);
        }
        g.FillEllipse(theme.FieldBackground, circle);
        g.DrawEllipse(this.Checked ? theme.Accent : theme.Border, circle);

        if (this.Checked)
        {
            var inset = _CircleSize / 4;
            var dot = new Rectangle(circle.X + inset, circle.Y + inset, _CircleSize - 2 * inset, _CircleSize - 2 * inset);
            g.FillEllipse(theme.Accent, dot);
        }

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
