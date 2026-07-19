using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// A top-level window. Maps to a native window on every platform; its <see cref="Control.Text"/> is
/// the title-bar caption and its <see cref="Control.Controls"/> are laid out in the client area.
/// Shown modelessly through <see cref="Application.Run(Form)"/> or modally through
/// <see cref="ShowDialog"/>.
/// </summary>
public class Form : Control
{
    private IWindowPeer? _window;
    private bool _modal;

    /// <summary>The realized native window peer, or <see langword="null"/> before realization.</summary>
    internal IWindowPeer? WindowPeer => _window;

    /// <summary>Raised after the user closes the window.</summary>
    public event EventHandler? FormClosed;

    /// <summary>
    /// The verdict this form reports from <see cref="ShowDialog"/>. Setting a value other than
    /// <see cref="DialogResult.None"/> while the form is shown modally closes it — the WinForms
    /// contract a <see cref="Button.DialogResult"/> click relies on.
    /// </summary>
    public DialogResult DialogResult
    {
        get => field;
        set
        {
            field = value;
            if (value != DialogResult.None && _modal)
                this.Close();
        }
    }

    /// <summary>
    /// The button that should act on Enter. Stored for the pending §7.1 focus/key model — nothing
    /// routes the Enter key yet, so today the button only acts when actually clicked (honoring its
    /// <see cref="Button.DialogResult"/>).
    /// </summary>
    public Button? AcceptButton { get; set; }

    /// <summary>
    /// The button that should act on Escape. As in WinForms, assigning it gives the button a
    /// <see cref="DialogResult.Cancel"/> result when it still has none; Escape routing itself waits
    /// on the pending §7.1 focus/key model, so today the button only acts when actually clicked.
    /// </summary>
    public Button? CancelButton
    {
        get => field;
        set
        {
            field = value;
            if (value is { DialogResult: DialogResult.None })
                value.DialogResult = DialogResult.Cancel;
        }
    }

    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateWindow();

    /// <summary>Raises <see cref="FormClosed"/>.</summary>
    protected virtual void OnFormClosed(EventArgs e) => this.FormClosed?.Invoke(this, e);

    /// <inheritdoc/>
    private protected override void OnUnrealized() => _window = null;

    /// <summary>
    /// Closes the form as the native close button would. A no-op before realization; on a modal form
    /// this ends the <see cref="ShowDialog"/> loop.
    /// </summary>
    public void Close() => _window?.Close();

    /// <summary>
    /// Shows this form modally: <paramref name="owner"/> (when given) is disabled while a nested
    /// native message loop runs, and the call blocks until the form closes. Returns
    /// <see cref="DialogResult"/> — <see cref="DialogResult.Cancel"/> when the form was closed
    /// without a verdict (close box, Alt+F4). The form unrealizes on return and can be shown again.
    /// </summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public DialogResult ShowDialog(Form? owner = null)
        => this.ShowDialog(owner, Application.Current ?? throw new InvalidOperationException(
            "Form.ShowDialog needs a running backend — call it from inside Application.Run."));

    /// <summary>Shows this form modally on an explicit backend. Intended for tests.</summary>
    internal DialogResult ShowDialog(Form? owner, IPlatformBackend backend)
    {
        if (_modal)
            throw new InvalidOperationException("The form is already being shown modally.");

        this.DialogResult = DialogResult.None;
        var window = this.RealizeAsWindow(backend);
        _modal = true;
        try
        {
            window.RunModal(owner?.WindowPeer);
        }
        finally
        {
            _modal = false;
            this.DisposePeerTree();
        }

        if (this.DialogResult == DialogResult.None)
            this.DialogResult = DialogResult.Cancel;

        return this.DialogResult;
    }

    /// <summary>
    /// Realizes this form and its children against <paramref name="backend"/>, then shows it. Returns
    /// the native window peer that <see cref="Application.Run(Form)"/> hands to the message loop.
    /// Child realization happens inside <see cref="Control.RealizeSelf"/>, which walks the whole
    /// control tree depth-first.
    /// </summary>
    internal IWindowPeer RealizeWindow(IPlatformBackend backend)
    {
        var window = this.RealizeAsWindow(backend);
        window.Show();
        return window;
    }

    /// <summary>Realizes the control tree into a window peer and wires its close notification.</summary>
    private IWindowPeer RealizeAsWindow(IPlatformBackend backend)
    {
        var window = (IWindowPeer)this.RealizeSelf(backend);
        _window = window;
        window.Closed += (_, _) => this.OnFormClosed(EventArgs.Empty);
        return window;
    }
}
