using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The GTK peer for a promoted <see cref="ListBox"/>: a <c>GtkTreeView</c> of one text column inside a
/// <c>GtkScrolledWindow</c> (PRD §12), so the desktop supplies the rows, the selection, the scrolling,
/// the kinetic overshoot and the type-ahead search.
/// </summary>
/// <remarks>
/// The scrolled window is the widget the container sees, because it is what carries the bounds and the
/// scroll bars; the tree view inside it is where the rows and every signal live. A single-column
/// <c>GtkListStore</c> backs it — the control's items are already flattened to strings by the core, which
/// owns <c>DisplaySelector</c>.
/// </remarks>
internal sealed class GtkListBoxPeer : GtkControlPeer, IListBoxPeer {
  private nint _treeView;
  private nint _store;
  private string[] _items = [];
  private int _selectedIndex = -1;
  private bool _suppress;

  /// <inheritdoc />
  public event EventHandler? SelectionChanged;

  /// <inheritdoc />
  public event EventHandler? ItemActivated;

  /// <inheritdoc />
  protected override nint CreateWidget() {
    _store = NativeMethods.gtk_list_store_newv(1, [(nint)NativeMethods.G_TYPE_STRING]);
    _treeView = NativeMethods.gtk_tree_view_new_with_model(_store);
    NativeMethods.gtk_tree_view_set_headers_visible(_treeView, 0);

    var renderer = NativeMethods.gtk_cell_renderer_text_new();
    var column = NativeMethods.gtk_tree_view_column_new();
    NativeMethods.gtk_tree_view_column_pack_start(column, renderer, 1);
    NativeMethods.gtk_tree_view_column_add_attribute(column, renderer, "text", 0);
    NativeMethods.gtk_tree_view_append_column(_treeView, column);

    var scroller = NativeMethods.gtk_scrolled_window_new(0, 0);
    NativeMethods.gtk_container_add(scroller, _treeView);
    NativeMethods.gtk_widget_show(_treeView);
    return scroller;
  }

  /// <inheritdoc />
  protected override void ApplyText(string text) { }

  /// <inheritdoc />
  public void SetItems(ReadOnlySpan<string> items, int selectedIndex) {
    _items = items.ToArray();
    _selectedIndex = selectedIndex;
    if (_store == 0)
      return;

    _suppress = true;
    try {
      NativeMethods.gtk_list_store_clear(_store);
      foreach (var item in _items) {
        NativeMethods.gtk_list_store_append(_store, out var iter);
        NativeMethods.gtk_list_store_set_string(_store, ref iter, 0, item, -1);
      }
    } finally {
      _suppress = false;
    }

    this.SetSelectedIndex(selectedIndex);
  }

  /// <inheritdoc />
  public void SetSelectedIndex(int index) {
    _selectedIndex = index;
    if (_treeView == 0)
      return;

    var selection = NativeMethods.gtk_tree_view_get_selection(_treeView);
    _suppress = true;
    try {
      if (index < 0) {
        NativeMethods.gtk_tree_selection_unselect_all(selection);
        return;
      }

      var path = NativeMethods.gtk_tree_path_new_from_indicesv([index], 1);
      NativeMethods.gtk_tree_selection_select_path(selection, path);
      NativeMethods.gtk_tree_path_free(path);
    } finally {
      _suppress = false;
    }
  }

  /// <inheritdoc />
  public int GetSelectedIndex() {
    if (_treeView == 0)
      return _selectedIndex;

    var selection = NativeMethods.gtk_tree_view_get_selection(_treeView);
    if (NativeMethods.gtk_tree_selection_get_selected(selection, out _, out var iter) == 0)
      return -1;

    var path = NativeMethods.gtk_tree_model_get_path(_store, ref iter);
    var index = IndexOfPath(path);
    NativeMethods.gtk_tree_path_free(path);
    return index;
  }

  /// <inheritdoc />
  public void ScrollIntoView(int index) {
    if (_treeView == 0 || index < 0)
      return;

    var path = NativeMethods.gtk_tree_path_new_from_indicesv([index], 1);
    NativeMethods.gtk_tree_view_scroll_to_cell(_treeView, path, 0, 0, 0, 0);
    NativeMethods.gtk_tree_path_free(path);
  }

  /// <inheritdoc />
  public int GetTopIndex() {
    if (_treeView == 0)
      return 0;

    if (NativeMethods.gtk_tree_view_get_path_at_pos(_treeView, 0, 0, out var path, out _, out _, out _) == 0)
      return 0;

    var index = IndexOfPath(path);
    NativeMethods.gtk_tree_path_free(path);
    return Math.Max(0, index);
  }

  /// <inheritdoc />
  public int IndexFromPoint(int x, int y) {
    if (_treeView == 0)
      return -1;

    // The coordinates arrive in the scrolled window's space, which is the tree view's space too
    // while the scroller adds no border of its own.
    if (NativeMethods.gtk_tree_view_get_path_at_pos(_treeView, x, y, out var path, out _, out _, out _) == 0)
      return -1;

    var index = IndexOfPath(path);
    NativeMethods.gtk_tree_path_free(path);
    return index;
  }

  /// <inheritdoc />
  protected override void OnWidgetRealized() {
    this.SetItems(_items, _selectedIndex); // flush the list buffered before the widget existed

    var data = this.PinSelf();
    unsafe {
      var changed = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnSelectionChanged;
      NativeMethods.g_signal_connect_data(NativeMethods.gtk_tree_view_get_selection(_treeView), "changed", changed, data, 0, 0);

      var activated = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint, void>)&OnRowActivated;
      NativeMethods.g_signal_connect_data(_treeView, "row-activated", activated, data, 0, 0);
    }
  }

  /// <summary>The first index of a <c>GtkTreePath</c>, or -1 when it carries none.</summary>
  private static int IndexOfPath(nint path) {
    if (path == 0 || NativeMethods.gtk_tree_path_get_depth(path) < 1)
      return -1;

    var indices = NativeMethods.gtk_tree_path_get_indices(path);
    return indices == 0 ? -1 : Marshal.ReadInt32(indices);
  }

  /// <summary>Reports a user selection.</summary>
  private void RaiseSelectionChanged() {
    if (_suppress)
      return;

    var index = this.GetSelectedIndex();
    if (index == _selectedIndex)
      return;

    _selectedIndex = index;
    SelectionChanged?.Invoke(this, EventArgs.Empty);
  }

  /// <summary>Native handler for "changed", shaped as <c>void (GtkTreeSelection *, gpointer)</c>.</summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void OnSelectionChanged(nint selection, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkListBoxPeer peer)
      peer.RaiseSelectionChanged();
  }

  /// <summary>
  /// Native handler for "row-activated", shaped as
  /// <c>void (GtkTreeView *, GtkTreePath *, GtkTreeViewColumn *, gpointer)</c>.
  /// </summary>
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void OnRowActivated(nint treeView, nint path, nint column, nint userData) {
    if (userData != 0 && GCHandle.FromIntPtr(userData).Target is GtkListBoxPeer peer)
      peer.ItemActivated?.Invoke(peer, EventArgs.Empty);
  }
}
