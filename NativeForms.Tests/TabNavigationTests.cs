using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Moving focus with the keyboard: Tab through the tab order, and Ctrl+Tab between the pages of a
/// <see cref="TabControl"/> from wherever focus happens to be inside it — the Windows Forms shape.
/// </summary>
[TestFixture]
internal sealed class TabNavigationTests {
  private static (Form Form, HeadlessBackend Backend) Shown(params Control[] controls) {
    var backend = new HeadlessBackend();
    var form = new Form { ClientSize = new(400, 300) };
    form.Controls.AddRange(controls);
    Application.Run(form, backend);
    return (form, backend);
  }

  /// <summary>An owner-drawn, focusable control — its peer is a canvas, which is where keys arrive.</summary>
  private static CheckBox Focusable() => new() { Bounds = new(10, 10, 100, 22), Text = "focusable" };

  private static void Press(Control target, Keys key, KeyModifiers modifiers)
      => ((HeadlessCanvasPeer)target.Peer!).RaiseKeyDown(key, modifiers);

  // --- Ctrl+Tab from inside a page ----------------------------------------------------------------

  [Test]
  public void Ctrl_Tab_from_a_control_inside_a_page_advances_to_the_next_page() {
    var tabs = new TabControl { Bounds = new(0, 0, 300, 200) };
    var first = new TabPage("one");
    var box = Focusable();
    first.Controls.Add(box);
    tabs.TabPages.Add(first);
    tabs.TabPages.Add(new TabPage("two"));
    Shown(tabs);
    box.Focus();

    Press(box, Keys.Tab, KeyModifiers.Control);

    Assert.That(tabs.SelectedIndex, Is.EqualTo(1), "Ctrl+Tab anywhere inside the pages switches page");
  }

  [Test]
  public void Ctrl_Shift_Tab_from_inside_a_page_goes_back() {
    var tabs = new TabControl { Bounds = new(0, 0, 300, 200) };
    tabs.TabPages.Add(new TabPage("one"));
    var second = new TabPage("two");
    var box = Focusable();
    second.Controls.Add(box);
    tabs.TabPages.Add(second);
    Shown(tabs);
    tabs.SelectedIndex = 1;
    box.Focus();

    Press(box, Keys.Tab, KeyModifiers.Control | KeyModifiers.Shift);

    Assert.That(tabs.SelectedIndex, Is.Zero);
  }

  [Test]
  public void Ctrl_Tab_wraps_round_the_pages() {
    var tabs = new TabControl { Bounds = new(0, 0, 300, 200) };
    tabs.TabPages.Add(new TabPage("one"));
    var last = new TabPage("two");
    var box = Focusable();
    last.Controls.Add(box);
    tabs.TabPages.Add(last);
    Shown(tabs);
    tabs.SelectedIndex = 1;
    box.Focus();

    Press(box, Keys.Tab, KeyModifiers.Control);

    Assert.That(tabs.SelectedIndex, Is.Zero);
  }

  [Test]
  public void Ctrl_Tab_outside_any_tab_control_does_nothing() {
    var box = Focusable();
    var other = new CheckBox { Bounds = new(10, 40, 100, 22) };
    Shown(box, other);
    box.Focus();

    Press(box, Keys.Tab, KeyModifiers.Control);

    Assert.That(box.Focused, Is.True, "there is no page to switch, and focus must not move either");
  }

  [Test]
  public void Ctrl_Tab_reaches_the_innermost_tab_control_when_they_are_nested() {
    var outer = new TabControl { Bounds = new(0, 0, 380, 260) };
    var outerPage = new TabPage("outer one");
    var inner = new TabControl { Bounds = new(0, 0, 300, 200) };
    var innerPage = new TabPage("inner one");
    var box = Focusable();
    innerPage.Controls.Add(box);
    inner.TabPages.Add(innerPage);
    inner.TabPages.Add(new TabPage("inner two"));
    outerPage.Controls.Add(inner);
    outer.TabPages.Add(outerPage);
    outer.TabPages.Add(new TabPage("outer two"));
    Shown(outer);
    box.Focus();

    Press(box, Keys.Tab, KeyModifiers.Control);

    Assert.Multiple(() => {
      Assert.That(inner.SelectedIndex, Is.EqualTo(1), "the nearest tab control takes it");
      Assert.That(outer.SelectedIndex, Is.Zero, "and the outer one is left alone");
    });
  }

  // --- SelectNextControl --------------------------------------------------------------------------

  [Test]
  public void SelectNextControl_moves_forward_through_the_tab_order() {
    var first = new CheckBox { Bounds = new(0, 0, 100, 22), TabIndex = 0 };
    var second = new CheckBox { Bounds = new(0, 30, 100, 22), TabIndex = 1 };
    var (form, _) = Shown(first, second);

    var moved = form.SelectNextControl(first, forward: true, tabStopOnly: true, nested: true, wrap: true);

    Assert.Multiple(() => {
      Assert.That(moved, Is.True);
      Assert.That(second.Focused, Is.True);
    });
  }

  [Test]
  public void SelectNextControl_moves_backward_too() {
    var first = new CheckBox { Bounds = new(0, 0, 100, 22), TabIndex = 0 };
    var second = new CheckBox { Bounds = new(0, 30, 100, 22), TabIndex = 1 };
    var (form, _) = Shown(first, second);

    form.SelectNextControl(second, forward: false, tabStopOnly: true, nested: true, wrap: true);

    Assert.That(first.Focused, Is.True);
  }

  [Test]
  public void SelectNextControl_from_null_starts_at_the_first_tab_stop() {
    var first = new CheckBox { Bounds = new(0, 0, 100, 22), TabIndex = 0 };
    var second = new CheckBox { Bounds = new(0, 30, 100, 22), TabIndex = 1 };
    var (form, _) = Shown(first, second);

    form.SelectNextControl(null, forward: true, tabStopOnly: true, nested: true, wrap: true);

    Assert.That(first.Focused, Is.True);
  }

  [Test]
  public void SelectNextControl_reports_false_when_there_is_nowhere_to_go() {
    var only = new Label { Bounds = new(0, 0, 100, 22), Text = "not a tab stop" };
    var (form, _) = Shown(only);

    Assert.That(form.SelectNextControl(null, true, true, true, true), Is.False);
  }
}
