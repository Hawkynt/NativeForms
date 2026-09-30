using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The continuous operating-system drop protocol (PRD §8): a backend whose platform reports the whole
/// hover (OLE's <c>IDropTarget</c>) calls <see cref="ExternalDropBridge.DragEnter"/>,
/// <see cref="ExternalDropBridge.DragOver"/>, and ends with <see cref="ExternalDropBridge.DragLeave"/>
/// or <see cref="ExternalDropBridge.Drop"/>. Managed targets see the same enter/over/leave/drop
/// sequence an in-process drag raises. Two targets side by side: left (0,0,50,50) and right (100,0,50,50).
/// </summary>
[TestFixture]
internal sealed class ExternalDragHoverTests {
  private sealed class TestSurface : OwnerDrawnControl;

  private static (IWindowPeer Window, TestSurface Left, TestSurface Right, List<string> Events) CreateScene(
      DragDropEffects leftAnswer = DragDropEffects.Copy,
      DragDropEffects rightAnswer = DragDropEffects.Copy) {
    var backend = new HeadlessBackend();
    var form = new Form { Bounds = new(0, 0, 300, 200) };
    var left = new TestSurface { Bounds = new(0, 0, 50, 50), AllowDrop = true };
    var right = new TestSurface { Bounds = new(100, 0, 50, 50), AllowDrop = true };
    form.Controls.Add(left);
    form.Controls.Add(right);
    var window = form.RealizeWindow(backend);
    ((HeadlessPeer)left.Peer!).ScreenOrigin = new Point(0, 0);
    ((HeadlessPeer)right.Peer!).ScreenOrigin = new Point(100, 0);

    var events = new List<string>();
    Wire(left, "left", leftAnswer);
    Wire(right, "right", rightAnswer);
    return (window, left, right, events);

    void Wire(TestSurface target, string name, DragDropEffects answer) {
      target.DragEnter += (_, e) => {
        events.Add(name + " enter");
        e.Effect = answer;
      };
      target.DragOver += (_, e) => events.Add($"{name} over {e.X},{e.Y} {e.Effect}");
      target.DragLeave += (_, _) => events.Add(name + " leave");
      target.DragDrop += (_, e) => events.Add($"{name} drop {e.Effect}");
    }
  }

  [Test]
  public void Given_a_drag_entering_over_a_target_when_it_moves_then_the_target_sees_enter_then_over_with_its_earlier_answer() {
    var (window, _, _, events) = CreateScene();

    var entered = ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.Copy, new Point(10, 10));
    var moved = ExternalDropBridge.DragOver(window, DragDropEffects.Copy, new Point(12, 11));

    Assert.Multiple(() => {
      Assert.That(entered, Is.EqualTo(DragDropEffects.Copy));
      Assert.That(moved, Is.EqualTo(DragDropEffects.Copy));
      Assert.That(events, Is.EqualTo(new[] { "left enter", "left over 12,11 Copy" }));
    });
  }

  [Test]
  public void Given_a_hover_when_the_pointer_crosses_to_another_target_then_the_first_is_left_and_the_second_entered() {
    var (window, _, _, events) = CreateScene();

    ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.Copy, new Point(10, 10));
    ExternalDropBridge.DragOver(window, DragDropEffects.Copy, new Point(75, 10)); // between the targets
    ExternalDropBridge.DragOver(window, DragDropEffects.Copy, new Point(110, 10));

    Assert.That(events, Is.EqualTo(new[] { "left enter", "left leave", "right enter" }));
  }

  [Test]
  public void Given_a_hover_over_nothing_when_moved_then_the_effect_is_None() {
    var (window, _, _, events) = CreateScene();

    var effect = ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.Copy, new Point(250, 150));

    Assert.Multiple(() => {
      Assert.That(effect, Is.EqualTo(DragDropEffects.None));
      Assert.That(events, Is.Empty);
    });
  }

  [Test]
  public void Given_a_hover_when_the_drag_leaves_the_window_then_the_target_is_left_and_a_later_drop_does_nothing() {
    var (window, _, _, events) = CreateScene();

    ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.Copy, new Point(10, 10));
    ExternalDropBridge.DragLeave(window);
    var dropped = ExternalDropBridge.Drop(window, DragDropEffects.Copy, new Point(10, 10));

    Assert.Multiple(() => {
      Assert.That(dropped, Is.EqualTo(DragDropEffects.None));
      Assert.That(events, Is.EqualTo(new[] { "left enter", "left leave" }));
    });
  }

  [Test]
  public void Given_an_accepting_target_when_dropped_then_it_gets_the_payload_from_enter_and_the_effect() {
    var (window, left, _, events) = CreateScene();
    var payload = new[] { "a", "b" };
    object? data = null;
    left.DragDrop += (_, e) => data = e.Data;

    ExternalDropBridge.DragEnter(window, payload, DragDropEffects.Copy | DragDropEffects.Move, new Point(10, 10));
    var effect = ExternalDropBridge.Drop(window, DragDropEffects.Copy | DragDropEffects.Move, new Point(10, 10));

    Assert.Multiple(() => {
      Assert.That(effect, Is.EqualTo(DragDropEffects.Copy));
      Assert.That(data, Is.SameAs(payload));
      Assert.That(events, Is.EqualTo(new[] { "left enter", "left drop Copy" }));
    });
  }

  [Test]
  public void Given_a_refusing_target_when_dropped_then_it_is_left_without_a_drop() {
    var (window, _, _, events) = CreateScene(leftAnswer: DragDropEffects.None);

    ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.Copy, new Point(10, 10));
    var effect = ExternalDropBridge.Drop(window, DragDropEffects.Copy, new Point(10, 10));

    Assert.Multiple(() => {
      Assert.That(effect, Is.EqualTo(DragDropEffects.None));
      Assert.That(events, Is.EqualTo(new[] { "left enter", "left leave" }));
    });
  }

  [Test]
  public void Given_an_answer_outside_the_allowed_effects_when_hovering_then_it_is_filtered() {
    var (window, _, _, _) = CreateScene(leftAnswer: DragDropEffects.Move);

    var effect = ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.Copy, new Point(10, 10));

    Assert.That(effect, Is.EqualTo(DragDropEffects.None));
  }

  [Test]
  public void Given_a_drop_somewhere_else_than_the_last_hover_when_released_then_the_target_under_the_drop_decides() {
    var (window, _, _, events) = CreateScene(rightAnswer: DragDropEffects.Link);

    ExternalDropBridge.DragEnter(window, new[] { "a" }, DragDropEffects.All, new Point(10, 10));
    var effect = ExternalDropBridge.Drop(window, DragDropEffects.All, new Point(110, 10));

    Assert.Multiple(() => {
      Assert.That(effect, Is.EqualTo(DragDropEffects.Link));
      Assert.That(events, Is.EqualTo(new[] { "left enter", "left leave", "right enter", "right drop Link" }));
    });
  }

  [Test]
  public void Given_a_hover_in_flight_when_a_new_drag_enters_then_the_stale_target_is_left_first() {
    var (window, _, _, events) = CreateScene();

    ExternalDropBridge.DragEnter(window, new[] { "first" }, DragDropEffects.Copy, new Point(10, 10));
    ExternalDropBridge.DragEnter(window, new[] { "second" }, DragDropEffects.Copy, new Point(110, 10));

    Assert.That(events, Is.EqualTo(new[] { "left enter", "left leave", "right enter" }));
  }

  [Test]
  public void Given_a_window_the_bridge_does_not_know_when_a_drag_arrives_then_nothing_happens() {
    var stranger = new HeadlessWindowPeer();

    Assert.Multiple(() => {
      Assert.That(ExternalDropBridge.DragEnter(stranger, new[] { "a" }, DragDropEffects.Copy, Point.Empty), Is.EqualTo(DragDropEffects.None));
      Assert.That(ExternalDropBridge.DragOver(stranger, DragDropEffects.Copy, Point.Empty), Is.EqualTo(DragDropEffects.None));
      Assert.That(() => ExternalDropBridge.DragLeave(stranger), Throws.Nothing);
      Assert.That(ExternalDropBridge.Drop(stranger, DragDropEffects.Copy, Point.Empty), Is.EqualTo(DragDropEffects.None));
    });
  }

  [Test]
  public void Given_a_null_payload_when_a_drag_enters_then_it_is_rejected() {
    var (window, _, _, _) = CreateScene();

    Assert.That(() => ExternalDropBridge.DragEnter(window, null!, DragDropEffects.Copy, Point.Empty), Throws.ArgumentNullException);
  }
}
