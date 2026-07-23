using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Hiding the column-header band and the row-header strip: both must collapse to nothing, moving the
/// cell content up (no column headers) and left (no row headers), and painting neither the captions
/// nor the strip.
/// </summary>
[TestFixture]
internal sealed class DataGridViewHeaderVisibilityTests
{
    private sealed record Row(string Name, int Age);

    private static RecordingGraphics Paint(DataGridView grid)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single().RaisePaint();
    }

    private static DataGridView MakeGrid()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 220, 140) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Row)o!).Age) { Width = 60 });
        grid.Items.AddRange([new Row("Alice", 30), new Row("Bob", 25)]);
        return grid;
    }

    /// <summary>The @x,y a text operation was drawn at, or (-1,-1) when it was not drawn.</summary>
    private static Point TextAt(RecordingGraphics g, string text)
    {
        var op = g.Operations.FirstOrDefault(o => o.StartsWith("text") && o.Contains($"\"{text}\""));
        if (op is null)
            return new(-1, -1);

        var at = op[(op.LastIndexOf('@') + 1)..].Split(',');
        return new(int.Parse(at[0]), int.Parse(at[1]));
    }

    [Test]
    public void Column_headers_paint_by_default()
    {
        var g = Paint(MakeGrid());
        Assert.That(g.DrewText("Name"), Is.True, "the column caption paints when headers are shown");
    }

    [Test]
    public void Hiding_the_column_headers_drops_the_captions_and_lifts_the_first_row()
    {
        var shownY = TextAt(Paint(MakeGrid()), "Alice").Y;

        var grid = MakeGrid();
        grid.ShowColumnHeaders = false;
        var g = Paint(grid);

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewText("Name"), Is.False, "no column caption is painted when hidden");
            Assert.That(TextAt(g, "Alice").Y, Is.LessThan(shownY), "the first row moves up into the freed header band");
            Assert.That(TextAt(g, "Alice").Y, Is.LessThan(4), "and starts at the very top");
        });
    }

    [Test]
    public void Showing_the_row_headers_shifts_the_cells_right()
    {
        var withoutX = TextAt(Paint(MakeGrid()), "Alice").X; // row headers off by default

        var grid = MakeGrid();
        grid.ShowRowHeaders = true;
        var withX = TextAt(Paint(grid), "Alice").X;

        Assert.That(withX, Is.GreaterThan(withoutX), "the row-header strip pushes the cells to the right");
    }
}
