using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Clipboard paste: tab-separated text lands on display rows/columns from the current cell, converts
/// per column kind and writes through the kind's setter — read-only cells, display-only columns,
/// unparseable text and <see cref="DataGridView.CellValidating"/> vetoes skip the cell while its
/// position is still consumed. Ctrl+V reads the system clipboard through the
/// <c>IPlatformBackend.GetClipboardText</c> seam and <see cref="DataGridView.PasteCompleted"/>
/// closes every paste.
/// </summary>
[TestFixture]
internal sealed class DataGridViewPasteTests
{
    private sealed class Row
    {
        public string Name = string.Empty;
        public string City = string.Empty;
        public bool Done;
        public int Age;
        public DateTime When;
        public string Status = string.Empty;
    }

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

    /// <summary>Two writable text columns (Name, City) over three rows, current cell at (0, 0).</summary>
    private static DataGridView MakeTextGrid(out List<Row> rows)
    {
        rows = [new() { Name = "Alice" }, new() { Name = "Bob" }, new() { Name = "Carol" }];
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name)
        {
            TextSetter = static (o, value) => ((Row)o!).Name = value,
        });
        grid.Columns.Add(new DataGridViewColumn("City", static o => ((Row)o!).City)
        {
            Width = 60,
            TextSetter = static (o, value) => ((Row)o!).City = value,
        });
        grid.DataSource = rows;
        grid.SelectedRowIndex = 0;
        grid.CurrentColumnIndex = 0;
        return grid;
    }

    [Test]
    public void Paste_writes_tsv_through_the_setters_starting_at_the_current_cell()
    {
        var grid = MakeTextGrid(out var rows);
        RealizeBackend(grid);

        grid.Paste("N1\tC1\r\nN2\tC2\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("N1"));
            Assert.That(rows[0].City, Is.EqualTo("C1"));
            Assert.That(rows[1].Name, Is.EqualTo("N2"));
            Assert.That(rows[1].City, Is.EqualTo("C2"));
            Assert.That(rows[2].Name, Is.EqualTo("Carol"), "the trailing newline carries no row");
        });
    }

    [Test]
    public void Paste_starts_at_the_current_column()
    {
        var grid = MakeTextGrid(out var rows);
        grid.CurrentColumnIndex = 1;
        RealizeBackend(grid);

        grid.Paste("C1");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Alice"));
            Assert.That(rows[0].City, Is.EqualTo("C1"));
        });
    }

    [Test]
    public void Paste_skips_read_only_cells_but_consumes_their_position()
    {
        var grid = MakeTextGrid(out var rows);
        grid.Columns[0].ReadOnly = true;
        RealizeBackend(grid);

        grid.Paste("N1\tC1");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Alice"), "read-only cell untouched");
            Assert.That(rows[0].City, Is.EqualTo("C1"), "the skipped cell still consumed its column");
        });
    }

    [Test]
    public void Paste_skips_merged_rows()
    {
        var grid = MakeTextGrid(out var rows);
        grid.FullRowTextSelector = static o => ((Row)o!).Name == "Bob" ? "— section —" : null;
        RealizeBackend(grid);

        grid.Paste("N1\nN3");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("N1"));
            Assert.That(rows[1].Name, Is.EqualTo("Bob"), "the merged row takes no paste");
            Assert.That(rows[2].Name, Is.EqualTo("N3"), "the second line landed past it");
        });
    }

    [Test]
    public void Paste_ignores_content_past_the_last_row_and_column()
    {
        var grid = MakeTextGrid(out var rows);
        grid.SelectedRowIndex = 2;
        RealizeBackend(grid);

        grid.Paste("N3\tC3\tExtra\nBeyond");

        Assert.Multiple(() =>
        {
            Assert.That(rows[2].Name, Is.EqualTo("N3"));
            Assert.That(rows[2].City, Is.EqualTo("C3"));
            Assert.That(rows[0].Name, Is.EqualTo("Alice"), "nothing wraps around");
        });
    }

    [Test]
    public void Paste_without_a_current_cell_is_a_no_op()
    {
        var grid = MakeTextGrid(out var rows);
        grid.SelectedRowIndex = -1;
        var completed = 0;
        grid.PasteCompleted += (_, _) => ++completed;
        RealizeBackend(grid);

        grid.Paste("N1");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Alice"));
            Assert.That(completed, Is.Zero);
        });
    }

    [Test]
    public void Paste_converts_check_numeric_date_and_combo_cells()
    {
        var choices = new object?[] { "New", "Active", "Done" };
        var rows = new List<Row> { new() { Status = "New" } };
        var grid = new DataGridView { Bounds = new(0, 0, 400, 110) };
        grid.Columns.Add(new DataGridViewColumn("Done", static _ => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
            CheckedSetter = static (o, value) => ((Row)o!).Done = value,
        });
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Row)o!).Age)
        {
            Kind = DataGridViewColumnKind.NumericUpDown,
            NumberSelector = static o => ((Row)o!).Age,
            NumberSetter = static (o, value) => ((Row)o!).Age = (int)value,
            Maximum = 100m,
        });
        grid.Columns.Add(new DataGridViewColumn("When", static o => ((Row)o!).When)
        {
            Kind = DataGridViewColumnKind.DateTime,
            DateSelector = static o => ((Row)o!).When,
            DateSetter = static (o, value) => ((Row)o!).When = value,
        });
        grid.Columns.Add(new DataGridViewColumn("Status", static o => ((Row)o!).Status)
        {
            Kind = DataGridViewColumnKind.ComboBox,
            ItemsSelector = _ => choices,
            ValueSetter = static (o, value) => ((Row)o!).Status = (string)value!,
        });
        grid.DataSource = rows;
        grid.SelectedRowIndex = 0;
        RealizeBackend(grid);

        grid.Paste("true\t420\t2026-07-14\tActive");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Done, Is.True);
            Assert.That(rows[0].Age, Is.EqualTo(100), "clamped into the column range");
            Assert.That(rows[0].When, Is.EqualTo(new DateTime(2026, 7, 14)));
            Assert.That(rows[0].Status, Is.EqualTo("Active"), "matched by display text");
        });
    }

    [Test]
    public void Unparseable_text_skips_the_cell_without_writing()
    {
        var rows = new List<Row> { new() { Done = true, Age = 7 } };
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Done", static _ => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
            CheckedSetter = static (o, value) => ((Row)o!).Done = value,
        });
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Row)o!).Age)
        {
            Kind = DataGridViewColumnKind.NumericUpDown,
            NumberSelector = static o => ((Row)o!).Age,
            NumberSetter = static (o, value) => ((Row)o!).Age = (int)value,
        });
        grid.DataSource = rows;
        grid.SelectedRowIndex = 0;
        RealizeBackend(grid);

        grid.Paste("maybe\tsoon");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Done, Is.True);
            Assert.That(rows[0].Age, Is.EqualTo(7));
        });
    }

    [Test]
    public void CellValidating_veto_skips_only_that_cell()
    {
        var grid = MakeTextGrid(out var rows);
        grid.CellValidating += static (_, e) => e.Cancel = e.ColumnIndex == 0;
        RealizeBackend(grid);

        grid.Paste("N1\tC1");

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Alice"), "the vetoed cell writes nothing");
            Assert.That(rows[0].City, Is.EqualTo("C1"), "its neighbor still pastes");
        });
    }

    [Test]
    public void Paste_lands_on_display_rows_while_sorted()
    {
        var grid = MakeTextGrid(out var rows);
        grid.Columns[0].SortMode = DataGridViewColumnSortMode.Automatic;
        grid.Sort(grid.Columns[0], SortOrder.Descending); // display: Carol, Bob, Alice
        grid.SelectedRowIndex = 2; // Carol — display row 0
        RealizeBackend(grid);

        grid.Paste("X1\nX2");

        Assert.Multiple(() =>
        {
            Assert.That(rows[2].Name, Is.EqualTo("X1"), "the current row in display order");
            Assert.That(rows[1].Name, Is.EqualTo("X2"), "the next display row (Bob)");
            Assert.That(rows[0].Name, Is.EqualTo("Alice"));
        });
    }

    [Test]
    public void Ctrl_V_pastes_through_the_backend_seam_and_raises_PasteCompleted()
    {
        var grid = MakeTextGrid(out var rows);
        var completed = 0;
        grid.PasteCompleted += (_, _) => ++completed;
        var backend = RealizeBackend(grid);
        backend.ClipboardText = "Zoe\tRome";

        CanvasOf(backend).RaiseKeyDown(Keys.V, KeyModifiers.Control);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Zoe"));
            Assert.That(rows[0].City, Is.EqualTo("Rome"));
            Assert.That(completed, Is.EqualTo(1));
        });
    }

    [Test]
    public void Ctrl_V_with_an_empty_clipboard_does_nothing()
    {
        var grid = MakeTextGrid(out var rows);
        var completed = 0;
        grid.PasteCompleted += (_, _) => ++completed;
        var backend = RealizeBackend(grid);

        CanvasOf(backend).RaiseKeyDown(Keys.V, KeyModifiers.Control);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Alice"));
            Assert.That(completed, Is.Zero);
        });
    }
}
