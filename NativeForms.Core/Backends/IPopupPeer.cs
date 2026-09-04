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
public interface IPopupPeer : ICanvasPeer {
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

  /// <summary>
  /// Resizes an already-shown surface in place, leaving its position, its grab and its stacking
  /// alone. A filtered menu shrinks as it narrows, and re-showing it to do that would hand the grab
  /// round again mid-gesture — which is exactly the churn multi-level light dismiss is fragile to.
  /// A backend that cannot resize a mapped surface keeps the size it opened with, hence the no-op
  /// default.
  /// </summary>
  void Resize(Size size) { }

  /// <summary>Hides the surface and releases any grab, without raising <see cref="Dismissed"/>.</summary>
  void Hide();

  /// <summary>
  /// Tells the surface that its light-dismiss grab is about to be handed off to a child popup it is
  /// opening, so the resulting grab loss is expected and must not dismiss it — consumed by the next
  /// grab loss. This is what lets a menu cascade open a submenu without the parent level closing.
  /// A surface that takes no grab (a tooltip) needs nothing here, hence the no-op default.
  /// </summary>
  void ExpectGrabHandoff() { }

  /// <summary>Re-takes the light-dismiss grab a now-closed child popup held, so this surface again
  /// catches outside clicks and Escape. A no-op if it already holds the grab, is hidden, or never grabs.</summary>
  void Regrab() { }

  /// <summary>
  /// Anchors this surface to another popup as its parent instead of the owning window — the relationship
  /// a submenu has to the menu level that opened it. On a stacked-popup display server (Wayland) a nested
  /// popup must chain to the top-most mapped popup, not the root window, or the server refuses to map it
  /// and the submenu never appears. Must be set before <see cref="ShowAt"/>. A backend whose popups are
  /// free-floating top-levels (Win32) needs nothing here, hence the no-op default.
  /// </summary>
  void SetParentPopup(IPopupPeer parent) { }

  /// <summary>
  /// Offered a press that landed outside this surface, in screen coordinates, before it light-dismisses.
  /// The owner returns <see langword="true"/> when it consumed the press — a menu whose deepest level holds
  /// the grab uses this to recognize a click on a <em>shallower</em> level of the same cascade and route it
  /// there rather than tearing the whole menu down. A false result, or no handler, dismisses as usual.
  /// Without it the grab that catches genuine outside clicks also swallows clicks on the parent menu.
  /// </summary>
  Func<Point, bool>? OutsidePress { get; set; }

  /// <summary>
  /// Offered pointer motion that landed outside this surface, in screen coordinates. The grab that keeps
  /// a menu's deepest level catching outside clicks also redirects motion over shallower levels to it, so
  /// without this hook moving back onto a parent menu would neither re-highlight its rows nor open a
  /// sibling submenu — the hover would freeze the moment a submenu opened. A backend delivers this only
  /// when its grab reports out-of-surface motion (GTK); one whose capture reports motion to the surface's
  /// own canvas instead (Win32) leaves it unused, and a passive surface never sets it.
  /// </summary>
  Action<Point>? OutsidePointerMove { get; set; }

  /// <summary>
  /// Raised when the user dismisses the surface: a click outside it, loss of the activation/grab
  /// that keeps it up, or Escape. The surface is hidden first, then the event is raised.
  /// </summary>
  event EventHandler? Dismissed;
}
