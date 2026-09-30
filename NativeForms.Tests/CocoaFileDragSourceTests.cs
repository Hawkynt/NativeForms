using System.Reflection;
using Hawkynt.NativeForms.Backends.MacOS;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The effect translation of the AppKit file drag (PRD §8). <c>NSDragOperationCopy</c>, <c>Link</c>
/// and <c>Move</c> are 1, 2 and 16; <c>Generic</c> (4), <c>Private</c> (8) and <c>Delete</c> (32)
/// carry no toolkit effect. Pure arithmetic, reached by reflection like the other Cocoa internals, so
/// it runs on every runner.
/// </summary>
[TestFixture]
internal sealed class CocoaFileDragSourceTests {
  private static readonly Type _Source = typeof(CocoaBackend).Assembly
      .GetType("Hawkynt.NativeForms.Backends.MacOS.CocoaFileDragSource", throwOnError: true)!;

  private static readonly MethodInfo _ToOperations = _Source.GetMethod("ToOperations", BindingFlags.Static | BindingFlags.NonPublic)!;
  private static readonly MethodInfo _ToEffects = _Source.GetMethod("ToEffects", BindingFlags.Static | BindingFlags.NonPublic)!;

  [TestCase(DragDropEffects.None, 0u)]
  [TestCase(DragDropEffects.Copy, 1u)]
  [TestCase(DragDropEffects.Link, 2u)]
  [TestCase(DragDropEffects.Move, 16u)]
  [TestCase(DragDropEffects.All, 19u)]
  [TestCase(DragDropEffects.Copy | (DragDropEffects)0x100, 1u)]
  public void Toolkit_effects_map_onto_NSDragOperation(DragDropEffects effects, uint expected)
      => Assert.That((nuint)_ToOperations.Invoke(null, [effects])!, Is.EqualTo((nuint)expected));

  [TestCase(0u, DragDropEffects.None)]
  [TestCase(1u, DragDropEffects.Copy)]
  [TestCase(2u, DragDropEffects.Link)]
  [TestCase(4u, DragDropEffects.None)]
  [TestCase(8u, DragDropEffects.None)]
  [TestCase(16u, DragDropEffects.Move)]
  [TestCase(32u, DragDropEffects.None)]
  [TestCase(16u | 4u, DragDropEffects.Move)]
  public void NSDragOperation_maps_back_to_toolkit_effects(uint operations, DragDropEffects expected)
      => Assert.That(_ToEffects.Invoke(null, [(nuint)operations]), Is.EqualTo(expected));
}
