using System.Drawing;
using System.Windows.Input;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A push button. Backed by the platform's native button widget, so it looks and behaves exactly
/// like every other button on the user's desktop — and owner-drawn in that platform's own theme for
/// the one face no platform button can express, an image beside a caption (PRD §12).
/// </summary>
public class Button : OwnerDrawnControl
{
    /// <inheritdoc/>
    private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.PushButton;

    private IButtonPeer? _buttonPeer;

    /// <summary>Whether a press that started on this button is still down, which is what the painted
    /// face shows sunken.</summary>
    private bool _pressed;

    /// <summary>
    /// The image shown on the button face, or <see langword="null"/> for a text-only button. An image
    /// on its own is rendered by the widget, which centres it on all three platforms. An image
    /// <em>beside a caption</em> is the promotion gate (§12): none of the three draws both the same
    /// way — GTK places the image, a classic Win32 button renders the bitmap alone and drops the
    /// caption, AppKit has its own idea — so a captioned image gives up the widget and is painted
    /// through the shared <see cref="ContentLayout"/>, which comes out identical everywhere.
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
            this.ReconsiderPromotion();
            this.PushImage();
            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    private protected override IImage? AnimatedImageSlot => this.Image;

    /// <summary>
    /// Where the image anchors within the button face. Honoured exactly by the painted face; advisory
    /// on the widget, which offers no free placement on any backend but is handed the value anyway so
    /// a capable one can use it. Defaults to <see cref="ContentAlignment.MiddleCenter"/>, matching
    /// Windows Forms.
    /// </summary>
    public ContentAlignment ImageAlign
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PushImage();
            this.Invalidate();
        }
    } = ContentAlignment.MiddleCenter;

    /// <summary>
    /// How image and text share the button face. Honoured exactly by the painted face — which is the
    /// half that draws whenever there is both an image and a caption to arrange. On the widget it is
    /// advisory: GTK maps the four directional values onto the button's image position
    /// (<see cref="TextImageRelation.Overlay"/> renders as
    /// <see cref="TextImageRelation.ImageBeforeText"/>), and Win32 offers no placement control at all.
    /// </summary>
    public TextImageRelation TextImageRelation
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PushImage();
            this.Invalidate();
        }
    } = TextImageRelation.ImageBeforeText;

    /// <summary>
    /// The verdict a click reports to the owning <see cref="Form"/>. Anything other than
    /// <see cref="DialogResult.None"/> makes a click set <see cref="Form.DialogResult"/>, which in
    /// turn closes the form when it is shown modally — exactly the WinForms dialog contract.
    /// </summary>
    public DialogResult DialogResult { get; set; }

    /// <summary>
    /// The MVVM command a click executes (with <see cref="CommandParameter"/>). Attaching a command
    /// puts <see cref="Control.Enabled"/> under its guard: <see cref="ICommand.CanExecute"/> is
    /// applied immediately and re-applied on every <see cref="ICommand.CanExecuteChanged"/>, so a
    /// view-model greys the button out automatically. Setting <see langword="null"/> detaches the
    /// subscription and leaves <see cref="Control.Enabled"/> at its last value.
    /// </summary>
    public ICommand? Command
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            if (field is not null)
                field.CanExecuteChanged -= this.OnCommandCanExecuteChanged;

            field = value;
            if (value is null)
                return;

            value.CanExecuteChanged += this.OnCommandCanExecuteChanged;
            this.Enabled = value.CanExecute(this.CommandParameter);
        }
    }

    /// <summary>The argument handed to <see cref="Command"/>'s guard and execute delegates.
    /// Changing it re-queries the guard.</summary>
    public object? CommandParameter
    {
        get => field;
        set
        {
            if (Equals(field, value))
                return;

            field = value;
            if (this.Command is { } command)
                this.Enabled = command.CanExecute(value);
        }
    }

    /// <inheritdoc/>
    public override bool IsNativeWidget => _buttonPeer is not null;

    /// <summary>
    /// Whether the configured properties stay inside what a platform button can express. Everything
    /// does except an image with a caption beside it — see <see cref="Image"/>.
    /// </summary>
    private bool IsNativeEligible => this.Image is null || this.Text.Length == 0;

    /// <summary>What <see cref="IsNativeWidget"/> would be if the peer were built right now.</summary>
    private bool WouldBeNative => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible;

    /// <summary>Moves the button between the widget and the canvas when a property change crossed the
    /// gate. The swap runs both ways and is invisible to the application.</summary>
    private void ReconsiderPromotion()
    {
        if (this.IsRealized && this.IsNativeWidget != this.WouldBeNative)
            this.RerealizePeer();
    }

    /// <inheritdoc/>
    private protected override IControlPeer CreatePeer(IPlatformBackend backend)
        => this.WouldBeNative ? backend.CreateButton() : base.CreatePeer(backend);

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);

        if (peer is IButtonPeer button)
        {
            _buttonPeer = button;
            button.Clicked += (_, _) => this.OnClick(EventArgs.Empty);
            this.PushImage();
            if (_isDefault)
                button.SetDefault(true);
        }

        // After the base, which unsubscribes on the way in: a backend now exists, so an animated image
        // assigned before realization can finally subscribe — to whichever half is going to draw it.
        this.UpdateImageAnimation();
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        _buttonPeer = null;
        base.OnUnrealized();
    }

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        this.ReconsiderPromotion(); // a caption arriving beside an image crosses the gate
    }

    /// <summary>Forwards the buffered image triple to the realized peer, resolving an animated image to
    /// its current frame — the shared clock calls this again as the frame advances.</summary>
    private void PushImage() => _buttonPeer?.SetImage(this.CurrentFrameOf(this.Image), this.ImageAlign, this.TextImageRelation);

    // --- The painted half --------------------------------------------------------------------------

    /// <summary>A button takes the keyboard on either path.</summary>
    protected override bool Focusable => true;

    /// <summary>Space and Enter work the button rather than reaching the form's accept routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter;

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var font = this.Font;
        var face = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

        g.FillRectangle(this.Parent?.BackColor ?? theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));
        GlyphRenderer.DrawButtonFace(g, theme, face, this.Enabled, _pressed ? theme.SelectionBackground : null);

        // The default button carries the accent border the platform would give it; that emphasis is
        // what tells the user which button Enter works, so the painted half cannot drop it.
        if (_isDefault)
            g.DrawRoundedRectangle(theme.Accent, face, theme.ButtonCornerRadius);

        var caption = Mnemonics.Strip(this.Text);
        var color = this.Enabled ? this.ForeColor : theme.DisabledText;
        var client = this.DisplayRectangle;
        if (this.Image is not { } image)
        {
            this.PaintCaption(g, font, color, caption, client, ContentAlignment.MiddleCenter);
            return;
        }

        // Right-to-left mirrors which side the icon leads on, exactly like the CheckBox face.
        var relation = this.IsRightToLeft ? RtlLayout.Mirror(this.TextImageRelation) : this.TextImageRelation;
        ContentLayout.Arrange(
            client,
            new Size(image.Width, image.Height),
            caption.Length == 0 ? Size.Empty : g.MeasureText(caption, font),
            relation,
            caption.Length == 0 ? this.ImageAlign : ContentAlignment.MiddleCenter,
            out var imageRect,
            out var textRect);

        g.DrawImage(this.CurrentFrameOf(image)!, imageRect);
        if (caption.Length > 0)
            this.PaintCaption(g, font, color, caption, textRect, ContentAlignment.MiddleCenter);
    }

    /// <summary>Draws the caption with its mnemonic underlined, in the box the layout gave it.</summary>
    private void PaintCaption(IGraphics g, Font font, Color color, string caption, Rectangle bounds, ContentAlignment alignment)
    {
        g.DrawText(caption, font, color, bounds, alignment);

        if (this.Focused)
            GlyphRenderer.DrawFocusRing(
                g,
                this.Theme,
                new(2, 2, Math.Max(0, this.Width - 5), Math.Max(0, this.Height - 5)),
                Math.Max(0, this.Theme.ButtonCornerRadius - 2)); // inset by the same amount as the ring

        Mnemonics.Underline(g, this.Text, caption, font, color, bounds, alignment);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _pressed = true;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_pressed)
            return;

        _pressed = false;
        this.Invalidate();

        // A press that wandered off the face before it was released is a cancelled click, which is
        // what every platform button does and what a user expects to be able to do.
        if (HitTest.ClientContains(this, e.Location))
            this.OnClick(EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_pressed)
            return;

        _pressed = false;
        this.Invalidate();
    }

    /// <summary>Space and Enter activate on the key <em>release</em>, like the Windows Forms button
    /// base — a held key must not auto-repeat the click.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Space or Keys.Enter))
            return;

        this.OnClick(EventArgs.Empty);
        e.Handled = true;
    }

    private bool _isDefault;

    /// <summary>
    /// Whether this is the form's default (accept) button, which the platform paints with its default
    /// emphasis and which Enter activates. Set by <see cref="Form.AcceptButton"/>; buffered until the
    /// peer exists, like every other button setting.
    /// </summary>
    internal void SetDefault(bool isDefault)
    {
        if (_isDefault == isDefault)
            return;

        _isDefault = isDefault;
        _buttonPeer?.SetDefault(isDefault);
        this.Invalidate(); // the painted half draws the emphasis itself
    }

    /// <summary>The guard's answer may have changed; re-apply it to <see cref="Control.Enabled"/>.</summary>
    private void OnCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        if (this.Command is { } command)
            this.Enabled = command.CanExecute(this.CommandParameter);
    }

    /// <summary>Raises <see cref="Control.Click"/>, executes <see cref="Command"/> when its guard
    /// agrees, then reports <see cref="DialogResult"/> to the owning form.</summary>
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);

        var command = this.Command;
        if (command is not null && command.CanExecute(this.CommandParameter))
            command.Execute(this.CommandParameter);

        if (this.DialogResult == DialogResult.None)
            return;

        for (var parent = this.Parent; parent is not null; parent = parent.Parent)
            if (parent is Form form)
            {
                form.DialogResult = this.DialogResult;
                return;
            }
    }
}
