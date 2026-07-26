using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="PropertyGrid"/> lists delegate-backed name/value rows under collapsible category
/// headers, edits each value with its typed editor (text, check box, drop-down), and raises
/// <see cref="PropertyGrid.PropertyValueChanged"/> on a commit.
/// </summary>
[TestFixture]
internal sealed class PropertyGridTests
{
    private sealed class Model
    {
        public string Name = "Widget";
        public bool Enabled = true;
        public string Align = "Left";
    }

    private static PropertyGrid Create(Model model, out HeadlessBackend backend, out HeadlessCanvasPeer canvas)
    {
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        grid.AddRow(new PropertyGridRow("Name", () => model.Name, v => model.Name = v) { Category = "General", Description = "The display name." });
        grid.AddRow(new PropertyGridRow("Enabled", () => model.Enabled ? "True" : "False", v => model.Enabled = v == "True")
        {
            Category = "General",
            Editor = PropertyGridEditor.Boolean,
        });
        grid.AddRow(new PropertyGridRow("Align", () => model.Align, v => model.Align = v)
        {
            Category = "Layout",
            Editor = PropertyGridEditor.Choice,
            Choices = new[] { "Left", "Center", "Right" },
        });

        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return grid;
    }

    [Test]
    public void Rows_render_under_their_category_headers()
    {
        var grid = Create(new Model(), out _, out var canvas);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.Contains("\"General\"")), Is.True);
            Assert.That(g.Operations.Exists(o => o.Contains("\"Layout\"")), Is.True);
            Assert.That(g.Operations.Exists(o => o.Contains("\"Name\"")), Is.True);
            Assert.That(g.Operations.Exists(o => o.Contains("\"Widget\"")), Is.True, "the value cell shows the current value");
        });
    }

    [Test]
    public void Clicking_a_category_header_collapses_its_rows()
    {
        var grid = Create(new Model(), out _, out var canvas);

        canvas.RaiseMouseDown(8, 11); // the "General" header at row 0

        var g = canvas.RaisePaint();
        Assert.That(g.Operations.Exists(o => o.Contains("\"Widget\"")), Is.False, "the Name row is hidden while General is collapsed");
    }

    [Test]
    public void Clicking_a_boolean_value_toggles_it_and_raises_the_change()
    {
        var model = new Model();
        var grid = Create(model, out _, out var canvas);
        PropertyValueChangedEventArgs? change = null;
        grid.PropertyValueChanged += (_, e) => change = e;

        // header row 0, Name row 1, Enabled row 2 → y in [44, 66); click the value column.
        canvas.RaiseMouseDown(200, 55);

        Assert.Multiple(() =>
        {
            Assert.That(model.Enabled, Is.False, "the bool flipped");
            Assert.That(change, Is.Not.Null);
            Assert.That(change!.NewValue, Is.EqualTo("False"));
        });
    }

    [Test]
    public void A_text_row_edits_through_the_hosted_editor()
    {
        var model = new Model();
        var grid = Create(model, out var backend, out var canvas);

        canvas.RaiseMouseDown(200, 33); // Name row (row 1) value cell
        var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
        editor.SimulateUserInput("Renamed");
        editor.SimulateKeyDown(Keys.Enter);

        Assert.That(model.Name, Is.EqualTo("Renamed"));
    }

    [Test]
    public void A_choice_row_opens_a_dropdown_and_picks_a_value()
    {
        var model = new Model();
        var grid = Create(model, out var backend, out var canvas);

        // header General(0) Name(1) Enabled(2) header Layout(3) Align(4) → y in [88,110).
        canvas.RaiseMouseDown(200, 99);
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(10, 33); // the popup lists Left(0)/Center(1)/Right(2) at 22-px rows; y=33 → "Center"

        Assert.That(model.Align, Is.EqualTo("Center"));
    }

    [Test]
    public void A_read_only_row_does_not_open_an_editor()
    {
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        grid.AddRow(new PropertyGridRow("Id", () => "42") { Category = "General" }); // no setter → read-only
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(200, 33);

        Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Any(p => p.Visible), Is.False, "no editor appears for a read-only row");
    }

    [Test]
    public void A_tristate_row_cycles_true_false_null()
    {
        var value = "True";
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        grid.AddRow(new PropertyGridRow("Flag", () => value, v => value = v) { Editor = PropertyGridEditor.TriState, AllowNull = true });
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(200, 33); // Flag row (row 1) value cell → True→False
        Assert.That(value, Is.EqualTo("False"));
        canvas.RaiseMouseDown(200, 33); // False→null
        Assert.That(value, Is.EqualTo(string.Empty));
        canvas.RaiseMouseDown(200, 33); // null→True
        Assert.That(value, Is.EqualTo("True"));
    }

    [Test]
    public void A_number_row_clamps_to_its_min_and_max()
    {
        var value = "5";
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        grid.AddRow(new PropertyGridRow("Size", () => value, v => value = v) { Editor = PropertyGridEditor.Number, Minimum = 0, Maximum = 10 });
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(200, 33); // open the hosted editor over "Size"
        var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
        editor.SimulateUserInput("99");
        editor.SimulateKeyDown(Keys.Enter);

        Assert.That(value, Is.EqualTo("10"), "the commit is clamped to the maximum");
    }

    [Test]
    public void An_align_row_opens_a_3x3_picker_and_commits_the_chosen_cell()
    {
        var value = "TopLeft";
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        grid.AddRow(new PropertyGridRow("Align", () => value, v => value = v) { Editor = PropertyGridEditor.Align });
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(200, 33); // open the align picker
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(1 + 22 + 11, 1 + 22 + 11); // centre cell (col 1, row 1) → MiddleCenter

        Assert.That(value, Is.EqualTo("MiddleCenter"));
    }

    [Test]
    public void A_color_row_opens_a_palette_and_commits_the_picked_swatch()
    {
        var value = "#FFFFFFFF";
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        grid.AddRow(new PropertyGridRow("Fill", () => value, v => value = v) { Editor = PropertyGridEditor.Color });
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseMouseDown(200, 33); // open the palette
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(10, 10); // the first swatch (Black)

        Assert.That(value, Is.EqualTo("#000000FF"));
    }

    [Test]
    public void Selecting_a_row_shows_its_description()
    {
        var grid = Create(new Model(), out _, out var canvas);

        canvas.RaiseMouseDown(60, 33); // select the Name row (name column)
        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.Contains("\"The display name.\"")), Is.True);
    }
}
