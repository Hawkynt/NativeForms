using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Dragging files out to the operating system (PRD §8). A drag started with
/// <see cref="Control.DoDragDrop(object, DragDropEffects, Action{DragDropEffects})"/> whose payload is
/// a <c>string[]</c> of existing, fully qualified paths stays in process while the pointer is over the
/// window, and is handed to the source peer's <see cref="IFileDragSourcePeer"/> when the pointer
/// leaves it. Every other payload, and every peer that cannot drag natively, stays in process. The
/// headless canvas peer scripts the native side; the window spans (0,0)-(300,200) on the fake screen.
/// </summary>
[TestFixture]
internal sealed class NativeFileDragTests {
  private sealed class TestSurface : OwnerDrawnControl {
    public int Moves { get; private set; }

    protected override void OnMouseMove(MouseEventArgs e) => ++this.Moves;
  }

  /// <summary>A scripted native drag: records each request, accepts or declines it, and completes at
  /// once (the modal Win32 shape) or later (the asynchronous GTK/macOS shape).</summary>
  private sealed class NativeDrag {
    public List<(string[] Paths, DragDropEffects Allowed)> Requests { get; } = [];
    public bool Accept { get; init; } = true;
    public DragDropEffects? CompleteAtOnce { get; init; }
    public Action<DragDropEffects>? Pending { get; private set; }

    public bool Run(string[] paths, DragDropEffects allowed, Action<DragDropEffects> completed) {
      this.Requests.Add((paths, allowed));
      if (!this.Accept)
        return false;

      if (this.CompleteAtOnce is { } effect)
        completed(effect);
      else
        this.Pending = completed;

      return true;
    }
  }

  private string _directory = string.Empty;
  private string _first = string.Empty;
  private string _second = string.Empty;
  private string _folder = string.Empty;

  [SetUp]
  public void CreateFiles() {
    _directory = Path.Combine(Path.GetTempPath(), "nf-drag-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_directory);
    _first = Path.Combine(_directory, "first.txt");
    _second = Path.Combine(_directory, "second.bin");
    _folder = Path.Combine(_directory, "folder");
    File.WriteAllText(_first, "first");
    File.WriteAllBytes(_second, [1, 2, 3]);
    Directory.CreateDirectory(_folder);
  }

  [TearDown]
  public void DeleteFiles() {
    if (Directory.Exists(_directory))
      Directory.Delete(_directory, recursive: true);
  }

  /// <summary>A form at screen origin hosting a drag source (0,0,50,50) and a drop target (100,0,50,50).</summary>
  private static (TestSurface Source, TestSurface Target) CreateScene(NativeDrag? native = null) {
    var backend = new HeadlessBackend();
    var form = new Form { Bounds = new(0, 0, 300, 200) };
    var source = new TestSurface { Bounds = new(0, 0, 50, 50) };
    var target = new TestSurface { Bounds = new(100, 0, 50, 50), AllowDrop = true };
    form.Controls.Add(source);
    form.Controls.Add(target);
    form.RealizeWindow(backend);

    ((HeadlessPeer)source.Peer!).ScreenOrigin = new Point(0, 0);
    ((HeadlessPeer)target.Peer!).ScreenOrigin = new Point(100, 0);
    if (native is not null)
      CanvasOf(source).NativeFileDrag = native.Run;

    return (source, target);
  }

  private static HeadlessCanvasPeer CanvasOf(Control control) => (HeadlessCanvasPeer)control.Peer!;

  /// <summary>A point on the fake screen outside the window.</summary>
  private const int _OutsideX = 400;

  // --- Inside the window: the in-process drag ------------------------------------------------------

  [Test]
  public void Given_a_file_list_when_it_moves_inside_the_window_then_targets_see_the_original_payload_and_no_native_drag_starts() {
    var native = new NativeDrag();
    var (source, target) = CreateScene(native);
    var files = new[] { _first, _second };
    DragEventArgs? dropped = null;
    var completed = new List<DragDropEffects>();
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    target.DragDrop += (_, e) => dropped = e;

    source.DoDragDrop(files, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.Multiple(() => {
      Assert.That(native.Requests, Is.Empty);
      Assert.That(dropped!.Data, Is.SameAs(files));
      Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Copy }));
    });
  }

  [Test]
  public void Given_the_window_edge_when_the_pointer_is_on_it_then_the_drag_is_still_inside() {
    var native = new NativeDrag();
    var (source, _) = CreateScene(native);

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(299, 199); // the last pixel inside

    Assert.That(native.Requests, Is.Empty);
  }

  // --- Leaving the window: the handover -------------------------------------------------------------

  [Test]
  public void Given_a_file_list_when_the_pointer_leaves_the_window_then_the_platform_drag_receives_the_paths_and_effects() {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.Copy };
    var (source, _) = CreateScene(native);
    var files = new[] { _first, _second };

    source.DoDragDrop(files, DragDropEffects.Copy | DragDropEffects.Move);
    CanvasOf(source).RaiseMouseMove(300, 10); // the first pixel outside

    Assert.Multiple(() => {
      Assert.That(native.Requests, Has.Count.EqualTo(1));
      Assert.That(native.Requests[0].Paths, Is.EqualTo(files));
      Assert.That(native.Requests[0].Allowed, Is.EqualTo(DragDropEffects.Copy | DragDropEffects.Move));
    });
  }

  [TestCase(-1, 10, TestName = "Given_the_pointer_left_of_the_window_when_it_moves_then_the_drag_is_handed_over")]
  [TestCase(10, -1, TestName = "Given_the_pointer_above_the_window_when_it_moves_then_the_drag_is_handed_over")]
  [TestCase(10, 200, TestName = "Given_the_pointer_below_the_window_when_it_moves_then_the_drag_is_handed_over")]
  public void Every_edge_of_the_window_hands_the_drag_over(int x, int y) {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.None };
    var (source, _) = CreateScene(native);

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(x, y);

    Assert.That(native.Requests, Has.Count.EqualTo(1));
  }

  [Test]
  public void Given_a_directory_among_the_files_when_handed_over_then_it_goes_to_the_platform_like_a_file() {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.Copy };
    var (source, _) = CreateScene(native);

    source.DoDragDrop(new[] { _folder, _first }, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(native.Requests, Has.Count.EqualTo(1));
  }

  [Test]
  public void Given_a_hover_over_a_target_when_the_pointer_leaves_the_window_then_the_target_is_left_before_the_handover() {
    var events = new List<string>();
    var native = new NativeDrag();
    var (source, target) = CreateScene(native);
    target.DragEnter += (_, e) => {
      events.Add("enter");
      e.Effect = DragDropEffects.Copy;
    };
    target.DragLeave += (_, _) => events.Add(native.Requests.Count == 0 ? "leave" : "leave after handover");

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(events, Is.EqualTo(new[] { "enter", "leave" }));
  }

  [Test]
  public void Given_a_modal_platform_drag_when_it_completes_then_its_effect_reaches_the_caller() {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.Move };
    var (source, _) = CreateScene(native);
    var completed = new List<DragDropEffects>();

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy | DragDropEffects.Move, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Move }));
  }

  [Test]
  public void Given_an_asynchronous_platform_drag_when_its_session_ends_later_then_its_effect_reaches_the_caller_once() {
    var native = new NativeDrag();
    var (source, _) = CreateScene(native);
    var completed = new List<DragDropEffects>();

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    var beforeTheSessionEnded = completed.Count;
    native.Pending!(DragDropEffects.Copy);

    Assert.Multiple(() => {
      Assert.That(beforeTheSessionEnded, Is.Zero);
      Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Copy }));
    });
  }

  [Test]
  public void Given_the_platform_reports_an_effect_that_was_not_allowed_when_it_completes_then_the_effect_is_filtered() {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.Move };
    var (source, _) = CreateScene(native);
    var completed = new List<DragDropEffects>();

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.None }));
  }

  [Test]
  public void Given_undefined_effect_bits_when_handed_over_then_the_platform_only_sees_copy_move_and_link() {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.Copy };
    var (source, _) = CreateScene(native);

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy | (DragDropEffects)0x100);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(native.Requests[0].Allowed, Is.EqualTo(DragDropEffects.Copy));
  }

  [Test]
  public void Given_a_handover_when_it_started_then_the_source_owns_its_mouse_stream_again() {
    var native = new NativeDrag();
    var (source, _) = CreateScene(native);

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseMove(10, 10);

    Assert.Multiple(() => {
      Assert.That(source.Moves, Is.EqualTo(1));
      Assert.That(native.Requests, Has.Count.EqualTo(1));
    });
  }

  [Test]
  public void Given_a_modal_platform_drag_when_its_completion_starts_another_drag_then_that_drag_survives() {
    var native = new NativeDrag { CompleteAtOnce = DragDropEffects.Copy };
    var (source, target) = CreateScene(native);
    var entered = false;
    target.DragEnter += (_, _) => entered = true;

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy, _ => source.DoDragDrop("next", DragDropEffects.Copy));
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseMove(110, 10);

    Assert.That(entered, Is.True);
  }

  // --- Declined or impossible handovers ----------------------------------------------------------

  [Test]
  public void Given_a_peer_that_declines_when_the_pointer_leaves_then_the_drag_stays_in_process_and_is_not_asked_again() {
    var native = new NativeDrag { Accept = false };
    var (source, target) = CreateScene(native);
    var completed = new List<DragDropEffects>();
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseMove(_OutsideX + 10, 10);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.Multiple(() => {
      Assert.That(native.Requests, Has.Count.EqualTo(1));
      Assert.That(source.Moves, Is.Zero, "the in-process drag kept the source's mouse stream");
      Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.Copy }));
    });
  }

  [Test]
  public void Given_a_platform_without_native_drag_when_files_are_released_outside_then_the_drag_ends_with_None() {
    var (source, _) = CreateScene(); // NativeFileDrag stays null: the peer declines
    var completed = new List<DragDropEffects>();

    source.DoDragDrop(new[] { _first }, DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseUp(_OutsideX, 10);

    Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.None }));
  }

  [Test]
  public void Given_no_allowed_effect_when_files_leave_the_window_then_no_platform_drag_starts() {
    var native = new NativeDrag();
    var (source, _) = CreateScene(native);

    source.DoDragDrop(new[] { _first }, DragDropEffects.None);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);

    Assert.That(native.Requests, Is.Empty);
  }

  /// <summary>
  /// The equivalence classes of payloads that are not an operating-system file list. A string array
  /// is also an ordinary in-process payload, so anything that cannot be a file drop stays one rather
  /// than throwing.
  /// </summary>
  private static IEnumerable<TestCaseData> NotAFileList() {
    yield return Case(_ => Array.Empty<string>(), "Given_an_empty_array_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(t => new string?[] { t._first, null }, "Given_a_null_entry_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(_ => new[] { string.Empty }, "Given_an_empty_path_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(_ => new[] { Path.Combine("relative", "file.txt") }, "Given_a_relative_path_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(t => new[] { Path.Combine(t._directory, "missing.txt") }, "Given_a_missing_file_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(t => new[] { t._first, Path.Combine(t._directory, "missing.txt") }, "Given_one_missing_file_among_existing_ones_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(t => t._first, "Given_a_single_path_string_when_it_leaves_the_window_then_it_stays_in_process");
    yield return Case(t => new List<string> { t._first }, "Given_a_list_instead_of_an_array_when_it_leaves_the_window_then_it_stays_in_process");

    static TestCaseData Case(Func<NativeFileDragTests, object> payload, string name) => new TestCaseData(payload).SetName(name);
  }

  [TestCaseSource(nameof(NotAFileList))]
  public void Payloads_that_are_not_a_file_list_never_reach_the_platform(Func<NativeFileDragTests, object> payload) {
    var native = new NativeDrag();
    var (source, target) = CreateScene(native);
    var data = payload(this);
    object? dropped = null;
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    target.DragDrop += (_, e) => dropped = e.Data;

    source.DoDragDrop(data, DragDropEffects.Copy);
    CanvasOf(source).RaiseMouseMove(_OutsideX, 10);
    CanvasOf(source).RaiseMouseMove(110, 10);
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.Multiple(() => {
      Assert.That(native.Requests, Is.Empty);
      Assert.That(dropped, Is.SameAs(data), "the in-process drag delivered the payload untouched");
    });
  }

  // --- Completion of in-process drags ------------------------------------------------------------

  [Test]
  public void Given_a_target_that_refuses_when_the_drag_is_released_then_the_completion_reports_None() {
    var (source, _) = CreateScene();
    var completed = new List<DragDropEffects>();

    source.DoDragDrop("text", DragDropEffects.Copy, completed.Add);
    CanvasOf(source).RaiseMouseMove(110, 10); // the target leaves Effect at None
    CanvasOf(source).RaiseMouseUp(110, 10);

    Assert.That(completed, Is.EqualTo(new[] { DragDropEffects.None }));
  }

  [Test]
  public void Given_a_drag_in_flight_when_another_drag_starts_then_the_first_completes_with_None() {
    var (source, _) = CreateScene();
    var first = new List<DragDropEffects>();

    source.DoDragDrop("first", DragDropEffects.Copy, first.Add);
    source.DoDragDrop("second", DragDropEffects.Copy);

    Assert.That(first, Is.EqualTo(new[] { DragDropEffects.None }));
  }

  [Test]
  public void Given_a_null_payload_when_dragged_then_it_is_rejected() {
    var (source, _) = CreateScene();

    Assert.That(() => source.DoDragDrop(null!, DragDropEffects.Copy), Throws.ArgumentNullException);
  }
}
