using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Rubber-band selection on <see cref="ListView"/> and <see cref="DataGridView"/> (PRD §14): the drag
/// threshold that keeps a click a click, the three combine modes Ctrl and Shift select, the band the
/// paint pass draws, and the edge auto-scroll that keeps the gesture going past the viewport.
/// </summary>
/// <remarks>
/// Both controls drive the same <c>MarqueeDrag</c>, so the fixture asserts the same semantics twice
/// rather than trusting that sharing an engine made them agree.
/// </remarks>
[TestFixture]
internal sealed class MarqueeSelectionTests
{
    private sealed record Person(string Name, int Age);

    private static HeadlessBackend Realize(OwnerDrawnControl control, out HeadlessCanvasPeer canvas)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return backend;
    }

    /// <summary>Five rows of 22px at y 0, 22, 44, 66, 88, with empty space from y 110 down.</summary>
    private static ListView MakeList(out HeadlessCanvasPeer canvas)
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.Items.AddRange(
        [
            new ListViewItem("a"), new ListViewItem("b"), new ListViewItem("c"),
            new ListViewItem("d"), new ListViewItem("e"),
        ]);

        Realize(list, out canvas);
        return list;
    }

    /// <summary>A 22px header then rows 0..2 at y 22, 44, 66.</summary>
    private static DataGridView MakeGrid(out HeadlessCanvasPeer canvas)
    {
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110), MultiSelect = true };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age) { Width = 60 });
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);

        Realize(grid, out canvas);
        return grid;
    }

    // --- ListView --------------------------------------------------------------------------------

    [Test]
    public void A_band_from_empty_space_selects_the_rows_it_crosses()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(250, 200); // below the last row, so a band rather than an item press
        canvas.RaiseMouseMove(5, 50);
        canvas.RaiseMouseUp(5, 50);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void The_selection_survives_the_button_coming_up()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(5, 50);
        canvas.RaiseMouseUp(5, 50);
        canvas.RaiseMouseMove(5, 5); // a move with no button held must not keep sweeping

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void A_press_that_barely_moves_stays_a_click()
    {
        var list = MakeList(out var canvas);
        canvas.RaiseMouseDown(10, 5); // select row 0 first, so there is something to lose

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(250 + MarqueeThreshold - 1, 200);
        canvas.RaiseMouseUp(250, 200);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }), "under the threshold nothing was swept");
    }

    /// <summary>Mirrors <c>MarqueeDrag.Threshold</c>, which is internal to the control assembly.</summary>
    private const int MarqueeThreshold = 4;

    [Test]
    public void A_plain_band_replaces_what_was_selected()
    {
        var list = MakeList(out var canvas);
        canvas.RaiseMouseDown(10, 5); // row 0

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(5, 50);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2, 3, 4 }), "row 0 is outside the band and drops");
    }

    [Test]
    public void Shift_adds_the_band_to_what_was_selected()
    {
        var list = MakeList(out var canvas);
        canvas.RaiseMouseDown(10, 5); // row 0

        canvas.RaiseMouseDown(250, 200, MouseButtons.Left, KeyModifiers.Shift);
        canvas.RaiseMouseMove(5, 50);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2, 3, 4 }));
    }

    [Test]
    public void Ctrl_flips_what_the_band_covers()
    {
        var list = MakeList(out var canvas);
        canvas.RaiseMouseDown(10, 5); // row 0
        canvas.RaiseMouseDown(10, 71, MouseButtons.Left, KeyModifiers.Control); // + row 3

        canvas.RaiseMouseDown(250, 200, MouseButtons.Left, KeyModifiers.Control);
        canvas.RaiseMouseMove(5, 50); // covers rows 2, 3, 4

        Assert.That(
            list.SelectedIndices,
            Is.EqualTo(new[] { 0, 2, 4 }),
            "row 3 was selected and the band flips it off; rows 2 and 4 flip on; row 0 is untouched");
    }

    [Test]
    public void One_selection_change_per_move_that_changes_something()
    {
        var list = MakeList(out var canvas);
        var changes = 0;
        list.SelectedIndexChanged += (_, _) => ++changes;

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(5, 50);
        var afterFirst = changes;
        canvas.RaiseMouseMove(5, 52); // same three rows, so nothing to report

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1), "a move that lands on the same rows is silent");
        });
    }

    [Test]
    public void The_band_is_drawn_while_it_is_being_swept_and_not_afterwards()
    {
        var list = MakeList(out var canvas);

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(50, 50);
        var sweeping = canvas.RaisePaint();
        var whileSweeping = sweeping.Operations.Exists(static op => op.StartsWith("rect") && op.EndsWith("50,50,200,150"));

        canvas.RaiseMouseUp(50, 50);
        var settled = canvas.RaisePaint();
        var afterwards = settled.Operations.Exists(static op => op.EndsWith("50,50,200,150"));

        Assert.Multiple(() =>
        {
            Assert.That(whileSweeping, Is.True, "the swept rectangle outlines while the button is down");
            Assert.That(afterwards, Is.False, "and vanishes when it comes up");
        });
    }

    [Test]
    public void The_edge_auto_scroll_runs_only_while_the_pointer_is_outside()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
        list.Items.AddRange([new ListViewItem("a"), new ListViewItem("b"), new ListViewItem("c")]);
        var backend = Realize(list, out var canvas);

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(5, 300); // below the control
        var outsideRunning = backend.Timers.Exists(static t => t.IsRunning);

        canvas.RaiseMouseMove(5, 50); // back inside
        var insideRunning = backend.Timers.Exists(static t => t.IsRunning);

        Assert.Multiple(() =>
        {
            Assert.That(outsideRunning, Is.True, "the band keeps growing while the pointer sits outside");
            Assert.That(insideRunning, Is.False, "and stops the moment it comes back");
        });
    }

    [Test]
    public void Dragging_a_grid_past_the_bottom_edge_scrolls_and_keeps_sweeping()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110), MultiSelect = true };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        for (var i = 0; i < 20; ++i)
            grid.Items.Add(new Person($"person {i}", i));

        var backend = Realize(grid, out var canvas);

        canvas.RaiseMouseDown(20, 30); // row 0
        canvas.RaiseMouseMove(20, 300); // held below the control
        var beforeTicks = grid.TopRow;

        var timer = backend.Timers.Single(static t => t.IsRunning);
        timer.FireTick();
        timer.FireTick();

        Assert.Multiple(() =>
        {
            Assert.That(beforeTicks, Is.Zero, "the move itself does not scroll");
            Assert.That(grid.TopRow, Is.EqualTo(2), "one row per tick while the pointer sits outside");
            Assert.That(grid.SelectedItems.Count(), Is.GreaterThan(3), "and the band keeps sweeping as it scrolls");
        });
    }

    [Test]
    public void A_single_select_list_never_starts_a_band()
    {
        var list = MakeList(out var canvas);
        list.MultiSelect = false;
        canvas.RaiseMouseDown(10, 5); // row 0

        canvas.RaiseMouseDown(250, 200);
        canvas.RaiseMouseMove(5, 50);

        Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
    }

    // --- DataGridView ----------------------------------------------------------------------------

    [Test]
    public void A_grid_band_selects_the_rows_it_crosses()
    {
        var grid = MakeGrid(out var canvas);

        canvas.RaiseMouseDown(20, 30); // row 0
        canvas.RaiseMouseMove(120, 70); // down across rows 1 and 2

        Assert.That(grid.SelectedItems.Select(o => ((Person)o!).Name), Is.EqualTo(new[] { "Alice", "Bob", "Carol" }));
    }

    [Test]
    public void A_grid_band_moves_the_current_row_to_the_edge_it_is_dragging()
    {
        var grid = MakeGrid(out var canvas);

        canvas.RaiseMouseDown(20, 30); // row 0
        canvas.RaiseMouseMove(120, 70);

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(2), "dragging down leaves the current row at the bottom");
    }

    [Test]
    public void A_grid_band_dragged_upward_leaves_the_current_row_at_the_top()
    {
        var grid = MakeGrid(out var canvas);

        canvas.RaiseMouseDown(20, 70); // row 2
        canvas.RaiseMouseMove(20, 30);

        Assert.That(grid.SelectedRowIndex, Is.Zero);
    }

    [Test]
    public void Ctrl_flips_what_a_grid_band_covers()
    {
        var grid = MakeGrid(out var canvas);
        canvas.RaiseMouseDown(20, 70); // row 2

        canvas.RaiseMouseDown(20, 30, MouseButtons.Left, KeyModifiers.Control); // toggles row 0 on, arms the band
        canvas.RaiseMouseMove(20, 70); // band now covers rows 0, 1 and 2

        Assert.That(
            grid.SelectedItems.Select(o => ((Person)o!).Name),
            Is.EqualTo(new[] { "Alice", "Bob" }),
            "rows 0 and 1 flip on, and row 2 — selected before the press — flips off");
    }

    [Test]
    public void A_grid_press_that_barely_moves_stays_a_click()
    {
        var grid = MakeGrid(out var canvas);

        canvas.RaiseMouseDown(20, 30); // row 0
        canvas.RaiseMouseMove(20 + MarqueeThreshold - 1, 30);

        Assert.That(grid.SelectedItems.Select(o => ((Person)o!).Name), Is.EqualTo(new[] { "Alice" }));
    }

    [Test]
    public void A_single_select_grid_never_starts_a_band()
    {
        var grid = MakeGrid(out var canvas);
        grid.MultiSelect = false;

        canvas.RaiseMouseDown(20, 30); // row 0
        canvas.RaiseMouseMove(120, 70);

        Assert.That(grid.SelectedRowIndex, Is.Zero, "the press selected row 0 and the move did nothing");
    }

    [Test]
    public void A_grid_band_skips_rows_the_selectors_rule_out()
    {
        var grid = MakeGrid(out var canvas);
        grid.RowSelectableSelector = static o => ((Person)o!).Name != "Bob";

        canvas.RaiseMouseDown(20, 30); // row 0
        canvas.RaiseMouseMove(120, 70);

        Assert.That(grid.SelectedItems.Select(o => ((Person)o!).Name), Is.EqualTo(new[] { "Alice", "Carol" }));
    }
}
