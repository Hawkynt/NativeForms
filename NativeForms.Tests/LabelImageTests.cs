using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Label.Image"/> is the label's promotion gate (PRD §12): no platform static renders a
/// bitmap and a caption in one widget, so a label carrying an image gives up the widget and paints both
/// through the shared content layout. Clearing it takes the widget back, with every property intact.
/// </summary>
[TestFixture]
internal sealed class LabelImageTests {
  private static HeadlessBackend Realize(Label label) {
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(label);
    Application.Run(form, backend);
    return backend;
  }

  private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
      => backend.Created.OfType<HeadlessCanvasPeer>().First();

  [TearDown]
  public void RestorePromotion() => Application.PreferNativeWidgets = true;

  [Test]
  public void A_label_without_an_image_is_a_platform_widget() {
    var label = new Label { Text = "plain" };

    var backend = Realize(label);

    Assert.Multiple(() => {
      Assert.That(label.IsNativeWidget, Is.True);
      Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Any(), Is.True);
    });
  }

  [Test]
  public void An_image_moves_the_label_onto_the_painter() {
    var label = new Label { Text = "shown", Image = new HeadlessImage(16, 16) };

    var backend = Realize(label);

    Assert.Multiple(() => {
      Assert.That(label.IsNativeWidget, Is.False);
      Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Any(), Is.False, "the widget was never asked for");
    });
  }

  [Test]
  public void The_icon_leads_the_caption_and_both_are_drawn() {
    var label = new Label {
      Text = "Go",
      Bounds = new(0, 0, 200, 30),
      Image = new HeadlessImage(16, 16),
      TextAlign = ContentAlignment.MiddleLeft,
    };
    var canvas = CanvasOf(Realize(label));

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.Operations.Exists(static o => o.StartsWith("image 16x16 @0,7,16,16")), Is.True, "the icon leads, vertically centered");
      Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Go\"")), Is.True, "the caption is drawn beside it");
    });
  }

  [Test]
  public void The_relation_decides_which_side_the_icon_takes() {
    var label = new Label {
      Text = "Go",
      Bounds = new(0, 0, 200, 30),
      Image = new HeadlessImage(16, 16),
      TextAlign = ContentAlignment.MiddleLeft,
      TextImageRelation = TextImageRelation.TextBeforeImage,
    };
    var canvas = CanvasOf(Realize(label));

    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(static o => o.StartsWith("image 16x16 @0,")), Is.False, "the caption leads now, so the icon cannot start at the left edge");
  }

  [Test]
  public void A_caption_less_label_places_its_image_by_ImageAlign() {
    var label = new Label {
      Bounds = new(0, 0, 200, 30),
      Image = new HeadlessImage(16, 16),
      ImageAlign = ContentAlignment.TopRight,
    };
    var canvas = CanvasOf(Realize(label));

    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(static o => o.StartsWith("image 16x16 @184,0,16,16")), Is.True);
  }

  [Test]
  public void Clearing_the_image_hands_the_label_back_to_the_widget() {
    var label = new Label { Text = "kept", Image = new HeadlessImage(16, 16), BorderStyle = BorderStyle.FixedSingle };
    var backend = Realize(label);
    Assert.That(label.IsNativeWidget, Is.False, "guard: it starts on the painter");

    label.Image = null;

    Assert.Multiple(() => {
      Assert.That(label.IsNativeWidget, Is.True);
      Assert.That(label.Text, Is.EqualTo("kept"), "the swap is invisible to the application");
      Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Last().BorderStyle, Is.EqualTo(BorderStyle.FixedSingle));
    });
  }

  [Test]
  public void An_image_set_after_realization_swaps_the_peer_in_place() {
    var label = new Label { Text = "later" };
    Realize(label);
    Assert.That(label.IsNativeWidget, Is.True, "guard: it starts on the widget");

    label.Image = new HeadlessImage(16, 16);

    Assert.Multiple(() => {
      Assert.That(label.IsNativeWidget, Is.False);
      Assert.That(label.Text, Is.EqualTo("later"));
    });
  }

  [Test]
  public void A_label_pinned_to_the_painter_never_asks_for_the_widget() {
    Application.PreferNativeWidgets = false;
    var label = new Label { Text = "painted" };

    var backend = Realize(label);

    Assert.Multiple(() => {
      Assert.That(label.IsNativeWidget, Is.False);
      Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Any(), Is.False);
    });
  }

  /// <summary>
  /// The painted half has to underline the mnemonic the widget half underlines, and draw the caption
  /// without its mark-up — a label that eats the marked character instead of the ampersand looks
  /// almost right, which is exactly the defect that survives a glance at a screenshot.
  /// </summary>
  [Test]
  public void The_painted_caption_drops_the_ampersand_and_underlines_what_it_marked() {
    var label = new Label {
      Text = "&Go",
      Bounds = new(0, 0, 200, 30),
      Image = new HeadlessImage(16, 16),
      TextAlign = ContentAlignment.MiddleLeft,
    };
    var canvas = CanvasOf(Realize(label));

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"Go\"")), Is.True, "the ampersand is mark-up, not a glyph");
      Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"&Go\"")), Is.False);
      Assert.That(g.Operations.Exists(static o => o.StartsWith("line ")), Is.True, "the marked character is underlined");
    });
  }

  /// <summary>And with the convention turned off the ampersand is a character like any other.</summary>
  [Test]
  public void A_painted_caption_with_UseMnemonic_off_keeps_its_ampersand() {
    var label = new Label {
      Text = "&Go",
      Bounds = new(0, 0, 200, 30),
      Image = new HeadlessImage(16, 16),
      UseMnemonic = false,
    };
    var canvas = CanvasOf(Realize(label));

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.Operations.Exists(static o => o.StartsWith("text \"&Go\"")), Is.True);
      Assert.That(g.Operations.Exists(static o => o.StartsWith("line ")), Is.False);
    });
  }

  [Test]
  public void AutoSize_reserves_room_for_the_image_beside_the_caption() {
    var text = new Label { Text = "Go", AutoSize = true };
    Realize(text);
    var textOnly = text.Size;

    var iconed = new Label { Text = "Go", AutoSize = true, Image = new HeadlessImage(16, 16) };
    Realize(iconed);

    Assert.That(iconed.Size.Width, Is.EqualTo(textOnly.Width + ContentLayout.Gap + 16));
  }
}
