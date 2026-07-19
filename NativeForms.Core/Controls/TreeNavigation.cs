namespace Hawkynt.NativeForms;

/// <summary>
/// The keyboard model <see cref="TreeView"/> and <see cref="TreeListView"/> share: arrows walk the
/// visible rows, Right expands then enters the first child, Left collapses then climbs to the parent,
/// +/−/* expand and collapse, Space checks (or toggles) and Enter toggles. One implementation lives
/// here so both controls navigate pixel- and event-identically.
/// </summary>
internal static class TreeNavigation
{
    /// <summary>Applies one key to the host's selection/expansion state; returns whether it was handled.</summary>
    public static bool HandleKey(ITreeNodeHost host, TreeRowList rows, int visibleRows, bool checkBoxes, KeyEventArgs e)
    {
        var count = rows.Count;
        var node = host.SelectedNode;
        var index = node is null ? -1 : rows.IndexOf(node);

        switch (e.KeyCode)
        {
            case Keys.Down when count > 0: Select(host, rows, index + 1); return true;
            case Keys.Up when count > 0: Select(host, rows, index - 1); return true;
            case Keys.Home when count > 0: Select(host, rows, 0); return true;
            case Keys.End when count > 0: Select(host, rows, count - 1); return true;
            case Keys.PageDown when count > 0: Select(host, rows, index + visibleRows); return true;
            case Keys.PageUp when count > 0: Select(host, rows, index - visibleRows); return true;
            case Keys.Right when node is not null:
                if (!node.HasChildren)
                    return true;

                if (node.IsExpanded)
                    host.SelectedNode = node.Nodes[0];
                else
                    node.Expand();

                return true;
            case Keys.Left when node is not null:
                if (node.IsExpanded && node.HasChildren)
                    node.Collapse();
                else if (node.Parent is not null)
                    host.SelectedNode = node.Parent;

                return true;
            case Keys.Add when node is not null: node.Expand(); return true;
            case Keys.Subtract when node is not null: node.Collapse(); return true;
            case Keys.Multiply when node is not null: node.ExpandAll(); return true;
            case Keys.Space when node is not null:
                if (checkBoxes)
                    node.Checked = !node.Checked;
                else
                    node.Toggle();

                return true;
            case Keys.Enter when node is not null: node.Toggle(); return true;
            default: return false;
        }
    }

    private static void Select(ITreeNodeHost host, TreeRowList rows, int index)
        => host.SelectedNode = rows[Math.Clamp(index, 0, rows.Count - 1)];
}
