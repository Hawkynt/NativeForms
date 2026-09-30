using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The effect translation of the GTK file drag (PRD §8). <c>GDK_ACTION_COPY</c>, <c>_MOVE</c> and
/// <c>_LINK</c> are 2, 4 and 8; <c>DEFAULT</c> (1), <c>PRIVATE</c> (16) and <c>ASK</c> (32) carry no
/// toolkit effect. Pure arithmetic, so it runs on every runner.
/// </summary>
[TestFixture]
internal sealed class GtkFileDragSourceTests {
  [TestCase(DragDropEffects.None, 0)]
  [TestCase(DragDropEffects.Copy, 2)]
  [TestCase(DragDropEffects.Move, 4)]
  [TestCase(DragDropEffects.Link, 8)]
  [TestCase(DragDropEffects.All, 14)]
  [TestCase(DragDropEffects.Copy | (DragDropEffects)0x100, 2)]
  public void Toolkit_effects_map_onto_GDK_actions(DragDropEffects effects, int expected)
      => Assert.That(GtkFileDragSource.ToActions(effects), Is.EqualTo(expected));

  [TestCase(0, DragDropEffects.None)]
  [TestCase(1, DragDropEffects.None)]
  [TestCase(2, DragDropEffects.Copy)]
  [TestCase(4, DragDropEffects.Move)]
  [TestCase(8, DragDropEffects.Link)]
  [TestCase(16, DragDropEffects.None)]
  [TestCase(32, DragDropEffects.None)]
  [TestCase(2 | 32, DragDropEffects.Copy)]
  public void GDK_actions_map_back_to_toolkit_effects(int actions, DragDropEffects expected)
      => Assert.That(GtkFileDragSource.ToEffects(actions), Is.EqualTo(expected));
}
