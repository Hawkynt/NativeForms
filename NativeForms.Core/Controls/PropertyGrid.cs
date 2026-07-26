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
    /// (e.g. <c>"MiddleCenter"</c>), or the row's <see cref="PropertyGridRow.GridValues"/> when supplied
    /// (a spatial enum picker such as dock).</summary>
    Align,

    /// <summary>A hosted <see cref="DateTimePicker"/> (date only) — for a <see cref="DateOnly"/>.</summary>
    Date,

    /// <summary>A hosted <see cref="TimePicker"/> — for a <see cref="TimeOnly"/>.</summary>
    Time,

    /// <summary>A hosted <see cref="DateTimePicker"/> with a date+time format — for a <see cref="DateTime"/>.</summary>
    DateTime,

    /// <summary>A check-box flyout of the members of a <c>[Flags]</c> enum; the value is the comma-separated
    /// set of selected names.</summary>
    Flags,
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

    /// <summary>The options for a <see cref="PropertyGridEditor.Choice"/> or <see cref="PropertyGridEditor.Flags"/> row.</summary>
    public IReadOnlyList<string>? Choices { get; set; }

    /// <summary>For an <see cref="PropertyGridEditor.Align"/> row, the nine values the 3×3 flyout cells map
    /// to (row-major; an empty entry disables that cell). <see langword="null"/> uses the default
    /// <see cref="System.Drawing.ContentAlignment"/> names — set it to repurpose the grid for a spatial enum
    /// (dock, anchor).</summary>
    public IReadOnlyList<string>? GridValues { get; set; }

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

    /// <summary>
    /// Adds a strongly-typed row, inferring the editor and the value's text formatting / parsing from
    /// <typeparamref name="T"/> at compile time — no per-row <see cref="PropertyGridRow.Get"/>/<see
    /// cref="PropertyGridRow.Set"/> string plumbing and no reflection: <c>bool</c> → check box,
    /// <c>bool?</c> → tristate, a numeric type → number (clamped to <paramref name="minimum"/>/
    /// <paramref name="maximum"/>, or nullable → allows empty), <see cref="System.Drawing.Color"/> →
    /// colour picker, everything else → text. Use <see cref="AddEnumRow{TEnum}"/> for an enum drop-down.
    /// </summary>
    public PropertyGridRow AddRow<T>(
        string name,
        Func<T> get,
        Action<T> set,
        string? category = null,
        string? description = null,
        double? minimum = null,
        double? maximum = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        var nullable = Nullable.GetUnderlyingType(typeof(T)) is not null;
        var underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var row = new PropertyGridRow(name, () => FormatValue(get()), s =>
        {
            try { set(ParseValue<T>(s)); }
            catch (FormatException) { /* keep the old value on bad input */ }
            catch (OverflowException) { }
            catch (ArgumentException) { }
        })
        {
            Editor = EditorFor(underlying, nullable),
            Minimum = minimum,
            Maximum = maximum,
            AllowNull = nullable,
            Description = description,
        };

        if (category is not null)
            row.Category = category;

        this.AddRow(row);
        return row;
    }

    /// <summary>Adds a drop-down row for an enum, its options being the enum's names — fully generic, so it
    /// stays reflection-free and AOT-safe.</summary>
    public PropertyGridRow AddEnumRow<TEnum>(
        string name,
        Func<TEnum> get,
        Action<TEnum> set,
        string? category = null,
        string? description = null)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        var row = new PropertyGridRow(name, () => get().ToString(), s =>
        {
            if (Enum.TryParse<TEnum>(s, out var value))
                set(value);
        })
        {
            Editor = PropertyGridEditor.Choice,
            Choices = Enum.GetNames<TEnum>(),
            Description = description,
        };

        if (category is not null)
            row.Category = category;

        this.AddRow(row);
        return row;
    }

    /// <summary>Adds a check-box flyout row for a <c>[Flags]</c> enum: each member is a toggle, and the value
    /// is the comma-separated set of selected names. Fully generic, so it stays reflection-free.</summary>
    public PropertyGridRow AddFlagsEnumRow<TEnum>(
        string name,
        Func<TEnum> get,
        Action<TEnum> set,
        string? category = null,
        string? description = null)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        // The togglable members are every name except the zero member (the "None" of a flags enum).
        var members = new List<string>();
        foreach (var member in Enum.GetNames<TEnum>())
            if (!Enum.Parse<TEnum>(member).Equals(default(TEnum)))
                members.Add(member);

        var row = new PropertyGridRow(name, () => get().ToString(), s =>
        {
            if (Enum.TryParse<TEnum>(s, out var value))
                set(value);
        })
        {
            Editor = PropertyGridEditor.Flags,
            Choices = members,
            Description = description,
        };

        if (category is not null)
            row.Category = category;

        this.AddRow(row);
        return row;
    }

    /// <summary>Adds a 3×3 spatial flyout row for an enum (dock, anchor): <paramref name="gridValues"/> maps
    /// the nine cells (row-major, empty = disabled) to enum names. Reflection-free.</summary>
    public PropertyGridRow AddGridEnumRow<TEnum>(
        string name,
        Func<TEnum> get,
        Action<TEnum> set,
        IReadOnlyList<string> gridValues,
        string? category = null,
        string? description = null)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(gridValues);

        var row = new PropertyGridRow(name, () => get().ToString(), s =>
        {
            if (Enum.TryParse<TEnum>(s, out var value))
                set(value);
        })
        {
            Editor = PropertyGridEditor.Align,
            GridValues = gridValues,
            Description = description,
        };

        if (category is not null)
            row.Category = category;

        this.AddRow(row);
        return row;
    }

    private static PropertyGridEditor EditorFor(Type underlying, bool nullable)
        => underlying == typeof(bool) ? (nullable ? PropertyGridEditor.TriState : PropertyGridEditor.Boolean)
            : underlying == typeof(Color) ? PropertyGridEditor.Color
            : underlying == typeof(DateOnly) ? PropertyGridEditor.Date
            : underlying == typeof(TimeOnly) ? PropertyGridEditor.Time
            : underlying == typeof(System.DateTime) ? PropertyGridEditor.DateTime
            : IsNumeric(underlying) ? PropertyGridEditor.Number
            : PropertyGridEditor.Text;

    private static bool IsNumeric(Type t)
        => t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort)
            || t == typeof(double) || t == typeof(float) || t == typeof(decimal);

    private static string FormatValue<T>(T value) => value switch
    {
        null => string.Empty,
        bool b => b ? "True" : "False",
        Color c => ColorMath.ToHex(c, withAlpha: true),
        string s => s,
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static T ParseValue<T>(string text)
    {
        var nullableUnderlying = Nullable.GetUnderlyingType(typeof(T));
        if (nullableUnderlying is not null && string.IsNullOrWhiteSpace(text))
            return default!; // an empty value on a nullable type is null

        var target = nullableUnderlying ?? typeof(T);
        object parsed;
        if (target == typeof(bool))
            parsed = string.Equals(text.Trim(), "True", StringComparison.OrdinalIgnoreCase);
        else if (target == typeof(Color))
        {
            ColorMath.TryParseHex(text, out var c);
            parsed = c;
        }
        else if (target == typeof(string))
            return (T)(object)text;
        else if (target == typeof(DateTime))
            parsed = DateTime.Parse(text, System.Globalization.CultureInfo.CurrentCulture);
        else if (target == typeof(DateOnly))
            parsed = DateOnly.Parse(text, System.Globalization.CultureInfo.CurrentCulture);
        else if (target == typeof(TimeOnly))
            parsed = TimeOnly.Parse(text, System.Globalization.CultureInfo.CurrentCulture);
        else
            parsed = Convert.ChangeType(text, target, System.Globalization.CultureInfo.CurrentCulture);

        return (T)parsed;
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

        if (this.IsPickerEditing(row))
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

            case PropertyGridEditor.Choice or PropertyGridEditor.Align or PropertyGridEditor.Date
                or PropertyGridEditor.Time or PropertyGridEditor.DateTime or PropertyGridEditor.Flags:
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
        this.EndPickerEdit();

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

            case PropertyGridEditor.Date:
                this.OpenDate(visual, row, withTime: false);
                break;

            case PropertyGridEditor.DateTime:
                this.OpenDate(visual, row, withTime: true);
                break;

            case PropertyGridEditor.Time:
                this.OpenTime(visual, row);
                break;

            case PropertyGridEditor.Flags:
                this.OpenFlags(visual, row);
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

    // The nine cell values for the row being grid-edited: its GridValues (spatial enum) or the default
    // alignment names.
    private IReadOnlyList<string> CurrentGridCells()
        => _alignVisual >= 0 && _alignVisual < _visual.Count && _visual[_alignVisual].RowIndex >= 0
            && _rows[_visual[_alignVisual].RowIndex].GridValues is { Count: > 0 } custom
                ? custom
                : _AlignNames;

    private void OnAlignPaint(IGraphics g)
    {
        var theme = this.Theme;
        var cells = this.CurrentGridCells();
        var isAlign = ReferenceEquals(cells, _AlignNames);
        var current = _alignVisual >= 0 && _alignVisual < _visual.Count && _visual[_alignVisual].RowIndex >= 0
            ? _rows[_visual[_alignVisual].RowIndex].Get() : string.Empty;

        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, (3 * _AlignCell) + 2, (3 * _AlignCell) + 2));
        for (var i = 0; i < 9; ++i)
        {
            var cell = new Rectangle(1 + ((i % 3) * _AlignCell), 1 + ((i / 3) * _AlignCell), _AlignCell, _AlignCell);
            var name = i < cells.Count ? cells[i] : string.Empty;
            var enabled = name.Length > 0;
            var selected = enabled && string.Equals(name, current, StringComparison.Ordinal);
            if (selected)
                g.FillRectangle(theme.Accent, cell);

            g.DrawRectangle(enabled ? theme.Border : theme.DisabledText, cell);
            if (!enabled)
                continue;

            if (isAlign)
            {
                // A small dot in the corner/edge/centre the alignment points at.
                var dot = new Rectangle(cell.X + 3 + ((i % 3) * ((cell.Width - 9) / 2)), cell.Y + 3 + ((i / 3) * ((cell.Height - 9) / 2)), 3, 3);
                g.FillRectangle(selected ? theme.SelectionText : theme.ControlText, dot);
            }
            else
            {
                var label = name.Length <= 2 ? name : name[..2];
                g.DrawText(label, this.Font, selected ? theme.SelectionText : theme.ControlText, cell, ContentAlignment.MiddleCenter);
            }
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, (3 * _AlignCell) + 1, (3 * _AlignCell) + 1));
    }

    private void OnAlignMouseDown(MouseEventArgs e)
    {
        var cells = this.CurrentGridCells();
        var col = Math.Clamp((e.X - 1) / _AlignCell, 0, 2);
        var rowIndex = Math.Clamp((e.Y - 1) / _AlignCell, 0, 2);
        var index = (rowIndex * 3) + col;
        var picked = index < cells.Count ? cells[index] : string.Empty;
        var visual = _alignVisual;
        if (picked.Length == 0)
            return; // an empty (disabled) cell

        _alignPopup?.Hide();
        _alignVisual = -1;
        if (visual >= 0 && visual < _visual.Count && _visual[visual].RowIndex >= 0)
            this.Commit(_rows[_visual[visual].RowIndex], picked);

        this.Focus();
    }

    // --- Flags checkbox flyout -------------------------------------------------------------------

    private IPopupPeer? _flagsPopup;
    private int _flagsVisual = -1;
    private readonly HashSet<string> _flagsChecked = new(StringComparer.Ordinal);

    private void OpenFlags(int visual, PropertyGridRow row)
    {
        if (row.Choices is not { Count: > 0 } names || this.Backend is not { } backend)
            return;

        _flagsVisual = visual;
        _flagsChecked.Clear();
        foreach (var part in row.Get().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _flagsChecked.Add(part);

        var popup = _flagsPopup ??= this.CreateFlagsPopup(backend);
        var rows = Math.Min(names.Count, _MaxPopupRows);
        var y = (visual + 1) * this.RowHeight;
        popup.ShowAt(this.PointToScreen(new Point(this.SplitX, y)), new Size(Math.Max(120, this.Width - this.SplitX), rows * this.RowHeight));
        popup.InvalidateAll();
    }

    private IReadOnlyList<string> FlagNames
        => _flagsVisual >= 0 && _flagsVisual < _visual.Count && _visual[_flagsVisual].RowIndex >= 0
            ? _rows[_visual[_flagsVisual].RowIndex].Choices ?? [] : [];

    private IPopupPeer CreateFlagsPopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup(this.OwnerWindowPeer);
        popup.Paint += (_, e) => this.OnFlagsPaint(e.Graphics);
        popup.MouseDown += (_, e) => this.OnFlagsMouseDown(e);
        popup.Dismissed += (_, _) => _flagsVisual = -1;
        return popup;
    }

    private void OnFlagsPaint(IGraphics g)
    {
        var theme = this.Theme;
        var names = this.FlagNames;
        var rowHeight = this.RowHeight;
        var width = Math.Max(120, this.Width - this.SplitX);
        var rows = Math.Min(names.Count, _MaxPopupRows);
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, rows * rowHeight));
        for (var i = 0; i < rows; ++i)
        {
            var r = new Rectangle(0, i * rowHeight, width, rowHeight);
            var box = new Rectangle(r.X + 4, r.Y + ((rowHeight - GlyphRenderer.CheckBoxSize) / 2), GlyphRenderer.CheckBoxSize, GlyphRenderer.CheckBoxSize);
            GlyphRenderer.DrawCheckBox(g, theme, box, _flagsChecked.Contains(names[i]));
            g.DrawText(names[i], this.Font, theme.ControlText, new Rectangle(box.Right + 6, r.Y, r.Width - box.Right - 8, r.Height), ContentAlignment.MiddleLeft);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, width - 1, (rows * rowHeight) - 1));
    }

    private void OnFlagsMouseDown(MouseEventArgs e)
    {
        var names = this.FlagNames;
        var index = e.Y / this.RowHeight;
        if (index < 0 || index >= Math.Min(names.Count, _MaxPopupRows))
            return;

        var name = names[index];
        if (!_flagsChecked.Remove(name))
            _flagsChecked.Add(name);

        // Commit the new set (in the enum's declared order) as a comma-separated value.
        var selected = new List<string>();
        foreach (var n in names)
            if (_flagsChecked.Contains(n))
                selected.Add(n);

        var value = selected.Count == 0 ? "0" : string.Join(", ", selected); // "0" parses to the enum's zero member
        if (_flagsVisual >= 0 && _flagsVisual < _visual.Count && _visual[_flagsVisual].RowIndex >= 0)
            this.Commit(_rows[_visual[_flagsVisual].RowIndex], value);

        _flagsPopup?.InvalidateAll();
    }

    // --- Hosted picker controls (Color / Date / Time reuse the toolkit's own controls) -----------
    //
    // Only one is open at a time, so a single slot tracks the active control, the row it covers, and a
    // delegate that reads the control's current value and commits it as the row's string.

    private ColorPicker? _colorEditor;
    private DateTimePicker? _dateEditor;
    private TimePicker? _timeEditor;

    private Control? _picker;          // the control currently hosted over a cell
    private int _pickerVisual = -1;
    private Action? _pickerCommit;     // reads _picker's value and commits it to the edited row

    private void OpenColor(int visual, PropertyGridRow row)
    {
        if (_colorEditor is null)
        {
            _colorEditor = new ColorPicker { Visible = false, TabStop = false };
            _colorEditor.SelectedColorChanged += (_, _) => this.PickerCommit();
            this.Controls.Add(_colorEditor);
        }

        if (ColorMath.TryParseHex(row.Get(), out var current))
            _colorEditor.SelectedColor = current;

        this.BeginPicker(visual, _colorEditor, () => ColorMath.ToHex(_colorEditor!.SelectedColor, withAlpha: true));
        _colorEditor.OpenDropDown();
    }

    private void OpenDate(int visual, PropertyGridRow row, bool withTime)
    {
        if (_dateEditor is null)
        {
            _dateEditor = new DateTimePicker { Visible = false, TabStop = false };
            _dateEditor.ValueChanged += (_, _) => this.PickerCommit();
            this.Controls.Add(_dateEditor);
        }

        _dateEditor.Format = withTime ? DateTimePickerFormat.Custom : DateTimePickerFormat.Short;
        _dateEditor.CustomFormat = withTime ? "yyyy-MM-dd HH:mm" : string.Empty;
        if (System.DateTime.TryParse(row.Get(), out var dt))
            _dateEditor.Value = dt;

        this.BeginPicker(visual, _dateEditor, () => withTime
            ? _dateEditor!.Value.ToString(System.Globalization.CultureInfo.CurrentCulture)
            : DateOnly.FromDateTime(_dateEditor!.Value).ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    private void OpenTime(int visual, PropertyGridRow row)
    {
        if (_timeEditor is null)
        {
            _timeEditor = new TimePicker { Visible = false, TabStop = false };
            _timeEditor.ValueChanged += (_, _) => this.PickerCommit();
            this.Controls.Add(_timeEditor);
        }

        if (TimeOnly.TryParse(row.Get(), out var t))
            _timeEditor.Value = t.ToTimeSpan();
        else if (System.TimeSpan.TryParse(row.Get(), out var ts))
            _timeEditor.Value = ts;

        this.BeginPicker(visual, _timeEditor, () => TimeOnly.FromTimeSpan(_timeEditor!.Value).ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    private void BeginPicker(int visual, Control picker, Func<string> read)
    {
        _pickerVisual = visual;
        _picker = picker;
        _pickerCommit = () =>
        {
            if (_pickerVisual >= 0 && _pickerVisual < _visual.Count && _visual[_pickerVisual].RowIndex >= 0)
                this.Commit(_rows[_visual[_pickerVisual].RowIndex], read());
        };

        var y = visual * this.RowHeight;
        var splitX = this.SplitX;
        picker.Bounds = new Rectangle(splitX + _CellPad, y, this.Width - splitX - (2 * _CellPad), this.RowHeight);
        picker.Visible = true;
        picker.Focus();
        this.Invalidate();
    }

    private void PickerCommit() => _pickerCommit?.Invoke();

    /// <summary>Whether the given row is the one a hosted picker currently covers.</summary>
    private bool IsPickerEditing(PropertyGridRow row)
        => _pickerVisual >= 0 && _pickerVisual < _visual.Count && _visual[_pickerVisual].RowIndex >= 0
            && ReferenceEquals(_rows[_visual[_pickerVisual].RowIndex], row);

    private void EndPickerEdit()
    {
        if (_pickerVisual < 0)
            return;

        _pickerVisual = -1;
        _pickerCommit = null;
        if (_picker is ColorPicker cp)
            cp.CloseDropDown();
        if (_picker is { } p)
            p.Visible = false;

        _picker = null;
        this.Invalidate();
    }
}
