using System.Text;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 peer for a promoted <see cref="LinkLabel"/> — a common-controls <c>SysLink</c> (PRD §12), so
/// the OS supplies the link colour, the underline, the visited shade, the hand cursor and the keyboard
/// activation.
/// </summary>
/// <remarks>
/// A <c>SysLink</c> takes a tiny markup dialect rather than plain text, so the caption is wrapped in a
/// single anchor spanning all of it — which is exactly what this control models — and the characters that
/// would otherwise start a tag are escaped. The anchor deliberately carries no <c>href</c>: the control
/// reports <c>NM_CLICK</c>/<c>NM_RETURN</c> either way, and without one there is nothing for the shell to
/// launch behind the application's back.
/// </remarks>
internal sealed class LinkLabelPeer : Win32ChildPeer, ILinkLabelPeer {
  private bool _visited;

  /// <inheritdoc/>
  public event EventHandler? LinkActivated;

  /// <inheritdoc/>
  protected override string WindowClass => NativeMethods.WC_LINK;

  /// <inheritdoc/>
  protected override uint ExtraStyle => NativeMethods.WS_TABSTOP;

  /// <inheritdoc/>
  /// <remarks>Stores the plain caption, but hands the control the marked-up form.</remarks>
  public override void SetText(string text) {
    _text = text ?? string.Empty;
    if (Handle != 0)
      NativeMethods.SetWindowTextW(Handle, Markup(_text));
  }

  /// <inheritdoc/>
  public unsafe void SetVisited(bool visited) {
    _visited = visited;
    if (Handle == 0)
      return;

    var item = new NativeMethods.LITEM {
      mask = NativeMethods.LIF_ITEMINDEX | NativeMethods.LIF_STATE,
      iLink = 0,
      state = visited ? NativeMethods.LIS_VISITED : 0,
      stateMask = NativeMethods.LIS_VISITED,
    };

    NativeMethods.SendMessageW(Handle, NativeMethods.LM_SETITEM, 0, (nint)(&item));
  }

  /// <inheritdoc/>
  internal override void CreateChildHandle(nint parent, int controlId) {
    base.CreateChildHandle(parent, controlId);

    // The base flush wrote the plain caption; replace it with the anchor, then restore the state.
    NativeMethods.SetWindowTextW(Handle, Markup(_text));
    this.SetVisited(_visited);
  }

  /// <inheritdoc/>
  internal override void OnNotify(int code, nint lParam) {
    if (code is NativeMethods.NM_CLICK or NativeMethods.NM_RETURN)
      LinkActivated?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>Wraps the caption in one anchor, escaping what the control would otherwise read as markup.</summary>
  private static string Markup(string text) {
    var builder = new StringBuilder(text.Length + 8).Append("<a>");
    foreach (var c in text)
      switch (c) {
        case '<':
          builder.Append("&lt;");
          break;
        case '>':
          builder.Append("&gt;");
          break;
        case '&':
          builder.Append("&amp;");
          break;
        default:
          builder.Append(c);
          break;
      }

    return builder.Append("</a>").ToString();
  }
}
