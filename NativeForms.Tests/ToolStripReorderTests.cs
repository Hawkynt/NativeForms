using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Drag-to-reorder on <see cref="ToolStrip"/> (PRD §14): the gesture a port had to give up for a
/// context menu. The threshold that keeps a click a click, the item walking along as the pointer
/// crosses its neighbours, and the drag not also firing the button it left under the cursor.
/// </summary>
[TestFixture]
internal sealed class ToolStripReorderTests {
  private static ToolStrip MakeStrip(out HeadlessCanvasPeer canvas, bool reorderable = true) {
    var strip = new ToolStrip { Bounds = new(0, 0, 400, 28), AllowUserToOrderItems = reorderable };
    strip.Items.AddRange(new ToolStripButton("One"), new ToolStripButton("Two"), new ToolStripButton("Three"));

    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(strip);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
    return strip;
  }

  private static string[] Order(ToolStrip strip) => [.. strip.Items.Select(static item => item.DisplayText)];

  /// <summary>The x-coordinate at the middle of the item currently at the given index.</summary>
  private static int MiddleOf(ToolStrip strip, int index) {
    var x = 0;
    for (var i = 0; i < index; ++i)
      x += strip.MeasureItemWidth(i);

    return x + (strip.MeasureItemWidth(index) / 2);
  }

  [Test]
  public void A_drag_past_a_neighbour_moves_the_item() {
    var strip = MakeStrip(out var canvas);

    canvas.RaiseMouseDown(MiddleOf(strip, 0), 14);
    canvas.RaiseMouseMove(MiddleOf(strip, 1), 14);
    canvas.RaiseMouseUp(MiddleOf(strip, 1), 14);

    Assert.That(Order(strip), Is.EqualTo(new[] { "Two", "One", "Three" }));
  }

  [Test]
  public void A_press_that_barely_moves_stays_a_click() {
    var strip = MakeStrip(out var canvas);
    var clicked = 0;
    strip.Items[0].Click += (_, _) => ++clicked;
    var start = MiddleOf(strip, 0);

    canvas.RaiseMouseDown(start, 14);
    canvas.RaiseMouseMove(start + 2, 14);
    canvas.RaiseMouseUp(start, 14);

    Assert.Multiple(() => {
      Assert.That(Order(strip), Is.EqualTo(new[] { "One", "Two", "Three" }));
      Assert.That(clicked, Is.EqualTo(1), "under the threshold the gesture is still a click");
    });
  }

  [Test]
  public void A_drag_does_not_also_click_the_item_it_moved() {
    var strip = MakeStrip(out var canvas);
    var clicked = 0;
    strip.Items[0].Click += (_, _) => ++clicked;

    canvas.RaiseMouseDown(MiddleOf(strip, 0), 14);
    canvas.RaiseMouseMove(MiddleOf(strip, 1), 14);
    canvas.RaiseMouseUp(MiddleOf(strip, 1), 14);

    Assert.That(clicked, Is.Zero, "the button ended up under the pointer by being dragged there");
  }

  [Test]
  public void Reordering_reports_where_the_item_landed() {
    var strip = MakeStrip(out var canvas);
    var landed = -1;
    strip.ItemOrderChanged += (_, index) => landed = index;

    canvas.RaiseMouseDown(MiddleOf(strip, 0), 14);
    canvas.RaiseMouseMove(MiddleOf(strip, 1), 14);

    Assert.That(landed, Is.EqualTo(1));
  }

  [Test]
  public void A_strip_that_does_not_allow_it_keeps_its_order() {
    var strip = MakeStrip(out var canvas, reorderable: false);

    canvas.RaiseMouseDown(MiddleOf(strip, 0), 14);
    canvas.RaiseMouseMove(MiddleOf(strip, 2), 14);
    canvas.RaiseMouseUp(MiddleOf(strip, 2), 14);

    Assert.That(Order(strip), Is.EqualTo(new[] { "One", "Two", "Three" }));
  }

  [Test]
  public void Dragging_all_the_way_along_walks_the_item_to_the_end() {
    var strip = MakeStrip(out var canvas);

    canvas.RaiseMouseDown(MiddleOf(strip, 0), 14);
    canvas.RaiseMouseMove(MiddleOf(strip, 1), 14);
    canvas.RaiseMouseMove(MiddleOf(strip, 2), 14);
    canvas.RaiseMouseUp(MiddleOf(strip, 2), 14);

    Assert.That(Order(strip), Is.EqualTo(new[] { "Two", "Three", "One" }));
  }
}
