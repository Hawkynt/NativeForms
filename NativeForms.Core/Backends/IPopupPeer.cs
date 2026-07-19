using System.Drawing;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// A light-dismiss floating surface: the native side of ComboBox drop-downs, menus, tooltips and
/// calendar fly-outs. It is a full <see cref="ICanvasPeer"/>, so popup content is owner-drawn (and can
/// even host native children) exactly like any other surface — a backend implements drawing and input
/// once and the popup inherits it. Unlike a child canvas it floats above every window at a screen
/// position, does not steal activation from its owner, and dismisses itself when the user clicks
/// outside it, it loses the grab that routes that click to it, or Escape is pressed.
/// </summary>
public interface IPopupPeer : ICanvasPeer
{
    /// <summary>Shows the surface at the given screen position with the given size, arming light dismiss.</summary>
    void ShowAt(Point screenLocation, Size size);

    /// <summary>Hides the surface and releases any grab, without raising <see cref="Dismissed"/>.</summary>
    void Hide();

    /// <summary>
    /// Raised when the user dismisses the surface: a click outside it, loss of the activation/grab
    /// that keeps it up, or Escape. The surface is hidden first, then the event is raised.
    /// </summary>
    event EventHandler? Dismissed;
}
