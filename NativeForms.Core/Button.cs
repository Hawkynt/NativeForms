using System.Windows.Input;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A push button. Backed by the platform's native button widget, so it looks and behaves exactly
/// like every other button on the user's desktop.
/// </summary>
public class Button : Control
{
    private IButtonPeer? _buttonPeer;

    /// <summary>
    /// The image shown on the button face, or <see langword="null"/> for a text-only button. Rendered
    /// natively: GTK shows image and text side by side; a plain Win32 button renders the bitmap alone
    /// while <see cref="Control.Text"/> is empty and needs themed common controls (a visual-styles
    /// manifest) to draw image and text together — with classic rendering a captioned button keeps
    /// its text only.
    /// </summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.PushImage();
        }
    }

    /// <summary>
    /// Where the image anchors within the button face. Advisory for now: neither the Win32 button nor
    /// the GTK button offers free image placement, so no backend renders it; the value is forwarded
    /// so a capable backend can honor it. Defaults to <see cref="ContentAlignment.MiddleCenter"/>,
    /// matching Windows Forms.
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
        }
    } = ContentAlignment.MiddleCenter;

    /// <summary>
    /// How image and text share the button face. GTK honors the four directional values through the
    /// button's image position (<see cref="TextImageRelation.Overlay"/> renders as
    /// <see cref="TextImageRelation.ImageBeforeText"/>); Win32 push buttons offer no placement
    /// control, so the image sits wherever the theme puts it.
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

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateButton();

    private protected override void OnRealized(IControlPeer peer)
    {
        if (peer is not IButtonPeer button)
            return;

        _buttonPeer = button;
        button.Clicked += (_, _) => this.OnClick(EventArgs.Empty);
        this.PushImage();
        if (_isDefault)
            button.SetDefault(true);
    }

    /// <inheritdoc/>
    private protected override void OnUnrealized() => _buttonPeer = null;

    /// <summary>Forwards the buffered image triple to the realized peer.</summary>
    private void PushImage() => _buttonPeer?.SetImage(this.Image, this.ImageAlign, this.TextImageRelation);

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
