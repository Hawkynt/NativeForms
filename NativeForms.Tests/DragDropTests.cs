using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The in-process drag-and-drop engine: a drag started with <see cref="Control.DoDragDrop"/>
/// consumes the source's mouse stream, hit-tests the window tree in screen space, and raises the
/// WinForms-shaped enter/over/leave/drop sequence on <see cref="Control.AllowDrop"/> targets.
/// Everything runs on the headless backend; screen geometry comes from the peers'
/// settable <c>ScreenOrigin</c>.
/// </summary>
[TestFixture]
internal sealed class DragDropTests
{
    private sealed class TestSurface : OwnerDrawnControl
    {
        public event Action? MouseMoved;
        public event Action? MouseWentUp;

        protected override void OnMouseMove(MouseEventArgs e) => this.MouseMoved?.Invoke();
        protected override void OnMouseUp(MouseEventArgs e) => this.MouseWentUp?.Invoke();
    }

    /// <summary>A form at screen origin hosting a drag source (0,0,50,50) and a drop target (100,0,50,50),
    /// with every canvas peer's screen origin aligned to its bounds.</summary>
    private static (TestSurface Source, TestSurface Target, HeadlessBackend Backend) CreateScene()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 300, 200) };
        var source = new TestSurface { Bounds = new(0, 0, 50, 50) };
        var target = new TestSurface { Bounds = new(100, 0, 50, 50), AllowDrop = true };
        form.Controls.Add(source);
        form.Controls.Add(target);
        form.RealizeWindow(backend);

        ((HeadlessPeer)source.Peer!).ScreenOrigin = new Point(0, 0);
        ((HeadlessPeer)target.Peer!).ScreenOrigin = new Point(100, 0);
        return (source, target, backend);
    }

    private static HeadlessCanvasPeer CanvasOf(Control control) => (HeadlessCanvasPeer)control.Peer!;

    [Test]
    public void Drop_on_an_accepting_target_delivers_the_data_and_effect()
    {
        var (source, target, _) = CreateScene();
        var events = new List<string>();
        DragEventArgs? dropped = null;
        target.DragEnter += (_, e) =>
        {
            events.Add($"enter data={e.Data} allowed={e.AllowedEffect}");
            e.Effect = DragDropEffects.Copy;
        };
        target.DragOver += (_, e) => events.Add($"over effect={e.Effect}");
        target.DragDrop += (_, e) =>
        {
            events.Add("drop");
            dropped = e;
        };

        source.DoDragDrop("payload", DragDropEffects.Copy | DragDropEffects.Move);
        CanvasOf(source).RaiseMouseMove(110, 10); // screen (110,10): over the target
        CanvasOf(source).RaiseMouseMove(120, 10);
        CanvasOf(source).RaiseMouseUp(120, 10);

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(new[]
            {
                "enter data=payload allowed=Copy, Move",
                "over effect=Copy",
                "drop",
            }));
            Assert.That(dropped!.Data, Is.EqualTo("payload"));
            Assert.That(dropped.Effect, Is.EqualTo(DragDropEffects.Copy));
            Assert.That((dropped.X, dropped.Y), Is.EqualTo((120, 10)));
        });
    }

    [Test]
    public void Leaving_the_target_raises_DragLeave_and_dropping_elsewhere_does_nothing()
    {
        var (source, target, _) = CreateScene();
        var events = new List<string>();
        target.DragEnter += (_, e) =>
        {
            events.Add("enter");
            e.Effect = DragDropEffects.Copy;
        };
        target.DragLeave += (_, _) => events.Add("leave");
        target.DragDrop += (_, _) => events.Add("drop");

        source.DoDragDrop("payload", DragDropEffects.Copy);
        CanvasOf(source).RaiseMouseMove(110, 10); // over the target
        CanvasOf(source).RaiseMouseMove(200, 10); // off it again
        CanvasOf(source).RaiseMouseUp(200, 10);

        Assert.That(events, Is.EqualTo(new[] { "enter", "leave" }));
    }

    [Test]
    public void A_target_without_AllowDrop_is_invisible_to_the_drag()
    {
        var (source, target, _) = CreateScene();
        target.AllowDrop = false;
        var any = false;
        target.DragEnter += (_, _) => any = true;
        target.DragDrop += (_, _) => any = true;

        source.DoDragDrop("payload", DragDropEffects.Copy);
        CanvasOf(source).RaiseMouseMove(110, 10);
        CanvasOf(source).RaiseMouseUp(110, 10);

        Assert.That(any, Is.False);
    }

    [Test]
    public void A_target_that_answers_None_gets_leave_instead_of_drop()
    {
        var (source, target, _) = CreateScene();
        var events = new List<string>();
        target.DragEnter += (_, _) => events.Add("enter"); // Effect stays None
        target.DragLeave += (_, _) => events.Add("leave");
        target.DragDrop += (_, _) => events.Add("drop");

        source.DoDragDrop("payload", DragDropEffects.All);
        CanvasOf(source).RaiseMouseMove(110, 10);
        CanvasOf(source).RaiseMouseUp(110, 10);

        Assert.That(events, Is.EqualTo(new[] { "enter", "leave" }));
    }

    [Test]
    public void Effects_outside_the_allowed_set_are_filtered()
    {
        var (source, target, _) = CreateScene();
        DragDropEffects? effect = null;
        target.DragEnter += (_, e) => e.Effect = DragDropEffects.Move; // not allowed
        target.DragDrop += (_, e) => effect = e.Effect;
        target.DragLeave += (_, _) => effect = DragDropEffects.None;

        source.DoDragDrop("payload", DragDropEffects.Copy);
        CanvasOf(source).RaiseMouseMove(110, 10);
        CanvasOf(source).RaiseMouseUp(110, 10);

        Assert.That(effect, Is.EqualTo(DragDropEffects.None), "Move was filtered by the Copy-only allowance, so no drop happened");
    }

    [Test]
    public void The_drag_consumes_the_sources_own_mouse_stream()
    {
        var (source, _, _) = CreateScene();
        var sourceSawMove = false;
        var sourceSawUp = false;
        source.MouseMoved += () => sourceSawMove = true;
        source.MouseWentUp += () => sourceSawUp = true;

        source.DoDragDrop("payload", DragDropEffects.Copy);
        CanvasOf(source).RaiseMouseMove(10, 10);
        CanvasOf(source).RaiseMouseUp(10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(sourceSawMove, Is.False);
            Assert.That(sourceSawUp, Is.False);
        });
    }

    [Test]
    public void After_the_drop_the_mouse_stream_belongs_to_the_source_again()
    {
        var (source, _, _) = CreateScene();
        var moves = 0;
        source.MouseMoved += () => ++moves;

        source.DoDragDrop("payload", DragDropEffects.Copy);
        CanvasOf(source).RaiseMouseUp(10, 10); // ends the drag
        CanvasOf(source).RaiseMouseMove(10, 10);

        Assert.That(moves, Is.EqualTo(1));
    }

    [Test]
    public void The_deepest_later_sibling_wins_the_hit_test()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 300, 200), AllowDrop = true };
        var source = new TestSurface { Bounds = new(0, 0, 50, 50) };
        var below = new TestSurface { Bounds = new(100, 0, 100, 100), AllowDrop = true };
        var above = new TestSurface { Bounds = new(100, 0, 100, 100), AllowDrop = true };
        form.Controls.Add(source);
        form.Controls.Add(below);
        form.Controls.Add(above);
        form.RealizeWindow(backend);
        ((HeadlessPeer)source.Peer!).ScreenOrigin = new Point(0, 0);
        ((HeadlessPeer)below.Peer!).ScreenOrigin = new Point(100, 0);
        ((HeadlessPeer)above.Peer!).ScreenOrigin = new Point(100, 0);
        var hits = new List<string>();
        below.DragEnter += (_, _) => hits.Add("below");
        above.DragEnter += (_, _) => hits.Add("above");
        form.DragEnter += (_, _) => hits.Add("form");

        source.DoDragDrop("payload", DragDropEffects.Copy);
        CanvasOf(source).RaiseMouseMove(150, 50);
        CanvasOf(source).RaiseMouseUp(150, 50);

        Assert.That(hits, Is.EqualTo(new[] { "above" }), "the last-added overlapping sibling is on top, and parents never see it");
    }
}
