using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The owner-drawn ribbon fields — <see cref="RibbonComboBox"/> and <see cref="RibbonSpinner"/> — size
/// to a small row, open their list through the popup engine, take typed input with validation, and keep
/// working wherever the group is shown: the expanded ribbon, a collapsed group's flyout and the
/// minimized tab flyout.
/// </summary>
[TestFixture]
internal sealed class RibbonFieldTests {
  // DefaultTheme row height 22 + 4px tab chrome; RecordingGraphics measures 7px per character.
  private const int _GroupTop = 26;
  private const int _RowHeight = 23; // a 70px content height over three stacked rows
  private const int _Row0 = _GroupTop + 4;
  private const int _Row1 = _Row0 + _RowHeight;

  // "Mode" (28px) + gaps + an 80px field = 120px, the column's width; "Passes" (42px) + a 60px field
  // = 114px. Fields are right-aligned in the column, so the combo's box spans x 40..120 and the
  // spinner's 60..120, whose last 14px are the up/down arrows.
  private const int _ComboFieldX = 60;
  private const int _SpinnerTextX = 80;
  private const int _SpinnerArrowX = 113;

  private static (Ribbon Ribbon, RibbonGroup Group, RibbonComboBox Combo, RibbonSpinner Spinner) Fields(int width = 600) {
    var ribbon = new Ribbon { Bounds = new(0, 0, width, 120) };
    var home = new RibbonTab("Home");
    var group = new RibbonGroup("Defrag");
    var combo = new RibbonComboBox("Mode") { FieldWidth = 80 };
    combo.Items.AddRange(["Fast", "Full", "Optimize"]);
    combo.SelectedIndex = 0;
    var spinner = new RibbonSpinner("Passes") { FieldWidth = 60, Minimum = 1, Maximum = 10, Value = 3 };
    group.Items.AddRange(combo, spinner);
    home.Groups.Add(group);
    ribbon.Tabs.Add(home);
    return (ribbon, group, combo, spinner);
  }

  /// <summary>A leading Clipboard group wide enough that a 200px ribbon folds the field group.</summary>
  private static (Ribbon Ribbon, RibbonGroup Group, RibbonComboBox Combo, RibbonSpinner Spinner) CollapsibleFields() {
    var (ribbon, group, combo, spinner) = Fields(width: 600);
    var clipboard = new RibbonGroup("Clipboard");
    clipboard.Items.AddRange(
        new RibbonButton("Paste"),
        new RibbonButton("Cut", RibbonItemSize.Small),
        new RibbonButton("Copy", RibbonItemSize.Small),
        new RibbonButton("Format", RibbonItemSize.Small));
    var home = ribbon.Tabs[0];
    home.Groups.Remove(group);
    home.Groups.AddRange(clipboard, group);
    return (ribbon, group, combo, spinner);
  }

  private static HeadlessCanvasPeer Realize(Ribbon ribbon, out HeadlessBackend backend) {
    backend = new HeadlessBackend();
    var form = new Form { Bounds = new(0, 0, 800, 400) };
    form.Controls.Add(ribbon);
    Application.Run(form, backend);
    return backend.Created.OfType<HeadlessCanvasPeer>().First(p => p is not HeadlessPopupPeer);
  }

  private static void Click(HeadlessCanvasPeer canvas, int x, int y) {
    canvas.RaiseMouseDown(x, y);
    canvas.RaiseMouseUp(x, y);
  }

  private static void Type(HeadlessCanvasPeer canvas, string text) {
    foreach (var c in text)
      canvas.RaiseKeyPress(c);
  }

  // --- Layout and painting -------------------------------------------------------------------------

  [Test]
  public void Fields_stack_as_small_rows_and_size_the_group_to_label_plus_field() {
    var (ribbon, group, _, _) = Fields();
    Realize(ribbon, out _);

    Assert.That(group.Bounds, Is.EqualTo(new Rectangle(0, _GroupTop, 128, 94)), "a 120px column plus the group padding");
  }

  [Test]
  public void A_large_field_still_takes_a_small_row() {
    var (ribbon, group, combo, _) = Fields();
    combo.ItemSize = RibbonItemSize.Large;
    var canvas = Realize(ribbon, out _);

    var g = canvas.RaisePaint();
    var text = g.TextRects.Single(t => t.Text == "Fast").Bounds;

    Assert.Multiple(() => {
      Assert.That(group.Bounds.Width, Is.EqualTo(128), "still stacked in one column with the spinner");
      Assert.That(text.Height, Is.LessThanOrEqualTo(_RowHeight));
    });
  }

  [Test]
  public void The_fields_paint_their_label_and_their_value_inside_a_row() {
    var (ribbon, _, _, _) = Fields();
    var canvas = Realize(ribbon, out _);

    var g = canvas.RaisePaint();
    var mode = g.TextRects.Single(t => t.Text == "Fast").Bounds;
    var passes = g.TextRects.Single(t => t.Text == "3").Bounds;

    Assert.Multiple(() => {
      Assert.That(g.DrewText("Mode"), Is.True);
      Assert.That(g.DrewText("Passes"), Is.True);
      Assert.That(mode.Top, Is.GreaterThanOrEqualTo(_Row0));
      Assert.That(mode.Bottom, Is.LessThanOrEqualTo(_Row1), "the combo's value fits its row");
      Assert.That(passes.Top, Is.GreaterThanOrEqualTo(_Row1));
      Assert.That(passes.Bottom, Is.LessThanOrEqualTo(_Row1 + _RowHeight), "and so does the spinner's");
      Assert.That(passes.Right, Is.LessThanOrEqualTo(_SpinnerArrowX - 3), "the value stops short of the arrows");
    });
  }

  // --- RibbonComboBox ------------------------------------------------------------------------------

  [Test]
  public void Clicking_the_combo_opens_its_items_under_the_field() {
    var (ribbon, _, _, _) = Fields();
    var canvas = Realize(ribbon, out var backend);

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);

    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    Assert.Multiple(() => {
      Assert.That(popup.IsShown, Is.True);
      Assert.That(popup.ShowCalls.Single().Location, Is.EqualTo(new Point(40, _Row0 + _RowHeight - 1)), "under the field box");
      Assert.That(popup.ShowCalls.Single().Size, Is.EqualTo(new Size(80, 3 * 22)), "the field's width, a row per item");
    });

    var g = popup.RaisePaint();
    Assert.That(g.DrewText("Optimize"), Is.True);
  }

  [Test]
  public void Picking_an_item_selects_it_raises_the_event_once_and_closes_the_list() {
    var (ribbon, _, combo, _) = Fields();
    var canvas = Realize(ribbon, out var backend);
    var changes = 0;
    combo.SelectedIndexChanged += (_, _) => ++changes;

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    popup.RaiseMouseDown(20, (2 * 22) + 5); // third row

    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.EqualTo(2));
      Assert.That(combo.SelectedItem, Is.EqualTo("Optimize"));
      Assert.That(changes, Is.EqualTo(1));
      Assert.That(popup.IsShown, Is.False);
    });
  }

  [Test]
  public void The_open_list_walks_with_the_arrows_and_commits_on_enter() {
    var (ribbon, _, combo, _) = Fields();
    var canvas = Realize(ribbon, out var backend);

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);
    canvas.RaiseKeyDown(Keys.Down);
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.EqualTo(1));
      Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Single().IsShown, Is.False);
    });
  }

  [Test]
  public void Escape_closes_the_list_without_changing_the_selection() {
    var (ribbon, _, combo, _) = Fields();
    var canvas = Realize(ribbon, out var backend);

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);
    canvas.RaiseKeyDown(Keys.Down);
    canvas.RaiseKeyDown(Keys.Escape);

    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.Zero);
      Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Single().IsShown, Is.False);
    });
  }

  [Test]
  public void A_light_dismissed_list_changes_nothing() {
    var (ribbon, _, combo, _) = Fields();
    var canvas = Realize(ribbon, out var backend);

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);
    backend.Created.OfType<HeadlessPopupPeer>().Single().FireDismiss();

    Assert.That(combo.SelectedIndex, Is.Zero);
  }

  [Test]
  public void A_disabled_combo_does_not_open() {
    var (ribbon, _, combo, _) = Fields();
    combo.Enabled = false;
    var canvas = Realize(ribbon, out var backend);

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);

    Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
  }

  [Test]
  public void An_empty_combo_does_not_open() {
    var (ribbon, _, combo, _) = Fields();
    combo.Items.Clear();
    var canvas = Realize(ribbon, out var backend);

    canvas.RaiseMouseDown(_ComboFieldX, _Row0 + 10);

    Assert.That(backend.Created.OfType<HeadlessPopupPeer>(), Is.Empty);
  }

  [Test]
  public void The_selection_is_clamped_to_the_items() {
    var combo = new RibbonComboBox();
    combo.Items.AddRange(["a", "b"]);

    combo.SelectedIndex = 5;
    Assert.That(combo.SelectedIndex, Is.EqualTo(-1), "out of range is no selection");

    combo.SelectedIndex = 1;
    combo.Items.RemoveAt(1);
    Assert.That(combo.SelectedIndex, Is.EqualTo(-1), "removing the selected item clears the selection");

    combo.SelectedIndex = 0;
    combo.Items.Insert(0, "z");
    Assert.That(combo.SelectedItem, Is.EqualTo("a"), "an insert before the selection keeps the selected item");
  }

  [Test]
  public void Without_a_selection_the_selected_item_is_null() {
    var combo = new RibbonComboBox();

    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.EqualTo(-1));
      Assert.That(combo.SelectedItem, Is.Null);
    });
  }

  // --- RibbonSpinner -------------------------------------------------------------------------------

  [Test]
  public void The_arrows_step_by_the_increment_and_stop_at_the_bounds() {
    var (ribbon, _, _, spinner) = Fields();
    spinner.Increment = 4;
    var canvas = Realize(ribbon, out _);
    var changes = 0;
    spinner.ValueChanged += (_, _) => ++changes;

    Click(canvas, _SpinnerArrowX, _Row1 + 3); // up
    Assert.That(spinner.Value, Is.EqualTo(7m));

    Click(canvas, _SpinnerArrowX, _Row1 + 3);
    Assert.That(spinner.Value, Is.EqualTo(10m), "clamped at the maximum");

    Click(canvas, _SpinnerArrowX, _Row1 + 3);
    Assert.That(changes, Is.EqualTo(2), "a step that cannot move raises nothing");

    Click(canvas, _SpinnerArrowX, _Row1 + _RowHeight - 4); // down
    Click(canvas, _SpinnerArrowX, _Row1 + _RowHeight - 4);
    Click(canvas, _SpinnerArrowX, _Row1 + _RowHeight - 4);
    Assert.That(spinner.Value, Is.EqualTo(1m), "and at the minimum");
  }

  [Test]
  public void Typing_a_number_and_pressing_enter_commits_it() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);
    var changes = 0;
    spinner.ValueChanged += (_, _) => ++changes;

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "7");
    Assert.That(canvas.RaisePaint().DrewText("7"), Is.True, "the typed text shows while editing");
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.Multiple(() => {
      Assert.That(spinner.Value, Is.EqualTo(7m), "the first keystroke replaces the whole value");
      Assert.That(changes, Is.EqualTo(1));
    });
  }

  [Test]
  public void A_typed_value_outside_the_range_is_clamped() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "42");
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.That(spinner.Value, Is.EqualTo(10m));
  }

  [Test]
  public void Letters_are_refused_while_typing() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "5x");
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.That(spinner.Value, Is.EqualTo(5m));
  }

  [Test]
  public void A_minus_is_refused_when_the_range_has_no_negatives() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "-2");
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.That(spinner.Value, Is.EqualTo(2m));
  }

  [Test]
  public void An_unparsable_entry_reverts_to_the_current_value() {
    var (ribbon, _, _, spinner) = Fields();
    spinner.Minimum = -5;
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "-");
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.That(spinner.Value, Is.EqualTo(3m));
  }

  [Test]
  public void Backspace_edits_and_escape_cancels() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "9");
    canvas.RaiseKeyDown(Keys.Back);
    Type(canvas, "8");
    canvas.RaiseKeyDown(Keys.Escape);
    Assert.That(spinner.Value, Is.EqualTo(3m), "escape throws the edit away");

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "9");
    canvas.RaiseKeyDown(Keys.Back);
    Type(canvas, "8");
    canvas.RaiseKeyDown(Keys.Enter);
    Assert.That(spinner.Value, Is.EqualTo(8m));
  }

  [Test]
  public void Up_and_down_keys_step_the_value_being_edited() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    canvas.RaiseKeyDown(Keys.Up);
    canvas.RaiseKeyDown(Keys.Up);
    canvas.RaiseKeyDown(Keys.Down);

    Assert.That(spinner.Value, Is.EqualTo(4m));
  }

  [Test]
  public void Clicking_elsewhere_commits_the_edit() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "6");
    Click(canvas, 400, 100); // empty group area

    Assert.That(spinner.Value, Is.EqualTo(6m));
  }

  [Test]
  public void Losing_focus_commits_the_edit() {
    var (ribbon, _, _, spinner) = Fields();
    var canvas = Realize(ribbon, out _);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, "6");
    canvas.RaiseLostFocus();

    Assert.That(spinner.Value, Is.EqualTo(6m));
  }

  [Test]
  public void Decimal_places_format_the_value_and_accept_a_fraction() {
    var (ribbon, _, _, spinner) = Fields();
    spinner.DecimalPlaces = 1;
    spinner.Increment = 0.5m;
    var canvas = Realize(ribbon, out _);
    var separator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

    Assert.That(canvas.RaisePaint().DrewText($"3{separator}0"), Is.True);

    Click(canvas, _SpinnerTextX, _Row1 + 10);
    Type(canvas, $"2{separator}25");
    canvas.RaiseKeyDown(Keys.Enter);

    Assert.That(spinner.Value, Is.EqualTo(2.2m).Or.EqualTo(2.3m), "rounded to the one decimal place shown");
  }

  [Test]
  public void The_range_properties_keep_the_value_inside_and_drag_each_other_along() {
    var spinner = new RibbonSpinner { Minimum = 0, Maximum = 100, Value = 50 };

    spinner.Maximum = 20;
    Assert.That(spinner.Value, Is.EqualTo(20m), "lowering the maximum re-clamps the value");

    spinner.Minimum = 30;
    Assert.Multiple(() => {
      Assert.That(spinner.Maximum, Is.EqualTo(30m), "the maximum is dragged up to the minimum");
      Assert.That(spinner.Value, Is.EqualTo(30m));
    });

    spinner.Value = -1;
    Assert.That(spinner.Value, Is.EqualTo(30m), "an assignment is clamped too");
  }

  [Test]
  public void Invalid_increments_and_decimal_places_are_refused() {
    var spinner = new RibbonSpinner();

    Assert.Multiple(() => {
      Assert.That(() => spinner.Increment = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => spinner.DecimalPlaces = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => spinner.DecimalPlaces = 29, Throws.TypeOf<ArgumentOutOfRangeException>());
      Assert.That(() => new RibbonComboBox().FieldWidth = 0, Throws.TypeOf<ArgumentOutOfRangeException>());
    });
  }

  // --- Inside a collapsed group --------------------------------------------------------------------

  [Test]
  public void A_collapsed_group_opens_its_layout_in_a_flyout_rather_than_a_menu() {
    var (ribbon, group, _, _) = CollapsibleFields();
    var canvas = Realize(ribbon, out var backend);
    ribbon.Width = 200;
    Assert.That(group.IsCollapsed, Is.True);

    canvas.RaiseMouseDown(group.Bounds.X + 20, group.Bounds.Y + 20);

    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();
    var g = flyout.RaisePaint();
    Assert.Multiple(() => {
      Assert.That(flyout.IsShown, Is.True);
      Assert.That(flyout.ShowCalls.Single().Location, Is.EqualTo(new Point(group.Bounds.X, group.Bounds.Bottom)), "under the collapsed button");
      Assert.That(flyout.ShowCalls.Single().Size, Is.EqualTo(new Size(128, 94)), "the group at its natural size");
      Assert.That(g.DrewText("Fast"), Is.True, "the combo is drawn as a field, not a menu row");
      Assert.That(g.DrewText("3"), Is.True);
    });
  }

  [Test]
  public void The_combo_keeps_working_inside_a_collapsed_group_flyout() {
    var (ribbon, group, combo, _) = CollapsibleFields();
    var canvas = Realize(ribbon, out var backend);
    ribbon.Width = 200;
    canvas.RaiseMouseDown(group.Bounds.X + 20, group.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();

    // Inside the flyout the group sits at its origin: the combo's box spans 40..120 of row 0 at y 4.
    flyout.RaiseMouseDown(_ComboFieldX, 4 + 10);
    var list = backend.Created.OfType<HeadlessPopupPeer>().Last();
    Assert.Multiple(() => {
      Assert.That(list, Is.Not.SameAs(flyout));
      Assert.That(list.IsShown, Is.True);
      Assert.That(list.ParentPopup, Is.SameAs(flyout), "the list chains to the flyout that opened it");
      Assert.That(flyout.ExpectGrabHandoffCount, Is.EqualTo(1), "and the flyout survives handing its grab over");
      Assert.That(list.ShowCalls.Single().Location, Is.EqualTo(new Point(group.Bounds.X + 40, group.Bounds.Bottom + 4 + _RowHeight - 1)));
    });

    list.RaiseMouseDown(20, 22 + 5); // second row

    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.EqualTo(1));
      Assert.That(list.IsShown, Is.False);
      Assert.That(flyout.IsShown, Is.True, "picking a value leaves the group open for the next one");
      Assert.That(flyout.RegrabCount, Is.EqualTo(1), "and hands the grab back to it");
    });
  }

  [Test]
  public void The_spinner_keeps_working_inside_a_collapsed_group_flyout() {
    var (ribbon, group, _, spinner) = CollapsibleFields();
    var canvas = Realize(ribbon, out var backend);
    ribbon.Width = 200;
    canvas.RaiseMouseDown(group.Bounds.X + 20, group.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();
    var row1 = 4 + _RowHeight;

    flyout.RaiseMouseDown(_SpinnerArrowX, row1 + 3);
    Assert.That(spinner.Value, Is.EqualTo(4m), "the arrow steps");

    flyout.RaiseMouseDown(_SpinnerTextX, row1 + 10);
    flyout.RaiseKeyPress('9'); // a backend whose popup holds the keyboard grab routes keys to it
    flyout.RaiseKeyDown(Keys.Enter);
    Assert.That(spinner.Value, Is.EqualTo(9m), "typing through the popup");

    flyout.RaiseMouseDown(_SpinnerTextX, row1 + 10);
    canvas.RaiseKeyPress('2'); // one without it leaves the keys with the focused ribbon
    canvas.RaiseKeyDown(Keys.Enter);
    Assert.Multiple(() => {
      Assert.That(spinner.Value, Is.EqualTo(2m), "typing through the ribbon");
      Assert.That(flyout.IsShown, Is.True);
    });
  }

  [Test]
  public void Closing_the_flyout_commits_a_pending_edit() {
    var (ribbon, group, _, spinner) = CollapsibleFields();
    var canvas = Realize(ribbon, out var backend);
    ribbon.Width = 200;
    canvas.RaiseMouseDown(group.Bounds.X + 20, group.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();

    flyout.RaiseMouseDown(_SpinnerTextX, 4 + _RowHeight + 10);
    flyout.RaiseKeyPress('5');
    flyout.FireDismiss();

    Assert.That(spinner.Value, Is.EqualTo(5m));
  }

  [Test]
  public void A_button_in_a_collapsed_group_flyout_fires_and_closes_it() {
    var ribbon = new Ribbon { Bounds = new(0, 0, 200, 120) };
    var home = new RibbonTab("Home");
    var clipboard = new RibbonGroup("Clipboard");
    clipboard.Items.AddRange(
        new RibbonButton("Paste"),
        new RibbonButton("Cut", RibbonItemSize.Small),
        new RibbonButton("Copy", RibbonItemSize.Small),
        new RibbonButton("Format", RibbonItemSize.Small));
    var font = new RibbonGroup("Font");
    var bold = new RibbonToggleButton("Bold", RibbonItemSize.Small);
    font.Items.AddRange(bold, new RibbonToggleButton("Italic", RibbonItemSize.Small));
    home.Groups.AddRange(clipboard, font);
    ribbon.Tabs.Add(home);
    var canvas = Realize(ribbon, out var backend);
    Assert.That(font.IsCollapsed, Is.True);

    canvas.RaiseMouseDown(font.Bounds.X + 20, font.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();
    flyout.RaiseMouseDown(20, 4 + 10); // Bold, row 0

    Assert.Multiple(() => {
      Assert.That(bold.Checked, Is.True);
      Assert.That(flyout.IsShown, Is.False);
    });
  }

  [Test]
  public void A_grid_button_in_a_collapsed_group_flyout_opens_its_picker_where_the_flyout_was() {
    var ribbon = new Ribbon { Bounds = new(0, 0, 200, 120) };
    var home = new RibbonTab("Home");
    var clipboard = new RibbonGroup("Clipboard");
    clipboard.Items.AddRange(
        new RibbonButton("Paste"),
        new RibbonButton("Cut", RibbonItemSize.Small),
        new RibbonButton("Copy", RibbonItemSize.Small),
        new RibbonButton("Format", RibbonItemSize.Small));
    var tables = new RibbonGroup("Tables and charts");
    var table = new RibbonGridButton("Table");
    tables.Items.AddRange(table, new RibbonButton("Chart"));
    home.Groups.AddRange(clipboard, tables);
    ribbon.Tabs.Add(home);
    var canvas = Realize(ribbon, out var backend);
    Assert.That(tables.IsCollapsed, Is.True);
    var committed = default((int Rows, int Columns)?);
    table.RangeSelected += (_, e) => committed = (e.Rows, e.Columns);

    canvas.RaiseMouseDown(tables.Bounds.X + 20, tables.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();
    flyout.RaiseMouseDown(20, 30); // the large Table button

    var picker = backend.Created.OfType<HeadlessPopupPeer>().Last();
    Assert.Multiple(() => {
      Assert.That(flyout.IsShown, Is.False);
      Assert.That(picker, Is.Not.SameAs(flyout));
      Assert.That(picker.ShowCalls.Single().Location, Is.EqualTo(new Point(tables.Bounds.X, tables.Bounds.Bottom)));
    });

    picker.RaiseMouseDown(6 + 9, 6 + 9);
    Assert.That(committed, Is.EqualTo((1, 1)));
  }

  [Test]
  public void Escape_closes_the_collapsed_group_flyout() {
    var (ribbon, group, _, _) = CollapsibleFields();
    var canvas = Realize(ribbon, out var backend);
    ribbon.Width = 200;
    canvas.RaiseMouseDown(group.Bounds.X + 20, group.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();

    canvas.RaiseKeyDown(Keys.Escape);

    Assert.That(flyout.IsShown, Is.False);
  }

  [Test]
  public void A_hosted_control_in_a_collapsed_group_flyout_stays_put_and_shows_a_placeholder() {
    var ribbon = new Ribbon { Bounds = new(0, 0, 200, 120) };
    var home = new RibbonTab("Home");
    var clipboard = new RibbonGroup("Clipboard");
    clipboard.Items.AddRange(
        new RibbonButton("Paste"),
        new RibbonButton("Cut", RibbonItemSize.Small),
        new RibbonButton("Copy", RibbonItemSize.Small),
        new RibbonButton("Format", RibbonItemSize.Small));
    var hosted = new ComboBox();
    var styles = new RibbonGroup("Styles");
    styles.Items.Add(new RibbonHostItem(hosted) { HostWidth = 120 });
    home.Groups.AddRange(clipboard, styles);
    ribbon.Tabs.Add(home);
    var canvas = Realize(ribbon, out var backend);
    Assert.That(styles.IsCollapsed, Is.True);

    canvas.RaiseMouseDown(styles.Bounds.X + 20, styles.Bounds.Y + 20);
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();
    var g = flyout.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(flyout.IsShown, Is.True, "the group opens rather than vanishing");
      Assert.That(hosted.Parent, Is.SameAs(ribbon), "the live control is not re-parented");
      Assert.That(hosted.Visible, Is.False, "and stays off screen with its collapsed group");
      Assert.That(g.Operations.Exists(o => o.StartsWith("fill ") && o.EndsWith(" 8,6,120,19")), Is.True, "its slot paints a placeholder box");
    });
  }

  // --- Inside the minimized tab flyout -------------------------------------------------------------

  [Test]
  public void The_fields_work_inside_the_minimized_tab_flyout() {
    var (ribbon, _, combo, spinner) = Fields();
    var canvas = Realize(ribbon, out var backend);
    ribbon.Minimized = true;
    canvas.RaiseMouseDown(20, 13); // the Home tab
    var flyout = backend.Created.OfType<HeadlessPopupPeer>().Single();

    flyout.RaiseMouseDown(_SpinnerArrowX, 4 + _RowHeight + 3);
    Assert.That(spinner.Value, Is.EqualTo(4m));

    flyout.RaiseMouseDown(_ComboFieldX, 4 + 10);
    var list = backend.Created.OfType<HeadlessPopupPeer>().Last();
    list.RaiseMouseDown(20, 5);
    Assert.That(combo.SelectedIndex, Is.Zero);
    list.FireDismiss();

    flyout.RaiseMouseDown(_ComboFieldX, 4 + 10);
    list.RaiseMouseDown(20, 22 + 5);
    Assert.Multiple(() => {
      Assert.That(combo.SelectedIndex, Is.EqualTo(1));
      Assert.That(list.ParentPopup, Is.SameAs(flyout));
      Assert.That(flyout.IsShown, Is.True);
    });
  }
}
