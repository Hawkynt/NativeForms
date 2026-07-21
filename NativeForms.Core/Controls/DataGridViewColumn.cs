using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A column definition for a <see cref="DataGridView"/>. Cell values are produced by reflection-free
/// selector delegates (row item → value / icon / state), so binding stays trim/AOT-safe. One class
/// covers every <see cref="DataGridViewColumnKind"/> — the kind-specific selectors are simply unused
/// by the other kinds — which keeps the grid's paint switch free of per-cell objects.
/// </summary>
public sealed class DataGridViewColumn
{
    /// <summary>Creates a column bound to the given value selector.</summary>
    /// <param name="headerText">The text painted in the column header.</param>
    /// <param name="valueSelector">Maps a row item to the value shown in this cell (rendered via <c>ToString()</c>).</param>
    public DataGridViewColumn(string headerText, Func<object?, object?> valueSelector)
    {
        this.HeaderText = headerText ?? string.Empty;
        this.ValueSelector = valueSelector ?? (static _ => null);
    }

    /// <summary>The text painted in the column header.</summary>
    public string HeaderText { get; set; }

    /// <summary>How this column renders and reacts to clicks. Defaults to
    /// <see cref="DataGridViewColumnKind.Text"/>.</summary>
    public DataGridViewColumnKind Kind { get; set; }

    /// <summary>The column width in pixels.</summary>
    public int Width { get; set; } = 100;

    /// <summary>The narrowest width the column accepts, floored at 2 px: the lower bound of a user's
    /// divider drag and of the share a <see cref="DataGridViewAutoSizeColumnMode.Fill"/> column
    /// receives.</summary>
    public int MinimumWidth
    {
        get => field;
        set => field = Math.Max(2, value);
    } = 8;

    /// <summary>The column's share of the leftover viewport width under
    /// <see cref="DataGridViewAutoSizeColumnMode.Fill"/>, relative to the other fill columns'
    /// weights. Kept above zero.</summary>
    public float FillWeight
    {
        get => field;
        set => field = value > 0f ? value : 1f;
    } = 100f;

    /// <summary>Whether the user may drag this column's divider: <see cref="DataGridViewTriState.True"/>
    /// and <see cref="DataGridViewTriState.False"/> override the grid's
    /// <see cref="DataGridView.AllowUserToResizeColumns"/>; <see cref="DataGridViewTriState.NotSet"/>
    /// (the default) inherits it — WinForms semantics.</summary>
    public DataGridViewTriState Resizable { get; set; }

    /// <summary>Alignment of the cell content within the column.</summary>
    public ContentAlignment Alignment { get; set; } = ContentAlignment.MiddleLeft;

    /// <summary>
    /// The formatted cell text per model row, filled lazily by the grid and dropped whenever the
    /// rows or this column's text-shaping selectors change. Repainting an unchanged cell must never
    /// re-run a selector, box a value or re-format it — the §4 zero-steady-state-paint-allocation
    /// guarantee for grids.
    /// </summary>
    internal string?[]? DisplayTextCache;

    /// <summary>Maps a row item to the value shown in this cell (rendered via <c>ToString()</c>).</summary>
    public Func<object?, object?> ValueSelector
    {
        get => field;
        set
        {
            field = value;
            this.DisplayTextCache = null;
        }
    }

    /// <summary>Optional selector producing a per-cell icon painted before the text;
    /// <see langword="null"/> for none.</summary>
    public Func<object?, IImage?>? ImageSelector { get; set; }

    /// <summary>Whether every cell in this column refuses edits and check toggling. Combined with the
    /// grid and per-cell levels by <see cref="DataGridView.IsCellReadOnly"/>.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Optional per-cell read-only predicate over the row item; <see langword="null"/> means
    /// not read-only at the cell level.</summary>
    public Func<object?, bool>? ReadOnlyCellSelector { get; set; }

    /// <summary>Maps a row item to the check state of a <see cref="DataGridViewColumnKind.Check"/>
    /// cell; unset cells render unchecked.</summary>
    public Func<object?, bool>? CheckedSelector { get; set; }

    /// <summary>Writes the toggled check state back to the row item when a
    /// <see cref="DataGridViewColumnKind.Check"/> cell is clicked and the cell is not read-only;
    /// <see langword="null"/> makes the glyph display-only.</summary>
    public Action<object?, bool>? CheckedSetter { get; set; }

    /// <summary>Maps a row item to whether a <see cref="DataGridViewColumnKind.Button"/> cell is
    /// enabled; <see langword="null"/> means always enabled. Disabled buttons grey their text and
    /// raise no <see cref="DataGridView.CellContentClick"/>.</summary>
    public Func<object?, bool>? EnabledSelector { get; set; }

    /// <summary>Maps a row item to the icons of a <see cref="DataGridViewColumnKind.MultiImage"/>
    /// cell, painted side by side. Return a cached list — the selector runs on the paint path.</summary>
    public Func<object?, IReadOnlyList<IImage>>? ImagesSelector { get; set; }

    /// <summary>Maps a row item to the 0..100 fill of a <see cref="DataGridViewColumnKind.Progress"/>
    /// cell; values outside the range are clamped.</summary>
    public Func<object?, int>? ProgressSelector { get; set; }

    /// <summary>Optional per-cell style overrides (colors, alignment); <see langword="null"/> keeps the
    /// defaults. Returns a value type, so it is safe on the paint path.</summary>
    public Func<object?, DataGridViewCellStyle>? CellStyleSelector { get; set; }

    /// <summary>Optional override of the displayed cell text; return <see langword="null"/> to fall
    /// back to <see cref="ValueSelector"/>.</summary>
    public Func<object?, string?>? DisplayTextSelector
    {
        get => field;
        set
        {
            field = value;
            this.DisplayTextCache = null;
        }
    }

    /// <summary>Optional formatter over the <see cref="ValueSelector"/> result — the reflection-free
    /// <c>CellFormatting</c> seam: it runs after the value selector and shapes the displayed text
    /// only, so editors still seed from the raw value. Its result is cached per cell until the row
    /// changes, so it never runs per frame.</summary>
    public Func<object?, string>? FormatSelector
    {
        get => field;
        set
        {
            field = value;
            this.DisplayTextCache = null;
        }
    }

    /// <summary>Optional per-cell tooltip text, surfaced through
    /// <see cref="DataGridView.GetCellTooltip"/>; <see langword="null"/> for none.</summary>
    public Func<object?, string?>? TooltipSelector { get; set; }

    /// <summary>Whether clicking this column's header sorts the grid. Defaults to
    /// <see cref="DataGridViewColumnSortMode.NotSortable"/>.</summary>
    public DataGridViewColumnSortMode SortMode { get; set; }

    /// <summary>Optional row-item comparison used when this column sorts; <see langword="null"/> falls
    /// back to comparing the values <see cref="ValueSelector"/> produces.</summary>
    public Comparison<object?>? SortComparison { get; set; }

    /// <summary>How the column width is computed. Defaults to
    /// <see cref="DataGridViewAutoSizeColumnMode.None"/>.</summary>
    public DataGridViewAutoSizeColumnMode AutoSizeMode { get; set; }

    /// <summary>Whether the column is pinned at the left edge: frozen columns form the leading run of
    /// the display order and stay put while <see cref="DataGridView.HorizontalOffset"/> scrolls the
    /// rest underneath them.</summary>
    public bool Frozen { get; set; }

    /// <summary>The column's position in the display order, or a negative value (the default) for its
    /// position in <see cref="DataGridView.Columns"/>. A drag-reorder gesture rewrites it on every
    /// column; assigning it directly reorders the presentation on the next repaint —
    /// <see cref="DataGridView.Columns"/> itself is never reordered.</summary>
    public int DisplayIndex { get; set; } = -1;

    /// <summary>Writes a committed <see cref="DataGridViewColumnKind.Text"/> edit back to the row
    /// item; <see langword="null"/> (the default) makes the cell display-only, like an unset
    /// <see cref="CheckedSetter"/>.</summary>
    public Action<object?, string>? TextSetter { get; set; }

    /// <summary>Maps a row item to the choices a <see cref="DataGridViewColumnKind.ComboBox"/> cell
    /// offers while editing. Return a cached list — required (with <see cref="ValueSetter"/>) for the
    /// cell to enter edit mode.</summary>
    public Func<object?, IReadOnlyList<object?>>? ItemsSelector { get; set; }

    /// <summary>Maps a <see cref="ItemsSelector"/> choice to its display text in the popup list;
    /// <see langword="null"/> falls back to <c>ToString()</c>. It also shapes the closed cell's text
    /// for the <see cref="DataGridViewColumnKind.ListBox"/> and
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> kinds, so writing it drops the cached
    /// display text.</summary>
    public Func<object?, string>? ItemDisplaySelector
    {
        get => field;
        set
        {
            field = value;
            this.DisplayTextCache = null;
        }
    }

    /// <summary>
    /// How a <see cref="DataGridViewColumnKind.ListBox"/> cell's popup lets the user pick:
    /// <see cref="Hawkynt.NativeForms.SelectionMode.One"/> (the default) is a single pick committed
    /// through <see cref="ValueSetter"/>; <see cref="Hawkynt.NativeForms.SelectionMode.MultiSimple"/>
    /// and <see cref="Hawkynt.NativeForms.SelectionMode.MultiExtended"/> turn the cell into a
    /// set-valued one — read through <see cref="CheckedItemsSelector"/>, committed through
    /// <see cref="CheckedItemsSetter"/> — and
    /// <see cref="Hawkynt.NativeForms.SelectionMode.None"/> makes it display-only. Ignored by every
    /// other kind (a <see cref="DataGridViewColumnKind.CheckedListBox"/> cell is always set-valued).
    /// </summary>
    public SelectionMode SelectionMode { get; set; } = SelectionMode.One;

    /// <summary>
    /// Maps a row item to the items currently in the set of a
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> — or multi-select
    /// <see cref="DataGridViewColumnKind.ListBox"/> — cell. The cell's text is those items' display
    /// texts joined with <c>", "</c>, cached per row, so the selector never runs on the paint path.
    /// </summary>
    public Func<object?, IReadOnlyList<object?>>? CheckedItemsSelector
    {
        get => field;
        set
        {
            field = value;
            this.DisplayTextCache = null;
        }
    }

    /// <summary>
    /// Writes the whole picked set back to the row item when a
    /// <see cref="DataGridViewColumnKind.CheckedListBox"/> — or multi-select
    /// <see cref="DataGridViewColumnKind.ListBox"/> — cell commits. The grid hands over a freshly
    /// allocated array holding the picked items in <see cref="ItemsSelector"/> order and keeps no
    /// reference to it, so the setter may store it as-is.
    /// </summary>
    public Action<object?, IReadOnlyList<object?>>? CheckedItemsSetter { get; set; }

    /// <summary>Writes the choice picked in a <see cref="DataGridViewColumnKind.ComboBox"/> editor
    /// back to the row item (row item, chosen value).</summary>
    public Action<object?, object?>? ValueSetter { get; set; }

    /// <summary>Maps a row item to the number a <see cref="DataGridViewColumnKind.NumericUpDown"/>
    /// editor starts from.</summary>
    public Func<object?, decimal>? NumberSelector { get; set; }

    /// <summary>Writes a committed <see cref="DataGridViewColumnKind.NumericUpDown"/> edit back to
    /// the row item, already clamped into [<see cref="Minimum"/>, <see cref="Maximum"/>].</summary>
    public Action<object?, decimal>? NumberSetter { get; set; }

    /// <summary>The lowest value the <see cref="DataGridViewColumnKind.NumericUpDown"/> editor accepts.</summary>
    public decimal Minimum { get; set; }

    /// <summary>The highest value the <see cref="DataGridViewColumnKind.NumericUpDown"/> editor accepts.</summary>
    public decimal Maximum { get; set; } = 100m;

    /// <summary>The step of the <see cref="DataGridViewColumnKind.NumericUpDown"/> editor's spinner
    /// buttons and Up/Down keys.</summary>
    public decimal Increment { get; set; } = 1m;

    /// <summary>The number of decimal digits the <see cref="DataGridViewColumnKind.NumericUpDown"/>
    /// editor displays (0–28).</summary>
    public int DecimalPlaces { get; set; }

    /// <summary>Maps a row item to the date a <see cref="DataGridViewColumnKind.DateTime"/> editor's
    /// popup calendar starts on.</summary>
    public Func<object?, DateTime>? DateSelector { get; set; }

    /// <summary>Writes the day picked in a <see cref="DataGridViewColumnKind.DateTime"/> editor back
    /// to the row item; the time of day of the <see cref="DateSelector"/> value is preserved.</summary>
    public Action<object?, DateTime>? DateSetter { get; set; }

    /// <summary>The input mask a <see cref="DataGridViewColumnKind.MaskedText"/> editor forces —
    /// the <see cref="MaskedTextBox.Mask"/> language; empty hosts a plain masked box.</summary>
    public string Mask
    {
        get => field;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Maps a row item to the swatch color of a <see cref="DataGridViewColumnKind.Color"/>
    /// cell; required (with <see cref="ColorSetter"/>) for the cell to edit.</summary>
    public Func<object?, Color>? ColorSelector { get; set; }

    /// <summary>Writes the color picked in the native color dialog back to the row item when a
    /// <see cref="DataGridViewColumnKind.Color"/> cell edits.</summary>
    public Action<object?, Color>? ColorSetter { get; set; }
}
