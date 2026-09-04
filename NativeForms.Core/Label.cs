using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A non-interactive line of static text, backed by the platform's native label widget. Supports
/// WinForms-style <see cref="AutoSize"/>, <see cref="TextAlign"/>, <see cref="BorderStyle"/>,
/// mnemonic rendering (<see cref="UseMnemonic"/>) and an <see cref="Image"/> beside the caption.
/// </summary>
/// <remarks>
/// The widget is used whenever it can express what the label is showing, which is every label without
/// an image (PRD §12). No platform static renders a bitmap and a caption together — Win32's
/// <c>SS_BITMAP</c> is image-only, GTK swaps the whole widget for a <c>GtkImage</c>, Cocoa for an
/// <c>NSImageView</c> — so a label carrying an image gives up the widget and is drawn instead, through
/// the same <see cref="Drawing.ContentLayout"/> geometry <see cref="Button"/>, <see cref="CheckBox"/>
/// and <see cref="GroupBox"/> use. The gate is the image alone, not the image-with-a-caption case: an
/// image-only label would otherwise be a native widget on one backend and a painted surface on the
/// next, and the same control has to look the same everywhere.
/// </remarks>
public class Label : OwnerDrawnControl {
  private ILabelPeer? _labelPeer;

  /// <summary>Static text never takes keyboard focus (and so never joins the tab order).</summary>
  protected override bool Focusable => false;

  /// <summary>
  /// When <see langword="true"/>, the label sizes itself to fit its content in the theme's default
  /// font. The size is computed through the backend's text measurement on realization and again on
  /// every <see cref="Control.Text"/> change; before realization the wish is simply buffered.
  /// Defaults to <see langword="false"/>, matching Windows Forms.
  /// </summary>
  public bool AutoSize {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      this.ApplyAutoSize();
    }
  }

  /// <summary>
  /// Where the text sits within the label's bounds. Win32 static controls honor the horizontal
  /// component plus a coarse vertical centering only; GTK honors all nine anchors. A label carrying
  /// an image is painted, and honors all nine everywhere.
  /// </summary>
  public ContentAlignment TextAlign {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      _labelPeer?.SetTextAlign(value);
      this.Invalidate();
    }
  }

  /// <summary>
  /// The border drawn around the label — <see cref="BorderStyle.None"/> or
  /// <see cref="BorderStyle.FixedSingle"/>. Rendered natively on Win32 (<c>WS_BORDER</c>); GTK has
  /// no native label frame, so the value is not rendered there. A painted label draws it from the
  /// theme on every backend.
  /// </summary>
  public BorderStyle BorderStyle {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      _labelPeer?.SetBorderStyle(value);
      this.Invalidate();
    }
  } = BorderStyle.None;

  /// <summary>
  /// Whether <c>&amp;</c> in <see cref="Control.Text"/> marks the following character as a mnemonic
  /// and renders it underlined (<c>&amp;&amp;</c> escapes a literal ampersand). Alt+mnemonic
  /// focuses the next tab stop after the label through the owning form's dialog-key chain — fed by
  /// owner-drawn surfaces; keys held inside native widgets cannot trigger it yet. Defaults to
  /// <see langword="true"/>.
  /// </summary>
  public bool UseMnemonic {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      _labelPeer?.SetUseMnemonic(value);
      this.Invalidate();
    }
  } = true;

  /// <summary>
  /// The image shown beside the caption, or <see langword="null"/>. Assigning one moves the label off
  /// the platform's static widget and onto the painter, because no platform static renders a bitmap
  /// and a caption together; clearing it moves the label back. The swap is invisible to the
  /// application — every property keeps its value across it.
  /// </summary>
  public IImage? Image {
    get => field;
    set {
      if (field == value)
        return;

      field = value;

      // The image is what the gate is on, so assigning or clearing one is exactly the change that
      // can move this control between a widget and the painter.
      if (this.IsRealized && this.IsNativeWidget != this.WouldBeNative)
        this.RerealizePeer();

      this.TrackImageAnimation(value, this.OnImageFrame);
      this.PushImage();
      this.ApplyAutoSize();
      this.Invalidate();
    }
  }

  /// <summary>
  /// Where the image anchors within the label's bounds when it is the only content — a caption-less
  /// label places its image by this rather than by <see cref="TextAlign"/>, matching Windows Forms.
  /// Defaults to <see cref="ContentAlignment.MiddleCenter"/>.
  /// </summary>
  public ContentAlignment ImageAlign {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      this.PushImage();
      this.Invalidate();
    }
  } = ContentAlignment.MiddleCenter;

  /// <summary>
  /// How the image sits relative to the caption. Defaults to
  /// <see cref="TextImageRelation.ImageBeforeText"/> — the icon leads, the caption follows.
  /// </summary>
  public TextImageRelation TextImageRelation {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      this.ApplyAutoSize();
      this.Invalidate();
    }
  } = TextImageRelation.ImageBeforeText;

  /// <summary>Whether this label is currently rendered by a real platform widget.</summary>
  public override bool IsNativeWidget => _labelPeer is not null;

  /// <summary>
  /// Whether the current property values are all expressible by a platform static. Everything is
  /// except an <see cref="Image"/>: none of the three renders a bitmap and a caption in one widget,
  /// and the image-only renderings they do offer disagree with each other about placement.
  /// </summary>
  private bool IsNativeEligible => this.Image is null;

  /// <summary>What <see cref="IsNativeWidget"/> would be if the peer were built right now.</summary>
  private bool WouldBeNative => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible;

  /// <summary>
  /// The label's uppercased mnemonic character — the one after a single <c>&amp;</c> in
  /// <see cref="Control.Text"/> (<c>&amp;&amp;</c> escapes) — or <c>'\0'</c> when there is none or
  /// <see cref="UseMnemonic"/> is off.
  /// </summary>
  internal char Mnemonic => this.UseMnemonic ? Mnemonics.CharOf(this.Text) : '\0';

  /// <inheritdoc/>
  private protected override IControlPeer CreatePeer(IPlatformBackend backend) {
    if (this.WouldBeNative) {
      var peer = backend.CreateLabel();
      _labelPeer = peer;
      return peer;
    }

    return base.CreatePeer(backend);
  }

  /// <inheritdoc/>
  private protected override void OnRealized(IControlPeer peer) {
    base.OnRealized(peer);

    if (peer is ILabelPeer label) {
      label.SetTextAlign(this.TextAlign);
      label.SetBorderStyle(this.BorderStyle);
      label.SetUseMnemonic(this.UseMnemonic);
    }

    this.PushImage();

    // After the base, which unsubscribes on the way in: a backend now exists, so an animated image
    // assigned before realization can finally subscribe — to whichever half is going to draw it.
    this.TrackImageAnimation(this.Image, this.OnImageFrame);
    this.ApplyAutoSize();
  }

  /// <summary>Pushes the image to the peer, resolving an animated image to its current frame — the
  /// shared clock calls this again as the frame advances.</summary>
  private void PushImage() => _labelPeer?.SetImage(this.CurrentFrameOf(this.Image), this.ImageAlign);

  /// <summary>One frame of an animated image has come round: the widget half is re-pushed, the
  /// painted half is repainted, and the label is only ever one of the two.</summary>
  private void OnImageFrame() {
    if (_labelPeer is not null)
      this.PushImage();
    else
      this.Invalidate();
  }

  /// <inheritdoc/>
  private protected override void OnUnrealized() {
    _labelPeer = null;
    base.OnUnrealized();
  }

  /// <inheritdoc/>
  protected override void OnTextChanged(EventArgs e) {
    base.OnTextChanged(e);
    this.ApplyAutoSize();
  }

  /// <inheritdoc/>
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    var theme = this.Theme;
    var font = this.Font;
    var client = this.DisplayRectangle;
    g.FillRectangle(this.BackColor, new Rectangle(0, 0, this.Width, this.Height));

    if (this.BorderStyle == BorderStyle.FixedSingle)
      g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));

    var text = this.Text;
    var caption = this.UseMnemonic ? Mnemonics.Strip(text) : text;
    var color = this.Enabled ? this.ForeColor : theme.DisabledText;
    var alignment = this.IsRightToLeft ? RtlLayout.Mirror(this.TextAlign) : this.TextAlign;
    if (this.Image is not { } image) {
      this.PaintCaption(g, font, color, text, caption, client, alignment);
      return;
    }

    // Right-to-left mirrors which side the icon leads on, exactly like the CheckBox face.
    var relation = this.IsRightToLeft ? RtlLayout.Mirror(this.TextImageRelation) : this.TextImageRelation;
    ContentLayout.Arrange(
        client,
        new Size(image.Width, image.Height),
        caption.Length == 0 ? Size.Empty : g.MeasureText(caption, font),
        relation,
        caption.Length == 0 ? this.ImageAlign : alignment,
        out var imageRect,
        out var textRect);

    g.DrawImage(this.CurrentFrameOf(image)!, imageRect);
    if (caption.Length > 0)
      this.PaintCaption(g, font, color, text, caption, textRect, ContentAlignment.MiddleCenter);
  }

  /// <summary>
  /// Draws the caption with its mnemonic underlined, in the box the layout gave it. The ampersand is
  /// a mark-up character, so it is removed before measuring as well as before drawing — measuring the
  /// raw string would reserve room for a glyph nobody ever sees.
  /// </summary>
  private void PaintCaption(IGraphics g, Font font, Color color, string text, string caption, Rectangle bounds, ContentAlignment alignment) {
    g.DrawText(caption, font, color, bounds, alignment);
    if (this.UseMnemonic)
      Mnemonics.Underline(g, text, caption, font, color, bounds, alignment);
  }

  /// <summary>Resizes the label to its measured content when <see cref="AutoSize"/> is on and a
  /// backend exists.</summary>
  private void ApplyAutoSize() {
    if (!this.AutoSize || this.Backend is not { } backend)
      return;

    var caption = this.Text;
    var textSize = backend.MeasureText(caption, backend.Theme.DefaultFont);
    if (this.Image is not { } image) {
      this.Size = textSize;
      return;
    }

    var imageSize = new Size(image.Width, image.Height);
    if (caption.Length == 0) {
      this.Size = imageSize;
      return;
    }

    this.Size = this.TextImageRelation is TextImageRelation.ImageBeforeText or TextImageRelation.TextBeforeImage
        ? new(imageSize.Width + ContentLayout.Gap + textSize.Width, Math.Max(imageSize.Height, textSize.Height))
        : new(Math.Max(imageSize.Width, textSize.Width), imageSize.Height + ContentLayout.Gap + textSize.Height);
  }
}
