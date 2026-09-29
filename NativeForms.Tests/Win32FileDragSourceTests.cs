using Hawkynt.NativeForms.Backends.Windows;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The effect translation of the Win32 shell drag (PRD §8). <c>DROPEFFECT_COPY</c>, <c>_MOVE</c> and
/// <c>_LINK</c> are 1, 2 and 4 — the values <see cref="DragDropEffects"/> shares with Windows Forms —
/// and <c>DROPEFFECT_SCROLL</c> (0x80000000) is feedback, never an effect. Pure arithmetic, so it runs
/// on every runner.
/// </summary>
[TestFixture]
internal sealed class Win32FileDragSourceTests {
  [TestCase(DragDropEffects.None, 0u)]
  [TestCase(DragDropEffects.Copy, 1u)]
  [TestCase(DragDropEffects.Move, 2u)]
  [TestCase(DragDropEffects.Link, 4u)]
  [TestCase(DragDropEffects.All, 7u)]
  [TestCase(DragDropEffects.Copy | (DragDropEffects)0x100, 1u)]
  public void Toolkit_effects_map_onto_the_DROPEFFECT_mask(DragDropEffects effects, uint expected)
      => Assert.That(Win32FileDragSource.ToDropEffect(effects), Is.EqualTo(expected));

  [TestCase(0u, DragDropEffects.None)]
  [TestCase(1u, DragDropEffects.Copy)]
  [TestCase(2u, DragDropEffects.Move)]
  [TestCase(4u, DragDropEffects.Link)]
  [TestCase(0x80000001u, DragDropEffects.Copy)]
  [TestCase(0x80000000u, DragDropEffects.None)]
  [TestCase(0xFFFFFFF8u, DragDropEffects.None)]
  public void A_DROPEFFECT_maps_back_without_scroll_or_unknown_bits(uint dropEffect, DragDropEffects expected)
      => Assert.That(Win32FileDragSource.ToEffects(dropEffect), Is.EqualTo(expected));
}
