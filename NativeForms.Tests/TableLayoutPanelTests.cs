using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="TableLayoutPanel"/> must slice its client area into a
/// <see cref="TableLayoutPanel.ColumnCount"/> × <see cref="TableLayoutPanel.RowCount"/> grid from
/// the <see cref="SizeType.Absolute"/>/<see cref="SizeType.Percent"/>/<see cref="SizeType.AutoSize"/>
/// styles, auto-place unassigned children row-major into free cells, honor explicit cell positions
/// and spans, fill each child into its cell minus <see cref="Control.Margin"/>, paint the optional
/// cell grid, and re-layout on every structural change.
/// </summary>
[TestFixture]
internal sealed class TableLayoutPanelTests
{
    private static readonly string _TrackFill = "fill #FFECECEC"; // HeaderBackground = scrollbar track
    private static readonly string _BorderLine = "line #FFC8C8C8"; // Border = cell grid lines

    private static HeadlessCanvasPeer Realize(TableLayoutPanel panel, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(panel);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    private static Button MakeChild(int width = 20, int height = 10, int margin = 0)
        => new() { Size = new(width, height), Margin = new(margin) };

    private static TableLayoutPanel MakeGrid(int columns, int rows, int width = 200, int height = 100)
        => new() { Bounds = new(0, 0, width, height), ColumnCount = columns, RowCount = rows };

    [Test]
    public void Absolute_styles_size_cells_exactly()
    {
        var table = MakeGrid(2, 1, 300);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.ColumnStyles.Add(new(SizeType.Absolute, 80));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.Controls.AddRange(MakeChild(), MakeChild());

        Assert.Multiple(() =>
        {
            Assert.That(table.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 50, 40)));
            Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(50, 0, 80, 40)));
        });
    }

    [Test]
    public void Percent_styles_share_the_client_area()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.Percent, 25));
        table.ColumnStyles.Add(new(SizeType.Percent, 75));
        table.RowStyles.Add(new(SizeType.Percent, 100));
        table.Controls.AddRange(MakeChild(), MakeChild());

        Assert.Multiple(() =>
        {
            Assert.That(table.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 50, 100)));
            Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(50, 0, 150, 100)));
        });
    }

    [Test]
    public void Percent_gets_the_space_left_after_absolute()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.Absolute, 40));
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        table.Controls.AddRange(MakeChild(), MakeChild());

        Assert.Multiple(() =>
        {
            Assert.That(table.Controls[0].Bounds.Width, Is.EqualTo(40));
            Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(40, 0, 160, 100)));
        });
    }

    [Test]
    public void AutoSize_column_measures_the_widest_child()
    {
        var table = MakeGrid(2, 2);
        table.ColumnStyles.Add(new(SizeType.AutoSize));
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        table.Controls.AddRange(MakeChild(30), MakeChild(20), MakeChild(55), MakeChild(20));

        Assert.Multiple(() =>
        {
            Assert.That(table.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 55, 50)), "narrow child fills the measured column");
            Assert.That(table.Controls[2].Bounds, Is.EqualTo(new Rectangle(0, 50, 55, 50)), "widest child sets the column");
            Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(55, 0, 145, 50)), "percent column takes the rest");
        });
    }

    [Test]
    public void Unstyled_columns_and_rows_share_the_space_equally()
    {
        var table = MakeGrid(2, 1, 200, 60);
        table.Controls.AddRange(MakeChild(), MakeChild());

        Assert.Multiple(() =>
        {
            Assert.That(table.Controls[0].Bounds, Is.EqualTo(new Rectangle(0, 0, 100, 60)));
            Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(100, 0, 100, 60)));
        });
    }

    [Test]
    public void Margins_inset_children_within_their_cells()
    {
        var table = MakeGrid(1, 1);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.Controls.Add(MakeChild(margin: 3));

        Assert.That(table.Controls[0].Bounds, Is.EqualTo(new Rectangle(3, 3, 44, 34)));
    }

    [Test]
    public void SetCellPosition_places_a_child_explicitly()
    {
        var table = MakeGrid(2, 2);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        var child = MakeChild();
        table.Controls.Add(child);

        table.SetCellPosition(child, 1, 1);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(50, 40, 50, 40)));
    }

    [Test]
    public void GetCellPosition_returns_the_assignment_or_minus_one()
    {
        var table = MakeGrid(2, 2);
        var assigned = MakeChild();
        var floating = MakeChild();
        table.Controls.AddRange(assigned, floating);

        table.SetCellPosition(assigned, 1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(table.GetCellPosition(assigned), Is.EqualTo(new TableLayoutPanelCellPosition(1, 0)));
            Assert.That(table.GetCellPosition(floating), Is.EqualTo(new TableLayoutPanelCellPosition(-1, -1)));
        });
    }

    [Test]
    public void ColumnSpan_stretches_across_cells()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.ColumnStyles.Add(new(SizeType.Absolute, 80));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        var child = MakeChild();
        table.Controls.Add(child);

        table.SetColumnSpan(child, 2);

        Assert.Multiple(() =>
        {
            Assert.That(table.GetColumnSpan(child), Is.EqualTo(2));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 130, 40)));
        });
    }

    [Test]
    public void RowSpan_stretches_down()
    {
        var table = MakeGrid(1, 2);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.RowStyles.Add(new(SizeType.Absolute, 30));
        var child = MakeChild();
        table.Controls.Add(child);

        table.SetRowSpan(child, 2);

        Assert.Multiple(() =>
        {
            Assert.That(table.GetRowSpan(child), Is.EqualTo(2));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 50, 70)));
        });
    }

    [Test]
    public void Auto_placement_fills_free_cells_row_major_around_explicit_ones()
    {
        var table = MakeGrid(2, 2);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        var pinned = MakeChild();
        var first = MakeChild();
        var second = MakeChild();
        table.Controls.AddRange(pinned, first, second);

        table.SetCellPosition(pinned, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(50, 0, 50, 40)), "skips the occupied cell");
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(0, 40, 50, 40)), "continues on the next row");
        });
    }

    [Test]
    public void Single_cell_borders_offset_cells_and_paint_grid_lines()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
        table.Controls.AddRange(MakeChild(), MakeChild());
        var canvas = Realize(table, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(table.Controls[0].Bounds, Is.EqualTo(new Rectangle(1, 1, 50, 40)));
            Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(52, 1, 50, 40)));
            Assert.That(g.Operations.Exists(o => o.StartsWith(_BorderLine)), Is.True, "grid lines painted");
        });
    }

    [Test]
    public void Changing_a_style_relayouts()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.Controls.AddRange(MakeChild(), MakeChild());

        table.ColumnStyles[0].Width = 70;

        Assert.That(table.Controls[1].Bounds.X, Is.EqualTo(70));
    }

    [Test]
    public void Changing_ColumnCount_relayouts()
    {
        var table = MakeGrid(2, 2);
        table.ColumnStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.Controls.AddRange(MakeChild(), MakeChild());

        table.ColumnCount = 1;

        Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(0, 40, 50, 40)));
    }

    [Test]
    public void Resizing_the_panel_recomputes_percent_columns()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.Percent, 50));
        table.ColumnStyles.Add(new(SizeType.Percent, 50));
        table.Controls.AddRange(MakeChild(), MakeChild());

        table.Width = 300;

        Assert.That(table.Controls[1].Bounds.X, Is.EqualTo(150));
    }

    [Test]
    public void Resizing_a_child_updates_its_AutoSize_column()
    {
        var table = MakeGrid(2, 1);
        table.ColumnStyles.Add(new(SizeType.AutoSize));
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        var measured = MakeChild(30);
        table.Controls.AddRange(measured, MakeChild());

        measured.Width = 80;

        Assert.That(table.Controls[1].Bounds, Is.EqualTo(new Rectangle(80, 0, 120, 100)));
    }

    [Test]
    public void Overflowing_rows_paint_a_scrollbar_with_AutoScroll()
    {
        var table = MakeGrid(1, 3, 100, 80);
        table.AutoScroll = true;
        table.RowStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 50));
        table.RowStyles.Add(new(SizeType.Absolute, 50));
        table.Controls.AddRange(MakeChild(), MakeChild(), MakeChild());
        var canvas = Realize(table, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith(_TrackFill)), Is.True);
    }
}
