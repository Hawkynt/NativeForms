using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A multiline plain-text code surface: a line-number gutter, a current-line highlight, tab/indent
/// handling, a pluggable delegate <see cref="Tokenizer"/> that colours keyword / string / comment /
/// number spans per line, and an optional <see cref="CompletionProvider"/> autocomplete drop-down. The
/// caret and selection are managed by the control (it owner-draws the text, so colouring stays
/// native-themed). The editing centrepiece an IDE builds on — richer than <see cref="RichTextBox"/>'s flat
/// RTF for source code.
/// </summary>
public class CodeTextBox : OwnerDrawnControl
{
    private const int _GutterPad = 6;
    private const int _TextPad = 4;
    private const int _MaxCompletionRows = 8;

    private readonly List<string> _lines = [""];
    private int _caretLine, _caretCol;
    private int _anchorLine, _anchorCol;   // selection origin; equals the caret when there is no selection
    private int _topLine;                  // first visible line
    private int? _charWidth, _lineHeight;

    // The autocomplete drop-down: a grabbing popup whose keys are routed back into the editor.
    private IPopupPeer? _completionPopup;
    private readonly List<string> _completions = [];
    private int _completionHover = -1;
    private bool _completionShown;
    private int _completionWordStart;

    /// <summary>The tab stop width in spaces. Tab inserts this many spaces. Defaults to 4.</summary>
    public int TabWidth
    {
        get => field;
        set => field = Math.Clamp(value, 1, 16);
    } = 4;

    /// <summary>Whether the line-number gutter is drawn. Defaults to <see langword="true"/>.</summary>
    public bool ShowLineNumbers
    {
        get => field;
        set { if (field != value) { field = value; this.Invalidate(); } }
    } = true;

    /// <summary>Whether the caret's line is tinted. Defaults to <see langword="true"/>.</summary>
    public bool HighlightCurrentLine
    {
        get => field;
        set { if (field != value) { field = value; this.Invalidate(); } }
    } = true;

    /// <summary>Splits a line into coloured spans, or <see langword="null"/> for uncoloured text. Called
    /// per visible line each paint — keep it cheap.</summary>
    public Func<string, IReadOnlyList<CodeToken>>? Tokenizer
    {
        get => field;
        set { field = value; this.Invalidate(); }
    }

    /// <summary>Produces completion candidates for the identifier prefix before the caret, or
    /// <see langword="null"/> for no autocomplete. Invoked on Ctrl+Space and as an identifier is typed;
    /// picking a candidate replaces the current word. Return an empty list to close the drop-down.</summary>
    public Func<string, IReadOnlyList<string>>? CompletionProvider { get; set; }

    /// <summary>Whether the completion drop-down is open — for headless tests.</summary>
    internal bool CompletionShownForTest => _completionShown;

    /// <summary>The current completion candidates — for headless tests.</summary>
    internal IReadOnlyList<string> CompletionsForTest => _completions;

    /// <summary>The whole document. Getting joins the lines with '\n'; setting splits on newlines and
    /// resets the caret to the start.</summary>
    public override string Text
    {
        get => string.Join('\n', _lines);
        set
        {
            _lines.Clear();
            var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
                _lines.Add(line);

            if (_lines.Count == 0)
                _lines.Add(string.Empty);

            _caretLine = _caretCol = _anchorLine = _anchorCol = _topLine = 0;
            this.Invalidate();
            this.OnTextChanged(EventArgs.Empty);
        }
    }

    /// <summary>The document lines.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>The caret line index (zero-based).</summary>
    public int CaretLine => _caretLine;

    /// <summary>The caret column index (zero-based).</summary>
    public int CaretColumn => _caretCol;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <inheritdoc/>
    private protected override Color FallbackBackColor => this.Theme.FieldBackground;

    /// <summary>Tab indents and Enter inserts a line, so both are claimed from the form's dialog-key
    /// routing (Tab navigation, AcceptButton) and delivered to the editor instead.</summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Tab or Keys.Enter || base.IsInputKey(keyData);

    private bool HasSelection => _anchorLine != _caretLine || _anchorCol != _caretCol;

    private int CharWidth => _charWidth ??= Math.Max(1, this.Backend?.MeasureText("0", this.Font).Width ?? 7);
    private int LineHeight => _lineHeight ??= Math.Max(1, this.Backend?.MeasureText("Ag", this.Font).Height ?? 16);

    /// <summary>The pixel width of a text run in the current font — measured, not a monospace estimate, so
    /// the caret and coloured spans line up under a proportional font too.</summary>
    private int MeasureWidth(string text)
        => text.Length == 0 ? 0 : this.Backend?.MeasureText(text, this.Font).Width ?? (text.Length * this.CharWidth);
    private int VisibleLines => Math.Max(1, this.Height / this.LineHeight);
    private int GutterWidth => this.ShowLineNumbers ? (Math.Max(2, _lines.Count.ToString().Length) * this.CharWidth) + (2 * _GutterPad) : 0;
    private int TextLeft => this.GutterWidth + _TextPad;

    private Color ColorFor(CodeTokenKind kind) => kind switch
    {
        CodeTokenKind.Keyword => Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
        CodeTokenKind.String => Color.FromArgb(0xFF, 0xA3, 0x15, 0x15),
        CodeTokenKind.Comment => Color.FromArgb(0xFF, 0x00, 0x80, 0x00),
        CodeTokenKind.Number => Color.FromArgb(0xFF, 0x09, 0x86, 0x58),
        CodeTokenKind.Type => Color.FromArgb(0xFF, 0x26, 0x7F, 0x99),
        _ => this.ForeColor,
    };

    /// <inheritdoc/>
    private protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        this.Invalidate();
    }

    // --- Painting --------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var lineHeight = this.LineHeight;
        var charWidth = this.CharWidth;
        var textLeft = this.TextLeft;
        var gutterWidth = this.GutterWidth;

        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var last = Math.Min(_lines.Count, _topLine + this.VisibleLines + 1);
        for (var i = _topLine; i < last; ++i)
        {
            var y = (i - _topLine) * lineHeight;
            if (this.HighlightCurrentLine && i == _caretLine && this.Focused)
                g.FillRectangle(Blend(theme.Accent, this.BackColor, 0.08), new Rectangle(textLeft - _TextPad, y, this.Width - textLeft, lineHeight));

            this.PaintSelection(g, theme, i, y, textLeft, charWidth, lineHeight);
            this.PaintLine(g, i, y, textLeft, charWidth, lineHeight);
        }

        // The gutter is drawn last so it overlays the current-line tint and never scrolls text under it.
        if (this.ShowLineNumbers)
        {
            g.FillRectangle(theme.HeaderBackground, new Rectangle(0, 0, gutterWidth, this.Height));
            g.DrawLine(theme.Border, gutterWidth, 0, gutterWidth, this.Height);
            for (var i = _topLine; i < last; ++i)
            {
                var y = (i - _topLine) * lineHeight;
                var color = i == _caretLine ? theme.ControlText : theme.DisabledText;
                g.DrawText((i + 1).ToString(), this.Font, color, new Rectangle(0, y, gutterWidth - _GutterPad, lineHeight), ContentAlignment.MiddleRight);
            }
        }

        if (this.Focused && !this.HasSelection)
            this.PaintCaret(g, theme, textLeft, charWidth, lineHeight);

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintLine(IGraphics g, int lineIndex, int y, int textLeft, int charWidth, int lineHeight)
    {
        var text = _lines[lineIndex];
        if (text.Length == 0)
            return;

        var tokens = this.Tokenizer?.Invoke(text);
        if (tokens is null || tokens.Count == 0)
        {
            g.DrawText(text, this.Font, this.ForeColor, new Rectangle(textLeft, y, this.Width - textLeft, lineHeight), ContentAlignment.MiddleLeft);
            return;
        }

        // Draw each token span at its column; gaps between spans fall back to the foreground colour.
        var col = 0;
        foreach (var token in tokens)
        {
            if (token.Start > col)
                this.DrawSpan(g, text, col, token.Start - col, this.ForeColor, textLeft, y, charWidth, lineHeight);

            var start = Math.Clamp(token.Start, 0, text.Length);
            var length = Math.Clamp(token.Length, 0, text.Length - start);
            this.DrawSpan(g, text, start, length, this.ColorFor(token.Kind), textLeft, y, charWidth, lineHeight);
            col = start + length;
        }

        if (col < text.Length)
            this.DrawSpan(g, text, col, text.Length - col, this.ForeColor, textLeft, y, charWidth, lineHeight);
    }

    private void DrawSpan(IGraphics g, string text, int start, int length, Color color, int textLeft, int y, int charWidth, int lineHeight)
    {
        if (length <= 0)
            return;

        var slice = text.Substring(start, length);
        var x = textLeft + this.MeasureWidth(text[..start]);
        g.DrawText(slice, this.Font, color, new Rectangle(x, y, this.MeasureWidth(slice) + charWidth, lineHeight), ContentAlignment.MiddleLeft);
    }

    private void PaintSelection(IGraphics g, ITheme theme, int lineIndex, int y, int textLeft, int charWidth, int lineHeight)
    {
        if (!this.HasSelection)
            return;

        var (startLine, startCol, endLine, endCol) = this.OrderedSelection();
        if (lineIndex < startLine || lineIndex > endLine)
            return;

        var line = _lines[lineIndex];
        var from = lineIndex == startLine ? startCol : 0;
        var to = lineIndex == endLine ? endCol : line.Length;
        var x = textLeft + this.MeasureWidth(line[..Math.Min(from, line.Length)]);
        var width = this.MeasureWidth(line[Math.Min(from, line.Length)..Math.Min(to, line.Length)]);
        if (lineIndex != endLine)
            width += charWidth; // a trailing sliver hints the wrapped newline is part of the selection
        g.FillRectangle(theme.SelectionBackground, new Rectangle(x, y, Math.Max(charWidth / 2, width), lineHeight));
    }

    private void PaintCaret(IGraphics g, ITheme theme, int textLeft, int charWidth, int lineHeight)
    {
        if (_caretLine < _topLine || _caretLine >= _topLine + this.VisibleLines + 1)
            return;

        var x = textLeft + this.MeasureWidth(_lines[_caretLine][.._caretCol]);
        var y = (_caretLine - _topLine) * lineHeight;
        g.DrawLine(theme.ControlText, x, y + 1, x, y + lineHeight - 1);
    }

    // --- Selection helpers -----------------------------------------------------------------------

    private (int StartLine, int StartCol, int EndLine, int EndCol) OrderedSelection()
    {
        if (_anchorLine < _caretLine || (_anchorLine == _caretLine && _anchorCol <= _caretCol))
            return (_anchorLine, _anchorCol, _caretLine, _caretCol);

        return (_caretLine, _caretCol, _anchorLine, _anchorCol);
    }

    private void ClearSelection() => (_anchorLine, _anchorCol) = (_caretLine, _caretCol);

    private void DeleteSelection()
    {
        if (!this.HasSelection)
            return;

        var (startLine, startCol, endLine, endCol) = this.OrderedSelection();
        var head = _lines[startLine][..startCol];
        var tail = _lines[endLine][endCol..];
        _lines.RemoveRange(startLine, endLine - startLine + 1);
        _lines.Insert(startLine, head + tail);
        _caretLine = _anchorLine = startLine;
        _caretCol = _anchorCol = startCol;
    }

    // --- Input -----------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        this.CaretFromPoint(e.X, e.Y, out _caretLine, out _caretCol);
        this.ClearSelection();
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if ((e.Button & MouseButtons.Left) == 0)
            return;

        this.CaretFromPoint(e.X, e.Y, out _caretLine, out _caretCol);
        this.Invalidate();
    }

    private void CaretFromPoint(int x, int y, out int line, out int col)
    {
        line = Math.Clamp(_topLine + (y / this.LineHeight), 0, _lines.Count - 1);

        // Walk columns, accumulating measured widths, and land on the glyph boundary nearest the click.
        var text = _lines[line];
        var target = x - this.TextLeft;
        var prev = 0;
        col = text.Length;
        for (var i = 1; i <= text.Length; ++i)
        {
            var w = this.MeasureWidth(text[..i]);
            if (w >= target)
            {
                col = target - prev < w - target ? i - 1 : i;
                return;
            }

            prev = w;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var maxTop = Math.Max(0, _lines.Count - this.VisibleLines);
        _topLine = Math.Clamp(_topLine - (e.Delta > 0 ? 3 : -3), 0, maxTop);
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
            return;

        this.InsertText(e.KeyChar.ToString());
        e.Handled = true;
        this.MaybeAutoComplete();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left: this.MoveCaret(0, -1, e.Shift); break;
            case Keys.Right: this.MoveCaret(0, 1, e.Shift); break;
            case Keys.Up: this.MoveCaret(-1, 0, e.Shift); break;
            case Keys.Down: this.MoveCaret(1, 0, e.Shift); break;
            case Keys.Home: this.MoveCaretTo(_caretLine, 0, e.Shift); break;
            case Keys.End: this.MoveCaretTo(_caretLine, _lines[_caretLine].Length, e.Shift); break;
            case Keys.PageUp: this.MoveCaret(-this.VisibleLines, 0, e.Shift); break;
            case Keys.PageDown: this.MoveCaret(this.VisibleLines, 0, e.Shift); break;
            case Keys.A when e.Control: this.SelectAll(); break;
            case Keys.Space when e.Control: this.ShowCompletion(); e.Handled = true; return;
            case Keys.Enter: this.InsertNewLine(); break;
            case Keys.Tab: this.InsertText(new string(' ', this.TabWidth)); break;
            case Keys.Back: this.Backspace(); break;
            case Keys.Delete: this.DeleteForward(); break;
            default: return;
        }

        e.Handled = true;
        this.EnsureCaretVisible();
        this.Invalidate();
    }

    private void SelectAll()
    {
        _anchorLine = _anchorCol = 0;
        _caretLine = _lines.Count - 1;
        _caretCol = _lines[_caretLine].Length;
    }

    // --- Autocomplete drop-down ------------------------------------------------------------------

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private int WordStart()
    {
        var line = _lines[_caretLine];
        var i = _caretCol;
        while (i > 0 && IsWordChar(line[i - 1]))
            --i;

        return i;
    }

    /// <summary>Auto-opens (or refilters) the completion list as an identifier is typed.</summary>
    private void MaybeAutoComplete()
    {
        if (this.CompletionProvider is null)
            return;

        if (_caretCol > 0 && IsWordChar(_lines[_caretLine][_caretCol - 1]))
            this.ShowCompletion();
        else
            this.HideCompletion();
    }

    /// <summary>Opens or refilters the completion list for the identifier before the caret.</summary>
    public void ShowCompletion()
    {
        if (this.CompletionProvider is not { } provider || this.Backend is not { } backend)
            return;

        _completionWordStart = this.WordStart();
        var prefix = _lines[_caretLine][_completionWordStart.._caretCol];

        _completions.Clear();
        foreach (var candidate in provider(prefix))
        {
            _completions.Add(candidate);
            if (_completions.Count >= _MaxCompletionRows)
                break;
        }

        if (_completions.Count == 0)
        {
            this.HideCompletion();
            return;
        }

        if (_completionHover >= _completions.Count)
            _completionHover = 0;
        else if (_completionHover < 0)
            _completionHover = 0;

        var popup = _completionPopup ??= this.CreateCompletionPopup(backend);
        var rows = Math.Min(_completions.Count, _MaxCompletionRows);
        var y = ((_caretLine - _topLine) + 1) * this.LineHeight;
        var x = this.TextLeft + this.MeasureWidth(_lines[_caretLine][.._completionWordStart]);
        if (_completionShown)
        {
            popup.InvalidateAll();
            return;
        }

        popup.ShowAt(this.PointToScreen(new Point(x, y)), new Size(180, rows * this.LineHeight));
        popup.InvalidateAll();
        _completionShown = true;
    }

    private void HideCompletion()
    {
        _completionHover = -1;
        if (!_completionShown)
            return;

        _completionShown = false;
        _completionPopup?.Hide();
        this.Focus();
    }

    private IPopupPeer CreateCompletionPopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.OnCompletionPaint(e.Graphics);
        popup.MouseMove += (_, e) => this.OnCompletionMouseMove(e);
        popup.MouseDown += (_, e) => this.OnCompletionMouseDown(e);
        popup.KeyDown += (_, e) => this.OnCompletionKeyDown(e);
        popup.KeyPress += (_, e) => this.OnCompletionKeyPress(e);
        popup.Dismissed += (_, _) => { _completionShown = false; _completionHover = -1; };
        return popup;
    }

    private void OnCompletionPaint(IGraphics g)
    {
        var theme = this.Theme;
        var lineHeight = this.LineHeight;
        var rows = Math.Min(_completions.Count, _MaxCompletionRows);
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, 180, rows * lineHeight));
        for (var i = 0; i < rows; ++i)
        {
            var row = new Rectangle(0, i * lineHeight, 180, lineHeight);
            if (i == _completionHover)
                GlyphRenderer.FillSelection(g, theme, row);

            g.DrawText(_completions[i], this.Font, i == _completionHover ? theme.SelectionText : theme.ControlText,
                new Rectangle(row.X + 4, row.Y, row.Width - 8, row.Height), ContentAlignment.MiddleLeft);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, 179, (rows * lineHeight) - 1));
    }

    private void OnCompletionMouseMove(MouseEventArgs e)
    {
        var hover = e.Y / this.LineHeight;
        if (hover == _completionHover || hover < 0 || hover >= Math.Min(_completions.Count, _MaxCompletionRows))
            return;

        _completionHover = hover;
        _completionPopup?.InvalidateAll();
    }

    private void OnCompletionMouseDown(MouseEventArgs e) => this.AcceptCompletion(e.Y / this.LineHeight);

    private void OnCompletionKeyDown(KeyEventArgs e)
    {
        var count = Math.Min(_completions.Count, _MaxCompletionRows);
        switch (e.KeyCode)
        {
            case Keys.Down: _completionHover = _completionHover + 1 >= count ? 0 : _completionHover + 1; _completionPopup?.InvalidateAll(); e.Handled = true; break;
            case Keys.Up: _completionHover = _completionHover <= 0 ? count - 1 : _completionHover - 1; _completionPopup?.InvalidateAll(); e.Handled = true; break;
            case Keys.Enter or Keys.Tab: this.AcceptCompletion(_completionHover); e.Handled = true; break;
            case Keys.Escape: this.HideCompletion(); e.Handled = true; break;
            case Keys.Back:
                this.HideCompletion();
                this.Backspace();
                this.EnsureCaretVisible();
                this.Invalidate();
                this.MaybeAutoComplete();
                e.Handled = true;
                break;
        }
    }

    private void OnCompletionKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
            return;

        this.InsertText(e.KeyChar.ToString());
        this.MaybeAutoComplete(); // refilter against the extended prefix
    }

    /// <summary>Replaces the current word with the completion at <paramref name="index"/>.</summary>
    private void AcceptCompletion(int index)
    {
        if (index < 0 || index >= Math.Min(_completions.Count, _MaxCompletionRows))
            return;

        var pick = _completions[index];
        var line = _lines[_caretLine];
        _lines[_caretLine] = line[.._completionWordStart] + pick + line[_caretCol..];
        _caretCol = _completionWordStart + pick.Length;
        this.ClearSelection();
        this.HideCompletion();
        this.EnsureCaretVisible();
        this.Invalidate();
        this.OnTextChanged(EventArgs.Empty);
    }

    private void MoveCaret(int dLine, int dCol, bool select)
    {
        if (dLine != 0)
        {
            _caretLine = Math.Clamp(_caretLine + dLine, 0, _lines.Count - 1);
            _caretCol = Math.Min(_caretCol, _lines[_caretLine].Length);
        }
        else if (dCol != 0)
        {
            _caretCol += dCol;
            if (_caretCol < 0)
            {
                if (_caretLine > 0)
                {
                    --_caretLine;
                    _caretCol = _lines[_caretLine].Length;
                }
                else
                    _caretCol = 0;
            }
            else if (_caretCol > _lines[_caretLine].Length)
            {
                if (_caretLine < _lines.Count - 1)
                {
                    ++_caretLine;
                    _caretCol = 0;
                }
                else
                    _caretCol = _lines[_caretLine].Length;
            }
        }

        if (!select)
            this.ClearSelection();
    }

    private void MoveCaretTo(int line, int col, bool select)
    {
        _caretLine = Math.Clamp(line, 0, _lines.Count - 1);
        _caretCol = Math.Clamp(col, 0, _lines[_caretLine].Length);
        if (!select)
            this.ClearSelection();
    }

    private void InsertText(string text)
    {
        this.DeleteSelection();
        var line = _lines[_caretLine];
        _lines[_caretLine] = line[.._caretCol] + text + line[_caretCol..];
        _caretCol += text.Length;
        this.ClearSelection();
        this.EnsureCaretVisible();
        this.Invalidate();
        this.OnTextChanged(EventArgs.Empty);
    }

    private void InsertNewLine()
    {
        this.DeleteSelection();
        var line = _lines[_caretLine];
        var indent = LeadingWhitespace(line);
        var head = line[.._caretCol];
        var tail = line[_caretCol..];
        _lines[_caretLine] = head;
        _lines.Insert(_caretLine + 1, indent + tail);
        _caretLine++;
        _caretCol = indent.Length;
        this.ClearSelection();
        this.OnTextChanged(EventArgs.Empty);
    }

    private void Backspace()
    {
        if (this.HasSelection)
        {
            this.DeleteSelection();
            this.OnTextChanged(EventArgs.Empty);
            return;
        }

        if (_caretCol > 0)
        {
            var line = _lines[_caretLine];
            _lines[_caretLine] = line[..(_caretCol - 1)] + line[_caretCol..];
            _caretCol--;
        }
        else if (_caretLine > 0)
        {
            var prev = _lines[_caretLine - 1];
            _caretCol = prev.Length;
            _lines[_caretLine - 1] = prev + _lines[_caretLine];
            _lines.RemoveAt(_caretLine);
            _caretLine--;
        }

        this.ClearSelection();
        this.OnTextChanged(EventArgs.Empty);
    }

    private void DeleteForward()
    {
        if (this.HasSelection)
        {
            this.DeleteSelection();
            this.OnTextChanged(EventArgs.Empty);
            return;
        }

        var line = _lines[_caretLine];
        if (_caretCol < line.Length)
            _lines[_caretLine] = line[.._caretCol] + line[(_caretCol + 1)..];
        else if (_caretLine < _lines.Count - 1)
        {
            _lines[_caretLine] = line + _lines[_caretLine + 1];
            _lines.RemoveAt(_caretLine + 1);
        }

        this.ClearSelection();
        this.OnTextChanged(EventArgs.Empty);
    }

    private void EnsureCaretVisible()
    {
        if (_caretLine < _topLine)
            _topLine = _caretLine;
        else if (_caretLine >= _topLine + this.VisibleLines)
            _topLine = _caretLine - this.VisibleLines + 1;

        _topLine = Math.Clamp(_topLine, 0, Math.Max(0, _lines.Count - 1));
    }

    private static string LeadingWhitespace(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            ++i;

        return line[..i];
    }

    private static Color Blend(Color a, Color b, double t)
        => Color.FromArgb(255,
            (int)Math.Round((a.R * t) + (b.R * (1 - t))),
            (int)Math.Round((a.G * t) + (b.G * (1 - t))),
            (int)Math.Round((a.B * t) + (b.B * (1 - t))));
}
