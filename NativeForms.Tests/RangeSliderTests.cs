using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="RangeSlider"/> keeps its two thumbs ordered, drags each independently, nudges the
/// active thumb with the arrow keys and fills the span between the thumbs in the accent.
/// </summary>
[TestFixture]
internal sealed class RangeSliderTests {
  private static RangeSlider Create(out HeadlessCanvasPeer canvas) {
    var slider = new RangeSlider { Bounds = new(0, 0, 200, 24), Minimum = 0, Maximum = 100 };
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(slider);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
    return slider;
  }

  [Test]
  public void The_lower_value_can_never_pass_the_upper() {
    var slider = Create(out _);
    slider.UpperValue = 50;

    slider.LowerValue = 90; // asked past the upper thumb

    Assert.That(slider.LowerValue, Is.EqualTo(50), "the lower thumb is clamped to the upper value");
  }

  [Test]
  public void Dragging_the_lower_thumb_moves_the_lower_value() {
    var slider = Create(out var canvas); // lower thumb sits at x≈8
    var changes = 0;
    slider.RangeChanged += (_, _) => ++changes;

    canvas.RaiseMouseDown(8, 12);   // grab the lower thumb
    canvas.RaiseMouseMove(44, 12);  // drag it a fifth of the way in
    canvas.RaiseMouseUp(44, 12);

    Assert.Multiple(() => {
      Assert.That(slider.LowerValue, Is.EqualTo(20).Within(1));
      Assert.That(slider.UpperValue, Is.EqualTo(100), "the upper thumb stays put");
      Assert.That(changes, Is.GreaterThan(0));
    });
  }

  [Test]
  public void Arrow_keys_nudge_the_last_touched_thumb() {
    var slider = Create(out var canvas);
    slider.UpperValue = 60;

    canvas.RaiseMouseDown(this_upperX(slider), 12); // touch the upper thumb
    canvas.RaiseMouseUp(this_upperX(slider), 12);
    canvas.RaiseKeyDown(Keys.Right);

    Assert.That(slider.UpperValue, Is.EqualTo(61), "the upper thumb (last touched) is nudged");
  }

  [Test]
  public void The_span_between_the_thumbs_is_filled_with_the_accent() {
    var slider = Create(out var canvas);
    slider.LowerValue = 20;
    slider.UpperValue = 80;

    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4") && !o.EndsWith(",14")), Is.True, "the span groove fill is the accent");
  }

  // The upper thumb centre for a 200-px, 0..N slider: 8 + 184·value/100.
  private static int this_upperX(RangeSlider slider) => 8 + (184 * slider.UpperValue / 100);
}
