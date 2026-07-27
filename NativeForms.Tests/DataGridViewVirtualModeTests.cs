using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="DataGridView"/> in <see cref="DataGridView.VirtualMode"/> serves its rows from
/// <see cref="DataGridView.RetrieveVirtualRow"/> over a <see cref="DataGridView.VirtualRowCount"/>:
/// only the visible rows are fetched, <see cref="DataGridView.Items"/> stays empty, and the
/// unknown-size mode probes until the source reports the end.
/// </summary>
[TestFixture]
internal sealed class DataGridViewVirtualModeTests
{
    private sealed record Row(string Name);

    private static DataGridView MakeGrid(out HeadlessBackend backend, out HeadlessCanvasPeer canvas)
    {
        var grid = new DataGridView { Bounds = new(0, 0, 300, 200) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name) { Width = 200 });
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return grid;
    }

    [Test]
    public void Only_the_visible_rows_are_fetched_never_the_whole_source()
    {
        var grid = MakeGrid(out _, out var canvas);
        var fetched = new List<int>();
        grid.VirtualMode = true;
        grid.VirtualRowCount = 1_000_000;
        grid.RetrieveVirtualRow += (_, e) => { fetched.Add(e.RowIndex); e.Item = new Row("R" + e.RowIndex); };

        fetched.Clear();
        canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(fetched, Is.Not.Empty, "the visible rows are fetched");
            Assert.That(fetched.Count, Is.LessThan(100), "only a screenful, not a million");
            Assert.That(grid.Items, Is.Empty, "no model row is materialised");
        });
    }

    [Test]
    public void A_virtual_row_is_painted_from_the_retrieved_item()
    {
        var grid = MakeGrid(out _, out var canvas);
        grid.VirtualMode = true;
        grid.VirtualRowCount = 50;
        grid.RetrieveVirtualRow += (_, e) => e.Item = new Row("R" + e.RowIndex);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.Contains("\"R0\"")), Is.True, "the first virtual row is drawn");
    }

    [Test]
    public void SelectedRowIndex_is_clamped_to_the_virtual_count()
    {
        var grid = MakeGrid(out _, out _);
        grid.VirtualMode = true;
        grid.VirtualRowCount = 10;
        grid.RetrieveVirtualRow += (_, e) => e.Item = new Row("R" + e.RowIndex);

        grid.SelectedRowIndex = 4;
        Assert.That(grid.SelectedRowIndex, Is.EqualTo(4));

        grid.SelectedRowIndex = 999;
        Assert.That(grid.SelectedRowIndex, Is.EqualTo(-1), "an out-of-range index clears the selection");
    }

    [Test]
    public void The_unknown_size_mode_probes_until_the_source_reports_the_end()
    {
        var grid = MakeGrid(out _, out var canvas);
        grid.VirtualMode = true;
        grid.VirtualRowCount = -1;
        grid.RetrieveVirtualRow += (_, e) =>
        {
            if (e.RowIndex >= 7)
                e.EndOfRows = true;
            else
                e.Item = new Row("R" + e.RowIndex);
        };

        canvas.RaisePaint();
        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.Contains("\"R6\"")), Is.True, "the last real row is drawn");
            Assert.That(g.Operations.Exists(o => o.Contains("\"R7\"")), Is.False, "nothing past the end is drawn");
        });

        grid.SelectedRowIndex = 6;
        Assert.That(grid.SelectedRowIndex, Is.EqualTo(6), "the discovered extent is selectable");
    }

    [Test]
    public void Leaving_virtual_mode_clears_the_selection_and_the_sort()
    {
        var grid = MakeGrid(out _, out _);
        grid.VirtualMode = true;
        grid.VirtualRowCount = 10;
        grid.RetrieveVirtualRow += (_, e) => e.Item = new Row("R" + e.RowIndex);
        grid.SelectedRowIndex = 3;

        grid.VirtualMode = false;

        Assert.That(grid.SelectedRowIndex, Is.EqualTo(-1));
    }

    [Test]
    public void Sorting_is_left_to_the_source_while_virtual()
    {
        var grid = MakeGrid(out _, out var canvas);
        grid.VirtualMode = true;
        grid.VirtualRowCount = 3;
        // Deliberately descending: a grid-side sort would reorder these, a source-side one would not.
        grid.RetrieveVirtualRow += (_, e) => e.Item = new Row("R" + (2 - e.RowIndex));
        grid.Sort(grid.Columns[0], SortOrder.Ascending);

        var g = canvas.RaisePaint();
        var texts = g.Operations.FindAll(o => o.StartsWith("text \"R"));

        Assert.That(texts[0], Does.Contain("\"R2\""), "the grid did not reorder rows it never fetched");
    }
}
