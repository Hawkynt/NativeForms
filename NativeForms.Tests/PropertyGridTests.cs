using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="PropertyGrid"/> lists delegate-backed name/value rows under collapsible category
/// headers, edits each value with its typed editor (text, check box, drop-down), and raises
/// <see cref="PropertyGrid.PropertyValueChanged"/> on a commit.
/// </summary>
[TestFixture]
internal sealed class PropertyGridTests {
  private sealed class Model {
    public string Name = "Widget";
    public bool Enabled = true;
    public string Align = "Left";
  }

  private static PropertyGrid Create(Model model, out HeadlessBackend backend, out HeadlessCanvasPeer canvas) {
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    grid.AddRow(new PropertyGridRow("Name", () => model.Name, v => model.Name = v) { Category = "General", Description = "The display name." });
    grid.AddRow(new PropertyGridRow("Enabled", () => model.Enabled ? "True" : "False", v => model.Enabled = v == "True") {
      Category = "General",
      Editor = PropertyGridEditor.Boolean,
    });
    grid.AddRow(new PropertyGridRow("Align", () => model.Align, v => model.Align = v) {
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
  public void Rows_render_under_their_category_headers() {
    var grid = Create(new Model(), out _, out var canvas);

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.Operations.Exists(o => o.Contains("\"General\"")), Is.True);
      Assert.That(g.Operations.Exists(o => o.Contains("\"Layout\"")), Is.True);
      Assert.That(g.Operations.Exists(o => o.Contains("\"Name\"")), Is.True);
      Assert.That(g.Operations.Exists(o => o.Contains("\"Widget\"")), Is.True, "the value cell shows the current value");
    });
  }

  [Test]
  public void Clicking_a_category_header_collapses_its_rows() {
    var grid = Create(new Model(), out _, out var canvas);

    canvas.RaiseMouseDown(8, 11); // the "General" header at row 0

    var g = canvas.RaisePaint();
    Assert.That(g.Operations.Exists(o => o.Contains("\"Widget\"")), Is.False, "the Name row is hidden while General is collapsed");
  }

  [Test]
  public void Clicking_a_boolean_value_toggles_it_and_raises_the_change() {
    var model = new Model();
    var grid = Create(model, out _, out var canvas);
    PropertyValueChangedEventArgs? change = null;
    grid.PropertyValueChanged += (_, e) => change = e;

    // header row 0, Name row 1, Enabled row 2 → y in [44, 66); click the value column.
    canvas.RaiseMouseDown(200, 55);

    Assert.Multiple(() => {
      Assert.That(model.Enabled, Is.False, "the bool flipped");
      Assert.That(change, Is.Not.Null);
      Assert.That(change!.NewValue, Is.EqualTo("False"));
    });
  }

  [Test]
  public void A_text_row_edits_through_the_hosted_editor() {
    var model = new Model();
    var grid = Create(model, out var backend, out var canvas);

    canvas.RaiseMouseDown(200, 33); // Name row (row 1) value cell
    var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
    editor.SimulateUserInput("Renamed");
    editor.SimulateKeyDown(Keys.Enter);

    Assert.That(model.Name, Is.EqualTo("Renamed"));
  }

  [Test]
  public void A_choice_row_opens_a_dropdown_and_picks_a_value() {
    var model = new Model();
    var grid = Create(model, out var backend, out var canvas);

    // header General(0) Name(1) Enabled(2) header Layout(3) Align(4) → y in [88,110).
    canvas.RaiseMouseDown(200, 99);
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    popup.RaiseMouseDown(10, 33); // the popup lists Left(0)/Center(1)/Right(2) at 22-px rows; y=33 → "Center"

    Assert.That(model.Align, Is.EqualTo("Center"));
  }

  [Test]
  public void A_read_only_row_does_not_open_an_editor() {
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
  public void A_tristate_row_cycles_true_false_null() {
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
  public void A_number_row_clamps_to_its_min_and_max() {
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
  public void An_align_row_opens_a_3x3_picker_and_commits_the_chosen_cell() {
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
  public void A_color_row_hosts_the_real_ColorPicker_and_commits_its_colour() {
    var value = "#FFFFFFFF";
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    grid.AddRow(new PropertyGridRow("Fill", () => value, v => value = v) { Editor = PropertyGridEditor.Color });
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    canvas.RaiseMouseDown(200, 33); // open the hosted ColorPicker over the "Fill" cell
    var picker = grid.Controls.OfType<ColorPicker>().Single();
    picker.SelectedColor = Color.FromArgb(0xFF, 0x11, 0x22, 0x33);

    Assert.That(value, Is.EqualTo("#112233FF"), "the row commits the picker's colour as hex");
  }

  private enum Fruit { Apple, Banana, Cherry }

  [Flags]
  private enum Sides { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

  [Test]
  public void A_date_row_hosts_a_DateTimePicker_and_commits_its_date() {
    var value = new DateOnly(2020, 5, 1);
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    grid.AddRow("When", () => value, v => value = v);
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    canvas.RaiseMouseDown(200, 33); // open the hosted DateTimePicker
    var picker = grid.Controls.OfType<DateTimePicker>().Single();
    picker.Value = new DateTime(2030, 1, 15, 9, 0, 0);

    Assert.That(value, Is.EqualTo(new DateOnly(2030, 1, 15)));
  }

  [Test]
  public void A_time_row_hosts_a_TimePicker_and_commits_its_time() {
    var value = new TimeOnly(8, 0);
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    grid.AddRow("At", () => value, v => value = v);
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    canvas.RaiseMouseDown(200, 33);
    var picker = grid.Controls.OfType<TimePicker>().Single();
    picker.Value = new TimeSpan(14, 30, 0);

    Assert.That(value, Is.EqualTo(new TimeOnly(14, 30)));
  }

  [Test]
  public void A_flags_row_opens_a_checkbox_flyout_and_commits_the_selected_set() {
    var value = Sides.Left;
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    grid.AddFlagsEnumRow("Edges", () => value, v => value = v);
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    canvas.RaiseMouseDown(200, 33); // open the flags flyout
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    // Members are Left(0), Top(1), Right(2), Bottom(3) at 22-px rows; toggle Top on.
    popup.RaiseMouseDown(10, 33);

    Assert.That(value, Is.EqualTo(Sides.Left | Sides.Top));
  }

  [Test]
  public void A_grid_enum_row_maps_the_3x3_cells_to_enum_values() {
    var value = Fruit.Apple;
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    grid.AddGridEnumRow("Slot", () => value, v => value = v,
        new[] { "", "Banana", "", "", "Cherry", "", "", "Apple", "" });
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    canvas.RaiseMouseDown(200, 33); // open the 3×3 flyout
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    popup.RaiseMouseDown(1 + 22 + 11, 1 + 11); // top-centre cell → "Banana"

    Assert.That(value, Is.EqualTo(Fruit.Banana));
  }

  [Test]
  public void Typed_AddRow_infers_the_editor_from_the_value_type() {
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    var flag = true;
    var maybe = (bool?)null;
    var count = 3;
    var tint = Color.Red;
    var r1 = grid.AddRow("Flag", () => flag, v => flag = v);
    var r2 = grid.AddRow("Maybe", () => maybe, v => maybe = v);
    var r3 = grid.AddRow("Count", () => count, v => count = v, minimum: 0, maximum: 10);
    var r4 = grid.AddRow("Tint", () => tint, v => tint = v);

    Assert.Multiple(() => {
      Assert.That(r1.Editor, Is.EqualTo(PropertyGridEditor.Boolean));
      Assert.That(r2.Editor, Is.EqualTo(PropertyGridEditor.TriState));
      Assert.That(r2.AllowNull, Is.True);
      Assert.That(r3.Editor, Is.EqualTo(PropertyGridEditor.Number));
      Assert.That(r3.Maximum, Is.EqualTo(10));
      Assert.That(r4.Editor, Is.EqualTo(PropertyGridEditor.Color));
    });
  }

  [Test]
  public void Typed_AddRow_round_trips_the_value_through_its_editor() {
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    var count = 3;
    var row = grid.AddRow("Count", () => count, v => count = v, minimum: 0, maximum: 10);
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    Assert.That(row.Get(), Is.EqualTo("3"), "the getter formats the typed value");

    canvas.RaiseMouseDown(200, 33); // open the hosted editor
    var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
    editor.SimulateUserInput("99");
    editor.SimulateKeyDown(Keys.Enter);

    Assert.That(count, Is.EqualTo(10), "the typed setter parses and the number editor clamps to the max");
  }

  [Test]
  public void AddEnumRow_lists_the_enum_names_and_commits_a_pick() {
    var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
    var fruit = Fruit.Apple;
    var row = grid.AddEnumRow("Fruit", () => fruit, v => fruit = v);

    Assert.That(row.Choices, Is.EqualTo(new[] { "Apple", "Banana", "Cherry" }));

    row.Set!("Cherry");
    Assert.That(fruit, Is.EqualTo(Fruit.Cherry));
  }

  [Test]
  public void Selecting_a_row_shows_its_description() {
    var grid = Create(new Model(), out _, out var canvas);

    canvas.RaiseMouseDown(60, 33); // select the Name row (name column)
    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(o => o.Contains("\"The display name.\"")), Is.True);
  }
}
