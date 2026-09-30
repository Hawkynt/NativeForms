using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The two <see cref="ListView"/> members an application needs to drag rows elsewhere:
/// <see cref="ListView.GetItemAt"/> resolves a client point to the item under it in every view, and
/// <see cref="ListView.ItemDrag"/> announces that a press on an item travelled far enough to be a drag
/// — the moment a handler calls <see cref="Control.DoDragDrop(object, DragDropEffects)"/>. A press that does not travel stays a
/// click, and a press on empty space stays a rubber band.
/// </summary>
[TestFixture]
internal sealed class ListViewItemDragTests {
  private const int _Threshold = 4; // mirrors TreeView's drag threshold

  private static HeadlessCanvasPeer Realize(OwnerDrawnControl control) {
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(control);
    Application.Run(form, backend);
    return backend.Created.OfType<HeadlessCanvasPeer>().Single();
  }

  /// <summary>Details view with a header and three rows.</summary>
  private static ListView MakeDetails(out HeadlessCanvasPeer canvas) {
    var list = new ListView { Bounds = new(0, 0, 300, 220) };
    list.Columns.AddRange([new ColumnHeader("Name", 140), new ColumnHeader("Size", 80)]);
    list.Items.AddRange([new ListViewItem("File1", "1 KB"), new ListViewItem("File2", "2 KB"), new ListViewItem("File3", "3 KB")]);
    canvas = Realize(list);
    return list;
  }

  private static ListView MakeView(ListViewView view, int count, out HeadlessCanvasPeer canvas) {
    var list = new ListView { Bounds = new(0, 0, 300, 220), View = view };
    list.Columns.Add(new ColumnHeader("Name", 140));
    for (var i = 0; i < count; ++i)
      list.Items.Add(new ListViewItem("Item" + i));

    canvas = Realize(list);
    return list;
  }

  private static Point CenterOf(Rectangle r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

  /// <summary>A point inside the item's row but clear of the leading check glyph and the scroll bar.</summary>
  private static Point PressPointOf(ListView list, int index) {
    var bounds = list.GetItemBounds(index);
    return new(bounds.X + 40, bounds.Y + (bounds.Height / 2));
  }

  // --- GetItemAt -------------------------------------------------------------------------------

  [Test]
  public void Given_details_view_when_the_point_is_inside_a_row_then_that_item_is_returned() {
    var list = MakeDetails(out _);

    Assert.Multiple(() => {
      for (var i = 0; i < list.Items.Count; ++i)
        Assert.That(list.GetItemAt(CenterOf(list.GetItemBounds(i)).X, CenterOf(list.GetItemBounds(i)).Y), Is.SameAs(list.Items[i]), $"row {i}");
    });
  }

  [Test]
  public void Given_details_view_when_the_point_is_on_the_row_edges_then_the_boundaries_are_exact() {
    var list = MakeDetails(out _);
    var first = list.GetItemBounds(0);
    var last = list.GetItemBounds(2);

    Assert.Multiple(() => {
      Assert.That(list.GetItemAt(0, first.Y), Is.SameAs(list.Items[0]), "first pixel below the header");
      Assert.That(list.GetItemAt(0, first.Y - 1), Is.Null, "last pixel of the header");
      Assert.That(list.GetItemAt(0, 0), Is.Null, "the header band");
      Assert.That(list.GetItemAt(0, first.Bottom - 1), Is.SameAs(list.Items[0]), "last pixel of the first row");
      Assert.That(list.GetItemAt(0, first.Bottom), Is.SameAs(list.Items[1]), "first pixel of the second row");
      Assert.That(list.GetItemAt(0, last.Bottom - 1), Is.SameAs(list.Items[2]), "last pixel of the last row");
      Assert.That(list.GetItemAt(0, last.Bottom), Is.Null, "first pixel of the empty space below");
      Assert.That(list.GetItemAt(299, first.Y), Is.SameAs(list.Items[0]), "last client column");
    });
  }

  [TestCase(-1, 30, TestName = "Given_list_when_x_is_negative_then_no_item_is_hit")]
  [TestCase(10, -1, TestName = "Given_list_when_y_is_negative_then_no_item_is_hit")]
  [TestCase(300, 30, TestName = "Given_list_when_x_is_the_client_width_then_no_item_is_hit")]
  [TestCase(10, 219, TestName = "Given_list_when_y_is_in_the_empty_space_then_no_item_is_hit")]
  [TestCase(10, 220, TestName = "Given_list_when_y_is_the_client_height_then_no_item_is_hit")]
  public void Points_off_every_item_hit_nothing(int x, int y)
      => Assert.That(MakeDetails(out _).GetItemAt(x, y), Is.Null);

  [TestCase(ListViewView.Details)]
  [TestCase(ListViewView.List)]
  [TestCase(ListViewView.SmallIcon)]
  [TestCase(ListViewView.LargeIcon)]
  [TestCase(ListViewView.Tile)]
  public void Given_any_view_when_the_point_is_at_an_items_cell_then_that_item_is_returned(ListViewView view) {
    var list = MakeView(view, 4, out _);

    Assert.Multiple(() => {
      for (var i = 0; i < list.Items.Count; ++i) {
        var center = CenterOf(list.GetItemBounds(i));
        Assert.That(list.GetItemAt(center.X, center.Y), Is.SameAs(list.Items[i]), $"{view} item {i}");
      }

      var below = list.GetItemBounds(list.Items.Count - 1).Bottom;
      Assert.That(below, Is.LessThan(list.Height), $"{view}: precondition, empty space is left below the items");
      Assert.That(list.GetItemAt(5, below), Is.Null, $"{view}: first pixel below the last row");
    });
  }

  [TestCase(ListViewView.SmallIcon)]
  [TestCase(ListViewView.LargeIcon)]
  [TestCase(ListViewView.Tile)]
  public void Given_a_grid_view_when_the_point_is_right_of_the_last_cell_in_a_row_then_no_item_is_hit(ListViewView view) {
    var list = MakeView(view, 1, out _);
    var only = list.GetItemBounds(0);

    Assert.Multiple(() => {
      Assert.That(list.GetItemAt(only.Right - 1, only.Y), Is.SameAs(list.Items[0]), "last pixel of the cell");
      Assert.That(list.GetItemAt(only.Right, only.Y), Is.Null, "first pixel right of the only cell");
    });
  }

  [Test]
  public void Given_groups_when_the_point_is_on_a_group_header_then_no_item_is_hit_but_grouped_items_are() {
    var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.Details };
    list.Columns.Add(new ColumnHeader("Name", 140));
    var docs = new ListViewGroup("Docs");
    var pics = new ListViewGroup("Pics");
    list.Groups.AddRange([docs, pics]);
    list.Items.AddRange([new ListViewItem("a") { Group = docs }, new ListViewItem("b") { Group = pics }, new ListViewItem("c") { Group = docs }]);
    Realize(list);

    var a = list.GetItemBounds(0);
    var b = list.GetItemBounds(1);
    var c = list.GetItemBounds(2);

    Assert.Multiple(() => {
      Assert.That(list.GetItemAt(10, a.Y - 1), Is.Null, "the Docs group header sits right above the first item");
      Assert.That(list.GetItemAt(10, b.Y - 1), Is.Null, "the Pics group header sits right above b");
      Assert.That(list.GetItemAt(10, a.Y + 1), Is.SameAs(list.Items[0]));
      Assert.That(list.GetItemAt(10, b.Y + 1), Is.SameAs(list.Items[1]));
      Assert.That(list.GetItemAt(10, c.Y + 1), Is.SameAs(list.Items[2]), "c renders under Docs, before Pics");
    });
  }

  [Test]
  public void Given_scrolled_list_when_the_point_is_on_the_top_row_then_the_scrolled_to_item_is_returned() {
    var list = MakeView(ListViewView.Details, 100, out _);
    list.EnsureVisible(99);
    var top = list.GetItemBounds(list.TopIndex);

    Assert.That(list.TopIndex, Is.GreaterThan(0), "precondition: the list scrolled");
    Assert.Multiple(() => {
      Assert.That(list.GetItemAt(10, top.Y + 1), Is.SameAs(list.Items[list.TopIndex]));
      Assert.That(list.GetItemAt(10, CenterOf(list.GetItemBounds(99)).Y), Is.SameAs(list.Items[99]));
    });
  }

  [Test]
  public void Given_overflowing_list_when_the_point_is_on_the_scroll_bar_then_no_item_is_hit() {
    var list = MakeView(ListViewView.Details, 100, out _);
    var first = list.GetItemBounds(0);

    Assert.Multiple(() => {
      Assert.That(list.GetItemAt(300 - 14, first.Y + 1), Is.Null, "the overlay scroll bar strip belongs to scrolling");
      Assert.That(list.GetItemAt(300 - 15, first.Y + 1), Is.SameAs(list.Items[0]), "last pixel left of the bar");
    });
  }

  [Test]
  public void Given_virtual_mode_when_the_point_is_on_a_row_then_the_retrieved_item_is_returned() {
    var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List, VirtualMode = true, VirtualListSize = 3 };
    var served = new ListViewItem[3];
    list.RetrieveVirtualItem += (_, e) => e.Item = served[e.ItemIndex] ??= new ListViewItem("Row" + e.ItemIndex);
    Realize(list);

    var second = CenterOf(list.GetItemBounds(1));
    var hit = list.GetItemAt(second.X, second.Y);

    Assert.Multiple(() => {
      Assert.That(hit, Is.SameAs(served[1]), "the item RetrieveVirtualItem handed out for that row");
      Assert.That(list.GetItemAt(5, 215), Is.Null, "below the last virtual row");
      Assert.That(list.Items, Is.Empty, "no model item is materialised");
    });
  }

  [Test]
  public void Given_unknown_size_virtual_mode_when_the_point_is_past_the_last_real_row_then_no_item_is_hit() {
    var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List, VirtualMode = true, VirtualListSize = -1 };
    list.RetrieveVirtualItem += (_, e) => {
      if (e.ItemIndex < 2)
        e.Item = new ListViewItem("Row" + e.ItemIndex);
      else
        e.EndOfList = true;
    };
    Realize(list);
    var h = list.ItemHeight;

    Assert.Multiple(() => {
      Assert.That(list.GetItemAt(5, h + 1)?.Text, Is.EqualTo("Row1"));
      Assert.That(list.GetItemAt(5, (2 * h) + 1), Is.Null, "the provider reports the end at row 2");
    });
  }

  // --- ItemDrag --------------------------------------------------------------------------------

  [Test]
  public void Given_press_on_an_item_when_the_pointer_travels_the_threshold_then_ItemDrag_carries_the_button_and_item() {
    var list = MakeDetails(out var canvas);
    var raised = new List<ItemDragEventArgs>();
    list.ItemDrag += (_, e) => raised.Add(e);
    var p = PressPointOf(list, 1);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X + _Threshold, p.Y);

    Assert.Multiple(() => {
      Assert.That(raised, Has.Count.EqualTo(1));
      Assert.That(raised[0].Button, Is.EqualTo(MouseButtons.Left));
      Assert.That(raised[0].Item, Is.SameAs(list.Items[1]));
    });
  }

  [TestCase(_Threshold - 1, 0, false, TestName = "Given_press_on_an_item_when_x_travel_is_threshold_minus_one_then_no_drag")]
  [TestCase(0, _Threshold - 1, false, TestName = "Given_press_on_an_item_when_y_travel_is_threshold_minus_one_then_no_drag")]
  [TestCase(-(_Threshold - 1), -(_Threshold - 1), false, TestName = "Given_press_on_an_item_when_diagonal_travel_stays_below_threshold_then_no_drag")]
  [TestCase(0, _Threshold, true, TestName = "Given_press_on_an_item_when_y_travel_is_the_threshold_then_drag")]
  [TestCase(-_Threshold, 0, true, TestName = "Given_press_on_an_item_when_x_travel_is_minus_the_threshold_then_drag")]
  public void The_drag_threshold_is_inclusive(int dx, int dy, bool drags) {
    var list = MakeDetails(out var canvas);
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;
    var p = PressPointOf(list, 1);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X + dx, p.Y + dy);

    Assert.That(raised, Is.EqualTo(drags ? 1 : 0));
  }

  [Test]
  public void Given_ItemDrag_raised_when_the_pointer_keeps_moving_then_it_is_not_raised_again() {
    var list = MakeDetails(out var canvas);
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;
    var p = PressPointOf(list, 0);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X + 10, p.Y);
    canvas.RaiseMouseMove(p.X + 20, p.Y + 30);
    canvas.RaiseMouseUp(p.X + 20, p.Y + 30);
    canvas.RaiseMouseMove(p.X + 40, p.Y + 40); // hover after release

    Assert.That(raised, Is.EqualTo(1));
  }

  [Test]
  public void Given_press_and_release_without_travel_when_released_then_it_is_a_plain_click() {
    var list = MakeDetails(out var canvas);
    var raised = 0;
    var selections = 0;
    list.ItemDrag += (_, _) => ++raised;
    list.SelectedIndexChanged += (_, _) => ++selections;
    var p = PressPointOf(list, 2);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X + 1, p.Y + 1);
    canvas.RaiseMouseUp(p.X + 1, p.Y + 1);

    Assert.Multiple(() => {
      Assert.That(raised, Is.Zero);
      Assert.That(list.SelectedIndex, Is.EqualTo(2));
      Assert.That(selections, Is.EqualTo(1));
    });
  }

  [Test]
  public void Given_two_quick_clicks_without_travel_when_released_then_the_item_is_still_activated() {
    var list = MakeDetails(out var canvas);
    var activations = 0;
    list.ItemActivate += (_, _) => ++activations;
    var p = PressPointOf(list, 0);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseUp(p.X, p.Y);
    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseUp(p.X, p.Y);

    Assert.That(activations, Is.EqualTo(1));
  }

  [Test]
  public void Given_ItemDrag_raised_when_the_next_press_follows_quickly_then_it_is_not_a_double_click() {
    var list = MakeDetails(out var canvas);
    var activations = 0;
    list.ItemActivate += (_, _) => ++activations;
    var p = PressPointOf(list, 0);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X, p.Y + 10);
    canvas.RaiseMouseUp(p.X, p.Y + 10);
    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseUp(p.X, p.Y);

    Assert.That(activations, Is.Zero, "a drag gesture is not the first half of a double-click");
  }

  [Test]
  public void Given_multi_selection_when_a_selected_item_is_dragged_then_the_whole_selection_survives() {
    var list = MakeDetails(out var canvas);
    list.Items[0].Selected = true;
    list.Items[2].Selected = true;
    var selections = 0;
    list.SelectedIndexChanged += (_, _) => ++selections;
    var p = PressPointOf(list, 2);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X, p.Y - 20);
    canvas.RaiseMouseUp(p.X, p.Y - 20);

    Assert.Multiple(() => {
      Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 2 }), "the drag carries every selected item");
      Assert.That(selections, Is.Zero);
    });
  }

  [Test]
  public void Given_multi_selection_when_a_selected_item_is_clicked_without_travel_then_it_becomes_the_only_selection_on_release() {
    var list = MakeDetails(out var canvas);
    list.Items[0].Selected = true;
    list.Items[2].Selected = true;
    var p = PressPointOf(list, 2);

    canvas.RaiseMouseDown(p.X, p.Y);
    var duringPress = list.SelectedIndices.ToArray();
    canvas.RaiseMouseUp(p.X, p.Y);

    Assert.Multiple(() => {
      Assert.That(duringPress, Is.EqualTo(new[] { 0, 2 }), "the press keeps the set so it can still be dragged");
      Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 2 }), "the click collapses to the clicked item");
    });
  }

  [Test]
  public void Given_ctrl_press_on_a_selected_item_when_dragged_then_it_stays_selected() {
    var list = MakeDetails(out var canvas);
    list.Items[0].Selected = true;
    list.Items[1].Selected = true;
    var p = PressPointOf(list, 1);

    canvas.RaiseMouseDown(p.X, p.Y, modifiers: KeyModifiers.Control);
    canvas.RaiseMouseMove(p.X + 10, p.Y);
    canvas.RaiseMouseUp(p.X + 10, p.Y, modifiers: KeyModifiers.Control);

    Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 1 }));
  }

  [Test]
  public void Given_ctrl_click_on_a_selected_item_without_travel_then_it_is_toggled_off_on_release() {
    var list = MakeDetails(out var canvas);
    list.Items[0].Selected = true;
    list.Items[1].Selected = true;
    var p = PressPointOf(list, 1);

    canvas.RaiseMouseDown(p.X, p.Y, modifiers: KeyModifiers.Control);
    canvas.RaiseMouseUp(p.X, p.Y, modifiers: KeyModifiers.Control);

    Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
  }

  [Test]
  public void Given_press_on_an_unselected_item_when_dragged_then_it_is_selected_by_the_press_and_dragged() {
    var list = MakeDetails(out var canvas);
    list.Items[0].Selected = true;
    ItemDragEventArgs? drag = null;
    list.ItemDrag += (_, e) => drag = e;
    var p = PressPointOf(list, 1);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X, p.Y + 10);
    canvas.RaiseMouseUp(p.X, p.Y + 10);

    Assert.Multiple(() => {
      Assert.That(drag?.Item, Is.SameAs(list.Items[1]));
      Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }), "an unselected item is selected on press, as before");
    });
  }

  [Test]
  public void Given_press_on_empty_space_when_the_pointer_travels_then_no_ItemDrag_and_the_band_selects() {
    var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List };
    list.Items.AddRange([new ListViewItem("a"), new ListViewItem("b"), new ListViewItem("c")]);
    var canvas = Realize(list);
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;

    canvas.RaiseMouseDown(250, 200);
    canvas.RaiseMouseMove(5, 5);
    canvas.RaiseMouseUp(5, 5);

    Assert.Multiple(() => {
      Assert.That(raised, Is.Zero);
      Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 1, 2 }), "the rubber band still sweeps");
    });
  }

  [Test]
  public void Given_press_on_the_header_when_the_pointer_travels_then_no_ItemDrag() {
    var list = MakeDetails(out var canvas);
    var raised = 0;
    var columnClicks = 0;
    list.ItemDrag += (_, _) => ++raised;
    list.ColumnClick += (_, _) => ++columnClicks;

    canvas.RaiseMouseDown(10, 2);
    canvas.RaiseMouseMove(10, 60);
    canvas.RaiseMouseUp(10, 60);

    Assert.Multiple(() => {
      Assert.That(raised, Is.Zero);
      Assert.That(columnClicks, Is.EqualTo(1), "the header click behaves as before");
    });
  }

  [Test]
  public void Given_press_on_a_check_glyph_when_the_pointer_travels_then_no_ItemDrag_and_the_check_flips() {
    var list = MakeDetails(out var canvas);
    list.CheckBoxes = true;
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;
    var bounds = list.GetItemBounds(1);

    canvas.RaiseMouseDown(bounds.X + 4, bounds.Y + 4);
    canvas.RaiseMouseMove(bounds.X + 40, bounds.Y + 4);
    canvas.RaiseMouseUp(bounds.X + 40, bounds.Y + 4);

    Assert.Multiple(() => {
      Assert.That(raised, Is.Zero);
      Assert.That(list.Items[1].Checked, Is.True);
    });
  }

  [Test]
  public void Given_press_on_the_scroll_bar_when_the_pointer_travels_then_no_ItemDrag_and_the_list_scrolls() {
    var list = MakeView(ListViewView.Details, 100, out var canvas);
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;
    var first = list.GetItemBounds(0);

    canvas.RaiseMouseDown(300 - 7, first.Y + 2); // on the thumb at the top of the track
    canvas.RaiseMouseMove(300 - 7, first.Y + 120);
    canvas.RaiseMouseUp(300 - 7, first.Y + 120);

    Assert.Multiple(() => {
      Assert.That(raised, Is.Zero);
      Assert.That(list.TopIndex, Is.GreaterThan(0), "the thumb drag scrolled");
    });
  }

  [Test]
  public void Given_right_button_press_on_an_item_when_the_pointer_travels_then_no_ItemDrag() {
    var list = MakeDetails(out var canvas);
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;
    var p = PressPointOf(list, 0);

    canvas.RaiseMouseDown(p.X, p.Y, MouseButtons.Right);
    canvas.RaiseMouseMove(p.X + 20, p.Y);
    canvas.RaiseMouseUp(p.X + 20, p.Y, MouseButtons.Right);

    Assert.That(raised, Is.Zero);
  }

  [Test]
  public void Given_press_on_an_item_when_the_pointer_leaves_before_the_threshold_then_a_later_hover_does_not_drag() {
    var list = MakeDetails(out var canvas);
    var raised = 0;
    list.ItemDrag += (_, _) => ++raised;
    var p = PressPointOf(list, 0);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseLeave(); // the release happens elsewhere and never reaches the list
    canvas.RaiseMouseMove(p.X + 20, p.Y);

    Assert.That(raised, Is.Zero);
  }

  [Test]
  public void Given_no_ItemDrag_subscriber_when_an_item_is_dragged_then_the_gesture_is_inert() {
    var list = MakeDetails(out var canvas);
    list.Items[0].Selected = true;
    list.Items[1].Selected = true;
    var p = PressPointOf(list, 1);

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X, p.Y + 30);
    canvas.RaiseMouseUp(p.X, p.Y + 30);

    Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0, 1 }), "neither a band nor a selection change");
  }

  [Test]
  public void Given_virtual_mode_when_a_row_is_dragged_then_ItemDrag_carries_the_retrieved_item() {
    var list = new ListView { Bounds = new(0, 0, 300, 220), View = ListViewView.List, VirtualMode = true, VirtualListSize = 3 };
    var served = new ListViewItem[3];
    list.RetrieveVirtualItem += (_, e) => e.Item = served[e.ItemIndex] ??= new ListViewItem("Row" + e.ItemIndex);
    var canvas = Realize(list);
    object? dragged = null;
    list.ItemDrag += (_, e) => dragged = e.Item;
    var p = CenterOf(list.GetItemBounds(2));

    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X, p.Y - 10);
    canvas.RaiseMouseUp(p.X, p.Y - 10);

    Assert.That(dragged, Is.SameAs(served[2]));
  }

  // --- ItemDrag → DoDragDrop → tree drop -----------------------------------------------------

  [Test]
  public void Given_ItemDrag_starts_DoDragDrop_when_released_over_a_tree_then_the_drop_resolves_the_node_under_the_pointer() {
    var backend = new HeadlessBackend();
    var form = new Form { Bounds = new(0, 0, 600, 300) };
    var list = new ListView { Bounds = new(0, 0, 250, 200), View = ListViewView.List };
    list.Items.AddRange([new ListViewItem("a"), new ListViewItem("b"), new ListViewItem("c")]);
    var tree = new TreeView { Bounds = new(300, 0, 250, 200), AllowDrop = true };
    tree.Nodes.Add("inbox");
    tree.Nodes.Add("archive");
    form.Controls.Add(list);
    form.Controls.Add(tree);
    form.RealizeWindow(backend);
    ((HeadlessPeer)list.Peer!).ScreenOrigin = new Point(1000, 500);
    ((HeadlessPeer)tree.Peer!).ScreenOrigin = new Point(1300, 500);
    var canvas = (HeadlessCanvasPeer)list.Peer!;

    list.ItemDrag += (_, e) => list.DoDragDrop(e.Item!, DragDropEffects.Move);
    TreeNode? over = null;
    TreeNode? droppedOn = null;
    object? payload = null;
    tree.DragEnter += (_, e) => e.Effect = DragDropEffects.Move;
    tree.DragOver += (_, e) => over = tree.GetNodeAt(ScreenToClient(tree, e.X, e.Y));
    tree.DragDrop += (_, e) => {
      droppedOn = tree.GetNodeAt(ScreenToClient(tree, e.X, e.Y));
      payload = e.Data;
    };

    var h = tree.ItemHeight;
    var p = CenterOf(list.GetItemBounds(1));
    canvas.RaiseMouseDown(p.X, p.Y);
    canvas.RaiseMouseMove(p.X + 10, p.Y);           // crosses the threshold: ItemDrag → DoDragDrop
    canvas.RaiseMouseMove(300 + 10, 2);             // list-client point over the tree's first row
    canvas.RaiseMouseMove(300 + 10, h + 2);         // over the second row
    var overAtSecondRow = over;
    canvas.RaiseMouseUp(300 + 10, h + 2);

    Assert.Multiple(() => {
      Assert.That(overAtSecondRow?.Text, Is.EqualTo("archive"));
      Assert.That(droppedOn?.Text, Is.EqualTo("archive"));
      Assert.That(payload, Is.SameAs(list.Items[1]));
      Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 1 }), "the press selected b; the drag changed nothing more");
    });

    // The gesture is over: the next plain click on the list is a click again.
    canvas.RaiseMouseDown(p.X, CenterOf(list.GetItemBounds(0)).Y);
    canvas.RaiseMouseUp(p.X, CenterOf(list.GetItemBounds(0)).Y);
    Assert.That(list.SelectedIndices, Is.EqualTo(new[] { 0 }));
  }

  /// <summary>
  /// Maps a <see cref="DragEventArgs"/> screen point into <paramref name="control"/>'s client space.
  /// <see cref="Control"/> exposes <see cref="Control.PointToScreen"/> only, so the inverse is the
  /// offset of the client origin on screen.
  /// </summary>
  private static Point ScreenToClient(Control control, int x, int y) {
    var origin = control.PointToScreen(Point.Empty);
    return new(x - origin.X, y - origin.Y);
  }
}
