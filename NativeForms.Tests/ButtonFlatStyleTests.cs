using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Button.FlatStyle"/> (PRD §7.3): the two styles a platform button can express keep the
/// widget, the two it cannot are painted (PRD §12), and the painted pair differ where their names say
/// they do — a flat face carries no frame, a popup face grows one under the pointer.
/// </summary>
[TestFixture]
internal sealed class ButtonFlatStyleTests
{
    private static HeadlessCanvasPeer Realize(Button button, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static Button New(FlatStyle style) => new() { Bounds = new(0, 0, 100, 28), Text = "Go", FlatStyle = style };

    /// <summary>
    /// Whether the painted face drew the button frame — the outline around the whole face, told apart
    /// from the focus ring, which is a rectangle too but inset.
    /// </summary>
    private static bool Framed(List<string> operations)
        => operations.Any(static op => (op.StartsWith("rect ") || op.StartsWith("round ")) && op.Contains(" 0,0,99,27"));

    [Test]
    public void Standard_and_System_keep_the_platform_button()
    {
        var standard = New(FlatStyle.Standard);
        var system = New(FlatStyle.System);
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.AddRange(standard, system);
        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(standard.IsNativeWidget, Is.True);
            Assert.That(system.IsNativeWidget, Is.True, "this toolkit never draws over a platform button, so System is Standard here");
        });
    }

    [Test]
    public void Flat_and_Popup_give_up_the_widget()
    {
        var flat = New(FlatStyle.Flat);
        var popup = New(FlatStyle.Popup);

        Realize(flat, out _);
        Realize(popup, out _);

        Assert.Multiple(() =>
        {
            Assert.That(flat.IsNativeWidget, Is.False, "no platform button offers a flat face");
            Assert.That(popup.IsNativeWidget, Is.False);
        });
    }

    [Test]
    public void Changing_the_style_moves_a_live_button_between_the_halves()
    {
        var button = New(FlatStyle.Standard);
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        Assume.That(button.IsNativeWidget, Is.True);

        button.FlatStyle = FlatStyle.Flat;
        var painted = button.IsNativeWidget;
        button.FlatStyle = FlatStyle.Standard;

        Assert.Multiple(() =>
        {
            Assert.That(painted, Is.False);
            Assert.That(button.IsNativeWidget, Is.True, "and back, invisibly to the application");
        });
    }

    [Test]
    public void A_flat_face_carries_no_frame()
    {
        var canvas = Realize(New(FlatStyle.Flat), out _);

        Assert.That(Framed(canvas.RaisePaint().Operations), Is.False);
    }

    [Test]
    public void A_popup_face_grows_one_under_the_pointer_and_loses_it_again()
    {
        var canvas = Realize(New(FlatStyle.Popup), out _);
        var atRest = Framed(canvas.RaisePaint().Operations);

        canvas.RaiseMouseMove(50, 14);
        var hovered = Framed(canvas.RaisePaint().Operations);
        canvas.RaiseMouseLeave();

        Assert.Multiple(() =>
        {
            Assert.That(atRest, Is.False);
            Assert.That(hovered, Is.True);
            Assert.That(Framed(canvas.RaisePaint().Operations), Is.False, "the pointer left, so the face flattens again");
        });
    }

    [Test]
    public void Hovering_a_flat_face_changes_nothing()
    {
        var canvas = Realize(New(FlatStyle.Flat), out _);

        canvas.RaiseMouseMove(50, 14);

        Assert.That(Framed(canvas.RaisePaint().Operations), Is.False, "flat is flat — that is the difference from Popup");
    }

    [Test]
    public void A_press_frames_either_of_them()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FramedWhilePressed(FlatStyle.Flat), Is.True, "a pressed rectangle that only changed colour says nothing");
            Assert.That(FramedWhilePressed(FlatStyle.Popup), Is.True);
        });

        static bool FramedWhilePressed(FlatStyle style)
        {
            var canvas = Realize(New(style), out _);
            canvas.RaiseMouseDown(50, 14);
            return Framed(canvas.RaisePaint().Operations);
        }
    }

    [Test]
    public void A_painted_flat_button_still_clicks()
    {
        var button = New(FlatStyle.Flat);
        var clicks = 0;
        button.Click += (_, _) => ++clicks;
        var canvas = Realize(button, out _);

        canvas.RaiseMouseDown(50, 14);
        canvas.RaiseMouseUp(50, 14);

        Assert.That(clicks, Is.EqualTo(1));
    }
}
