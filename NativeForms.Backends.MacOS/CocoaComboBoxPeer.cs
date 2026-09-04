using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A drop-down list: a real <c>NSPopUpButton</c>, which is what AppKit has where Win32 has a
/// <c>COMBOBOX</c> with <c>CBS_DROPDOWNLIST</c> and GTK a <c>GtkComboBoxText</c>.
/// </summary>
/// <remarks>
/// <para>
/// The items are added as <c>NSMenuItem</c>s straight into the button's menu rather than through
/// <c>addItemWithTitle:</c>. That looks like the obvious call and quietly loses data: it removes any
/// existing item with the same title first, so a list holding the same string twice — two files called
/// <c>index.html</c>, two people called Chris — arrives one item short and every index after it is
/// wrong. A menu does no such thing.
/// </para>
/// <para>
/// Opening the list is where this widget and the seam disagree, and the disagreement is the platform's.
/// AppKit tracks a menu in a nested event loop, so <c>performClick:</c> does not return until the menu
/// closes; there is no "show the list and carry on". So a caller setting <c>DroppedDown</c> here blocks
/// where the same line on Windows returns at once. It is served that way rather than ignored, because
/// what the property asks for — the user sees the list — does happen.
/// </para>
/// </remarks>
internal sealed class CocoaComboBoxPeer : CocoaControlPeer, IComboBoxPeer {
  /// <summary>The names AppKit posts when a menu starts and stops tracking.</summary>
  private static readonly nint _BeganTracking = CocoaRuntime.Constant("NSMenuDidBeginTrackingNotification");
  private static readonly nint _EndedTracking = CocoaRuntime.Constant("NSMenuDidEndTrackingNotification");

  private readonly nint _target;
  private readonly nint _openObserver;
  private readonly nint _closeObserver;

  public CocoaComboBoxPeer()
      : base(Create()) {
    if (this.Handle == 0)
      return;

    _target = CocoaAction.Create(this.OnSelectionChanged);
    if (_target != 0) {
      CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTarget:"), _target);
      CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
    }

    // The open and close events have no target/action of their own; the menu announces them, so
    // the peer listens for exactly its own menu's announcements rather than every menu's.
    _openObserver = this.Observe(_BeganTracking, this.OnDropDownOpened);
    _closeObserver = this.Observe(_EndedTracking, this.OnDropDownClosed);
  }

  /// <inheritdoc/>
  public event EventHandler? SelectionChanged;

  /// <inheritdoc/>
  public event EventHandler? DropDownOpened;

  /// <inheritdoc/>
  public event EventHandler? DropDownClosed;

  private static nint Create() {
    var allocated = CocoaRuntime.Allocate("NSPopUpButton");
    return allocated == 0
        ? 0
        : CocoaRuntime.SendRectInit(
            allocated,
            CocoaRuntime.sel_registerName("initWithFrame:pullsDown:"),
            new(0, 0, 1, 1),
            false); // a pop-up shows the selection; a pull-down is a menu button, which this is not
  }

  /// <summary>The button's menu, or zero.</summary>
  private nint Menu()
      => this.Handle == 0 ? 0 : CocoaRuntime.SendPointer(this.Handle, CocoaRuntime.sel_registerName("menu"));

  /// <summary>Registers a handler for one notification from this button's own menu.</summary>
  private nint Observe(nint name, Action handler) {
    var centre = CocoaRuntime.SendToClass("NSNotificationCenter", "defaultCenter");
    var menu = this.Menu();
    if (name == 0 || centre == 0 || menu == 0)
      return 0;

    var observer = CocoaAction.Create(handler);
    if (observer == 0)
      return 0;

    CocoaRuntime.SendVoid(
        centre,
        CocoaRuntime.sel_registerName("addObserver:selector:name:object:"),
        observer,
        CocoaAction.Selector,
        name,
        menu);

    return observer;
  }

  /// <inheritdoc/>
  /// <remarks>A pop-up carries its caption as the selected item; it has no title of its own to set.</remarks>
  public override void SetText(string text) { }

  /// <inheritdoc/>
  public void SetItems(ReadOnlySpan<string> items, int selectedIndex) {
    if (this.Handle == 0)
      return;

    CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("removeAllItems"));
    var menu = this.Menu();
    if (menu != 0)
      foreach (var item in items)
        AddItem(menu, item);

    this.SetSelectedIndex(selectedIndex);
  }

  /// <summary>Appends one item to a menu, keeping a title that is already there.</summary>
  private static void AddItem(nint menu, string text) {
    var allocated = CocoaRuntime.Allocate("NSMenuItem");
    var item = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
    if (item == 0)
      return;

    var title = CocoaRuntime.NSString(text);
    if (title != 0) {
      CocoaRuntime.SendVoid(item, CocoaRuntime.sel_registerName("setTitle:"), title);
      CocoaNative.CFRelease(title);
    }

    CocoaRuntime.SendVoid(menu, CocoaRuntime.sel_registerName("addItem:"), item);
    CocoaRuntime.SendVoid(item, CocoaRuntime.sel_registerName("release")); // the menu owns it now
  }

  /// <inheritdoc/>
  /// <remarks>
  /// No echo to suppress: AppKit sends the action only when the user works the control, so a
  /// programmatic selection is silent here in a way the table view's is not.
  /// </remarks>
  public void SetSelectedIndex(int index) {
    if (this.Handle == 0)
      return;

    if (index < 0) {
      CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("selectItem:"), 0);
      return;
    }

    CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("selectItemAtIndex:"), index);
  }

  /// <inheritdoc/>
  public int GetSelectedIndex()
      => this.Handle == 0
          ? -1
          : (int)CocoaRuntime.SendInteger(this.Handle, CocoaRuntime.sel_registerName("indexOfSelectedItem"));

  /// <inheritdoc/>
  /// <inheritdoc cref="CocoaComboBoxPeer"/>
  public void SetDroppedDown(bool droppedDown) {
    if (this.Handle == 0)
      return;

    if (!droppedDown) {
      if (this.Menu() is var menu && menu != 0)
        CocoaRuntime.SendVoid(menu, CocoaRuntime.sel_registerName("cancelTracking"));

      return;
    }

    CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("performClick:"), 0);
  }

  private void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

  private void OnDropDownOpened() => DropDownOpened?.Invoke(this, EventArgs.Empty);

  private void OnDropDownClosed() => DropDownClosed?.Invoke(this, EventArgs.Empty);

  /// <inheritdoc/>
  public override void Dispose() {
    var centre = CocoaRuntime.SendToClass("NSNotificationCenter", "defaultCenter");
    foreach (var observer in (ReadOnlySpan<nint>)[_openObserver, _closeObserver]) {
      if (observer == 0)
        continue;

      // Unregistered before it is forgotten: a notification centre holds its observers weakly by
      // pointer, and one that fires after the map is emptied would find nothing to call.
      if (centre != 0)
        CocoaRuntime.SendVoid(centre, CocoaRuntime.sel_registerName("removeObserver:"), observer);

      CocoaAction.Forget(observer);
    }

    CocoaAction.Forget(_target);
    base.Dispose();
  }
}
