using System.Collections.Generic;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §12: an <see cref="HScrollBar"/> or <see cref="VScrollBar"/> realizes onto a real platform scroll
/// bar when the backend offers one. The contract worth pinning is the event order and the gesture type —
/// the platforms report which gesture moved the thumb, and <see cref="ScrollBar.Scroll"/> must carry it
/// through unchanged, exactly as the owner-drawn path does.
/// </summary>
[TestFixture]
internal sealed class NativeScrollBarPromotionTests
{
    private static HeadlessBackend Promoting() => new() { OfferNativeScrollBar = true };

    private static T Realize<T>(T bar, IPlatformBackend backend)
        where T : ScrollBar
    {
        var form = new Form();
        form.Controls.Add(bar);
        Application.Run(form, backend);
        return bar;
    }

    private static HScrollBar Bar() => new() { Bounds = new(0, 0, 200, 16), Minimum = 0, Maximum = 100, LargeChange = 10 };

    [TearDown]
    public void Restore() => Application.PreferNativeWidgets = true;

    [Test]
    public void A_backend_that_declines_leaves_the_bar_owner_drawn()
        => Assert.That(Realize(Bar(), new HeadlessBackend()).IsNativeWidget, Is.False);

    [Test]
    public void A_backend_that_offers_a_widget_promotes_the_bar()
        => Assert.That(Realize(Bar(), Promoting()).IsNativeWidget, Is.True);

    [Test]
    public void The_global_switch_turns_promotion_off()
    {
        Application.PreferNativeWidgets = false;

        Assert.That(Realize(Bar(), Promoting()).IsNativeWidget, Is.False);
    }

    [Test]
    public void A_vertical_bar_asks_the_backend_for_a_vertical_widget()
    {
        var backend = Promoting();

        Realize(new VScrollBar { Bounds = new(0, 0, 16, 200) }, backend);

        Assert.That(backend.LastScrollBar!.Vertical, Is.True);
    }

    [Test]
    public void The_range_set_before_realization_reaches_the_widget()
    {
        var backend = Promoting();
        var bar = new HScrollBar { Bounds = new(0, 0, 200, 16), Minimum = 5, Maximum = 80, LargeChange = 20, SmallChange = 3 };

        Realize(bar, backend);

        var peer = backend.LastScrollBar!;
        Assert.Multiple(() =>
        {
            Assert.That(peer.Minimum, Is.EqualTo(5));
            Assert.That(peer.Maximum, Is.EqualTo(80));
            Assert.That(peer.LargeChange, Is.EqualTo(20));
            Assert.That(peer.SmallChange, Is.EqualTo(3));
        });
    }

    [Test]
    public void A_range_change_after_realization_reaches_the_widget()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);

        bar.Maximum = 500;

        Assert.That(backend.LastScrollBar!.Maximum, Is.EqualTo(500));
    }

    [Test]
    public void A_programmatic_value_reaches_the_widget()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);

        bar.Value = 40;

        Assert.That(backend.LastScrollBar!.GetValue(), Is.EqualTo(40));
    }

    [Test]
    public void A_widget_scroll_moves_the_value()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);

        backend.LastScrollBar!.RaiseUserScroll(30, ScrollEventType.LargeIncrement);

        Assert.That(bar.Value, Is.EqualTo(30));
    }

    [Test]
    public void A_widget_scroll_carries_its_gesture_through_to_Scroll()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);
        var types = new List<ScrollEventType>();
        bar.Scroll += (_, e) => types.Add(e.Type);

        backend.LastScrollBar!.RaiseUserScroll(1, ScrollEventType.SmallIncrement);
        backend.LastScrollBar!.RaiseUserScroll(11, ScrollEventType.LargeIncrement);
        backend.LastScrollBar!.RaiseUserScroll(50, ScrollEventType.ThumbTrack);

        Assert.That(
            types,
            Is.EqualTo(new[] { ScrollEventType.SmallIncrement, ScrollEventType.LargeIncrement, ScrollEventType.ThumbTrack }));
    }

    [Test]
    public void A_widget_scroll_raises_Scroll_before_ValueChanged()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);
        var order = new List<string>();
        bar.Scroll += (_, _) => order.Add("scroll");
        bar.ValueChanged += (_, _) => order.Add("value");

        backend.LastScrollBar!.RaiseUserScroll(20, ScrollEventType.ThumbTrack);

        Assert.That(order, Is.EqualTo(new[] { "scroll", "value" }), "the owner-drawn path raises them in this order");
    }

    [Test]
    public void The_end_of_a_gesture_is_reported_without_moving_the_value()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);
        bar.Value = 25;
        var types = new List<ScrollEventType>();
        var valueChanges = 0;
        bar.Scroll += (_, e) => types.Add(e.Type);
        bar.ValueChanged += (_, _) => ++valueChanges;

        backend.LastScrollBar!.RaiseEndScroll();

        Assert.Multiple(() =>
        {
            Assert.That(types, Is.EqualTo(new[] { ScrollEventType.EndScroll }));
            Assert.That(valueChanges, Is.Zero);
            Assert.That(bar.Value, Is.EqualTo(25));
        });
    }

    [Test]
    public void A_widget_scroll_past_the_reachable_maximum_is_clamped_like_the_owner_drawn_bar()
    {
        var backend = Promoting();
        var bar = Realize(Bar(), backend);

        backend.LastScrollBar!.RaiseUserScroll(500, ScrollEventType.ThumbTrack);

        Assert.That(bar.Value, Is.EqualTo(91), "Maximum - LargeChange + 1, the Windows Forms convention");
    }
}
