using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The editor a <see cref="PropertyGridRow"/>'s value cell uses.</summary>
public enum PropertyGridEditor
{
    /// <summary>A free-text field.</summary>
    Text,

    /// <summary>A numeric text field.</summary>
    Number,

    /// <summary>An inline check box toggled between <c>"True"</c> and <c>"False"</c>.</summary>
    Boolean,

    /// <summary>A drop-down of <see cref="PropertyGridRow.Choices"/>.</summary>
    Choice,

    /// <summary>A colour swatch plus its hex value in a text field.</summary>
    Color,

    /// <summary>A three-state check box cycling <c>"True"</c> → <c>"False"</c> → <c>""</c> (null), for a
    /// <c>bool?</c>.</summary>
    TriState,

    /// <summary>A 3×3 alignment picker whose value is a <see cref="System.Drawing.ContentAlignment"/> name
    /// (e.g. <c>"MiddleCenter"</c>).</summary>
    Align,
}

/// <summary>
/// One row in a <see cref="PropertyGrid"/>: a named value backed by delegate get/set (never reflection),
/// an <see cref="Editor"/>, a <see cref="Category"/> it groups under and an optional
/// <see cref="Description"/>. A <see langword="null"/> <see cref="Set"/> makes the row read-only.
/// </summary>
public sealed class PropertyGridRow(string name, Func<string> get, Action<string>? set = null)
{
    /// <summary>The property name shown in the left column.</summary>
    public string Name { get; set; } = name;

    /// <summary>Reads the current value as display text.</summary>
    public Func<string> Get { get; set; } = get;

    /// <summary>Commits an edited value, or <see langword="null"/> for a read-only row.</summary>
    public Action<string>? Set { get; set; } = set;

    /// <summary>The category header the row groups under. Defaults to "Misc".</summary>
    public string Category { get; set; } = "Misc";

    /// <summary>A one-line explanation shown in the description strip while the row is selected.</summary>
    public string? Description { get; set; }

    /// <summary>Which editor the value cell uses. Defaults to <see cref="PropertyGridEditor.Text"/>.</summary>
    public PropertyGridEditor Editor { get; set; } = PropertyGridEditor.Text;

    /// <summary>The options for a <see cref="PropertyGridEditor.Choice"/> row.</summary>
    public IReadOnlyList<string>? Choices { get; set; }

    /// <summary>The inclusive lower bound a <see cref="PropertyGridEditor.Number"/> commit is clamped to,
    /// or <see langword="null"/> for none.</summary>
    public double? Minimum { get; set; }

    /// <summary>The inclusive upper bound a <see cref="PropertyGridEditor.Number"/> commit is clamped to,
    /// or <see langword="null"/> for none.</summary>
    public double? Maximum { get; set; }

    /// <summary>Whether an empty value is allowed (a <c>null</c> number, or the third state of a
    /// <see cref="PropertyGridEditor.TriState"/>). Defaults to <see langword="false"/>.</summary>
    public bool AllowNull { get; set; }
}

/// <summary>
/// A two-column property editor: name/value rows grouped under collapsible category headers, each value
/// cell edited by a typed inline editor (text, number, check box, drop-down or colour). Reflection-free —
/// rows are described by delegate get/set — with a description strip along the bottom and a draggable
/// splitter. The inspector a settings screen, an IDE or a file-properties dialog is built from.
/// </summary>
public class PropertyGrid : OwnerDrawnControl
{
    private const int _DescriptionHeight = 46;
    private const int _Indent = 16;      // property rows sit one glyph-cell in from the category header
    private const int _CellPad = 4;
    private const int _SwatchSize = 12;
    private const int _MaxPopupRows = 10;

    private readonly List<PropertyGridRow> _rows = [];
    private readonly List<string> _collapsed = [];   // category names currently collapsed

    // The flattened visible rows: a category header (RowIndex < 0, its name in Category) or a property
    // (RowIndex is the index into _rows). Rebuilt lazily.
    private readonly List<(int RowIndex, string Category)> _visual = [];
    private bool _visualDirty = true;

    private int _selected = -1;          // selected index into _visual
    private double _splitFraction = 0.42;
    private bool _draggingSplit;

    private readonly TextBox _editor;
    private int _editVisual = -1;

    private IPopupPeer? _choicePopup;
    private int _choiceVisual = -1;
    private int _choiceHover = -1;

    /// <summary>Creates an empty property grid.</summary>
    public PropertyGrid()
    {
        _editor = new FramelessTextBox { Visible = false, TabStop = false };
        _editor.KeyDown += this.OnEditorKeyDown;
        this.Controls.Add(_editor);
    }

    /// <summary>The rows, in insertion order; categories are formed from their <see cref="PropertyGridRow.Category"/>.</summary>
    public IReadOnlyList<PropertyGridRow> Rows => _rows;

    /// <summary>Appends a row. Rows sharing a <see cref="PropertyGridRow.Category"/> group under one header.</summary>
    public void AddRow(PropertyGridRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows.Add(row);
        _visualDirty = true;
        this.Invalidate();
    }

    /// <summary>Removes every row.</summary>
    public void ClearRows()
    {
        this.EndEdit(commit: false);
        _rows.Clear();
        _collapsed.Clear();
        _selected = -1;
        _visualDirty = true;
        this.Invalidate();
    }

    /// <summary>Raised after an editor commits a new value to a row.</summary>
    public event EventHandler<PropertyValueChangedEventArgs>? PropertyValueChanged;

    /// <summary>Raises <see cref="PropertyValueChanged"/>.</summary>
    protected virtual void OnPropertyValueChanged(PropertyValueChangedEventArgs e) => this.PropertyValueChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <inheritdoc/>
    private protected override Color FallbackBackColor => this.Theme.FieldBackground;

    /// <summary>The selected row, or <see langword="null"/>.</summary>
    public PropertyGridRow? SelectedRow
        => _selected >= 0 && _selected < _visual.Count && _visual[_selected].RowIndex >= 0 ? _rows[_visual[_selected].RowIndex] : null;

    private int RowHeight => this.Theme.RowHeight;
    private int SplitX => Math.Clamp((int)(this.Width * _splitFraction), 60, Math.Max(60, this.Width - 80));
    private int GridBottom => Math.Max(0, this.Height - _DescriptionHeight);

    private void EnsureVisual()
    {
        if (!_visualDirty)
            return;

        _visualDirty = false;
        _visual.Clear();
        string? lastCategory = null;
        for (var i = 0; i < _rows.Count; ++i)
        {
            var category = _rows[i].Category;
            if (!string.Equals(category, lastCategory, StringComparison.Ordinal))
            {
                _visual.Add((-1, category));
                lastCategory = category;
            }

            if (!_collapsed.Contains(category))
                _visual.Add((i, category));
        }
    }

    private bool IsCollapsed(string category) => _collapsed.Contains(category);

    /// <inheritdoc/>
    private protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        if (_editVisual >= 0)
            this.EndEdit(commit: true);

        this.Invalidate();
    }

    // --- Painting --------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        this.EnsureVisual();
        var g = e.Graphics;
        var theme = this.Theme;
        var rowHeight = this.RowHeight;
        var splitX = this.SplitX;
        var gridBottom = this.GridBottom;

        g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

        var y = 0;
        for (var v = 0; v < _visual.Count && y < gridBottom; ++v)
        {
            var (rowIndex, category) = _visual[v];
            if (rowIndex < 0)
                this.PaintCategory(g, theme, category, y, rowHeight);
            else
                this.PaintRow(g, theme, _rows[rowIndex], v == _selected, splitX, y, rowHeight);

            y += rowHeight;
        }

        // The value/name splitter.
        g.DrawLine(theme.Border, splitX, 0, splitX, gridBottom);

        this.PaintDescription(g, theme, gridBottom);
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintCategory(IGraphics g, ITheme theme, string category, int y, int rowHeight)
    {
        var row = new Rectangle(0, y, this.Width, rowHeight);
        g.FillRectangle(theme.HeaderBackground, row);
        ExpandGlyph.Draw(g, theme, 0, y, rowHeight, rowHeight, !this.IsCollapsed(category));
        g.DrawText(category, this.Font, theme.HeaderText, new Rectangle(rowHeight, y, this.Width - rowHeight, rowHeight), ContentAlignment.MiddleLeft);
    }

    private void PaintRow(IGraphics g, ITheme theme, PropertyGridRow row, bool selected, int splitX, int y, int rowHeight)
    {
        if (selected)
            GlyphRenderer.FillSelection(g, theme, new Rectangle(0, y, this.Width, rowHeight));

        var nameColor = selected ? theme.SelectionText : theme.ControlText;
        g.DrawText(row.Name, this.Font, nameColor, new Rectangle(_Indent + _CellPad, y, splitX - _Indent - (2 * _CellPad), rowHeight), ContentAlignment.MiddleLeft);

        if (_editVisual >= 0 && _visual[_editVisual].RowIndex >= 0 && ReferenceEquals(_rows[_visual[_editVisual].RowIndex], row))
            return; // the hosted text editor covers this value cell

        if (this.IsColorEditing(row))
            return; // the hosted colour picker covers this value cell

        var valueRect = new Rectangle(splitX + _CellPad, y, this.Width - splitX - (2 * _CellPad), rowHeight);
        var value = row.Get();
        switch (row.Editor)
        {
            case PropertyGridEditor.Boolean:
                var box = new Rectangle(splitX + _CellPad, y + ((rowHeight - GlyphRenderer.CheckBoxSize) / 2), GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
                GlyphRenderer.DrawCheckBox(g, theme, box, string.Equals(value, "True", StringComparison.OrdinalIgnoreCase));
                break;

            case PropertyGridEditor.Color:
                var swatch = new Rectangle(splitX + _CellPad, y + ((rowHeight - _SwatchSize) / 2), _SwatchSize, _SwatchSize);
                if (ColorMath.TryParseHex(value, out var color))
                    g.FillRectangle(color, swatch);
                g.DrawRectangle(theme.Border, swatch);
                g.DrawText(value, this.Font, theme.ControlText, new Rectangle(swatch.Right + _CellPad, y, valueRect.Width - _SwatchSize, rowHeight), ContentAlignment.MiddleLeft);
                break;

            case PropertyGridEditor.Choice or PropertyGridEditor.Align:
                g.DrawText(value, this.Font, theme.ControlText, valueRect, ContentAlignment.MiddleLeft);
                GlyphRenderer.DrawComboArrow(g, theme.ControlText, new Rectangle(this.Width - 18, y, 14, rowHeight));
                break;

            case PropertyGridEditor.TriState:
                var tri = new Rectangle(splitX + _CellPad, y + ((rowHeight - GlyphRenderer.CheckBoxSize) / 2), GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
                var isNull = value.Length == 0 || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
                GlyphRenderer.DrawCheckBox(g, theme, tri, string.Equals(value, "True", StringComparison.OrdinalIgnoreCase));
                if (isNull)
                    g.FillRectangle(theme.Accent, new Rectangle(tri.X + 3, tri.Y + 3, tri.Width - 6, tri.Height - 6)); // indeterminate square
                break;

            default:
                g.DrawText(value, this.Font, row.Set is null ? theme.DisabledText : theme.ControlText, valueRect, ContentAlignment.MiddleLeft);
                break;
        }
    }

    private void PaintDescription(IGraphics g, ITheme theme, int top)
    {
        g.FillRectangle(theme.HeaderBackground, new Rectangle(0, top, this.Width, _DescriptionHeight));
        g.DrawLine(theme.Border, 0, top, this.Width, top);
        var selected = this.SelectedRow;
        if (selected is null)
            return;

        g.DrawText(selected.Name, this.Font, theme.ControlText, new Rectangle(_CellPad, top + 2, this.Width - (2 * _CellPad), 18), ContentAlignment.MiddleLeft);
        if (!string.IsNullOrEmpty(selected.Description))
            g.DrawText(selected.Description, this.Font, theme.HeaderText, new Rectangle(_CellPad, top + 20, this.Width - (2 * _CellPad), _DescriptionHeight - 22), ContentAlignment.TopLeft);
    }

    // --- Input -----------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        this.EnsureVisual();
        this.EndEdit(commit: true);
        this.EndColorEdit();

        if (Math.Abs(e.X - this.SplitX) <= 3 && e.Y < this.GridBottom)
        {
            _draggingSplit = true;
            return;
        }

        var v = this.VisualAt(e.Y);
        if (v < 0)
            return;

        var (rowIndex, category) = _visual[v];
        if (rowIndex < 0)
        {
            this.ToggleCategory(category);
            return;
        }

        _selected = v;
        this.Invalidate();

        if (e.X > this.SplitX)
            this.ActivateValue(v, rowIndex);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_draggingSplit)
            return;

        _splitFraction = Math.Clamp((double)e.X / Math.Max(1, this.Width), 0.15, 0.85);
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e) => _draggingSplit = false;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down: this.MoveSelection(1); e.Handled = true; break;
            case Keys.Up: this.MoveSelection(-1); e.Handled = true; break;
            case Keys.Enter or Keys.F2 when _selected >= 0 && _visual[_selected].RowIndex >= 0:
                this.ActivateValue(_selected, _visual[_selected].RowIndex);
                e.Handled = true;
                break;
        }
    }

    private int VisualAt(int y)
    {
        if (y < 0 || y >= this.GridBottom)
            return -1;

        var index = y / this.RowHeight;
        return index >= 0 && index < _visual.Count ? index : -1;
    }

    private void MoveSelection(int delta)
    {
        this.EnsureVisual();
        if (_visual.Count == 0)
            return;

        var v = _selected;
        for (var step = 0; step < _visual.Count; ++step)
        {
            v = Math.Clamp(v + delta, 0, _visual.Count - 1);
            if (_visual[v].RowIndex >= 0)
                break;

            if (v == 0 || v == _visual.Count - 1)
                break;
        }

        _selected = v;
        this.Invalidate();
    }

    private void ToggleCategory(string category)
    {
        this.EndEdit(commit: true);
        if (!_collapsed.Remove(category))
            _collapsed.Add(category);

        _visualDirty = true;
        _selected = -1;
        this.Invalidate();
    }

    private void ActivateValue(int visual, int rowIndex)
    {
        var row = _rows[rowIndex];
        if (row.Set is null)
            return;

        switch (row.Editor)
        {
            case PropertyGridEditor.Boolean:
                var next = string.Equals(row.Get(), "True", StringComparison.OrdinalIgnoreCase) ? "False" : "True";
                this.Commit(row, next);
                break;

            case PropertyGridEditor.TriState:
                this.Commit(row, NextTriState(row.Get(), row.AllowNull));
                break;

            case PropertyGridEditor.Choice:
                this.OpenChoice(visual, row);
                break;

            case PropertyGridEditor.Align:
                this.OpenAlign(visual, row);
                break;

            case PropertyGridEditor.Color:
                this.OpenColor(visual, row);
                break;

            default:
                this.BeginEdit(visual, row);
                break;
        }
    }

    // True → False → (null) → True. The null third state only appears when the row allows it.
    private static string NextTriState(string value, bool allowNull)
    {
        if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase))
            return "False";

        if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase))
            return allowNull ? string.Empty : "True";

        return "True";
    }

    // --- Hosted text editor ----------------------------------------------------------------------

    private void BeginEdit(int visual, PropertyGridRow row)
    {
        var y = visual * this.RowHeight;
        var splitX = this.SplitX;
        var swatch = row.Editor == PropertyGridEditor.Color ? _SwatchSize + _CellPad : 0;
        _editVisual = visual;
        _editor.Bounds = new Rectangle(splitX + _CellPad + swatch, y, this.Width - splitX - (2 * _CellPad) - swatch, this.RowHeight);
        _editor.Text = row.Get();
        _editor.Visible = true;
        _editor.SelectionStart = 0;
        _editor.SelectionLength = _editor.Text.Length;
        _editor.Focus();
        this.Invalidate();
    }

    private void EndEdit(bool commit)
    {
        if (_editVisual < 0)
            return;

        var visual = _editVisual;
        _editVisual = -1;
        _editor.Visible = false;
        if (commit && visual < _visual.Count && _visual[visual].RowIndex >= 0)
            this.Commit(_rows[_visual[visual].RowIndex], _editor.Text);

        this.Invalidate();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter: this.EndEdit(commit: true); this.Focus(); e.Handled = true; break;
            case Keys.Escape: this.EndEdit(commit: false); this.Focus(); e.Handled = true; break;
        }
    }

    private void Commit(PropertyGridRow row, string value)
    {
        value = Normalize(row, value);
        var old = row.Get();
        row.Set?.Invoke(value);
        var applied = row.Get();
        this.Invalidate();
        if (!string.Equals(old, applied, StringComparison.Ordinal))
            this.OnPropertyValueChanged(new PropertyValueChangedEventArgs(row, old, applied));
    }

    // Clamps a numeric commit to the row's Min/Max and honours AllowNull; a value that will not parse is
    // rejected by returning the current value unchanged.
    private static string Normalize(PropertyGridRow row, string value)
    {
        if (row.Editor != PropertyGridEditor.Number)
            return value;

        var text = value.Trim();
        if (text.Length == 0)
            return row.AllowNull ? string.Empty : row.Get();

        if (!double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var d)
            && !double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
            return row.Get(); // unparseable → keep the old value

        if (row.Minimum is { } min)
            d = Math.Max(d, min);
        if (row.Maximum is { } max)
            d = Math.Min(d, max);

        return d.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    // --- Choice drop-down ------------------------------------------------------------------------

    private void OpenChoice(int visual, PropertyGridRow row)
    {
        if (row.Choices is not { Count: > 0 } choices || this.Backend is not { } backend)
            return;

        _choiceVisual = visual;
        _choiceHover = Math.Max(0, IndexOf(choices, row.Get()));
        var popup = _choicePopup ??= this.CreateChoicePopup(backend);
        var rows = Math.Min(choices.Count, _MaxPopupRows);
        var width = Math.Max(80, this.Width - this.SplitX);
        var y = (visual + 1) * this.RowHeight;
        popup.ShowAt(this.PointToScreen(new Point(this.SplitX, y)), new Size(width, rows * this.RowHeight));
        popup.InvalidateAll();
    }

    private IPopupPeer CreateChoicePopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.OnChoicePaint(e.Graphics);
        popup.MouseMove += (_, e) => this.OnChoiceMouseMove(e);
        popup.MouseDown += (_, e) => this.OnChoiceMouseDown(e);
        popup.KeyDown += (_, e) => this.OnChoiceKeyDown(e);
        popup.Dismissed += (_, _) => { _choiceVisual = -1; _choiceHover = -1; };
        return popup;
    }

    private IReadOnlyList<string> ChoiceList
        => _choiceVisual >= 0 && _choiceVisual < _visual.Count && _visual[_choiceVisual].RowIndex >= 0
            ? _rows[_visual[_choiceVisual].RowIndex].Choices ?? [] : [];

    private void OnChoicePaint(IGraphics g)
    {
        var theme = this.Theme;
        var choices = this.ChoiceList;
        var rowHeight = this.RowHeight;
        var rows = Math.Min(choices.Count, _MaxPopupRows);
        var width = Math.Max(80, this.Width - this.SplitX);
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, rows * rowHeight));
        for (var i = 0; i < rows; ++i)
        {
            var r = new Rectangle(0, i * rowHeight, width, rowHeight);
            if (i == _choiceHover)
                GlyphRenderer.FillSelection(g, theme, r);

            g.DrawText(choices[i], this.Font, i == _choiceHover ? theme.SelectionText : theme.ControlText,
                new Rectangle(r.X + _CellPad, r.Y, r.Width - (2 * _CellPad), r.Height), ContentAlignment.MiddleLeft);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, (rows * rowHeight) - 1));
    }

    private void OnChoiceMouseMove(MouseEventArgs e)
    {
        var hover = e.Y / this.RowHeight;
        if (hover == _choiceHover || hover < 0 || hover >= Math.Min(this.ChoiceList.Count, _MaxPopupRows))
            return;

        _choiceHover = hover;
        _choicePopup?.InvalidateAll();
    }

    private void OnChoiceMouseDown(MouseEventArgs e)
    {
        var index = e.Y / this.RowHeight;
        this.PickChoice(index);
    }

    private void OnChoiceKeyDown(KeyEventArgs e)
    {
        var count = Math.Min(this.ChoiceList.Count, _MaxPopupRows);
        switch (e.KeyCode)
        {
            case Keys.Down: _choiceHover = _choiceHover + 1 >= count ? 0 : _choiceHover + 1; _choicePopup?.InvalidateAll(); e.Handled = true; break;
            case Keys.Up: _choiceHover = _choiceHover <= 0 ? count - 1 : _choiceHover - 1; _choicePopup?.InvalidateAll(); e.Handled = true; break;
            case Keys.Enter: this.PickChoice(_choiceHover); e.Handled = true; break;
            case Keys.Escape: _choicePopup?.Hide(); _choiceVisual = -1; e.Handled = true; break;
        }
    }

    private void PickChoice(int index)
    {
        var choices = this.ChoiceList;
        var visual = _choiceVisual;
        if (index < 0 || index >= Math.Min(choices.Count, _MaxPopupRows) || visual < 0)
            return;

        var picked = choices[index];
        _choicePopup?.Hide();
        _choiceVisual = -1;
        if (visual < _visual.Count && _visual[visual].RowIndex >= 0)
            this.Commit(_rows[_visual[visual].RowIndex], picked);

        this.Focus();
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; ++i)
            if (string.Equals(list[i], value, StringComparison.Ordinal))
                return i;

        return -1;
    }

    // --- 3×3 alignment picker --------------------------------------------------------------------

    private static readonly string[] _AlignNames =
    [
        "TopLeft", "TopCenter", "TopRight",
        "MiddleLeft", "MiddleCenter", "MiddleRight",
        "BottomLeft", "BottomCenter", "BottomRight",
    ];

    private const int _AlignCell = 22;

    private IPopupPeer? _alignPopup;
    private int _alignVisual = -1;

    private void OpenAlign(int visual, PropertyGridRow row)
    {
        if (this.Backend is not { } backend)
            return;

        _alignVisual = visual;
        var popup = _alignPopup ??= this.CreateAlignPopup(backend);
        var size = new Size((3 * _AlignCell) + 2, (3 * _AlignCell) + 2);
        var y = (visual + 1) * this.RowHeight;
        popup.ShowAt(this.PointToScreen(new Point(this.SplitX, y)), size);
        popup.InvalidateAll();
    }

    private IPopupPeer CreateAlignPopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.OnAlignPaint(e.Graphics);
        popup.MouseDown += (_, e) => this.OnAlignMouseDown(e);
        popup.Dismissed += (_, _) => _alignVisual = -1;
        return popup;
    }

    private void OnAlignPaint(IGraphics g)
    {
        var theme = this.Theme;
        var current = _alignVisual >= 0 && _alignVisual < _visual.Count && _visual[_alignVisual].RowIndex >= 0
            ? _rows[_visual[_alignVisual].RowIndex].Get() : string.Empty;

        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, (3 * _AlignCell) + 2, (3 * _AlignCell) + 2));
        for (var i = 0; i < 9; ++i)
        {
            var cell = new Rectangle(1 + ((i % 3) * _AlignCell), 1 + ((i / 3) * _AlignCell), _AlignCell, _AlignCell);
            var selected = string.Equals(_AlignNames[i], current, StringComparison.Ordinal);
            if (selected)
                g.FillRectangle(theme.Accent, cell);

            g.DrawRectangle(theme.Border, cell);

            // A small dot in the corner/edge/centre the alignment points at.
            var dot = new Rectangle(cell.X + 3 + ((i % 3) * ((cell.Width - 9) / 2)), cell.Y + 3 + ((i / 3) * ((cell.Height - 9) / 2)), 3, 3);
            g.FillRectangle(selected ? theme.SelectionText : theme.ControlText, dot);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, (3 * _AlignCell) + 1, (3 * _AlignCell) + 1));
    }

    private void OnAlignMouseDown(MouseEventArgs e)
    {
        var col = Math.Clamp((e.X - 1) / _AlignCell, 0, 2);
        var rowIndex = Math.Clamp((e.Y - 1) / _AlignCell, 0, 2);
        var picked = _AlignNames[(rowIndex * 3) + col];
        var visual = _alignVisual;
        _alignPopup?.Hide();
        _alignVisual = -1;
        if (visual >= 0 && visual < _visual.Count && _visual[visual].RowIndex >= 0)
            this.Commit(_rows[_visual[visual].RowIndex], picked);

        this.Focus();
    }

    // --- Colour palette picker -------------------------------------------------------------------

    // A Color row hosts the real ColorPicker over its value cell (re-using its full mixer, numeric tabs and
    // eyedropper) rather than a bespoke palette — one colour UI across the toolkit.
    private ColorPicker? _colorEditor;
    private int _colorEditVisual = -1;

    private void OpenColor(int visual, PropertyGridRow row)
    {
        if (_colorEditor is null)
        {
            _colorEditor = new ColorPicker { Visible = false, TabStop = false };
            _colorEditor.SelectedColorChanged += this.OnColorEditorChanged;
            this.Controls.Add(_colorEditor);
        }

        _colorEditVisual = visual;
        var y = visual * this.RowHeight;
        var splitX = this.SplitX;
        _colorEditor.Bounds = new Rectangle(splitX + _CellPad, y, this.Width - splitX - (2 * _CellPad), this.RowHeight);
        if (ColorMath.TryParseHex(row.Get(), out var current))
            _colorEditor.SelectedColor = current;

        _colorEditor.Visible = true;
        _colorEditor.Focus();
        _colorEditor.OpenDropDown();
        this.Invalidate();
    }

    private void OnColorEditorChanged(object? sender, EventArgs e)
    {
        if (_colorEditVisual < 0 || _colorEditVisual >= _visual.Count)
            return;

        var rowIndex = _visual[_colorEditVisual].RowIndex;
        if (rowIndex >= 0)
            this.Commit(_rows[rowIndex], ColorMath.ToHex(_colorEditor!.SelectedColor, withAlpha: true));
    }

    /// <summary>Whether the given row is the one the hosted colour picker currently covers.</summary>
    private bool IsColorEditing(PropertyGridRow row)
        => _colorEditVisual >= 0 && _colorEditVisual < _visual.Count && _visual[_colorEditVisual].RowIndex >= 0
            && ReferenceEquals(_rows[_visual[_colorEditVisual].RowIndex], row);

    private void EndColorEdit()
    {
        if (_colorEditVisual < 0)
            return;

        _colorEditVisual = -1;
        if (_colorEditor is { } editor)
        {
            editor.CloseDropDown();
            editor.Visible = false;
        }

        this.Invalidate();
    }
}
