using System.Drawing;
using System.Text;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Dragging virtual files out (PRD §8). A <c>VirtualFile[]</c> passed to
/// <see cref="Control.DoDragDrop(object, DragDropEffects, Action{DragDropEffects})"/> is an ordinary
/// in-process payload while the pointer is over the window — targets receive the same array — and is
/// handed to the source peer when the pointer leaves: natively to an
/// <see cref="Backends.IVirtualFileDragSourcePeer"/>, or, on a peer that can only drag paths, written
/// into a temporary folder and handed to its <see cref="Backends.IFileDragSourcePeer"/> as a file list.
/// The window spans (0,0)-(300,200) on the fake screen.
/// </summary>
[TestFixture]
internal sealed class VirtualFileDragTests {
  private sealed class TestSurface : OwnerDrawnControl;

  private const int _OutsideX = 400;

  private static VirtualFile Text(string path, string content)
      => new(path, () => new MemoryStream(Encoding.UTF8.GetBytes(content)));

  private static (TestSurface Source, TestSurface Target) CreateScene(bool virtualCapable) {
    var backend = new HeadlessBackend { OfferVirtualFileDrag = virtualCapable };
    var form = new Form { Bounds = new(0, 0, 300, 200) };
    var source = new TestSurface { Bounds = new(0, 0, 50, 50) };
    var target = new TestSurface { Bounds = new(100, 0, 50, 50), AllowDrop = true };
    form.Controls.Add(source);
    form.Controls.Add(target);
    form.RealizeWindow(backend);
    ((HeadlessPeer)source.Peer!).ScreenOrigin = new Point(0, 0);
    ((HeadlessPeer)target.Peer!).ScreenOrigin = new Point(100, 0);
    return (source, target);
  }

  private static HeadlessCanvasPeer CanvasOf(Control control) => (HeadlessCanvasPeer)control.Peer!;

  // --- Inside the window ---------------------------------------------------------------------------

  [TestCase(true, TestName = "Given_virtual_files_on_a_native_peer_when_dropped_inside_the_window_then_the_target_gets_the_same_array")]
  [TestCase(false, TestName = "Given_virtual_files_on_a_path_only_peer_when_dropped_inside_the_window_then_the_target_gets_the_same_array")]
  public void Inside_the_window_virtual_files_stay_an_in_process_payload(bool virtualCapable) {
    var (source, target) = CreateScene(virtualCapable);
    VirtualFile[] files = [Text("a.txt", "a")];
    var seen = new List<object>();
    var nativeCalls = 0;
    if (source.Peer is HeadlessVirtualFileCanvasPeer native)
      native.NativeVirtualFileDrag = (_, _, _) => ++nativeCalls > 0;
    CanvasOf(source).NativeFileDrag = (_, _, _) => ++nativeCalls > 0;
    target.DragEnter += (_, e) => {
      seen.Add(e.Data);
      e.Effect = DragDropEffects.Copy;
    };
    target.DragOver += (_, e) => seen.Add(e.Data);
    target.DragDrop += (_, e) => seen.Add(e.Data);

    source.DoDragDrop(files, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseMove(111, 10);
    CanvasOf(source).RaiseMouseUp(111, 10);

    Assert.Multiple(() => {
      Assert.That(seen, Has.Count.EqualTo(3));
      Assert.That(seen, Has.All.SameAs(files));
      Assert.That(nativeCalls, Is.Zero);
    });
  }

  // --- The native handover -------------------------------------------------------------------------

  [Test]
  public void Given_a_native_peer_when_virtual_files_leave_the_window_then_the_peer_receives_the_same_array_and_effects() {
    var (source, _) = CreateScene(virtualCapable: true);
    VirtualFile[] files = [Text("a.txt", "a"), VirtualFile.Directory("docs")];
    VirtualFile[]? handed = null;
    var allowed = DragDropEffects.None;
    var pathDrags = 0;
    var native = (HeadlessVirtualFileCanvasPeer)source.Peer!;
    native.NativeVirtualFileDrag = (f, a, _) => {
      handed = f;
      allowed = a;
      return true;
    };
    native.NativeFileDrag = (_, _, _) => ++pathDrags > 0;

    source.DoDragDrop(files, DragDropEffects.Copy | DragDropEffects.Move | (DragDropEffects)0x100);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.Multiple(() => {
      Assert.That(handed, Is.SameAs(files));
      Assert.That(allowed, Is.EqualTo(DragDropEffects.Copy | DragDropEffects.Move));
      Assert.That(pathDrags, Is.Zero, "nothing was written to a temporary folder");
    });
  }

  [Test]
  public void Given_a_native_drag_when_it_completes_then_the_filtered_effect_reaches_the_caller_once() {
    var (source, _) = CreateScene(virtualCapable: true);
    Action<DragDropEffects>? pending = null;
    ((HeadlessVirtualFileCanvasPeer)source.Peer!).NativeVirtualFileDrag = (_, _, c) => {
      pending = c;
      return true;
    };
    var completed = new List<DragDropEffects>();

    source.DoDragDrop(new[] { Text("a.txt", "a") }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    var before = completed.Count;
    pending!(DragDropEffects.Copy | DragDropEffects.Move);

    Assert.Multiple(() => {
      Assert.That(before, Is.Zero);
      Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Copy }));
    });
  }

  [Test]
  public void Given_a_native_peer_that_declines_when_virtual_files_leave_the_window_then_the_drag_stays_in_process_without_a_fallback() {
    var (source, target) = CreateScene(virtualCapable: true);
    var native = (HeadlessVirtualFileCanvasPeer)source.Peer!;
    var asked = 0;
    var pathDrags = 0;
    native.NativeVirtualFileDrag = (_, _, _) => ++asked < 0;
    native.NativeFileDrag = (_, _, _) => ++pathDrags > 0;
    var completed = new List<DragDropEffects>();
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;

    source.DoDragDrop(new[] { Text("a.txt", "a") }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseMove(_OutsideX + 5, 10);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.Multiple(() => {
      Assert.That(asked, Is.EqualTo(1), "asked once per drag");
      Assert.That(pathDrags, Is.Zero);
      Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Copy }));
    });
  }

  // --- The temporary-folder fallback ---------------------------------------------------------------

  [Test]
  public void Given_a_path_only_peer_when_virtual_files_leave_the_window_then_they_are_written_to_a_temporary_folder_and_dragged_as_paths() {
    var (source, _) = CreateScene(virtualCapable: false);
    string[]? paths = null;
    var contents = new List<string>();
    CanvasOf(source).NativeFileDrag = (p, _, c) => {
      paths = p;
      contents.Add(File.ReadAllText(p[0]));
      contents.Add(File.ReadAllText(Path.Combine(p[1], "inner.txt")));
      c(DragDropEffects.Copy);
      return true;
    };
    var completed = new List<DragDropEffects>();

    source.DoDragDrop(new[] { Text("top.txt", "top"), Text("folder/inner.txt", "inner") }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    try {
      Assert.Multiple(() => {
        Assert.That(paths!.Select(Path.GetFileName), Is.EqualTo(new[] { "top.txt", "folder" }));
        Assert.That(paths!, Has.All.Matches<string>(Path.IsPathFullyQualified));
        Assert.That(contents, Is.EqualTo(new[] { "top", "inner" }));
        Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Copy }));
      });
    } finally {
      Directory.Delete(Path.GetDirectoryName(paths![0])!, recursive: true);
    }
  }

  [Test]
  public void Given_a_path_only_peer_when_the_drag_stays_inside_then_nothing_is_written() {
    var (source, _) = CreateScene(virtualCapable: false);
    var opened = 0;
    var file = new VirtualFile("lazy.txt", () => {
      ++opened;
      return new MemoryStream();
    });
    CanvasOf(source).NativeFileDrag = (_, _, _) => true;

    source.DoDragDrop(new[] { file }, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.That(opened, Is.Zero);
  }

  [Test]
  public void Given_content_that_cannot_be_produced_when_the_fallback_writes_it_then_the_drag_stays_in_process() {
    var (source, target) = CreateScene(virtualCapable: false);
    var pathDrags = 0;
    CanvasOf(source).NativeFileDrag = (_, _, _) => ++pathDrags > 0;
    var broken = new VirtualFile("broken.bin", () => throw new IOException("archive is damaged"));
    object? dropped = null;
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    target.DragDrop += (_, e) => dropped = e.Data;
    var files = new[] { broken };

    source.DoDragDrop(files, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.Multiple(() => {
      Assert.That(pathDrags, Is.Zero);
      Assert.That(dropped, Is.SameAs(files));
    });
  }

  // --- Payloads that are not virtual files ---------------------------------------------------------

  private static IEnumerable<TestCaseData> NotVirtualFiles() {
    yield return new TestCaseData((object)Array.Empty<VirtualFile>()).SetName("Given_an_empty_virtual_file_array_when_it_leaves_the_window_then_it_stays_in_process");
    yield return new TestCaseData((object)new VirtualFile?[] { Text("a.txt", "a"), null }).SetName("Given_a_null_virtual_file_entry_when_it_leaves_the_window_then_it_stays_in_process");
    yield return new TestCaseData((object)new List<VirtualFile> { Text("a.txt", "a") }).SetName("Given_a_list_of_virtual_files_when_it_leaves_the_window_then_it_stays_in_process");
    yield return new TestCaseData((object)Text("a.txt", "a")).SetName("Given_a_single_virtual_file_when_it_leaves_the_window_then_it_stays_in_process");
  }

  [TestCaseSource(nameof(NotVirtualFiles))]
  public void Payloads_that_are_not_a_virtual_file_array_never_reach_the_platform(object data) {
    var (source, _) = CreateScene(virtualCapable: true);
    var native = (HeadlessVirtualFileCanvasPeer)source.Peer!;
    var calls = 0;
    native.NativeVirtualFileDrag = (_, _, _) => ++calls > 0;
    native.NativeFileDrag = (_, _, _) => ++calls > 0;

    source.DoDragDrop(data, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(calls, Is.Zero);
  }

  [Test]
  public void Given_no_allowed_effect_when_virtual_files_leave_the_window_then_no_platform_drag_starts() {
    var (source, _) = CreateScene(virtualCapable: true);
    var calls = 0;
    ((HeadlessVirtualFileCanvasPeer)source.Peer!).NativeVirtualFileDrag = (_, _, _) => ++calls > 0;

    source.DoDragDrop(new[] { Text("a.txt", "a") }, DragDropEffects.None);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(calls, Is.Zero);
  }
}
