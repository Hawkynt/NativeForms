using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Drag-to-reorder on <see cref="Accordion"/> (PRD §14) — the sidebar half of the gesture a port had to
/// give up for a context menu, built to the same rules as <see cref="ToolStrip"/>'s: the threshold that
/// keeps a click a click, the section walking to the header under the pointer, the drag not also
/// toggling the section it moved, and the open section staying open where it landed.
/// </summary>
[TestFixture]
internal sealed class AccordionReorderTests {
  private static Accordion ThreePanes(out HeadlessCanvasPeer canvas, bool reorderable = true) {
    var accordion = new Accordion { Bounds = new(0, 0, 200, 300), AllowUserToOrderPanes = reorderable };
    accordion.Panes.AddRange(new AccordionPane("Mail"), new AccordionPane("Calendar"), new AccordionPane("Contacts"));

    var backend = new HeadlessBackend();
    var form = new Form { Bounds = new(0, 0, 400, 400) };
    form.Controls.Add(accordion);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().First();
    return accordion;
  }

  private static string[] Order(Accordion accordion) => [.. accordion.Panes.Select(static pane => pane.Text)];

  /// <summary>The y-coordinate at the middle of the header currently at the given index.</summary>
  private static int MiddleOf(Accordion accordion, int index) {
    var bounds = accordion.GetHeaderBounds(index);
    return bounds.Y + (bounds.Height / 2);
  }

  [Test]
  public void A_drag_onto_another_header_moves_the_section() {
    var accordion = ThreePanes(out var canvas);

    canvas.RaiseMouseDown(10, MiddleOf(accordion, 0));
    canvas.RaiseMouseMove(10, MiddleOf(accordion, 1));
    canvas.RaiseMouseUp(10, MiddleOf(accordion, 1));

    Assert.That(Order(accordion), Is.EqualTo(new[] { "Calendar", "Mail", "Contacts" }));
  }

  [Test]
  public void A_press_that_barely_moves_stays_a_click() {
    var accordion = ThreePanes(out var canvas);
    var start = MiddleOf(accordion, 1);

    canvas.RaiseMouseDown(10, start);
    canvas.RaiseMouseMove(10, start + 2);
    canvas.RaiseMouseUp(10, start);

    Assert.Multiple(() => {
      Assert.That(Order(accordion), Is.EqualTo(new[] { "Mail", "Calendar", "Contacts" }));
      Assert.That(accordion.SelectedIndex, Is.EqualTo(1), "under the threshold the gesture is still a click");
    });
  }

  [Test]
  public void A_drag_does_not_also_toggle_the_section_it_moved() {
    var accordion = ThreePanes(out var canvas);
    var closed = accordion.Panes[1];

    canvas.RaiseMouseDown(10, MiddleOf(accordion, 1));
    canvas.RaiseMouseMove(10, MiddleOf(accordion, 0));
    canvas.RaiseMouseUp(10, MiddleOf(accordion, 0));

    Assert.Multiple(() => {
      Assert.That(Order(accordion), Is.EqualTo(new[] { "Calendar", "Mail", "Contacts" }));
      Assert.That(closed.IsExpanded, Is.False, "the section arrived under the pointer by being dragged there");
    });
  }

  [Test]
  public void Reordering_reports_where_the_section_landed() {
    var accordion = ThreePanes(out var canvas);
    var landed = -1;
    accordion.PaneOrderChanged += (_, index) => landed = index;

    canvas.RaiseMouseDown(10, MiddleOf(accordion, 0));
    canvas.RaiseMouseMove(10, MiddleOf(accordion, 1));

    Assert.That(landed, Is.EqualTo(1));
  }

  [Test]
  public void An_accordion_that_does_not_allow_it_keeps_its_order() {
    var accordion = ThreePanes(out var canvas, reorderable: false);

    canvas.RaiseMouseDown(10, MiddleOf(accordion, 0));
    canvas.RaiseMouseMove(10, MiddleOf(accordion, 2));
    canvas.RaiseMouseUp(10, MiddleOf(accordion, 2));

    Assert.That(Order(accordion), Is.EqualTo(new[] { "Mail", "Calendar", "Contacts" }));
  }

  [Test]
  public void The_open_section_stays_open_where_it_landed() {
    var accordion = ThreePanes(out var canvas);
    var open = accordion.Panes[0];

    canvas.RaiseMouseDown(10, MiddleOf(accordion, 0));
    canvas.RaiseMouseMove(10, MiddleOf(accordion, 1));
    canvas.RaiseMouseUp(10, MiddleOf(accordion, 1));

    Assert.Multiple(() => {
      Assert.That(open.IsExpanded, Is.True);
      Assert.That(accordion.SelectedIndex, Is.EqualTo(1), "the selection follows the pane, not the position it left");
      Assert.That(accordion.SelectedPane, Is.SameAs(open));
    });
  }

  /// <summary>
  /// The gesture and the program reach the same writer: what a drag does is what an application does
  /// when it restores a saved order, so a section moved either way lands in the same place with the
  /// same selection.
  /// </summary>
  [Test]
  public void Moving_a_section_through_the_collection_takes_the_selection_with_it() {
    var accordion = ThreePanes(out _);
    var open = accordion.Panes[0];

    accordion.Panes.Move(0, 2);

    Assert.Multiple(() => {
      Assert.That(Order(accordion), Is.EqualTo(new[] { "Calendar", "Contacts", "Mail" }));
      Assert.That(accordion.SelectedIndex, Is.EqualTo(2));
      Assert.That(accordion.SelectedPane, Is.SameAs(open));
      Assert.That(open.IsExpanded, Is.True);
    });
  }

  /// <summary>A moved section keeps its children — the reorder touches the list, not the peer tree.</summary>
  [Test]
  public void A_moved_section_keeps_its_children() {
    var accordion = ThreePanes(out _);
    var pane = accordion.Panes[2];
    var child = new Button { Bounds = new(0, 0, 60, 20), Text = "Go" };
    pane.Controls.Add(child);

    accordion.Panes.Move(2, 0);

    Assert.Multiple(() => {
      Assert.That(pane.Controls, Does.Contain(child));
      Assert.That(child.Parent, Is.SameAs(pane));
      Assert.That(accordion.Panes[0], Is.SameAs(pane));
    });
  }

  [Test]
  public void Moving_outside_the_stack_is_refused() {
    var accordion = ThreePanes(out _);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => accordion.Panes.Move(0, 3));
      Assert.Throws<ArgumentOutOfRangeException>(() => accordion.Panes.Move(-1, 0));
    });
  }
}
