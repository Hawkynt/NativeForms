using System.Collections;

namespace Hawkynt.NativeForms;

/// <summary>
/// The ordered set of child controls owned by a <see cref="Control"/>. Mirrors
/// <c>Control.ControlCollection</c>: adding a control re-parents it (and realizes it immediately
/// when the owner is already live), removing it clears the parent and tears down its native peers.
/// </summary>
public sealed class ControlCollection : IReadOnlyList<Control>
{
    private readonly Control _owner;
    private readonly List<Control> _items = [];

    internal ControlCollection(Control owner) => _owner = owner;

    /// <summary>The number of child controls.</summary>
    public int Count => _items.Count;

    /// <summary>The child control at the given index.</summary>
    public Control this[int index] => _items[index];

    /// <summary>
    /// Adds a control and sets its <see cref="Control.Parent"/> to the owner. When the owner is
    /// already realized as a container, the control (and its subtree) realizes immediately.
    /// </summary>
    public void Add(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Parent = _owner;
        _items.Add(control);
        _owner.RealizeAddedChild(control);
        _owner.NotifyChildAdded(control);
    }

    /// <summary>Adds several controls in order.</summary>
    public void AddRange(params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        foreach (var control in controls)
            this.Add(control);
    }

    /// <summary>
    /// Removes a control, clears its parent and disposes its peer tree, returning the control to its
    /// unrealized (but re-realizable) shape. Returns whether it was present.
    /// </summary>
    public bool Remove(Control control)
    {
        if (!_items.Remove(control))
            return false;

        control.Parent = null;
        _owner.UnrealizeChildPeer(control);
        control.DisposePeerTree();
        _owner.NotifyChildRemoved(control);
        return true;
    }

    /// <summary>Whether the control is a direct child.</summary>
    public bool Contains(Control control) => _items.Contains(control);

    /// <summary>Removes every child, clearing their parents and disposing their peer trees.</summary>
    public void Clear()
    {
        foreach (var control in _items)
        {
            control.Parent = null;
            _owner.UnrealizeChildPeer(control);
            control.DisposePeerTree();
            _owner.NotifyChildRemoved(control);
        }

        _items.Clear();
    }

    /// <inheritdoc/>
    public IEnumerator<Control> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
