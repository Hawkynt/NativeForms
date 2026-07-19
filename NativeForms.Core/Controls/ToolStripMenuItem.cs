using System.Text;

namespace Hawkynt.NativeForms;

/// <summary>
/// A menu entry: caption with mnemonic, optional icon, check or radio mark, keyboard shortcut and a
/// nested drop-down of child items. Radio behavior comes from <see cref="CheckedGroup"/> — checking
/// an item unchecks every sibling in the same group and both paint as radio bullets, sparing the
/// manual sibling bookkeeping classic Windows Forms demands.
/// </summary>
public class ToolStripMenuItem : ToolStripDropDownItem
{
    private bool _checked;

    /// <summary>Creates an empty menu item.</summary>
    public ToolStripMenuItem() { }

    /// <summary>Creates a menu item with the given caption.</summary>
    public ToolStripMenuItem(string text) => this.Text = text;

    /// <summary>
    /// Whether the item shows its check (or radio) mark. Checking an item that belongs to a
    /// <see cref="CheckedGroup"/> unchecks its sibling group members.
    /// </summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            if (value)
                this.UncheckGroupSiblings();

            this.NotifyOwner();
            this.CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether clicking the item toggles <see cref="Checked"/> automatically.</summary>
    public bool CheckOnClick { get; set; }

    /// <summary>
    /// The radio group this item belongs to, or <see langword="null"/> for an ordinary check item.
    /// Members of one group are mutually exclusive among their siblings and paint a bullet instead
    /// of a check mark.
    /// </summary>
    public string? CheckedGroup { get; set; }

    /// <summary>The shortcut chord that activates this item, for example <c>Keys.Control | Keys.S</c>,
    /// or <see cref="Keys.None"/>. Dispatched by the owning <see cref="MenuStrip"/>.</summary>
    public Keys ShortcutKeys
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _shortcutText = null;
            this.NotifyOwner();
        }
    }

    /// <summary>Overrides the shortcut text shown right-aligned in the drop-down;
    /// <see langword="null"/> renders the formatted <see cref="ShortcutKeys"/>.</summary>
    public string? ShortcutKeyDisplayString
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            _shortcutText = null;
            this.NotifyOwner();
        }
    }

    /// <summary>Raised after <see cref="Checked"/> changes.</summary>
    public event EventHandler? CheckedChanged;

    private string? _shortcutText;

    /// <summary>The shortcut text to render: the explicit display string, else the formatted chord.
    /// Cached so the paint path never re-formats.</summary>
    internal string ShortcutText => _shortcutText ??= this.ShortcutKeyDisplayString ?? FormatShortcut(this.ShortcutKeys);

    /// <inheritdoc/>
    protected override void OnClick(EventArgs e)
    {
        if (this.CheckOnClick)
            this.Checked = this.CheckedGroup is null ? !_checked : true;

        base.OnClick(e);
    }

    /// <summary>Renders a shortcut chord as its display text, for example <c>"Ctrl+Shift+S"</c>.</summary>
    internal static string FormatShortcut(Keys shortcut)
    {
        if (shortcut == Keys.None)
            return string.Empty;

        var builder = new StringBuilder();
        if ((shortcut & Keys.Control) != 0)
            builder.Append("Ctrl+");
        if ((shortcut & Keys.Shift) != 0)
            builder.Append("Shift+");
        if ((shortcut & Keys.Alt) != 0)
            builder.Append("Alt+");

        var code = shortcut & Keys.KeyCode;
        var name = code.ToString();

        // Digits are declared as D0…D9; the display drops the prefix.
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            name = name[1..];

        return builder.Append(name).ToString();
    }

    /// <summary>Turns off every sibling that shares this item's <see cref="CheckedGroup"/>.</summary>
    private void UncheckGroupSiblings()
    {
        var group = this.CheckedGroup;
        var owner = this.Owner;
        if (group is null || owner is null)
            return;

        for (var i = 0; i < owner.Count; ++i)
        {
            if (owner[i] is not ToolStripMenuItem sibling || ReferenceEquals(sibling, this) || sibling.CheckedGroup != group || !sibling._checked)
                continue;

            // Direct write: the checking item already notifies the owner, and a group member turning
            // off must never re-enter the group scan.
            sibling._checked = false;
            sibling.CheckedChanged?.Invoke(sibling, EventArgs.Empty);
        }
    }
}
