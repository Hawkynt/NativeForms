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
    /// <summary>
    /// Whether showing the surface arms light dismiss — the pointer grab that routes the next click
    /// anywhere in the application to this surface so it can close itself. Defaults to
    /// <see langword="true"/>, which is what a menu, a drop-down list or a fly-out calendar wants: the
    /// click that closes them belongs to them.
    /// </summary>
    /// <remarks>
    /// A tooltip is the counter-example and must set this to <see langword="false"/>. It is a passive
    /// surface the user never aims at, and a grab would make it consume the very click the user meant
    /// for the control underneath — the control would neither take the focus nor see the press, and
    /// only the <em>second</em> click would work. A passive surface is taken down by whoever put it
    /// up (a tooltip hides on pointer-leave, on a press, or on its auto-pop delay) rather than by a
    /// grab, so it never competes for input.
    /// </remarks>
    bool LightDismiss { get; set; }

    /// <summary>Shows the surface at the given screen position with the given size, arming light
    /// dismiss unless <see cref="LightDismiss"/> was turned off.</summary>
    void ShowAt(Point screenLocation, Size size);

    /// <summary>Hides the surface and releases any grab, without raising <see cref="Dismissed"/>.</summary>
    void Hide();

    /// <summary>
    /// Raised when the user dismisses the surface: a click outside it, loss of the activation/grab
    /// that keeps it up, or Escape. The surface is hidden first, then the event is raised.
    /// </summary>
    event EventHandler? Dismissed;
}
