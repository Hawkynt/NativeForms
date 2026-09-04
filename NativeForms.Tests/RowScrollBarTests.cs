using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using NUnit.Framework;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The bar an owner-drawn list paints down its own edge.
/// </summary>
/// <remarks>
/// Written because both owner-drawn lists scrolled perfectly well and showed nothing to say so: the
/// wheel worked, the keys worked, and a list of sixty columns in a box that fits twenty looked like a
/// list of twenty. A scrollbar is not decoration — it is the only thing on screen that says there is
/// more.
/// </remarks>
[TestFixture]
public sealed class RowScrollBarTests {
  private static readonly ITheme _Theme = new DefaultTheme();

  [Test]
  public void A_list_that_fits_gets_no_bar() {
    Assert.That(RowScrollBar.IsNeeded(10, 10), Is.False);
    Assert.That(RowScrollBar.WidthOf(_Theme, 10, 10), Is.Zero);
  }

  [Test]
  public void A_list_that_does_not_fit_gets_one_and_it_costs_the_rows_its_width() {
    Assert.That(RowScrollBar.IsNeeded(11, 10), Is.True);
    Assert.That(RowScrollBar.WidthOf(_Theme, 11, 10), Is.EqualTo(_Theme.ScrollBarSize));
  }

  /// <summary>The bar starts below the header, or it would paint over a column caption.</summary>
  [Test]
  public void The_strip_hugs_the_right_edge_below_whatever_it_is_told_to_clear() {
    var strip = RowScrollBar.StripOf(_Theme, 300, 20, 200);

    Assert.That(strip.Right, Is.EqualTo(300));
    Assert.That(strip.Width, Is.EqualTo(_Theme.ScrollBarSize));
    Assert.That(strip.Y, Is.EqualTo(20));
    Assert.That(strip.Bottom, Is.EqualTo(200));
  }

  [Test]
  public void A_press_beside_the_bar_is_not_a_press_on_it() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 200);

    Assert.That(bar.MouseDown(_Theme, strip, 100, 10, 0, new Point(4, 40)), Is.EqualTo(-1));
    Assert.That(bar.IsDragging, Is.False);
  }

  [Test]
  public void A_press_on_a_list_that_does_not_scroll_is_not_a_press_on_it_either() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 200);

    Assert.That(bar.MouseDown(_Theme, strip, 5, 10, 0, new Point(295, 100)), Is.EqualTo(-1));
  }

  [Test]
  public void The_arrows_step_one_row_each_way() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 200);

    Assert.That(bar.MouseDown(_Theme, strip, 100, 10, 20, new Point(295, strip.Y + 2)), Is.EqualTo(19));
    Assert.That(bar.MouseDown(_Theme, strip, 100, 10, 20, new Point(295, strip.Bottom - 2)), Is.EqualTo(21));
  }

  /// <summary>A click in the trough moves a page, which for a list is a screenful of rows.</summary>
  [Test]
  public void The_channel_pages() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 400);
    var thumb = ScrollBarRenderer.ThumbRect(strip, vertical: true, 0, 99, 50, 10);

    Assert.That(bar.MouseDown(_Theme, strip, 100, 10, 50, new Point(295, thumb.Y - 4)), Is.EqualTo(40));
    Assert.That(bar.MouseDown(_Theme, strip, 100, 10, 50, new Point(295, thumb.Bottom + 4)), Is.EqualTo(60));
  }

  [Test]
  public void Grabbing_the_thumb_arms_a_drag_and_releasing_ends_it() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 400);
    var thumb = ScrollBarRenderer.ThumbRect(strip, vertical: true, 0, 99, 50, 10);

    Assert.That(bar.MouseDown(_Theme, strip, 100, 10, 50, new Point(295, thumb.Y + 2)), Is.EqualTo(50), "the grab itself moves nothing");
    Assert.That(bar.IsDragging, Is.True);

    bar.Release();
    Assert.That(bar.IsDragging, Is.False);
  }

  /// <summary>
  /// The grab offset is what stops the thumb jumping under the pointer: taking hold of it low down
  /// and moving nowhere must land on the row it was already on.
  /// </summary>
  [Test]
  public void Dragging_nowhere_scrolls_nowhere() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 400);
    var thumb = ScrollBarRenderer.ThumbRect(strip, vertical: true, 0, 99, 50, 10);
    var grabbedAt = thumb.Bottom - 2;

    bar.MouseDown(_Theme, strip, 100, 10, 50, new Point(295, grabbedAt));

    Assert.That(bar.Drag(strip, 100, 10, grabbedAt), Is.EqualTo(50).Within(1));
  }

  [Test]
  public void Dragging_to_the_bottom_lands_on_the_last_page_rather_than_the_last_row() {
    var bar = new RowScrollBar();
    var strip = RowScrollBar.StripOf(_Theme, 300, 0, 400);
    var thumb = ScrollBarRenderer.ThumbRect(strip, vertical: true, 0, 99, 0, 10);

    bar.MouseDown(_Theme, strip, 100, 10, 0, new Point(295, thumb.Y + 1));

    // A hundred rows, ten showing: the furthest useful top row is ninety, not ninety-nine.
    Assert.That(bar.Drag(strip, 100, 10, strip.Bottom + 500), Is.EqualTo(90));
  }
}
