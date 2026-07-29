using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A list of strings: a real <c>NSTableView</c> inside an <c>NSScrollView</c>, which is what AppKit
/// offers where Win32 has a <c>LISTBOX</c> and GTK a <c>GtkTreeView</c>.
/// </summary>
/// <remarks>
/// <para>
/// The scroll view is the peer's handle because the scroll view is what a parent adds and positions;
/// the table is its document, and everything the contract asks about — the selection, the scroll
/// position, the row under a point — belongs to the table.
/// </para>
/// <para>
/// A table has no items of its own. It asks a data source how many rows there are and what is in
/// each, which is why this peer needs an Objective-C object to be that source — the same runtime class
/// with <see cref="UnmanagedCallersOnlyAttribute"/> implementations the canvas and the check box
/// already use, with one method more and two of them answering rather than returning void.
/// </para>
/// <para>
/// The row strings are built once when the list is set rather than per request. A cell-based table
/// asks for a row's value every time it draws it, and minting an <c>NSString</c> inside that call
/// would put an allocation on the paint path and leave its lifetime to be guessed at; owning them for
/// as long as the list holds them makes both questions go away.
/// </para>
/// </remarks>
internal sealed class CocoaListBoxPeer : CocoaControlPeer, IListBoxPeer
{
    private readonly nint _table;
    private readonly nint _source;
    private readonly nint _doubleTarget;

    /// <summary>The rows, as retained <c>NSString</c>s owned by this peer.</summary>
    private nint[] _items = [];

    /// <summary>
    /// Whether the selection is being pushed rather than pulled, so the widget's own notification is
    /// not reported back as the user's doing.
    /// </summary>
    /// <remarks>
    /// AppKit sends <c>tableViewSelectionDidChange:</c> for a programmatic selection as readily as for
    /// a clicked one — unlike its target/action, which only fires for the user. So the flag is needed
    /// here where the check box needs none.
    /// </remarks>
    private bool _pushing;

    public CocoaListBoxPeer()
        : base(Create(out var table))
    {
        _table = table;
        if (this.Handle == 0 || _table == 0)
            return;

        _source = CocoaTableSource.Create(this);
        if (_source != 0)
        {
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setDataSource:"), _source);
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setDelegate:"), _source);
        }

        // Activation is the double click. AppKit's own doubleAction fires for the user only, so this
        // needs none of the suppression the selection does.
        _doubleTarget = CocoaAction.Create(this.OnActivated);
        if (_doubleTarget == 0)
            return;

        CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setTarget:"), _doubleTarget);
        CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setDoubleAction:"), CocoaAction.Selector);
    }

    /// <inheritdoc/>
    public event EventHandler? SelectionChanged;

    /// <inheritdoc/>
    public event EventHandler? ItemActivated;

    /// <summary>How many rows the table should show — the data source's first question.</summary>
    internal int RowCount => _items.Length;

    /// <summary>The row string at an index, or zero when the table asks past the end.</summary>
    internal nint RowValue(int row) => (uint)row < (uint)_items.Length ? _items[row] : 0;

    private static nint Create(out nint table)
    {
        table = 0;

        var scroll = CocoaRuntime.Allocate("NSScrollView");
        if (scroll != 0)
            scroll = CocoaRuntime.SendRectInit(scroll, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        var view = CocoaRuntime.Allocate("NSTableView");
        if (view != 0)
            view = CocoaRuntime.SendRectInit(view, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (scroll == 0 || view == 0)
            return 0;

        // One column, no header: this control models a list of strings, and a header would be a table
        // the toolkit's API has no way to describe.
        var column = CocoaRuntime.Allocate("NSTableColumn");
        var identifier = CocoaRuntime.NSString("item");
        if (column != 0 && identifier != 0)
            column = CocoaRuntime.SendPointer(column, CocoaRuntime.sel_registerName("initWithIdentifier:"), identifier);

        if (identifier != 0)
            CocoaNative.CFRelease(identifier);

        if (column != 0)
        {
            // NSTableColumnAutoresizingMask, with the table's uniform style, so the single column is
            // always exactly as wide as the list rather than as wide as it was created.
            CocoaRuntime.SendVoid(column, CocoaRuntime.sel_registerName("setResizingMask:"), 1);
            CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("addTableColumn:"), column);
        }

        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setHeaderView:"), 0);
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setColumnAutoresizingStyle:"), 1);

        // The core owns multi-selection and declines to promote a list that has any, so the widget is
        // asked for exactly the one mode it will ever be in.
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setAllowsMultipleSelection:"), false);
        CocoaRuntime.SendVoid(view, CocoaRuntime.sel_registerName("setAllowsEmptySelection:"), true);

        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setHasVerticalScroller:"), true);
        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setAutohidesScrollers:"), true);
        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setBorderType:"), 2); // NSBezelBorder
        CocoaRuntime.SendVoid(scroll, CocoaRuntime.sel_registerName("setDocumentView:"), view);

        table = view;
        return scroll;
    }

    /// <inheritdoc/>
    /// <remarks>The column follows the width, so a resized list is not one with a stale column in it.</remarks>
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        if (_table == 0)
            return;

        var columns = CocoaRuntime.SendPointer(_table, CocoaRuntime.sel_registerName("tableColumns"));
        var column = columns == 0 ? 0 : CocoaRuntime.SendIndex(columns, CocoaRuntime.sel_registerName("objectAtIndex:"), 0);
        if (column != 0)
            CocoaRuntime.SendVoid(column, CocoaRuntime.sel_registerName("setWidth:"), (double)Math.Max(1, bounds.Width));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A list has no caption, and the base class would send <c>setStringValue:</c> to a scroll view —
    /// which is not an <c>NSControl</c> and does not answer it. An unrecognized selector here is not
    /// ignored, it ends the process.
    /// </remarks>
    public override void SetText(string text) { }

    /// <inheritdoc/>
    /// <remarks>Enablement belongs to the table; a scroll view answers no <c>setEnabled:</c>.</remarks>
    public override void SetEnabled(bool enabled)
    {
        if (_table != 0)
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setEnabled:"), enabled);
    }

    /// <inheritdoc/>
    /// <remarks>The scroll view is scenery; the keyboard belongs to the table inside it.</remarks>
    public override void Focus()
    {
        if (_table == 0)
            return;

        var window = CocoaRuntime.SendPointer(_table, CocoaRuntime.sel_registerName("window"));
        if (window != 0)
            CocoaRuntime.SendVoid(window, CocoaRuntime.sel_registerName("makeFirstResponder:"), _table);
    }

    /// <inheritdoc/>
    public void SetItems(ReadOnlySpan<string> items, int selectedIndex)
    {
        ReleaseItems();
        _items = items.Length == 0 ? [] : new nint[items.Length];
        for (var i = 0; i < items.Length; ++i)
            _items[i] = CocoaRuntime.NSString(items[i]);

        if (_table != 0)
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("reloadData"));

        this.SetSelectedIndex(selectedIndex);
    }

    /// <inheritdoc/>
    public void SetSelectedIndex(int index)
    {
        if (_table == 0)
            return;

        _pushing = true;
        try
        {
            if ((uint)index >= (uint)_items.Length)
            {
                CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("deselectAll:"), 0);
                return;
            }

            var set = CocoaRuntime.SendPointer(
                CocoaRuntime.objc_getClass("NSIndexSet"),
                CocoaRuntime.sel_registerName("indexSetWithIndex:"),
                index);

            if (set != 0)
                CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("selectRowIndexes:byExtendingSelection:"), set, false);
        }
        finally
        {
            _pushing = false;
        }
    }

    /// <inheritdoc/>
    public int GetSelectedIndex()
        => _table == 0 ? -1 : (int)CocoaRuntime.SendInteger(_table, CocoaRuntime.sel_registerName("selectedRow"));

    /// <inheritdoc/>
    public void ScrollIntoView(int index)
    {
        if (_table != 0 && (uint)index < (uint)_items.Length)
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("scrollRowToVisible:"), index);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The clip view's own origin, rather than a row height multiplied out: a table is free to give
    /// rows different heights, and reading where it actually scrolled to asks the object that knows.
    /// </remarks>
    public int GetTopIndex()
    {
        if (_table == 0)
            return 0;

        var visible = CocoaRuntime.SendRect(this.Handle, CocoaRuntime.sel_registerName("documentVisibleRect"));
        var row = (int)CocoaRuntime.SendIntegerAt(_table, CocoaRuntime.sel_registerName("rowAtPoint:"), new() { X = 0, Y = visible.Y });
        return Math.Max(0, row);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The point arrives in the control's own coordinates, which start at the top left of what is
    /// visible; the table's start at the top of the whole document. The difference is exactly how far
    /// the list has been scrolled, which the scroll view reports — and the table view is flipped, so
    /// that offset grows downwards like the toolkit's own coordinates rather than away from them.
    /// </remarks>
    public int IndexFromPoint(int x, int y)
    {
        if (_table == 0)
            return -1;

        var visible = CocoaRuntime.SendRect(this.Handle, CocoaRuntime.sel_registerName("documentVisibleRect"));
        var row = (int)CocoaRuntime.SendIntegerAt(
            _table,
            CocoaRuntime.sel_registerName("rowAtPoint:"),
            new() { X = visible.X + x, Y = visible.Y + y });

        return row < 0 || row >= _items.Length ? -1 : row;
    }

    /// <summary>The table reporting that its selection moved, whoever moved it.</summary>
    internal void OnSelectionChanged()
    {
        if (!_pushing)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A row was double-clicked. A click below the last row selects nothing and is not one.</summary>
    private void OnActivated()
    {
        if (this.GetSelectedIndex() >= 0)
            ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        // The table outlives this call only if AppKit still holds it, and a data source is not
        // retained by the view it feeds — so it is unhooked before the map that finds it is emptied.
        if (_table != 0)
        {
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setDataSource:"), 0);
            CocoaRuntime.SendVoid(_table, CocoaRuntime.sel_registerName("setDelegate:"), 0);
        }

        CocoaTableSource.Forget(_source);
        CocoaAction.Forget(_doubleTarget);
        ReleaseItems();
    }

    private void ReleaseItems()
    {
        foreach (var item in _items)
            if (item != 0)
                CocoaNative.CFRelease(item);

        _items = [];
    }
}

/// <summary>
/// The object an <c>NSTableView</c> asks for its contents: a runtime class answering the data-source
/// and selection methods.
/// </summary>
/// <remarks>
/// <see cref="CocoaAction"/>'s pattern with three methods instead of one, two of which answer. The
/// encoded signatures matter more here than they do for a void action — <c>q</c> is an
/// <c>NSInteger</c> and <c>@</c> an object — because they are what the runtime consults when anything
/// asks the class about itself.
/// </remarks>
internal static unsafe class CocoaTableSource
{
    /// <summary>The runtime class, built on first use.</summary>
    private static nint _class;

    /// <summary>The list each source speaks for, by source pointer.</summary>
    private static readonly ConcurrentDictionary<nint, CocoaListBoxPeer> _lists = new();

    /// <summary>Builds a source feeding <paramref name="list"/>, or zero.</summary>
    internal static nint Create(CocoaListBoxPeer list)
    {
        EnsureClass();
        if (_class == 0)
            return 0;

        var allocated = CocoaRuntime.SendPointer(_class, CocoaRuntime.sel_registerName("alloc"));
        var source = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
        if (source != 0)
            _lists[source] = list;

        return source;
    }

    /// <summary>Forgets a source, so a disposed peer is not held alive by this map.</summary>
    internal static void Forget(nint source)
    {
        if (source != 0)
            _lists.TryRemove(source, out _);
    }

    private static void EnsureClass()
    {
        if (_class != 0 || !CocoaRuntime.Available)
            return;

        var superclass = CocoaRuntime.objc_getClass("NSObject");
        if (superclass == 0)
            return;

        var created = CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsTableSource", 0);
        if (created == 0)
            return;

        // "q@:@": returns NSInteger, takes self, _cmd and the table.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("numberOfRowsInTableView:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint>)&NumberOfRows,
            "q@:@");

        // "@@:@@q": returns an object, takes self, _cmd, the table, the column and the row.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("tableView:objectValueForTableColumn:row:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint>)&ObjectValue,
            "@@:@@q");

        // "v@:@": returns void, takes self, _cmd and the notification.
        CocoaRuntime.class_addMethod(
            created,
            CocoaRuntime.sel_registerName("tableViewSelectionDidChange:"),
            (nint)(delegate* unmanaged<nint, nint, nint, void>)&SelectionDidChange,
            "v@:@");

        CocoaRuntime.objc_registerClassPair(created);
        _class = created;
    }

    [UnmanagedCallersOnly]
    private static nint NumberOfRows(nint self, nint selector, nint table)
        => _lists.TryGetValue(self, out var list) ? list.RowCount : 0;

    [UnmanagedCallersOnly]
    private static nint ObjectValue(nint self, nint selector, nint table, nint column, nint row)
        => _lists.TryGetValue(self, out var list) ? list.RowValue((int)row) : 0;

    [UnmanagedCallersOnly]
    private static void SelectionDidChange(nint self, nint selector, nint notification)
    {
        if (_lists.TryGetValue(self, out var list))
            list.OnSelectionChanged();
    }
}
