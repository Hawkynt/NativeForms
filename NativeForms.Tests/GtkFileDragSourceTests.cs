using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The pure parts of the GTK file drag (PRD §8): which virtual-file drags can go through the X
/// direct-save protocol (XDS carries exactly one file), and the effect translation. <c>GDK_ACTION_COPY</c>, <c>_MOVE</c> and
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

  private static VirtualFile File(string path) => new(path, () => Stream.Null);

  private static IEnumerable<TestCaseData> DirectSaveCases() {
    yield return new TestCaseData((object)new[] { File("report.pdf") }, "report.pdf")
        .SetName("Given_one_file_when_dragged_then_direct_save_proposes_its_name");
    yield return new TestCaseData((object)new[] { File("Übersicht – 日本語.txt") }, "Übersicht – 日本語.txt")
        .SetName("Given_one_file_with_a_Unicode_name_when_dragged_then_direct_save_proposes_it_verbatim");
    yield return new TestCaseData((object)new[] { File("a.txt"), File("b.txt") }, null)
        .SetName("Given_two_files_when_dragged_then_direct_save_is_not_offered");
    yield return new TestCaseData((object)new[] { VirtualFile.Directory("folder") }, null)
        .SetName("Given_one_folder_when_dragged_then_direct_save_is_not_offered");
    yield return new TestCaseData((object)new[] { File("folder/inner.txt") }, null)
        .SetName("Given_one_file_inside_an_implied_folder_when_dragged_then_direct_save_is_not_offered");
    yield return new TestCaseData((object)new[] { File("same.txt"), File("same.txt") }, "same.txt")
        .SetName("Given_the_same_file_listed_twice_when_dragged_then_direct_save_offers_it_once");
  }

  [TestCaseSource(nameof(DirectSaveCases))]
  public void Direct_save_carries_a_single_file_only(VirtualFile[] files, string? expected)
      => Assert.That(GtkFileDragSource.DirectSaveName(files), Is.EqualTo(expected));
}
