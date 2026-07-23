using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Menus page: one rich <see cref="ContextMenuStrip"/> — icons, shortcuts, a check-on-click
    /// toggle, a radio group, a disabled item, a separator and a colour submenu — shown attached to
    /// several kinds of surface (a button, a group box, a label, a list) so the attach points are
    /// visible in one place.
    /// </summary>
    private TabPage BuildMenusPage()
    {
        var page = new TabPage("Menus") { ImageIndex = _IconFolder };

        ContextMenuStrip Rich(string tag)
        {
            var menu = new ContextMenuStrip();
            var cut = new ToolStripMenuItem("Cut") { ImageList = _icons, ImageIndex = _IconRed, ShortcutKeys = Keys.Control | Keys.X };
            cut.Click += (_, _) => this.SetStatus($"Menu ({tag}): Cut.");
            var copy = new ToolStripMenuItem("Copy") { ImageList = _icons, ImageIndex = _IconGreen, ShortcutKeys = Keys.Control | Keys.C };
            copy.Click += (_, _) => this.SetStatus($"Menu ({tag}): Copy.");
            var paste = new ToolStripMenuItem("Paste (disabled)") { ImageList = _icons, ImageIndex = _IconOpen, Enabled = false };

            var wrap = new ToolStripMenuItem("Word wrap") { CheckOnClick = true, Checked = true };
            wrap.CheckedChanged += (_, _) => this.SetStatus($"Menu ({tag}): word wrap {(wrap.Checked ? "on" : "off")}.");

            var small = new ToolStripMenuItem("Small") { CheckedGroup = "size" };
            var medium = new ToolStripMenuItem("Medium") { CheckedGroup = "size", Checked = true };
            var large = new ToolStripMenuItem("Large") { CheckedGroup = "size" };
            foreach (var size in new[] { small, medium, large })
                size.Click += (_, _) => this.SetStatus($"Menu ({tag}): size {size.Text}.");

            var highlight = new ToolStripMenuItem("Highlight") { ImageList = _icons, ImageIndex = _IconYellow };
            foreach (var (name, swatch) in new (string, Color)[] { ("Yellow", Color.Gold), ("Green", Color.SeaGreen), ("Pink", Color.HotPink) })
            {
                var choice = new ToolStripMenuItem(name) { Image = this.SquareImage(swatch) };
                choice.Click += (_, _) => this.SetStatus($"Menu ({tag}): highlight {name}.");
                highlight.DropDownItems.Add(choice);
            }

            menu.Items.AddRange(cut, copy, paste, new ToolStripSeparator(), wrap, new ToolStripSeparator(), small, medium, large, new ToolStripSeparator(), highlight);
            return menu;
        }

        // The surfaces a context menu attaches to (Control.ContextMenuStrip).
        var button = new Button { Bounds = new(16, 40, 220, 40), Text = "Right-click this button" };
        button.ContextMenuStrip = Rich("button");

        var group = new GroupBox { Bounds = new(16, 96, 220, 90), Text = "Group box" };
        group.Controls.Add(new Label { Bounds = new(12, 30, 196, 40), Text = "Right-click anywhere here" });
        group.ContextMenuStrip = Rich("group box");

        var label = new Label { Bounds = new(16, 204, 220, 20), Text = "Right-click this label" };
        label.ContextMenuStrip = Rich("label");

        var list = new ListBox { Bounds = new(340, 40, 220, 180) };
        list.Items.AddRange(["Right-click", "any", "of", "these", "rows"]);
        list.ContextMenuStrip = Rich("list");

        page.Controls.AddRange(
            Caption("A rich context menu on several surfaces", 16, 12, 320),
            button, group, label,
            Caption("Same menu on a list box", 340, 12, 300),
            list,
            Caption("Items: icons · shortcuts · a check · a radio group · a disabled item · a colour submenu", 16, 240, 900));

        this.Publish("menus.button", button);
        return page;
    }
}
