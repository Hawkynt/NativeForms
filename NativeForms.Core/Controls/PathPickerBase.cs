using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The shared engine behind <see cref="FilePicker"/> and <see cref="FolderPicker"/>: a hosted native
/// <see cref="TextBox"/> carrying the path, framed by an owner-drawn surface that paints a themed
/// browse button at the right edge. Clicking that button opens the platform's own file or folder
/// dialog and writes the choice back; Enter inside the editor commits a typed path.
/// </summary>
/// <remarks>
/// The committed path and the editor's live text are deliberately separate. <see cref="SelectedPath"/>
/// is the value the control stands behind; the editor may hold an uncommitted edit until a commit
/// point promotes it — Enter, focus leaving the field, a dialog result, or a programmatic assignment.
/// <see cref="PathExists"/> is refreshed at exactly those points and nowhere else, so the filesystem
/// is never touched from <see cref="OnPaint"/> (the §4 rule) nor once per keystroke.
/// Keyboard focus belongs to the hosted editor — it is the widget that takes text — so
/// <see cref="Control.Focus"/> lands there rather than on the painted shell, and Enter is claimed
/// back from it through <see cref="ITextBoxPeer.KeyDown"/>.
/// </remarks>
public abstract class PathPickerBase : OwnerDrawnControl
{
    /// <summary>The width of the trailing zone carrying the browse button.</summary>
    internal const int BrowseZoneWidth = 28;

    /// <summary>The caption of the browse button — the ellipsis every path field has worn since Win3.</summary>
    private const string _BrowseCaption = "…";

    private readonly TextBox _editor;
    private string _path = string.Empty;
    private bool _pathExists;

    /// <summary>Creates the picker shell and its hosted editor.</summary>
    private protected PathPickerBase()
    {
        _editor = new() { TabStop = false };
        _editor.TextChanged += this.OnEditorTextChanged;
        _editor.KeyDown += this.OnEditorKeyDown;

        // The commit-on-leave point has to hang off the editor, not off the shell: focus lives in the
        // hosted editor (see FocusTarget), so the shell's own peer never reports a loss and its
        // OnLostFocus would never run. Wiring it to the shell instead silently drops any path the
        // user typed and then tabbed away from.
        _editor.LostFocus += this.OnEditorLostFocus;
        this.Controls.Add(_editor);
    }

    /// <summary>The live content of the hosted editor, including an edit not committed yet.</summary>
    public override string Text
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    /// <summary>
    /// The committed path. Assigning rewrites the editor, re-evaluates <see cref="PathExists"/> and
    /// raises <see cref="PathChanged"/> when the value actually changed.
    /// </summary>
    public string SelectedPath
    {
        get => _path;
        set => this.Commit(value ?? string.Empty);
    }

    /// <summary>
    /// Whether the field is display-only: the path can still be selected and copied, but only the
    /// browse button may change it. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ReadOnlyText
    {
        get => _editor.ReadOnly;
        set => _editor.ReadOnly = value;
    }

    /// <summary>
    /// Whether the committed <see cref="SelectedPath"/> pointed at something real when it was last
    /// evaluated (see the class remarks for when that is). An empty path reports
    /// <see langword="false"/> without being flagged as broken.
    /// </summary>
    public bool PathExists => _pathExists;

    /// <summary>The greyed hint shown while the field is empty.</summary>
    public string PlaceholderText
    {
        get => _editor.PlaceholderText;
        set => _editor.PlaceholderText = value;
    }

    /// <summary>Raised after <see cref="SelectedPath"/> changed, however it was committed.</summary>
    public event EventHandler? PathChanged;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The keyboard belongs to the hosted editor, not to the painted shell around it.</summary>
    private protected override Control FocusTarget => _editor;

    /// <summary>Enter commits the typed path, so it stays out of the form's AcceptButton routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter;

    /// <summary>Opens the platform dialog this picker browses with and returns the chosen path, or
    /// <see langword="null"/> when the user backed out.</summary>
    private protected abstract string? Browse(IPlatformBackend backend);

    /// <summary>Whether <paramref name="path"/> currently names something on disk — a file for a file
    /// picker, a directory for a folder picker.</summary>
    private protected abstract bool Exists(string path);

    /// <summary>Whether <paramref name="path"/> is a value this picker may stand behind at all. A veto
    /// is not the same as "does not exist yet": an empty field and a not-yet-written save target both
    /// commit and merely flag through <see cref="PathExists"/>, whereas a vetoed value is refused
    /// outright and never becomes <see cref="SelectedPath"/>. The base picker vetoes nothing; a
    /// <see cref="FilePicker"/> in Open mode uses it to refuse a directory, which can never be a file
    /// selection.</summary>
    private protected virtual bool CanCommit(string path) => true;

    /// <summary>Raises <see cref="PathChanged"/>.</summary>
    protected virtual void OnPathChanged(EventArgs e) => this.PathChanged?.Invoke(this, e);

    /// <summary>Opens the browse dialog exactly as a click on the button would, and commits an OK.</summary>
    public void PerformBrowse()
    {
        if (this.Backend is not { } backend)
            return;

        if (this.Browse(backend) is { } picked)
            this.Commit(picked);

        this.AfterBrowse();
    }

    /// <summary>
    /// Runs after a browse has been committed, whatever its outcome. The hook a picker needs when
    /// its dialog carries more out than the one path <see cref="Browse"/> returns — a multi-selection
    /// has to be applied after the commit, or the commit's own bookkeeping would narrow it back to
    /// a single entry.
    /// </summary>
    private protected virtual void AfterBrowse() { }

    /// <summary>The trailing zone the browse button occupies.</summary>
    private Rectangle BrowseButtonRect => new(this.Width - BrowseZoneWidth, 0, BrowseZoneWidth, this.Height);

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        _editor.Bounds = new(0, 0, Math.Max(0, this.Width - BrowseZoneWidth), this.Height);
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        // The browse button reuses the shared themed button face rather than restating it.
        GlyphRenderer.DrawButtonFace(g, theme, this.BrowseButtonRect, _BrowseCaption, this.Enabled);

        // A committed path that names nothing is flagged by the frame — the cheapest signal that
        // costs no layout and no extra widget. An empty field is not an error.
        var broken = _path.Length > 0 && !_pathExists;
        g.DrawRectangle(broken ? GlyphRenderer.Warning : theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !this.BrowseButtonRect.Contains(e.Location))
            return;

        this.PerformBrowse();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Enter)
            return;

        this.Commit(_editor.Text);
        e.Handled = true;
    }

    /// <summary>Commits a pending edit when the keyboard leaves the hosted editor.</summary>
    private void OnEditorLostFocus(object? sender, EventArgs e) => this.Commit(_editor.Text);

    /// <summary>Promotes a path to the committed one: rewrites the editor, re-evaluates existence,
    /// repaints and reports the change. A no-op when nothing actually moved.</summary>
    private protected void Commit(string path)
    {
        // A vetoed value never becomes the committed path: the earlier one stands and the editor is
        // pulled back to it, so the field always shows something the picker will actually stand behind
        // (a file, never a directory, for a FilePicker in Open mode). WinForms' Open dialog does the
        // same — a folder is navigated into, never returned as the selection.
        if (path.Length > 0 && !this.CanCommit(path))
        {
            if (!string.Equals(_editor.Text, _path, StringComparison.Ordinal))
                _editor.Text = _path;

            return;
        }

        if (string.Equals(_path, path, StringComparison.Ordinal))
        {
            // The editor may still hold a discarded edit that resolved back to the committed value.
            if (!string.Equals(_editor.Text, path, StringComparison.Ordinal))
                _editor.Text = path;

            return;
        }

        _path = path;
        _pathExists = path.Length > 0 && this.Exists(path);
        if (!string.Equals(_editor.Text, path, StringComparison.Ordinal))
            _editor.Text = path;

        this.Invalidate();
        this.OnPathChanged(EventArgs.Empty);
    }

    /// <summary>Mirrors editor edits out as <see cref="Control.TextChanged"/> without committing them.</summary>
    private void OnEditorTextChanged(object? sender, EventArgs e) => this.OnTextChanged(EventArgs.Empty);

    /// <summary>Claims Enter back from the hosted editor, so a path typed into it still commits.</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e) => this.OnKeyDown(e);
}
