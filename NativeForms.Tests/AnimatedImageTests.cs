using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The animation layer: <see cref="AnimatedImage.FrameIndexAt"/> as a pure function of elapsed time
/// (loop modes and the modulo "stay in sync whether visible or not" guarantee), the shared
/// <see cref="AnimationClock"/> repainting only visible subscribers, and <see cref="PictureBox"/>
/// drawing the current frame.
/// </summary>
[TestFixture]
internal sealed class AnimatedImageTests
{
    /// <summary>Three 100 ms frames — one 300 ms loop.</summary>
    private static AnimatedImage ThreeFrames(int loopCount)
        => new(new DecodedImage(2, 2,
            [new ImageFrame(new int[4], 100), new ImageFrame(new int[4], 100), new ImageFrame(new int[4], 100)],
            loopCount));

    [Test]
    public void FrameIndexAt_walks_the_frames_by_their_delays()
    {
        var image = ThreeFrames(0);
        Assert.Multiple(() =>
        {
            Assert.That(image.FrameIndexAt(0), Is.EqualTo(0));
            Assert.That(image.FrameIndexAt(50), Is.EqualTo(0));
            Assert.That(image.FrameIndexAt(150), Is.EqualTo(1));
            Assert.That(image.FrameIndexAt(250), Is.EqualTo(2));
        });
    }

    [Test]
    public void Loop_forever_takes_the_time_modulo_the_loop()
    {
        var image = ThreeFrames(0);
        Assert.Multiple(() =>
        {
            Assert.That(image.FrameIndexAt(300), Is.EqualTo(0), "one loop on → back to the first frame");
            Assert.That(image.FrameIndexAt(350), Is.EqualTo(image.FrameIndexAt(50)), "same phase, same frame");
            // The virtual-clock guarantee: a huge elapsed maps to exactly the in-loop phase.
            Assert.That(image.FrameIndexAt((5 * 300) + 150), Is.EqualTo(image.FrameIndexAt(150)));
        });
    }

    [Test]
    public void Playing_once_holds_the_last_frame_after_one_loop()
    {
        var image = ThreeFrames(1); // "don't loop"
        Assert.Multiple(() =>
        {
            Assert.That(image.FrameIndexAt(150), Is.EqualTo(1), "still animating within the first loop");
            Assert.That(image.FrameIndexAt(300), Is.EqualTo(2), "held on the last frame");
            Assert.That(image.FrameIndexAt(10_000), Is.EqualTo(2));
        });
    }

    [Test]
    public void Playing_n_times_holds_the_last_frame_after_n_loops()
    {
        var image = ThreeFrames(2);
        Assert.Multiple(() =>
        {
            Assert.That(image.FrameIndexAt(450), Is.EqualTo(image.FrameIndexAt(150)), "second loop still runs");
            Assert.That(image.FrameIndexAt(600), Is.EqualTo(2), "two loops played → held on the last frame");
        });
    }

    [Test]
    public void The_clock_repaints_only_visible_subscribers_but_the_frame_stays_correct_while_hidden()
    {
        var clock = new AnimationClock();
        var image = ThreeFrames(0);
        var repaints = 0;
        var visible = true;
        clock.Register("box", image, () => visible, () => ++repaints);

        clock.Advance(image.StartTick + 150); // frame 0 → 1
        clock.Advance(image.StartTick + 170); // still frame 1
        clock.Advance(image.StartTick + 250); // frame 2
        Assert.That(repaints, Is.EqualTo(2), "repaints only when the frame actually advances");

        visible = false;
        clock.Advance(image.StartTick + 650); // would be frame 1, but it is hidden
        Assert.Multiple(() =>
        {
            Assert.That(repaints, Is.EqualTo(2), "a hidden subscriber is not repainted");
            Assert.That(image.CurrentFrameIndex(image.StartTick + 650), Is.EqualTo(image.FrameIndexAt(50)),
                "yet its frame is exactly what it would show had it stayed visible");
        });

        visible = true;
        clock.Advance(image.StartTick + 650); // shown again → repaint to the correct frame
        Assert.That(repaints, Is.EqualTo(3), "coming back repaints to the right frame");

        clock.Unregister("box");
    }

    [Test]
    public void Pausing_freezes_the_frame_and_resuming_continues_from_it()
    {
        var image = ThreeFrames(0);
        var start = image.StartTick;

        image.Pause(start + 150); // freeze on frame 1
        Assert.Multiple(() =>
        {
            Assert.That(image.IsPaused, Is.True);
            Assert.That(image.CurrentFrameIndex(start + 1000), Is.EqualTo(1), "time passing does not advance a paused image");
        });

        image.Resume(start + 1000); // 850 ms were spent paused
        Assert.Multiple(() =>
        {
            Assert.That(image.IsPaused, Is.False);
            Assert.That(image.CurrentFrameIndex(start + 1000), Is.EqualTo(1), "resumes exactly where it froze");
            Assert.That(image.CurrentFrameIndex(start + 1100), Is.EqualTo(2), "then keeps advancing (paused time excluded)");
        });
    }

    [Test]
    public void Grayscale_luminance_weights_the_channels_and_keeps_alpha()
    {
        int[] source = [unchecked((int)0xFFFF0000), unchecked((int)0x8000FF00), unchecked((int)0xFF0000FF)];
        var gray = AnimatedImage.GrayscaleForTest(source);

        Assert.Multiple(() =>
        {
            Assert.That(gray[0], Is.EqualTo(unchecked((int)0xFF4C4C4C)), "red → 0x4C, alpha kept");
            Assert.That(gray[1], Is.EqualTo(unchecked((int)0x80959595)), "green → 0x95, alpha 0x80 kept");
            Assert.That(gray[2], Is.EqualTo(unchecked((int)0xFF1C1C1C)), "blue → 0x1C");
        });
    }

    [Test]
    public void A_disabled_picture_box_freezes_the_animation_and_re_enabling_resumes_it()
    {
        var image = ThreeFrames(0);
        var box = new PictureBox { Bounds = new(0, 0, 40, 40), Image = image };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        box.Enabled = false;
        canvas.RaisePaint();
        Assert.That(image.IsPaused, Is.True, "a disabled box freezes its animation");

        box.Enabled = true;
        canvas.RaisePaint();
        Assert.That(image.IsPaused, Is.False, "re-enabling resumes it");
    }

    [Test]
    public void A_picture_box_draws_the_current_animation_frame()
    {
        var box = new PictureBox { Bounds = new(0, 0, 40, 40), Image = ThreeFrames(0) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("image 2x2")), Is.True, "the 2×2 frame is blitted");
    }

    // --- Animated image through any control's plain IImage property --------------------------------
    // AnimatedImage implements IImage, so it can be assigned to a control's ordinary Image property —
    // "the same interface" a still image uses — and the owner-drawn base animates it uniformly.

    private static CheckBox RealizedCheckBox(AnimatedImage image, out HeadlessCanvasPeer canvas)
    {
        var box = new CheckBox { Bounds = new(0, 0, 80, 24), Text = "Go", Image = image };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return box;
    }

    [Test]
    public void An_animated_image_on_a_plain_Image_property_draws_the_current_frame()
    {
        RealizedCheckBox(ThreeFrames(0), out var canvas);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.StartsWith("image 2x2")), Is.True,
            "the CheckBox blits the animated image's frame through its plain Image property, not nothing");
    }

    [Test]
    public void An_animated_image_on_a_plain_Image_property_subscribes_to_the_shared_clock()
    {
        var image = ThreeFrames(0);
        RealizedCheckBox(image, out var canvas);
        var before = canvas.InvalidateCount;

        AnimationClock.Instance.Advance(image.StartTick + 150); // frame 0 → 1

        Assert.That(canvas.InvalidateCount, Is.GreaterThan(before),
            "advancing the shared clock repaints the control, so an animated image on a plain property actually animates");
    }

    [Test]
    public void A_disabled_control_freezes_an_animated_image_on_its_plain_property()
    {
        var image = ThreeFrames(0);
        var box = RealizedCheckBox(image, out var canvas);

        box.Enabled = false;
        canvas.RaisePaint();

        Assert.That(image.IsPaused, Is.True, "a disabled control freezes the animation on a plain Image property, like PictureBox");
    }

    [Test]
    public void An_animated_image_on_a_native_button_pushes_a_resolved_frame_to_the_peer()
    {
        var button = new Button { Bounds = new(0, 0, 80, 26), Text = "Go", Image = ThreeFrames(0) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(button);
        Application.Run(form, backend);
        var peer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(peer.Image, Is.Not.Null, "the native button pushed the animated image's current frame");
            Assert.That(peer.Image, Is.Not.InstanceOf<AnimatedImage>(), "it pushed a resolved frame, not the animated image itself");
            Assert.That(peer.Image!.Width, Is.EqualTo(2), "the pushed frame is the 2×2 image");
        });
    }

    // --- Animated entries in an ImageList (tree / list / tab / … icons) ----------------------------

    [Test]
    public void An_image_list_resolves_an_animated_entry_to_a_frame_and_raises_FrameChanged()
    {
        var image = ThreeFrames(0);
        var list = new ImageList(2);
        var index = list.Add(image);
        var backend = new HeadlessBackend();
        var frameChanges = 0;
        list.FrameChanged += (_, _) => ++frameChanges;

        var resolved = list.GetImage(index, backend);
        AnimationClock.Instance.Advance(image.StartTick + 150); // frame 0 → 1

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Not.InstanceOf<AnimatedImage>(), "GetImage resolves an animated entry to a concrete frame");
            Assert.That(resolved.Width, Is.EqualTo(2), "the frame is the 2×2 image");
            Assert.That(frameChanges, Is.GreaterThan(0), "advancing the shared clock raises FrameChanged so consumers repaint");
        });
    }

    [Test]
    public void A_control_repaints_as_its_image_lists_animated_icon_advances()
    {
        var image = ThreeFrames(0);
        var list = new ImageList(2);
        list.Add(image);
        var tree = new TreeView { Bounds = new(0, 0, 120, 80), ImageList = list };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(tree);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var before = canvas.InvalidateCount;

        AnimationClock.Instance.Advance(image.StartTick + 150); // frame 0 → 1

        Assert.That(canvas.InvalidateCount, Is.GreaterThan(before),
            "the control repaints as its image list's animated icon advances a frame");
    }
}
