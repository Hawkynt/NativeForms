using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="NavigationView"/> lists icon+caption items, selects the first added, moves the selection
/// on a click or arrow key (raising <see cref="NavigationView.SelectedIndexChanged"/>), and toggles the
/// icons-only <see cref="NavigationView.Collapsed"/> state from its hamburger button.
/// </summary>
[TestFixture]
internal sealed class NavigationViewTests {
  private static NavigationView Create(out HeadlessCanvasPeer canvas) {
    var nav = new NavigationView { Bounds = new(0, 0, 200, 300) };
    nav.AddItem("Home");
    nav.AddItem("Files");
    nav.AddItem("Settings");
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(nav);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
    return nav;
  }

  [Test]
  public void The_first_added_item_is_selected() {
    var nav = Create(out _);
    Assert.That(nav.SelectedIndex, Is.EqualTo(0));
  }

  [Test]
  public void Clicking_a_row_selects_it_and_raises_the_change() {
    var nav = Create(out var canvas);
    var changes = 0;
    nav.SelectedIndexChanged += (_, _) => ++changes;

    canvas.RaiseMouseDown(100, 34 + 34 + 17); // the second item row (rows start below the 34-px hamburger)

    Assert.Multiple(() => {
      Assert.That(nav.SelectedIndex, Is.EqualTo(1));
      Assert.That(changes, Is.EqualTo(1));
    });
  }

  [Test]
  public void The_hamburger_toggles_the_collapsed_state() {
    var nav = Create(out var canvas);
    var toggles = 0;
    nav.CollapsedChanged += (_, _) => ++toggles;

    canvas.RaiseMouseDown(100, 17); // the hamburger row at the top

    Assert.Multiple(() => {
      Assert.That(nav.Collapsed, Is.True);
      Assert.That(toggles, Is.EqualTo(1));
      Assert.That(nav.PreferredWidth, Is.EqualTo(44), "collapsed reports the icons-only width");
    });
  }

  [Test]
  public void Collapsing_narrows_the_rail_and_expanding_restores_its_width() {
    var nav = Create(out var canvas);
    Assert.That(nav.Width, Is.EqualTo(200));

    canvas.RaiseMouseDown(100, 17); // hamburger → collapse
    Assert.That(nav.Width, Is.EqualTo(44), "collapsed to the icons-only strip");

    canvas.RaiseMouseDown(20, 17); // hamburger (now inside the 44-px strip) → expand
    Assert.That(nav.Width, Is.EqualTo(200), "expanding restores the previous width");
  }

  [Test]
  public void The_selected_row_carries_an_accent_stripe() {
    var nav = Create(out var canvas);
    nav.SelectedIndex = 1;

    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4 0,68,3,34")), Is.True, "the second row's accent stripe");
  }
}
