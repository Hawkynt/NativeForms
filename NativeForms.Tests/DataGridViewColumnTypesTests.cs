using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The non-text <see cref="DataGridViewColumnKind"/>s: themed check glyphs with click toggling,
/// button faces with per-cell enabled state, accent links, icon-beside-text, multi-image cells with
/// per-icon hit-testing and progress fills — all painted through the recording graphics and driven
/// by simulated input.
/// </summary>
[TestFixture]
internal sealed class DataGridViewColumnTypesTests
{
    private const string _Accent = "#FF0078D4";
    private const string _DisabledText = "#FF9A9A9A";

    private sealed class Row
    {
        public string Name = string.Empty;
        public bool Done;
        public int Percent;
    }

    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static DataGridView MakeGrid(DataGridViewColumn column)
    {
        // 22px header + 4 rows at 22px; a single 100px column.
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(column);
        return grid;
    }

    [Test]
    public void Check_column_paints_accent_checkmark_when_checked()
    {
        var grid = MakeGrid(new("Done", static o => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
        });
        grid.Items.Add(new Row { Done = true });
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        // 14px box centered in the 100x22 cell at y=22: box at (43,26).
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 43,26,14,14"), "glyph box");
            Assert.That(g.Operations, Does.Contain($"rect {_Accent} 43,26,14,14"), "accent border");
            Assert.That(g.Operations, Does.Contain($"line {_Accent} 46,33-49,36"), "checkmark stroke");
            Assert.That(g.Operations, Does.Contain($"line {_Accent} 49,36-54,29"), "checkmark stroke");
        });
    }

    [Test]
    public void Check_column_paints_plain_border_when_unchecked()
    {
        var grid = MakeGrid(new("Done", static o => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
        });
        grid.Items.Add(new Row());
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 43,26,14,14"), "themed border");
            Assert.That(g.Operations.Exists(o => o.StartsWith($"line {_Accent}")), Is.False, "no checkmark");
        });
    }

    [Test]
    public void Check_click_toggles_through_setter_and_raises_content_click()
    {
        var grid = MakeGrid(new("Done", static o => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
            CheckedSetter = static (o, value) => ((Row)o!).Done = value,
        });
        var row = new Row();
        grid.Items.Add(row);
        DataGridViewCellEventArgs? content = null;
        grid.CellContentClick += (_, e) => content = e;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 30); // row 0 cell

        Assert.Multiple(() =>
        {
            Assert.That(row.Done, Is.True, "toggled on");
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.RowIndex, Is.EqualTo(0));
            Assert.That(content.ColumnIndex, Is.EqualTo(0));
        });

        canvas.RaiseMouseDown(50, 30);
        Assert.That(row.Done, Is.False, "toggled back off");
    }

    [Test]
    public void Check_click_without_setter_raises_content_click_but_changes_nothing()
    {
        var grid = MakeGrid(new("Done", static o => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
        });
        var row = new Row();
        grid.Items.Add(row);
        var contentClicks = 0;
        grid.CellContentClick += (_, _) => ++contentClicks;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 30);

        Assert.Multiple(() =>
        {
            Assert.That(contentClicks, Is.EqualTo(1));
            Assert.That(row.Done, Is.False);
        });
    }

    [Test]
    public void Check_toggle_is_blocked_by_read_only_but_content_click_still_fires()
    {
        var grid = MakeGrid(new("Done", static o => null)
        {
            Kind = DataGridViewColumnKind.Check,
            CheckedSelector = static o => ((Row)o!).Done,
            CheckedSetter = static (o, value) => ((Row)o!).Done = value,
        });
        grid.ReadOnly = true;
        var row = new Row();
        grid.Items.Add(row);
        var contentClicks = 0;
        grid.CellContentClick += (_, _) => ++contentClicks;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 30);

        Assert.Multiple(() =>
        {
            Assert.That(row.Done, Is.False, "read-only blocks the toggle");
            Assert.That(contentClicks, Is.EqualTo(1), "content click is raised regardless");
        });
    }

    [Test]
    public void Button_column_paints_face_border_and_centered_text()
    {
        var grid = MakeGrid(new("Action", static o => "Run")
        {
            Kind = DataGridViewColumnKind.Button,
        });
        grid.Items.Add(new Row());
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        // Face inset 2px into the 100x22 cell at y=22.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFDFDFD 2,24,96,18"), "button face");
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 2,24,96,18"), "button border");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Run\"") && o.Contains("MiddleCenter")), Is.True, "centered text");
        });
    }

    [Test]
    public void Button_click_raises_content_click_when_enabled()
    {
        var grid = MakeGrid(new("Action", static o => "Run")
        {
            Kind = DataGridViewColumnKind.Button,
        });
        grid.Items.Add(new Row());
        DataGridViewCellEventArgs? content = null;
        grid.CellContentClick += (_, e) => content = e;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(50, 30);

        Assert.Multiple(() =>
        {
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.RowIndex, Is.EqualTo(0));
            Assert.That(content.ColumnIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void Disabled_button_greys_text_and_raises_no_content_click()
    {
        var grid = MakeGrid(new("Action", static o => "Run")
        {
            Kind = DataGridViewColumnKind.Button,
            EnabledSelector = static _ => false,
        });
        grid.Items.Add(new Row());
        var contentClicks = 0;
        var cellClicks = 0;
        grid.CellContentClick += (_, _) => ++contentClicks;
        grid.CellClick += (_, _) => ++cellClicks;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();
        canvas.RaiseMouseDown(50, 30);

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Run\"") && o.Contains(_DisabledText)), Is.True, "greyed text");
            Assert.That(contentClicks, Is.Zero, "disabled button raises no content click");
            Assert.That(cellClicks, Is.EqualTo(1), "the generic cell click still fires");
        });
    }

    [Test]
    public void Link_column_paints_accent_underlined_text_and_raises_content_click()
    {
        var grid = MakeGrid(new("Site", static o => ((Row)o!).Name)
        {
            Kind = DataGridViewColumnKind.Link,
        });
        grid.Items.Add(new Row { Name = "Alice" });
        DataGridViewCellEventArgs? content = null;
        grid.CellContentClick += (_, e) => content = e;
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();
        canvas.RaiseMouseDown(50, 30);

        // "Alice" measures 5*7=35px; MiddleLeft in the 22px row at y=22 puts the underline at y=40.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Alice\"") && o.Contains(_Accent)), Is.True, "accent text");
            Assert.That(g.Operations, Does.Contain($"line {_Accent} 4,40-39,40"), "underline");
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.RowIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void Image_selector_paints_icon_before_the_cell_text()
    {
        var grid = MakeGrid(new("Name", static o => ((Row)o!).Name)
        {
            ImageSelector = static _ => new HeadlessImage(8, 8),
        });
        grid.Items.Add(new Row { Name = "Alice" });
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        // 18px icon at the cell padding, text shifted right past icon + gap: 4 + 18 + 4 = 26.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("image 8x8 @4,24"), "icon before text");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Alice\"") && o.Contains("@26,22")), Is.True, "text after icon");
        });
    }

    [Test]
    public void MultiImage_column_paints_icons_side_by_side()
    {
        var images = new IImage[] { new HeadlessImage(2, 2), new HeadlessImage(3, 3) };
        var grid = MakeGrid(new("Flags", static o => null)
        {
            Kind = DataGridViewColumnKind.MultiImage,
            ImagesSelector = _ => images,
        });
        grid.Items.Add(new Row());
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        // 18px icons in 22px slots: first at x=4, second at x=26.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("image 2x2 @4,24"));
            Assert.That(g.Operations, Does.Contain("image 3x3 @26,24"));
        });
    }

    [Test]
    public void MultiImage_click_reports_the_icon_index()
    {
        var images = new IImage[] { new HeadlessImage(2, 2), new HeadlessImage(3, 3) };
        var grid = MakeGrid(new("Flags", static o => null)
        {
            Kind = DataGridViewColumnKind.MultiImage,
            ImagesSelector = _ => images,
        });
        grid.Items.Add(new Row());
        DataGridViewCellEventArgs? content = null;
        grid.CellContentClick += (_, e) => content = e;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(30, 30); // inside the second 18px icon starting at x=26

        Assert.Multiple(() =>
        {
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.RowIndex, Is.EqualTo(0));
            Assert.That(content.ColumnIndex, Is.EqualTo(0));
            Assert.That(content.ContentIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void MultiImage_click_between_or_past_icons_raises_no_content_click()
    {
        var images = new IImage[] { new HeadlessImage(2, 2), new HeadlessImage(3, 3) };
        var grid = MakeGrid(new("Flags", static o => null)
        {
            Kind = DataGridViewColumnKind.MultiImage,
            ImagesSelector = _ => images,
        });
        grid.Items.Add(new Row());
        var contentClicks = 0;
        grid.CellContentClick += (_, _) => ++contentClicks;
        var canvas = Realize(grid);

        canvas.RaiseMouseDown(24, 30); // in the gap between icon 0 (ends at 22) and icon 1 (starts at 26)
        canvas.RaiseMouseDown(90, 30); // past the last icon

        Assert.That(contentClicks, Is.Zero);
    }

    [Test]
    public void Progress_column_paints_a_proportional_accent_fill()
    {
        var grid = MakeGrid(new("Load", static o => null)
        {
            Kind = DataGridViewColumnKind.Progress,
            ProgressSelector = static o => ((Row)o!).Percent,
        });
        grid.Items.Add(new Row { Percent = 50 });
        var canvas = Realize(grid);

        var g = canvas.RaisePaint();

        // Bar inset 2px into the 100x22 cell: 94px track, 50% => 47px accent fill.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 2,24,96,18"), "track");
            Assert.That(g.Operations, Does.Contain($"fill {_Accent} 3,25,47,16"), "proportional fill");
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 2,24,95,17"), "border");
        });
    }
}
