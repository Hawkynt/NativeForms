using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>A per-chip visual style returned by <see cref="TokenBox.ChipStyleProvider"/>. Any
/// <see langword="null"/> colour falls back to the accent-tinted default; <see cref="FontStyle.Regular"/>
/// keeps the control font.</summary>
public readonly struct TokenChipStyle
{
    /// <summary>The chip fill, or <see langword="null"/> for the default accent tint.</summary>
    public Color? BackColor { get; init; }

    /// <summary>The chip text and × colour, or <see langword="null"/> for the theme text colour.</summary>
    public Color? ForeColor { get; init; }

    /// <summary>The chip font style (e.g. <see cref="FontStyle.Italic"/>/<see cref="FontStyle.Bold"/>).</summary>
    public FontStyle FontStyle { get; init; }
}

/// <summary>
/// A tag / chip input: a text field whose committed entries become removable chips. Enter or a comma
/// turns the typed text into a chip; each chip carries an × zone that deletes it and Backspace on the
/// empty editor removes the last one. An optional <see cref="AutoCompleteSource"/> delegate drops down a
/// filtered suggestion list under the editor. The chips flow left-to-right and wrap; a hosted native
/// <see cref="TextBox"/> holds the caret so typing, selection and clipboard stay platform-native.
/// Used for tags, recipient fields and search scopes.
/// </summary>
public class TokenBox : OwnerDrawnControl
{
    private const int _ChipHeight = 20;
    private const int _ChipGapX = 4;
    private const int _ChipGapY = 4;
    private const int _ChipPadX = 8;
    private const int _RemoveZone = 16;   // the × hit zone at a chip's right edge
    private const int _EditorMinWidth = 60;
    private const int _Inset = 4;
    private const int _MaxSuggestRows = 8;

    private readonly List<string> _tokens = [];
    private readonly TextBox _editor;

    // The suggestion drop-down, allocated lazily on the first suggestion and only while it has candidates.
    private IPopupPeer? _suggestPopup;
    private readonly List<string> _suggestions = [];
    private int _suggestHover = -1;
    private bool _suggestShown;

    /// <summary>Creates the token field and its hosted editor.</summary>
    public TokenBox()
    {
        _editor = new FramelessTextBox { TabStop = false };
        _editor.KeyDown += this.OnEditorKeyDown;
        _editor.TextChanged += this.OnEditorTextChanged;
        this.Controls.Add(_editor);
    }

    /// <summary>The committed chips, in order.</summary>
    public IReadOnlyList<string> Tokens => _tokens;

    /// <summary>The greyed hint shown while the field is empty.</summary>
    public string PlaceholderText
    {
        get => _editor.PlaceholderText;
        set => _editor.PlaceholderText = value;
    }

    /// <summary>Whether the same token may be added twice. Defaults to <see langword="false"/>.</summary>
    public bool AllowDuplicates { get; set; }

    /// <summary>A filter over a typed prefix producing suggestions dropped down under the editor, or
    /// <see langword="null"/> for no autocomplete.</summary>
    public Func<string, IReadOnlyList<string>>? AutoCompleteSource { get; set; }

    /// <summary>An optional per-chip style (fill, text colour, font style) chosen from the token text, so a
    /// host can colour-code chips or italicise/bolden them. <see langword="null"/> uses the accent-tinted
    /// default.</summary>
    public Func<string, TokenChipStyle>? ChipStyleProvider
    {
        get => field;
        set { field = value; this.Invalidate(); }
    }

    /// <summary>Raised whenever the chip set changes (add or remove).</summary>
    public event EventHandler? TokensChanged;

    /// <summary>Raises <see cref="TokensChanged"/>.</summary>
    protected virtual void OnTokensChanged(EventArgs e) => this.TokensChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The keyboard belongs to the hosted editor.</summary>
    private protected override Control FocusTarget => _editor;

    /// <inheritdoc/>
    private protected override Color FallbackBackColor => this.Theme.FieldBackground;

    /// <summary>Enter and comma commit a chip, so they stay out of the form's AcceptButton routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter;

    /// <summary>The current suggestion list, for headless tests.</summary>
    internal IReadOnlyList<string> SuggestionsForTest => _suggestions;

    /// <summary>Whether the suggestion drop-down is shown, for headless tests.</summary>
    internal bool SuggestionsShownForTest => _suggestShown;

    /// <summary>Adds <paramref name="text"/> as a chip (unless empty or a rejected duplicate).</summary>
    public void AddToken(string text)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return;

        if (!this.AllowDuplicates && _tokens.Contains(text))
            return;

        _tokens.Add(text);
        this.LayoutEditor();
        this.Invalidate();
        this.OnTokensChanged(EventArgs.Empty);
    }

    /// <summary>Removes the chip at <paramref name="index"/>; a no-op if out of range.</summary>
    public void RemoveToken(int index)
    {
        if (index < 0 || index >= _tokens.Count)
            return;

        _tokens.RemoveAt(index);
        this.LayoutEditor();
        this.Invalidate();
        this.OnTokensChanged(EventArgs.Empty);
    }

    /// <summary>Clears every chip.</summary>
    public void ClearTokens()
    {
        if (_tokens.Count == 0)
            return;

        _tokens.Clear();
        this.LayoutEditor();
        this.Invalidate();
        this.OnTokensChanged(EventArgs.Empty);
    }

    /// <inheritdoc/>
    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        this.LayoutEditor();
    }

    /// <inheritdoc/>
    private protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        this.LayoutEditor();
    }

    // Walks the chip flow and returns the rectangle each chip and finally the editor occupy. The caller
    // supplies a sink for chip rects (or null to only place the editor).
    private Rectangle FlowLayout(List<Rectangle>? chipRects)
    {
        var backend = this.Backend;
        var font = this.Font;
        var x = _Inset;
        var y = _Inset;
        var right = this.Width - _Inset;

        chipRects?.Clear();
        for (var i = 0; i < _tokens.Count; ++i)
        {
            var textWidth = backend?.MeasureText(_tokens[i], font).Width ?? (_tokens[i].Length * 7);
            var chipWidth = (2 * _ChipPadX) + textWidth + _RemoveZone;
            if (x + chipWidth > right && x > _Inset)
            {
                x = _Inset;
                y += _ChipHeight + _ChipGapY;
            }

            chipRects?.Add(new Rectangle(x, y, chipWidth, _ChipHeight));
            x += chipWidth + _ChipGapX;
        }

        // The editor takes the rest of the current row; if too little is left it drops to the next.
        if (right - x < _EditorMinWidth && x > _Inset)
        {
            x = _Inset;
            y += _ChipHeight + _ChipGapY;
        }

        return new Rectangle(x, y, Math.Max(_EditorMinWidth, right - x), _ChipHeight);
    }

    private void LayoutEditor()
    {
        if (!this.IsRealized)
            return;

        _editor.Bounds = this.FlowLayout(null);
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var chips = new List<Rectangle>(_tokens.Count);
        this.FlowLayout(chips);
        var defaultFill = Blend(theme.Accent, theme.FieldBackground, 0.16);
        for (var i = 0; i < chips.Count; ++i)
        {
            var chip = chips[i];
            var style = this.ChipStyleProvider?.Invoke(_tokens[i]) ?? default;
            var fill = style.BackColor ?? defaultFill;
            var ink = style.ForeColor ?? theme.ControlText;
            var font = style.FontStyle == FontStyle.Regular ? this.Font : this.Font.WithStyle(style.FontStyle);

            g.FillRoundedRectangle(fill, chip, _ChipHeight / 2);
            g.DrawText(_tokens[i], font, ink,
                new Rectangle(chip.X + _ChipPadX, chip.Y, chip.Width - (2 * _ChipPadX) - _RemoveZone + _ChipPadX, chip.Height),
                ContentAlignment.MiddleLeft);

            // The × in the trailing remove zone.
            var cx = chip.Right - (_RemoveZone / 2) - 2;
            var cy = chip.Y + (chip.Height / 2);
            g.DrawLine(ink, cx - 3, cy - 3, cx + 3, cy + 3);
            g.DrawLine(ink, cx - 3, cy + 3, cx + 3, cy - 3);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        var chips = new List<Rectangle>(_tokens.Count);
        this.FlowLayout(chips);
        for (var i = 0; i < chips.Count; ++i)
        {
            var chip = chips[i];
            if (!chip.Contains(e.Location))
                continue;

            var removeZone = new Rectangle(chip.Right - _RemoveZone, chip.Y, _RemoveZone, chip.Height);
            if (removeZone.Contains(e.Location))
                this.RemoveToken(i);

            return;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e) => this.Refilter();

    /// <summary>Commits the typed text (Enter/comma), removes the last chip on Backspace over an empty
    /// editor, or lets the suggestion list swallow navigation keys.</summary>
    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_suggestShown && this.HandleSuggestKey(e))
            return;

        switch (e.KeyCode)
        {
            case Keys.Enter:
                this.CommitEditor();
                e.Handled = true;
                break;

            case Keys.Oemcomma:
                this.CommitEditor();
                e.Handled = true;
                break;

            case Keys.Back when _editor.Text.Length == 0 && _tokens.Count > 0:
                this.RemoveToken(_tokens.Count - 1);
                e.Handled = true;
                break;
        }
    }

    private void CommitEditor()
    {
        var pick = _suggestShown && _suggestHover >= 0 ? _suggestions[_suggestHover] : _editor.Text;
        this.HideSuggestions();
        if (pick.Trim().Length == 0)
            return;

        this.AddToken(pick);
        _editor.Text = string.Empty;
    }

    // --- Autocomplete drop-down (a light-dismiss popup that routes navigation keys back here). ---

    private bool HandleSuggestKey(KeyEventArgs e)
    {
        var count = Math.Min(_suggestions.Count, _MaxSuggestRows);
        switch (e.KeyCode)
        {
            case Keys.Down:
                _suggestHover = _suggestHover + 1 >= count ? 0 : _suggestHover + 1;
                _suggestPopup?.InvalidateAll();
                e.Handled = true;
                return true;

            case Keys.Up:
                _suggestHover = _suggestHover <= 0 ? count - 1 : _suggestHover - 1;
                _suggestPopup?.InvalidateAll();
                e.Handled = true;
                return true;

            case Keys.Escape:
                this.HideSuggestions();
                e.Handled = true;
                return true;
        }

        return false;
    }

    private void Refilter()
    {
        var text = _editor.Text;
        if (this.AutoCompleteSource is not { } source || text.Trim().Length == 0)
        {
            this.HideSuggestions();
            return;
        }

        _suggestions.Clear();
        foreach (var candidate in source(text))
        {
            if (!this.AllowDuplicates && _tokens.Contains(candidate))
                continue;

            _suggestions.Add(candidate);
            if (_suggestions.Count >= _MaxSuggestRows)
                break;
        }

        this.ShowSuggestions();
    }

    private void ShowSuggestions()
    {
        if (_suggestions.Count == 0 || this.Backend is not { } backend)
        {
            this.HideSuggestions();
            return;
        }

        if (_suggestHover >= _suggestions.Count)
            _suggestHover = -1;

        var popup = _suggestPopup ??= this.CreateSuggestPopup(backend);
        if (_suggestShown)
        {
            popup.InvalidateAll();
            return;
        }

        var rows = Math.Min(_suggestions.Count, _MaxSuggestRows);
        var size = new Size(Math.Max(1, this.Width), rows * this.Theme.RowHeight);
        popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), size);
        popup.InvalidateAll();
        _suggestShown = true;
    }

    private void HideSuggestions()
    {
        _suggestHover = -1;
        if (!_suggestShown)
            return;

        _suggestShown = false;
        _suggestPopup?.Hide();
        _editor.Focus();
    }

    private IPopupPeer CreateSuggestPopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.OnSuggestPaint(e.Graphics);
        popup.MouseMove += (_, e) => this.OnSuggestMouseMove(e);
        popup.MouseDown += (_, e) => this.OnSuggestMouseDown(e);
        popup.KeyDown += (_, e) => this.HandleSuggestKey(e);
        popup.KeyPress += (_, e) => this.OnSuggestKeyPress(e);
        popup.Dismissed += (_, _) => { _suggestShown = false; _suggestHover = -1; };
        return popup;
    }

    private void OnSuggestPaint(IGraphics g)
    {
        var theme = this.Theme;
        var rowHeight = theme.RowHeight;
        var rows = Math.Min(_suggestions.Count, _MaxSuggestRows);
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, this.Width, rows * rowHeight));
        for (var i = 0; i < rows; ++i)
        {
            var row = new Rectangle(0, i * rowHeight, this.Width, rowHeight);
            if (i == _suggestHover)
                GlyphRenderer.FillSelection(g, theme, row);

            g.DrawText(_suggestions[i], this.Font, i == _suggestHover ? theme.SelectionText : theme.ControlText,
                new Rectangle(row.X + 6, row.Y, row.Width - 12, row.Height), ContentAlignment.MiddleLeft);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, (rows * rowHeight) - 1));
    }

    private void OnSuggestMouseMove(MouseEventArgs e)
    {
        var hover = e.Y / this.Theme.RowHeight;
        if (hover == _suggestHover || hover < 0 || hover >= Math.Min(_suggestions.Count, _MaxSuggestRows))
            return;

        _suggestHover = hover;
        _suggestPopup?.InvalidateAll();
    }

    private void OnSuggestMouseDown(MouseEventArgs e)
    {
        var index = e.Y / this.Theme.RowHeight;
        if (index < 0 || index >= Math.Min(_suggestions.Count, _MaxSuggestRows))
            return;

        var picked = _suggestions[index];
        this.HideSuggestions();
        this.AddToken(picked);
        _editor.Text = string.Empty;
    }

    private void OnSuggestKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
            return;

        _editor.Text += e.KeyChar; // OnEditorTextChanged refilters
        _editor.Focus();
    }

    private static Color Blend(Color a, Color b, double t)
        => Color.FromArgb(255,
            (int)Math.Round((a.R * t) + (b.R * (1 - t))),
            (int)Math.Round((a.G * t) + (b.G * (1 - t))),
            (int)Math.Round((a.B * t) + (b.B * (1 - t))));
}
