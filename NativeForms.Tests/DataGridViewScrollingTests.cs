using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The interactive scrollbar strips: they appear only when the content overflows, their arrows,
/// channels and thumbs drive <see cref="DataGridView.TopRow"/> and
/// <see cref="DataGridView.HorizontalOffset"/> through the shared scrollbar renderer, the thumbs
/// stay in sync with wheel and programmatic scrolling (one state, no mirror), and the strips narrow
/// the scrollable viewport. Also proves virtualization stays bounded with scrollbars, fill sizing
/// and display formatting all on.
/// </summary>
[TestFixture]
internal sealed class DataGridViewScrollingTests
{
    private sealed record Person(string Name, int Age);

    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    /// <summary>22px header + 4 rows at 22px; columns at x 0..100 and 100..160; scrollbars 16px.</summary>
    private static DataGridView MakeGrid(int rowCount)
    {
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age) { Width = 60 });
        for (var i = 0; i < rowCount; ++i)
            grid.Items.Add(new Person($"P{i}", i));
        return grid;
    }

    [Test]
    public void Vertical_scrollbar_appears_only_when_the_rows_overflow()
    {
        var fitting = MakeGrid(3); // 66px of rows in an 88px viewport
        Realize(fitting);
        Assert.That(fitting.IsVerticalScrollBarVisible, Is.False);

        var overflowing = MakeGrid(10);
        Realize(overflowing);
        Assert.Multiple(() =>
        {
            Assert.That(overflowing.IsVerticalScrollBarVisible, Is.True);
            Assert.That(overflowing.IsHorizontalScrollBarVisible, Is.False, "160px of columns fit the remaining 184px");
        });
    }

    [Test]
    public void Vertical_arrow_clicks_step_one_row()
    {
        var grid = MakeGrid(10); // bar at x 184..200, arrows y 22..38 and 94..110
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(190, 100); // increase arrow
        Assert.That(grid.TopRow, Is.EqualTo(1));

        canvas.RaiseMouseDown(190, 30); // decrease arrow
        Assert.Multiple(() =>
        {
            Assert.That(grid.TopRow, Is.Zero);
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(-1), "scrollbar presses never select");
        });
    }

    [Test]
    public void Vertical_channel_click_pages_by_the_visible_rows()
    {
        var grid = MakeGrid(10); // track y 38..94, thumb at value 0 spans y 38..60
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(190, 80); // channel below the thumb
        Assert.That(grid.TopRow, Is.EqualTo(4), "one page of 4 visible rows");

        canvas.RaiseMouseDown(190, 40); // channel above the thumb (thumb now near the bottom)
        Assert.That(grid.TopRow, Is.Zero);
    }

    [Test]
    public void Vertical_thumb_drag_scrubs_the_top_row()
    {
        var grid = MakeGrid(10);
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(190, 45); // inside the thumb (y 38..60)
        canvas.RaiseMouseMove(190, 62); // 17px along the 34px travel over a 0..6 range
        Assert.That(grid.TopRow, Is.EqualTo(3));

        canvas.RaiseMouseUp(190, 62);
        canvas.RaiseMouseMove(190, 100); // the drag ended with the button
        Assert.That(grid.TopRow, Is.EqualTo(3));
    }

    [Test]
    public void Horizontal_scrollbar_appears_only_when_the_columns_overflow()
    {
        var grid = MakeGrid(3);
        Realize(grid);
        Assert.That(grid.IsHorizontalScrollBarVisible, Is.False);

        grid.Columns[0].Width = 300; // total 360 in a 200px viewport
        Assert.Multiple(() =>
        {
            Assert.That(grid.IsHorizontalScrollBarVisible, Is.True);
            Assert.That(grid.IsVerticalScrollBarVisible, Is.False, "66px of rows fit the remaining 72px");
        });
    }

    [Test]
    public void Horizontal_arrow_clicks_step_thirty_pixels()
    {
        var grid = MakeGrid(3);
        grid.Columns[0].Width = 300; // bar at y 94..110, arrows x 0..16 and 184..200
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(190, 100); // increase arrow
        Assert.That(grid.HorizontalOffset, Is.EqualTo(30));

        canvas.RaiseMouseDown(8, 100); // decrease arrow
        Assert.That(grid.HorizontalOffset, Is.Zero);
    }

    [Test]
    public void Horizontal_thumb_drag_scrubs_the_offset()
    {
        var grid = MakeGrid(3);
        grid.Columns[0].Width = 300; // track x 16..184, 93px thumb over a 0..160 range
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 100); // inside the thumb (x 16..109)
        canvas.RaiseMouseMove(90, 100); // 40px along the 75px travel
        Assert.Multiple(() =>
        {
            Assert.That(grid.HorizontalOffset, Is.EqualTo(85));
            Assert.That(grid.TopRow, Is.Zero, "vertical position is untouched");
        });
    }

    [Test]
    public void Wheel_scrolling_moves_the_painted_thumb()
    {
        var grid = MakeGrid(10);
        var canvas = Realize(grid);

        canvas.RaiseMouseWheel(-120); // 3 rows down — value 3 of 6 puts the 22px thumb at y 38+17
        var g = canvas.RaisePaint();

        Assert.That(g.Operations, Does.Contain("fill #FFC8C8C8 184,55,16,22"), "the thumb reads TopRow directly");
    }

    [Test]
    public void TopRow_setter_scrolls_and_clamps()
    {
        var grid = MakeGrid(10);
        Realize(grid);

        grid.TopRow = 5;
        Assert.That(grid.TopRow, Is.EqualTo(5));

        grid.TopRow = 100;
        Assert.That(grid.TopRow, Is.EqualTo(6), "clamped one page short of the end");

        grid.TopRow = -3;
        Assert.That(grid.TopRow, Is.Zero);
    }

    [Test]
    public void Scrollbar_strips_narrow_the_scrollable_viewport()
    {
        var grid = MakeGrid(10); // vertical bar shown
        grid.Columns[0].Width = 300; // total 360; horizontal bar shown too
        var canvas = Realize(grid);

        grid.HorizontalOffset = 10_000;
        Assert.That(grid.HorizontalOffset, Is.EqualTo(176), "360 - (200 - 16px vertical strip)");

        canvas.RaiseMouseWheel(-120);
        canvas.RaiseMouseWheel(-120);
        canvas.RaiseMouseWheel(-120);
        Assert.That(grid.TopRow, Is.EqualTo(7), "10 rows minus the 3 that fit above the horizontal strip");
    }

    [Test]
    public void Virtualization_stays_bounded_with_scrollbars_fill_and_formatting()
    {
        var grid = MakeGrid(100_000);
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        grid.Columns[1].FormatSelector = static value => $"{value} y";
        grid.AlternatingRows = true;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        var textOps = g.Operations.FindAll(static o => o.StartsWith("text ")).Count;
        Assert.Multiple(() =>
        {
            Assert.That(grid.IsVerticalScrollBarVisible, Is.True);
            Assert.That(grid.Columns[0].Width, Is.EqualTo(124), "fill takes the viewport minus the 60px column and the strip");
            Assert.That(g.DrewText("0 y"), Is.True, "formatted through the FormatSelector");
            Assert.That(textOps, Is.LessThan(32), $"rendered {textOps} text ops for 100000 rows");
        });
    }
}
