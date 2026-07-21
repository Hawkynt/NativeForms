using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The two popup-list column kinds: <see cref="DataGridViewColumnKind.ListBox"/> — a single pick or,
/// once the column's <see cref="DataGridViewColumn.SelectionMode"/> admits more than one, a whole
/// set — and <see cref="DataGridViewColumnKind.CheckedListBox"/>, whose cells are always set-valued.
/// Covers the closed cell's summary text and its caching, the popup's geometry and painting, every
/// pick gesture, the commit shape handed to <see cref="DataGridViewColumn.CheckedItemsSetter"/>, the
/// <see cref="DataGridView.CellItemCheck"/> veto, the read-only matrix, validation and paste.
/// </summary>
[TestFixture]
internal sealed class DataGridViewListColumnTests
{
    private sealed class Row
    {
        public string Name = string.Empty;
        public string Status = string.Empty;
        public IReadOnlyList<object?> Tags = [];
    }

    private static readonly object?[] _Choices = ["New", "Active", "Blocked", "Done"];

    private static HeadlessBackend RealizeBackend(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessCanvasPeer>().First(static peer => peer is not HeadlessPopupPeer);

    /// <summary>A three-row grid whose second column (x 100..160) is the column under test; the
    /// header is 22 px and every row 22 px, so row 1's cell is y 44..66.</summary>
    private static DataGridView MakeGrid(out List<Row> rows)
    {
        rows = [new() { Name = "Alice" }, new() { Name = "Bob" }, new() { Name = "Carol" }];
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Status", static o => ((Row)o!).Status) { Width = 60 });
        grid.DataSource = rows;
        return grid;
    }

    /// <summary>Turns the second column into a single-select list bound to <see cref="Row.Status"/>.</summary>
    private static DataGridViewColumn AsSingleList(DataGridView grid)
    {
        var column = grid.Columns[1];
        column.Kind = DataGridViewColumnKind.ListBox;
        column.ItemsSelector = static _ => _Choices;
        column.ValueSetter = static (o, value) => ((Row)o!).Status = (string)value!;
        return column;
    }

    /// <summary>Turns the second column into a set-valued list of the given kind, bound to
    /// <see cref="Row.Tags"/>.</summary>
    private static DataGridViewColumn AsSetList(DataGridView grid, DataGridViewColumnKind kind, SelectionMode mode = SelectionMode.MultiExtended)
    {
        var column = grid.Columns[1];
        column.Kind = kind;
        column.SelectionMode = mode;
        column.ItemsSelector = static _ => _Choices;
        column.CheckedItemsSelector = static o => ((Row)o!).Tags;
        column.CheckedItemsSetter = static (o, items) => ((Row)o!).Tags = items;
        return column;
    }

    private static HeadlessPopupPeer PopupOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessPopupPeer>().Single();

    /// <summary>The y a click lands on to hit the popup's nth row, at the theme's 22 px row height.</summary>
    private static int PopupRow(int index) => (index * 22) + 11;

    // --- Single-select list ------------------------------------------------------------------------

    [Test]
    public void ListBox_cell_paints_the_value_and_the_drop_arrow()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Status = "Active";
        AsSingleList(grid);
        var backend = RealizeBackend(grid);

        var g = CanvasOf(backend).RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Active\"") && o.Contains("@104,22")), Is.True, "cell value");
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 148,30-156,30"), "the drop arrow the combo cell paints too");
        });
    }

    [Test]
    public void ListBox_cell_text_runs_through_the_item_display_selector()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Status = "Active";
        var column = AsSingleList(grid);
        column.ItemDisplaySelector = static o => $"[{o}]";
        var backend = RealizeBackend(grid);

        var g = CanvasOf(backend).RaisePaint();

        Assert.That(g.DrewText("[Active]"), Is.True);
    }

    [Test]
    public void ListBox_editing_opens_a_popup_below_the_cell_and_a_click_commits_the_pick()
    {
        var grid = MakeGrid(out var rows);
        AsSingleList(grid);
        var backend = RealizeBackend(grid);

        Assert.That(grid.BeginEdit(1, 1), Is.True);

        var popup = PopupOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsShown, Is.True);
            Assert.That(popup.ShowCalls[0].Location, Is.EqualTo(new Point(100, 66)), "anchored under the cell");
            Assert.That(popup.ShowCalls[0].Size, Is.EqualTo(new Size(60, 4 * 22)), "cell width, one row per choice");
        });

        var g = popup.RaisePaint();
        Assert.That(g.DrewText("Blocked"), Is.True, "the choices are painted as list rows");

        popup.RaiseMouseDown(10, PopupRow(2)); // "Blocked"
        Assert.Multiple(() =>
        {
            Assert.That(rows[1].Status, Is.EqualTo("Blocked"), "committed through the value setter");
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void ListBox_popup_is_taller_than_a_combo_drop_down_and_scrolls()
    {
        var many = new object?[20];
        for (var i = 0; i < many.Length; ++i)
            many[i] = $"Item {i}";

        var grid = MakeGrid(out _);
        var column = AsSingleList(grid);
        column.ItemsSelector = _ => many;
        var backend = RealizeBackend(grid);

        grid.BeginEdit(1, 1);
        var popup = PopupOf(backend);
        Assert.That(popup.ShowCalls[0].Size.Height, Is.EqualTo(12 * 22), "12 rows, where a combo stops at 8");

        popup.RaiseMouseWheel(-1); // one notch = three rows down
        var g = popup.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Item 3"), Is.True, "scrolled to the fourth item");
            Assert.That(g.DrewText("Item 0"), Is.False, "the first rows scrolled out");
        });
    }

    [Test]
    public void ListBox_popup_dismissal_cancels_a_single_select_edit()
    {
        var grid = MakeGrid(out var rows);
        AsSingleList(grid);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);

        PopupOf(backend).FireDismiss();

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(rows[0].Status, Is.Empty, "dismissing a single-select list writes nothing");
        });
    }

    [Test]
    public void ListBox_column_without_a_value_setter_never_edits()
    {
        var grid = MakeGrid(out _);
        var column = grid.Columns[1];
        column.Kind = DataGridViewColumnKind.ListBox;
        column.ItemsSelector = static _ => _Choices;
        RealizeBackend(grid);

        Assert.That(grid.BeginEdit(0, 1), Is.False);
    }

    [Test]
    public void ListBox_column_in_SelectionMode_None_never_edits()
    {
        var grid = MakeGrid(out _);
        AsSingleList(grid).SelectionMode = SelectionMode.None;
        RealizeBackend(grid);

        Assert.That(grid.BeginEdit(0, 1), Is.False);
    }

    // --- Multi-select list -------------------------------------------------------------------------

    [Test]
    public void Multi_select_ListBox_cell_paints_the_joined_summary()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "New", "Done" };
        AsSetList(grid, DataGridViewColumnKind.ListBox);
        var backend = RealizeBackend(grid);

        var g = CanvasOf(backend).RaisePaint();

        Assert.That(g.DrewText("New, Done"), Is.True, "the set summarises comma-joined");
    }

    [Test]
    public void Multi_select_ListBox_commits_the_picked_items_as_a_collection()
    {
        var grid = MakeGrid(out var rows);
        rows[1].Tags = new object?[] { "New" };
        AsSetList(grid, DataGridViewColumnKind.ListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(1, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(1)); // a plain click replaces the set with "Active"
        popup.RaiseMouseDown(10, PopupRow(3), modifiers: KeyModifiers.Control); // Ctrl adds "Done"
        Assert.That(rows[1].Tags, Has.Count.EqualTo(1), "nothing is written while the popup is still open");

        popup.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(rows[1].Tags, Is.EqualTo(new object?[] { "Active", "Done" }), "in ItemsSelector order");
        });
    }

    [Test]
    public void Multi_extended_ListBox_shift_click_picks_the_range_from_the_anchor()
    {
        var grid = MakeGrid(out var rows);
        AsSetList(grid, DataGridViewColumnKind.ListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(1)); // anchor on "Active"
        popup.RaiseMouseDown(10, PopupRow(3), modifiers: KeyModifiers.Shift);
        popup.RaiseKeyDown(Keys.Enter);

        Assert.That(rows[0].Tags, Is.EqualTo(new object?[] { "Active", "Blocked", "Done" }));
    }

    [Test]
    public void Multi_simple_ListBox_toggles_on_every_plain_click()
    {
        var grid = MakeGrid(out var rows);
        AsSetList(grid, DataGridViewColumnKind.ListBox, SelectionMode.MultiSimple);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(0));
        popup.RaiseMouseDown(10, PopupRow(2));
        popup.RaiseMouseDown(10, PopupRow(0)); // toggles the first one back off
        popup.RaiseKeyDown(Keys.Enter);

        Assert.That(rows[0].Tags, Is.EqualTo(new object?[] { "Blocked" }));
    }

    [Test]
    public void Escape_abandons_a_set_valued_edit_without_writing()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "New" };
        AsSetList(grid, DataGridViewColumnKind.ListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(2));
        popup.RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(rows[0].Tags, Is.EqualTo(new object?[] { "New" }), "the cell keeps its set");
        });
    }

    // --- Checked list ------------------------------------------------------------------------------

    [Test]
    public void CheckedListBox_cell_paints_the_joined_summary_of_the_checked_items()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "Active", "Blocked" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);

        var g = CanvasOf(backend).RaisePaint();

        Assert.That(g.DrewText("Active, Blocked"), Is.True);
    }

    [Test]
    public void CheckedListBox_popup_paints_a_check_square_per_row()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "Active" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);

        var g = PopupOf(backend).RaisePaint();

        Assert.Multiple(() =>
        {
            // The glyph box sits at x 2 and the label starts past it, exactly like a CheckedListBox row.
            Assert.That(g.Operations.Exists(static o => o.StartsWith("rect") && o.Contains("2,4,14,14")), Is.True, "the first row's check square");
            Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Active\"") && o.Contains("@22,")), Is.True, "the label indents past the square");
        });
    }

    [Test]
    public void CheckedListBox_ticks_commit_as_one_set_on_Enter()
    {
        var grid = MakeGrid(out var rows);
        rows[2].Tags = new object?[] { "New" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(2, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(0)); // untick "New"
        popup.RaiseMouseDown(10, PopupRow(1)); // tick "Active"
        popup.RaiseMouseDown(10, PopupRow(3)); // tick "Done"
        popup.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(rows[2].Tags, Is.EqualTo(new object?[] { "Active", "Done" }));
            Assert.That(grid.IsEditing, Is.False);
        });
    }

    [Test]
    public void Dismissing_a_set_valued_popup_abandons_the_ticks()
    {
        // Dismissal has to mean the same thing on every backend, and one of them swallows Escape at
        // the popup's own top-level rather than routing it to the grid: were dismissal to commit,
        // Escape would abandon the edit on one backend and save it on the next.
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "New" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(2)); // tick "Blocked"
        popup.FireDismiss();

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(rows[0].Tags, Is.EqualTo(new object?[] { "New" }), "the tick was abandoned");
        });
    }

    [Test]
    public void CheckedListBox_space_toggles_the_row_under_the_caret()
    {
        var grid = MakeGrid(out var rows);
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseKeyDown(Keys.Down); // the caret starts unset, so Down lands on row 0
        popup.RaiseKeyDown(Keys.Space);
        popup.RaiseKeyDown(Keys.Enter);

        Assert.That(rows[0].Tags, Is.EqualTo(new object?[] { "New" }));
    }

    [Test]
    public void CheckedListBox_honours_a_CellItemCheck_veto()
    {
        var grid = MakeGrid(out var rows);
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        var seen = 0;
        grid.CellItemCheck += (_, e) =>
        {
            ++seen;
            if (e.Index == 1)
                e.NewValue = e.CurrentValue; // veto exactly the "Active" tick
        };
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(1)); // vetoed
        popup.RaiseMouseDown(10, PopupRow(2)); // allowed
        popup.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(2), "every attempted tick is announced");
            Assert.That(rows[0].Tags, Is.EqualTo(new object?[] { "Blocked" }));
        });
    }

    // --- Shared editing contract -------------------------------------------------------------------

    [Test]
    public void Set_valued_commit_runs_CellValidating_over_the_picked_collection()
    {
        var grid = MakeGrid(out var rows);
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        object? proposed = null;
        grid.CellValidating += (_, e) =>
        {
            proposed = e.ProposedValue;
            e.Cancel = true;
        };
        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);

        popup.RaiseMouseDown(10, PopupRow(0));
        popup.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(proposed, Is.EqualTo(new object?[] { "New" }), "the whole set is the proposed value");
            Assert.That(rows[0].Tags, Is.Empty, "the veto wrote nothing");
            Assert.That(grid.IsEditing, Is.True, "and kept the popup open");
        });
    }

    [Test]
    public void Set_valued_commit_raises_CellEndEdit_once()
    {
        var grid = MakeGrid(out _);
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        var ended = 0;
        grid.CellEndEdit += (_, _) => ++ended;
        grid.BeginEdit(0, 1);

        PopupOf(backend).RaiseKeyDown(Keys.Enter);

        Assert.That(ended, Is.EqualTo(1));
    }

    [TestCase(DataGridViewColumnKind.ListBox)]
    [TestCase(DataGridViewColumnKind.CheckedListBox)]
    public void List_columns_honour_the_three_level_read_only_matrix(DataGridViewColumnKind kind)
    {
        var grid = MakeGrid(out _);
        var column = AsSetList(grid, kind);
        RealizeBackend(grid);

        grid.ReadOnly = true;
        Assert.That(grid.BeginEdit(0, 1), Is.False, "the grid level refuses");
        grid.ReadOnly = false;

        column.ReadOnly = true;
        Assert.That(grid.BeginEdit(0, 1), Is.False, "the column level refuses");
        column.ReadOnly = false;

        column.ReadOnlyCellSelector = static o => ((Row)o!).Name == "Alice";
        Assert.That(grid.BeginEdit(0, 1), Is.False, "the per-cell predicate refuses Alice's row");
        Assert.That(grid.BeginEdit(1, 1), Is.True, "and admits Bob's");
    }

    [Test]
    public void Committing_a_set_refreshes_the_cached_summary_text()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "New" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);
        Assert.That(canvas.RaisePaint().DrewText("New"), Is.True, "primes the display-text cache");

        grid.BeginEdit(0, 1);
        var popup = PopupOf(backend);
        popup.RaiseMouseDown(10, PopupRow(3)); // tick "Done"
        popup.RaiseKeyDown(Keys.Enter);

        Assert.That(canvas.RaisePaint().DrewText("New, Done"), Is.True, "the cell re-formatted after the write");
    }

    [Test]
    public void Pasting_a_summary_maps_it_back_onto_the_checked_set()
    {
        var grid = MakeGrid(out var rows);
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        RealizeBackend(grid);
        grid.SelectedRowIndex = 1;
        grid.CurrentColumnIndex = 1;

        grid.Paste("Active, Done");

        Assert.That(rows[1].Tags, Is.EqualTo(new object?[] { "Active", "Done" }));
    }

    [Test]
    public void Pasting_an_unknown_item_leaves_the_set_untouched()
    {
        var grid = MakeGrid(out var rows);
        rows[1].Tags = new object?[] { "New" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        RealizeBackend(grid);
        grid.SelectedRowIndex = 1;
        grid.CurrentColumnIndex = 1;

        grid.Paste("Active, Nonsense");

        Assert.That(rows[1].Tags, Is.EqualTo(new object?[] { "New" }), "a partial set is never written");
    }

    [Test]
    public void Copying_a_set_valued_cell_yields_its_summary()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Tags = new object?[] { "New", "Blocked" };
        AsSetList(grid, DataGridViewColumnKind.CheckedListBox);
        RealizeBackend(grid);
        grid.SelectedRowIndex = 0;

        Assert.That(grid.GetClipboardContent(), Is.EqualTo("Alice\tNew, Blocked"));
    }
}
