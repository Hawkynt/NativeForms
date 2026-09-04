using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Button.Image"/> (with <see cref="Button.ImageAlign"/> and
/// <see cref="Button.TextImageRelation"/>) on both halves of the button: forwarded to the peer as one
/// <c>SetImage</c> triple wherever the widget can express the face, and painted through the shared
/// <see cref="ContentLayout"/> only where it cannot (PRD §12).
/// </summary>
/// <remarks>
/// Whether a captioned image needs the painted half is the <em>backend's</em> answer
/// (<see cref="IPlatformBackend.ButtonRendersImageWithText"/>), not one rule for all three: GTK and
/// AppKit render both and keep the widget, a classic Win32 <c>BUTTON</c> drops the caption and falls
/// back. The fake says no by default, which is the Win32 shape and so the one whose fallback needs
/// covering.
/// </remarks>
[TestFixture]
internal sealed class ButtonImageTests {
  private static HeadlessButtonPeer Realize(Button button) {
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(button);
    Application.Run(form, backend);
    return backend.Created.OfType<HeadlessButtonPeer>().Single();
  }

  private static HeadlessCanvasPeer RealizeDrawn(Button button, out HeadlessBackend backend) {
    backend = new HeadlessBackend(); // says no to image+text, like the classic Win32 button
    var form = new Form();
    form.Controls.Add(button);
    Application.Run(form, backend);
    return backend.Created.OfType<HeadlessCanvasPeer>().Single();
  }

  // --- The widget half ---------------------------------------------------------------------------

  [Test]
  public void An_image_without_a_caption_is_flushed_to_the_peer_on_realization() {
    var image = new HeadlessImage(16, 16);
    var button = new Button { Image = image };

    var peer = Realize(button);

    Assert.Multiple(() => {
      Assert.That(button.IsNativeWidget, Is.True, "every platform centres a bare image on a button, so the widget can say it");
      Assert.That(peer.Image, Is.SameAs(image));
      Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.MiddleCenter), "WinForms default");
      Assert.That(peer.ImageRelation, Is.EqualTo(TextImageRelation.ImageBeforeText));
    });
  }

  [Test]
  public void Alignment_and_relation_changes_are_forwarded() {
    var button = new Button { Image = new HeadlessImage(16, 16) };
    var peer = Realize(button);

    button.ImageAlign = ContentAlignment.TopLeft;
    button.TextImageRelation = TextImageRelation.ImageAboveText;

    Assert.Multiple(() => {
      Assert.That(peer.ImageAlign, Is.EqualTo(ContentAlignment.TopLeft));
      Assert.That(peer.ImageRelation, Is.EqualTo(TextImageRelation.ImageAboveText));
    });
  }

  [Test]
  public void Clearing_the_image_reaches_the_peer() {
    var button = new Button { Image = new HeadlessImage(16, 16) };
    var peer = Realize(button);

    button.Image = null;

    Assert.That(peer.Image, Is.Null);
  }

  [Test]
  public void A_caption_alone_keeps_the_widget() {
    var button = new Button { Text = "Go" };

    Realize(button);

    Assert.That(button.IsNativeWidget, Is.True);
  }

  // --- The gate ----------------------------------------------------------------------------------

  [Test]
  public void An_image_beside_a_caption_gives_up_the_widget_only_where_the_widget_cannot_draw_it() {
    var button = new Button { Text = "Go", Image = new HeadlessImage(16, 16) };

    RealizeDrawn(button, out var backend);

    Assert.Multiple(() => {
      Assert.That(button.IsNativeWidget, Is.False, "this backend's button drops the caption, so the face is painted instead");
      Assert.That(backend.Created.OfType<HeadlessButtonPeer>(), Is.Empty, "and no widget was asked for");
    });
  }

  [Test]
  public void A_backend_whose_button_draws_both_keeps_the_widget() {
    var image = new HeadlessImage(16, 16);
    var button = new Button { Text = "Go", Image = image };
    var backend = new HeadlessBackend { OfferButtonImageWithText = true };
    var form = new Form();
    form.Controls.Add(button);
    Application.Run(form, backend);

    Assert.Multiple(() => {
      Assert.That(button.IsNativeWidget, Is.True, "the widget is the faster path and this one can say it — GTK and AppKit both do");
      Assert.That(backend.Created.OfType<HeadlessCanvasPeer>(), Is.Empty, "so nothing was painted");
      Assert.That(backend.Created.OfType<HeadlessButtonPeer>().Single().Image, Is.SameAs(image));
    });
  }

  [Test]
  public void The_same_button_takes_different_halves_on_different_desktops() {
    Assert.That(Native(canDrawBoth: true), Is.True);
    Assert.That(Native(canDrawBoth: false), Is.False, "one control, one configuration, and the answer is the backend's");

    static bool Native(bool canDrawBoth) {
      var button = new Button { Text = "Go", Image = new HeadlessImage(16, 16) };
      var form = new Form();
      form.Controls.Add(button);
      Application.Run(form, new HeadlessBackend { OfferButtonImageWithText = canDrawBoth });
      return button.IsNativeWidget;
    }
  }

  [Test]
  public void Adding_a_caption_to_an_image_button_moves_it_onto_the_canvas_where_the_widget_cannot_hold_both() {
    var button = new Button { Image = new HeadlessImage(16, 16) };
    Realize(button);
    Assume.That(button.IsNativeWidget, Is.True);

    button.Text = "Go";

    Assert.That(button.IsNativeWidget, Is.False, "the swap runs on a live control, not only at realization");
  }

  [Test]
  public void Taking_the_image_away_again_moves_it_back_onto_the_widget() {
    var button = new Button { Text = "Go", Image = new HeadlessImage(16, 16) };
    RealizeDrawn(button, out _);
    Assume.That(button.IsNativeWidget, Is.False);

    button.Image = null;

    Assert.That(button.IsNativeWidget, Is.True, "the gate swings both ways and the application never sees it");
  }

  [Test]
  public void A_button_kept_off_the_widget_path_paints_even_without_an_image() {
    var button = new Button { Text = "Go", UseNativeWidget = false };

    var canvas = RealizeDrawn(button, out _);
    var painted = canvas.RaisePaint();

    Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Contain("Go"));
  }

  // --- The painted half --------------------------------------------------------------------------

  [Test]
  public void The_painted_face_draws_the_image_and_the_caption() {
    var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };

    var canvas = RealizeDrawn(button, out _);
    var painted = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(painted.Operations.Count(static op => op.StartsWith("image ")), Is.EqualTo(1));
      Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Contain("Go"));
    });
  }

  [Test]
  public void The_relation_decides_which_side_the_image_leads_on() {
    Assert.That(ImageLeft(TextImageRelation.ImageBeforeText), Is.LessThan(ImageLeft(TextImageRelation.TextBeforeImage)));

    static int ImageLeft(TextImageRelation relation) {
      var button = new Button {
        Bounds = new(0, 0, 160, 30),
        Text = "Go",
        Image = new HeadlessImage(16, 16),
        TextImageRelation = relation,
      };

      var canvas = RealizeDrawn(button, out _);
      var op = canvas.RaisePaint().Operations.First(static op => op.StartsWith("image "));
      return int.Parse(op.Split('@')[^1].Split(',')[0]);
    }
  }

  [Test]
  public void The_mnemonic_is_stripped_from_the_painted_caption_and_underlined() {
    var plain = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
    var marked = new Button { Bounds = new(0, 0, 120, 30), Text = "&Go", Image = new HeadlessImage(16, 16) };

    var plainPaint = RealizeDrawn(plain, out _).RaisePaint();
    var markedPaint = RealizeDrawn(marked, out _).RaisePaint();

    Assert.Multiple(() => {
      Assert.That(markedPaint.TextDraws.Select(static draw => draw.Text), Does.Contain("Go"), "the ampersand is mark-up, not a glyph");
      Assert.That(
          markedPaint.Operations.Count(static op => op.StartsWith("line ")),
          Is.EqualTo(plainPaint.Operations.Count(static op => op.StartsWith("line ")) + 1),
          "and the marked character gains an underline");
    });
  }

  [Test]
  public void The_painted_face_clicks_like_the_widget_does() {
    var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
    var clicks = 0;
    button.Click += (_, _) => ++clicks;
    var canvas = RealizeDrawn(button, out _);

    canvas.RaiseMouseDown(20, 15);
    canvas.RaiseMouseUp(20, 15);

    Assert.That(clicks, Is.EqualTo(1));
  }

  [Test]
  public void A_press_released_off_the_face_is_a_cancelled_click() {
    var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
    var clicks = 0;
    button.Click += (_, _) => ++clicks;
    var canvas = RealizeDrawn(button, out _);

    canvas.RaiseMouseDown(20, 15);
    canvas.RaiseMouseUp(400, 15);

    Assert.That(clicks, Is.Zero, "every platform button lets a press be taken back by sliding off it");
  }

  [Test]
  public void Space_and_Enter_work_the_painted_face_on_the_key_release() {
    var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
    var clicks = 0;
    button.Click += (_, _) => ++clicks;
    var canvas = RealizeDrawn(button, out _);

    canvas.RaiseKeyDown(Keys.Space);
    var afterDown = clicks;
    canvas.RaiseKeyUp(Keys.Space);
    canvas.RaiseKeyUp(Keys.Enter);

    Assert.Multiple(() => {
      Assert.That(afterDown, Is.Zero, "a held key must not auto-repeat the click");
      Assert.That(clicks, Is.EqualTo(2));
    });
  }

  [Test]
  public void A_painted_button_still_reports_its_DialogResult_to_the_form() {
    var button = new Button {
      Bounds = new(0, 0, 120, 30),
      Text = "OK",
      Image = new HeadlessImage(16, 16),
      DialogResult = DialogResult.OK,
    };

    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(button);
    Application.Run(form, backend);
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    canvas.RaiseMouseDown(20, 15);
    canvas.RaiseMouseUp(20, 15);

    Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK), "the dialog contract belongs to the control, not to the peer");
  }

  // --- The face itself ---------------------------------------------------------------------------

  [Test]
  public void A_desktop_that_rounds_its_buttons_gets_a_rounded_painted_face() {
    var square = FaceOps(0);
    var rounded = FaceOps(6);

    Assert.Multiple(() => {
      Assert.That(square.Any(static op => op.StartsWith("round ")), Is.False, "a theme reporting 0 keeps the plain rectangle");
      Assert.That(rounded.Any(static op => op.Contains("r6")), Is.True, "and one reporting a radius is drawn to it");
      Assert.That(
          rounded.Any(static op => op.StartsWith("rect ")),
          Is.False,
          "nothing square is left on a rounded face — a square focus ring inside a rounded frame is the same tell");
    });

    static List<string> FaceOps(int radius) {
      var button = new Button { Bounds = new(0, 0, 120, 30), Text = "Go", Image = new HeadlessImage(16, 16) };
      var backend = new HeadlessBackend { Theme = new StubTheme { ButtonCornerRadius = radius } };
      var form = new Form();
      form.Controls.Add(button);
      Application.Run(form, backend);
      return backend.Created.OfType<HeadlessCanvasPeer>().Single().RaisePaint().Operations;
    }
  }
}
