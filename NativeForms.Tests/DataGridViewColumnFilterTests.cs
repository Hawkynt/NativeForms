using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Column filtering on <see cref="DataGridView"/> (PRD §14): the accepted-value sets that hide rows,
/// the distinct values the menu offers, and the searchable menu the header funnel opens.
/// </summary>
[TestFixture]
internal sealed class DataGridViewColumnFilterTests {
  private sealed record Person(string Name, string City);

  private static DataGridView MakeGrid(out HeadlessBackend backend, out HeadlessCanvasPeer canvas) {
    // 22px header, then rows at 22px; columns at x 0..100 (Name) and 100..200 (City).
    var grid = new DataGridView { Bounds = new(0, 0, 200, 200), AllowUserToFilterColumns = true };
    grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
    grid.Columns.Add(new DataGridViewColumn("City", static o => ((Person)o!).City));
    grid.Items.AddRange(
    [
        new Person("Alice", "Berlin"),
            new Person("Bob", "Munich"),
            new Person("Carol", "Berlin"),
            new Person("Dave", "Hamburg"),
        ]);

    backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(grid);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
    return grid;
  }

  /// <summary>The row captions the grid actually painted, in display order.</summary>
  private static List<string> PaintedNames(DataGridView grid, HeadlessCanvasPeer canvas) {
    var names = grid.Items.Cast<Person>().Select(static p => p.Name).ToHashSet();
    return canvas.RaisePaint().TextDraws.Select(static draw => draw.Text).Where(names.Contains).ToList();
  }

  [Test]
  public void No_filter_shows_every_row() {
    var grid = MakeGrid(out _, out var canvas);

    Assert.That(PaintedNames(grid, canvas), Is.EqualTo(new[] { "Alice", "Bob", "Carol", "Dave" }));
  }

  [Test]
  public void A_filter_hides_the_rows_it_rejects() {
    var grid = MakeGrid(out _, out var canvas);

    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    Assert.That(PaintedNames(grid, canvas), Is.EqualTo(new[] { "Alice", "Carol" }));
  }

  [Test]
  public void An_empty_accepted_set_hides_everything() {
    var grid = MakeGrid(out _, out var canvas);

    grid.SetColumnFilter(grid.Columns[1], []);

    Assert.That(PaintedNames(grid, canvas), Is.Empty, "unchecking every value means every value, not none");
  }

  [Test]
  public void Filters_on_two_columns_both_apply() {
    var grid = MakeGrid(out _, out var canvas);

    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);
    grid.SetColumnFilter(grid.Columns[0], ["Carol", "Dave"]);

    Assert.That(PaintedNames(grid, canvas), Is.EqualTo(new[] { "Carol" }));
  }

  [Test]
  public void Clearing_a_filter_brings_its_rows_back() {
    var grid = MakeGrid(out _, out var canvas);
    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    grid.SetColumnFilter(grid.Columns[1], null);

    Assert.That(PaintedNames(grid, canvas), Is.EqualTo(new[] { "Alice", "Bob", "Carol", "Dave" }));
  }

  [Test]
  public void Setting_a_filter_reports_the_column_it_was_set_on() {
    var grid = MakeGrid(out _, out _);
    DataGridViewCellEventArgs? reported = null;
    grid.ColumnFilterChanged += (_, e) => reported = e;

    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    Assert.That(reported?.ColumnIndex, Is.EqualTo(1));
  }

  [Test]
  public void The_values_a_column_offers_are_its_distinct_ones_in_first_seen_order() {
    var grid = MakeGrid(out _, out _);

    Assert.That(grid.GetFilterValues(grid.Columns[1]), Is.EqualTo(new[] { "Berlin", "Munich", "Hamburg" }));
  }

  [Test]
  public void A_column_still_offers_every_value_while_it_is_the_one_being_filtered() {
    var grid = MakeGrid(out _, out _);
    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    Assert.That(
        grid.GetFilterValues(grid.Columns[1]),
        Is.EqualTo(new[] { "Berlin", "Munich", "Hamburg" }),
        "narrowing a column must not empty its own menu, or the filter could never be widened again");
  }

  [Test]
  public void Another_columns_menu_offers_only_what_the_first_filter_left() {
    var grid = MakeGrid(out _, out _);
    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    Assert.That(grid.GetFilterValues(grid.Columns[0]), Is.EqualTo(new[] { "Alice", "Carol" }));
  }

  [Test]
  public void The_filter_menu_is_searchable_and_lists_the_values_under_an_all_toggle() {
    var grid = MakeGrid(out _, out _);

    var menu = grid.CreateColumnFilterMenu(grid.Columns[1]);

    Assert.Multiple(() => {
      Assert.That(menu.ShowSearchBox, Is.True, "a column of four hundred values is unusable without it");
      Assert.That(menu.Items[0].DisplayText, Is.EqualTo(Strings.FilterAll));
      Assert.That(menu.Items[1], Is.InstanceOf<ToolStripSeparator>());
      Assert.That(
          menu.Items.Skip(2).Select(static item => item.DisplayText),
          Is.EqualTo(new[] { "Berlin", "Munich", "Hamburg" }));
    });
  }

  [Test]
  public void Every_value_starts_checked_while_nothing_is_filtered() {
    var grid = MakeGrid(out _, out _);

    var menu = grid.CreateColumnFilterMenu(grid.Columns[1]);

    Assert.That(menu.Items.OfType<ToolStripMenuItem>().Select(static item => item.Checked), Is.All.True);
  }

  [Test]
  public void Only_the_accepted_values_are_checked_while_a_filter_is_active() {
    var grid = MakeGrid(out _, out _);
    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    var menu = grid.CreateColumnFilterMenu(grid.Columns[1]);

    Assert.Multiple(() => {
      Assert.That(menu.Items.OfType<ToolStripMenuItem>().First().Checked, Is.False, "(All) is off while a filter narrows");
      Assert.That(
          menu.Items.Skip(2).OfType<ToolStripMenuItem>().Select(static item => (item.DisplayText, item.Checked)),
          Is.EqualTo(new[] { ("Berlin", true), ("Munich", false), ("Hamburg", false) }));
    });
  }

  [Test]
  public void Unchecking_a_value_in_the_menu_narrows_the_grid() {
    var grid = MakeGrid(out _, out var canvas);
    var menu = grid.CreateColumnFilterMenu(grid.Columns[1]);

    menu.Items.Skip(2).OfType<ToolStripMenuItem>().Single(static item => item.DisplayText == "Berlin").PerformClick();

    Assert.That(PaintedNames(grid, canvas), Is.EqualTo(new[] { "Bob", "Dave" }));
  }

  [Test]
  public void Re_checking_the_last_missing_value_clears_the_filter_rather_than_listing_them_all() {
    var grid = MakeGrid(out _, out _);
    grid.SetColumnFilter(grid.Columns[1], ["Berlin", "Munich"]);
    var menu = grid.CreateColumnFilterMenu(grid.Columns[1]);

    menu.Items.Skip(2).OfType<ToolStripMenuItem>().Single(static item => item.DisplayText == "Hamburg").PerformClick();

    Assert.That(grid.Columns[1].Filter, Is.Null, "so the header funnel stops claiming a filter is active");
  }

  [Test]
  public void The_all_toggle_switches_between_everything_and_nothing() {
    var grid = MakeGrid(out _, out var canvas);
    var menu = grid.CreateColumnFilterMenu(grid.Columns[1]);
    var all = menu.Items.OfType<ToolStripMenuItem>().First();

    all.PerformClick(); // (All) off
    var afterOff = PaintedNames(grid, canvas);
    all.PerformClick(); // (All) back on

    Assert.Multiple(() => {
      Assert.That(afterOff, Is.Empty);
      Assert.That(PaintedNames(grid, canvas), Is.EqualTo(new[] { "Alice", "Bob", "Carol", "Dave" }));
    });
  }

  [Test]
  public void A_click_on_the_header_funnel_opens_the_menu_instead_of_sorting() {
    var grid = MakeGrid(out var backend, out var canvas);
    grid.Columns[1].SortMode = DataGridViewColumnSortMode.Automatic;

    canvas.RaiseMouseDown(195, 10); // the funnel corner of the City header

    Assert.Multiple(() => {
      Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Any(), Is.True, "the filter menu opened");
      Assert.That(grid.SortedColumn, Is.Null, "and the header did not also sort");
    });
  }

  [Test]
  public void A_click_on_the_rest_of_the_header_still_sorts() {
    var grid = MakeGrid(out var backend, out var canvas);
    grid.Columns[1].SortMode = DataGridViewColumnSortMode.Automatic;

    canvas.RaiseMouseDown(130, 10); // well left of the funnel zone

    Assert.Multiple(() => {
      Assert.That(grid.SortedColumn, Is.SameAs(grid.Columns[1]));
      Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Any(), Is.False);
    });
  }

  [Test]
  public void A_grid_that_does_not_offer_filtering_keeps_its_whole_header_for_sorting() {
    var grid = MakeGrid(out var backend, out var canvas);
    grid.AllowUserToFilterColumns = false;
    grid.Columns[1].SortMode = DataGridViewColumnSortMode.Automatic;

    canvas.RaiseMouseDown(195, 10);

    Assert.Multiple(() => {
      Assert.That(grid.SortedColumn, Is.SameAs(grid.Columns[1]));
      Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Any(), Is.False);
    });
  }

  /// <summary>The rectangle a header caption was laid out in.</summary>
  private static System.Drawing.Rectangle HeaderRectOf(HeadlessCanvasPeer canvas, string caption)
      => canvas.RaisePaint().TextRects.First(draw => draw.Text == caption).Bounds;

  [Test]
  public void The_funnel_is_reserved_space_rather_than_painted_over_the_caption() {
    // Found on screen, not here: with the caption laid out across the whole cell, a right-aligned
    // header ran straight under the funnel. A headless test sees text and a glyph both drawn and
    // is perfectly happy; only the rectangles say whether they overlap.
    var grid = MakeGrid(out _, out var canvas);
    var withFunnels = HeaderRectOf(canvas, "City");

    grid.AllowUserToFilterColumns = false;
    var without = HeaderRectOf(canvas, "City");

    Assert.That(withFunnels.Right, Is.LessThanOrEqualTo(without.Right - 16), "the funnel's 16 px are kept clear");
  }

  [Test]
  public void A_sorted_and_filtered_header_reserves_room_for_both_glyphs() {
    var grid = MakeGrid(out _, out var canvas);
    var filteredOnly = HeaderRectOf(canvas, "City");

    grid.Columns[1].SortMode = DataGridViewColumnSortMode.Automatic;
    grid.Sort(grid.Columns[1], SortOrder.Ascending);
    var both = HeaderRectOf(canvas, "City");

    Assert.That(both.Right, Is.LessThanOrEqualTo(filteredOnly.Right - 14), "the arrow sits inboard of the funnel, not under it");
  }

  [Test]
  public void A_filtered_row_takes_no_selection() {
    var grid = MakeGrid(out _, out var canvas);
    grid.SetColumnFilter(grid.Columns[1], ["Berlin"]);

    canvas.RaiseMouseDown(20, 30); // the first row on screen, which is now Alice

    Assert.That(((Person?)grid.SelectedItem)?.Name, Is.EqualTo("Alice"));
  }
}
