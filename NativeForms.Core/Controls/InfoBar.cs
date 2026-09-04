using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>The tone of an <see cref="InfoBar"/> — sets its accent stripe, icon and tint.</summary>
public enum InfoBarSeverity {
  /// <summary>Neutral information (accent).</summary>
  Info,

  /// <summary>A success confirmation (green).</summary>
  Success,

  /// <summary>A warning (amber).</summary>
  Warning,

  /// <summary>An error (red).</summary>
  Error,
}

/// <summary>
/// An inline, dismissible message strip: a severity stripe and icon, a bold title, a message, an
/// optional action link and a close (×) button — the in-window banner WinForms never had (its only
/// notification surface is the OS tray <see cref="NotifyIcon"/>). Owner-drawn and native-themed;
/// <see cref="Closed"/> fires when the user dismisses it, <see cref="ActionClicked"/> when the link is hit.
/// </summary>
public class InfoBar : OwnerDrawnControl {
  private const int _Stripe = 4;
  private const int _IconZone = 30;
  private const int _CloseZone = 26;
  private const int _Pad = 8;

  /// <summary>The bar's tone. Setting it repaints.</summary>
  public InfoBarSeverity Severity {
    get => field;
    set { if (field != value) { field = value; this.Invalidate(); } }
  }

  /// <summary>The bold leading title.</summary>
  public string Title {
    get => field;
    set { value ??= string.Empty; if (field != value) { field = value; this.Invalidate(); } }
  } = string.Empty;

  /// <summary>The message text after the title.</summary>
  public string Message {
    get => field;
    set { value ??= string.Empty; if (field != value) { field = value; this.Invalidate(); } }
  } = string.Empty;

  /// <summary>An optional action link shown before the close button; empty hides it.</summary>
  public string ActionText {
    get => field;
    set { value ??= string.Empty; if (field != value) { field = value; this.Invalidate(); } }
  } = string.Empty;

  /// <summary>Whether the close (×) button is shown. Defaults to <see langword="true"/>.</summary>
  public bool ShowCloseButton {
    get => field;
    set { if (field != value) { field = value; this.Invalidate(); } }
  } = true;

  /// <summary>The bar's opacity in [0, 1], used by the <see cref="Toast"/> fade animation. Every drawn
  /// colour is blended toward the parent background as this drops, so it fades regardless of whether the
  /// backend composites child alpha. Defaults to 1 (opaque).</summary>
  public double Opacity {
    get => field;
    set { value = Math.Clamp(value, 0, 1); if (field != value) { field = value; this.Invalidate(); } }
  } = 1.0;

  private Color Fade(Color c)
      => this.Opacity >= 1.0 ? c : Blend(c, this.Parent?.BackColor ?? this.Theme.WindowBackground, this.Opacity);

  /// <summary>Raised when the close button is clicked; the bar hides itself first.</summary>
  public event EventHandler? Closed;

  /// <summary>Raised when the action link is clicked.</summary>
  public event EventHandler? ActionClicked;

  /// <summary>Raises <see cref="Closed"/>.</summary>
  protected virtual void OnClosed(EventArgs e) => this.Closed?.Invoke(this, e);

  /// <summary>Raises <see cref="ActionClicked"/>.</summary>
  protected virtual void OnActionClicked(EventArgs e) => this.ActionClicked?.Invoke(this, e);

  private Color SeverityColor => this.Severity switch {
    InfoBarSeverity.Success => Color.FromArgb(0x2E, 0x7D, 0x32),
    InfoBarSeverity.Warning => Color.FromArgb(0xE8, 0x8A, 0x00),
    InfoBarSeverity.Error => GlyphRenderer.Warning,
    _ => this.Theme.Accent,
  };

  private Rectangle CloseRect => new(this.Width - _CloseZone, 0, _CloseZone, this.Height);
  private int ActionWidth => this.ActionText.Length == 0 ? 0 : (this.ActionText.Length * 7) + (2 * _Pad);
  private Rectangle ActionRect => new(this.Width - (this.ShowCloseButton ? _CloseZone : _Pad) - this.ActionWidth, 0, this.ActionWidth, this.Height);

  /// <inheritdoc/>
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    var theme = this.Theme;
    var accent = this.SeverityColor;

    // A light tint of the severity colour over the control background, plus a full frame and stripe.
    var tint = Blend(accent, theme.ControlBackground, 0.14);
    g.FillRectangle(this.Fade(tint), new Rectangle(0, 0, this.Width, this.Height));
    g.FillRectangle(this.Fade(accent), new Rectangle(0, 0, _Stripe, this.Height));
    g.DrawRectangle(this.Fade(theme.Border), new Rectangle(0, 0, this.Width - 1, this.Height - 1));

    // The severity icon: a filled disc with a white glyph.
    var mid = this.Height / 2;
    var disc = new Rectangle(_Stripe + _Pad, mid - 8, 16, 16);
    g.FillEllipse(this.Fade(accent), disc);
    g.DrawText(this.Severity switch {
      InfoBarSeverity.Success => "✓",
      InfoBarSeverity.Warning => "!",
      InfoBarSeverity.Error => "×",
      _ => "i",
    }, theme.DefaultFont, this.Fade(Color.White), disc, ContentAlignment.MiddleCenter);

    var textLeft = _Stripe + _IconZone + _Pad;
    var textRight = (this.ActionText.Length > 0 ? this.ActionRect.X : this.ShowCloseButton ? this.CloseRect.X : this.Width) - _Pad;
    var textRect = new Rectangle(textLeft, 0, Math.Max(0, textRight - textLeft), this.Height);

    if (this.Title.Length > 0) {
      var titleWidth = g.MeasureText(this.Title, theme.DefaultFont).Width;
      g.DrawText(this.Title, theme.DefaultFont, this.Fade(theme.ControlText), textRect, ContentAlignment.MiddleLeft);
      textRect = new Rectangle(textRect.X + titleWidth + _Pad, textRect.Y, Math.Max(0, textRect.Width - titleWidth - _Pad), textRect.Height);
    }

    if (this.Message.Length > 0)
      g.DrawText(this.Message, theme.DefaultFont, this.Fade(theme.ControlText), textRect, ContentAlignment.MiddleLeft);

    if (this.ActionText.Length > 0)
      g.DrawText(this.ActionText, theme.DefaultFont, this.Fade(theme.Accent), this.ActionRect, ContentAlignment.MiddleCenter);

    if (this.ShowCloseButton) {
      var c = this.CloseRect;
      var box = new Rectangle(c.X + ((c.Width - 8) / 2), mid - 4, 8, 8);
      g.DrawLine(this.Fade(theme.ControlText), box.Left, box.Top, box.Right, box.Bottom);
      g.DrawLine(this.Fade(theme.ControlText), box.Left, box.Bottom, box.Right, box.Top);
    }
  }

  /// <inheritdoc/>
  protected override void OnMouseDown(MouseEventArgs e) {
    if (e.Button != MouseButtons.Left)
      return;

    if (this.ShowCloseButton && this.CloseRect.Contains(e.Location)) {
      this.Visible = false;
      this.OnClosed(EventArgs.Empty);
      return;
    }

    if (this.ActionText.Length > 0 && this.ActionRect.Contains(e.Location))
      this.OnActionClicked(EventArgs.Empty);
  }

  private static Color Blend(Color a, Color b, double t)
      => Color.FromArgb(
          255,
          (int)Math.Round((a.R * t) + (b.R * (1 - t))),
          (int)Math.Round((a.G * t) + (b.G * (1 - t))),
          (int)Math.Round((a.B * t) + (b.B * (1 - t))));
}
