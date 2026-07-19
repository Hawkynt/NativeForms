using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>An entry with a generated icon for the list-box and combo-box demos.</summary>
    private sealed record IconItem(string Text, IImage Image, int IconIndex);

    /// <summary>A file-system-shaped row for the tree-list-view demo.</summary>
    private sealed record FsEntry(string Name, string Kind, string Size, FsEntry[]? Children);

    /// <summary>
    /// The Lists page: a multi-select list box with icons, a checked list box, a drop-down-list
    /// combo box with icons next to an editable one, list views in Details/SmallIcon/List layouts,
    /// a tree view with icons and check boxes, and a tree list view filled through
    /// <see cref="TreeListView.SetDataSource{T}"/> with three columns.
    /// </summary>
    private TabPage BuildListsPage()
    {
        var page = new TabPage("Lists") { ImageIndex = _IconYellow };

        // --- Column 1: list box, checked list box, combo boxes ----------------------------------

        var planets = new IconItem[]
        {
            new("Mercury", this.DiscImage(Color.Silver), _IconFile),
            new("Venus", this.DiscImage(Color.Gold), _IconYellow),
            new("Earth", this.DiscImage(Color.RoyalBlue), _IconBlue),
            new("Mars", this.DiscImage(Color.Crimson), _IconRed),
            new("Jupiter", this.DiscImage(Color.Orange), _IconOpen),
            new("Saturn", this.DiscImage(Color.Goldenrod), _IconFolder),
            new("Neptune", this.DiscImage(Color.MediumOrchid), _IconPurple),
        };

        var listBox = new ListBox
        {
            Bounds = new(16, 36, 300, 140),
            ItemHeight = 20,
            SelectionMode = SelectionMode.MultiExtended,
            DisplaySelector = static o => ((IconItem)o!).Text,
            ImageSelector = static o => ((IconItem)o!).Image,
        };
        listBox.Items.AddRange(planets);
        listBox.SelectedIndexChanged += (_, _) =>
            this.SetStatus(listBox.SelectedIndices.Count == 0
                ? "ListBox: nothing selected."
                : $"ListBox: {listBox.SelectedIndices.Count} planet(s) selected.");
        listBox.SelectedIndex = 2;

        var checkedList = new CheckedListBox
        {
            Bounds = new(16, 212, 300, 120),
            ItemHeight = 20,
            CheckOnClick = true,
        };
        checkedList.Items.AddRange(["Build", "Test", "Publish", "Sign", "Deploy"]);
        checkedList.SetItemChecked(0, true);
        checkedList.SetItemChecked(1, true);
        checkedList.ItemCheck += (_, e) =>
            this.SetStatus($"CheckedListBox: \"{checkedList.Items[e.Index]}\" will be {(e.NewValue ? "checked" : "unchecked")}.");

        var comboList = new ComboBox
        {
            Bounds = new(16, 368, 300, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            ImageList = _icons,
            DisplaySelector = static o => ((IconItem)o!).Text,
            ImageIndexSelector = static o => ((IconItem)o!).IconIndex,
        };
        comboList.Items.AddRange(planets);
        comboList.SelectedIndexChanged += (_, _)
            => this.SetStatus($"ComboBox: {(comboList.SelectedItem as IconItem)?.Text ?? "(none)"} picked.");
        comboList.SelectedIndex = 1;

        var comboEdit = new ComboBox
        {
            Bounds = new(16, 400, 300, 26),
            DropDownStyle = ComboBoxStyle.DropDown,
            PlaceholderText = "Type or pick a tag…",
        };
        comboEdit.Items.AddRange(["alpha", "beta", "release-candidate", "stable"]);

        page.Controls.AddRange(
            Caption("ListBox (MultiExtended, icons)", 16, 12),
            listBox,
            Caption("CheckedListBox (CheckOnClick)", 16, 188),
            checkedList,
            Caption("ComboBox (DropDownList + DropDown)", 16, 344),
            comboList, comboEdit);

        // --- Column 2: list views ---------------------------------------------------------------

        var details = new ListView
        {
            Bounds = new(340, 36, 300, 180),
            ItemHeight = 22,
            FullRowSelect = true,
        };
        details.Columns.AddRange([
            new ColumnHeader("Name", 130),
            new ColumnHeader("Size", 70),
            new ColumnHeader("Type", 90),
        ]);
        details.Items.AddRange([
            new ListViewItem("Readme.md", "4 KB", "Markdown") { Image = this.SquareImage(Color.CornflowerBlue) },
            new ListViewItem("MainForm.cs", "12 KB", "Source") { Image = this.SquareImage(Color.MediumSeaGreen) },
            new ListViewItem("Logo.png", "48 KB", "Image") { Image = this.SquareImage(Color.Orange) },
            new ListViewItem("Data.bin", "1 MB", "Binary") { Image = this.SquareImage(Color.Silver) },
            new ListViewItem("Notes.txt", "2 KB", "Text") { Image = this.SquareImage(Color.Goldenrod) },
        ]);
        details.SelectedIndexChanged += (_, _)
            => this.SetStatus($"ListView: {details.SelectedItem?.Text ?? "(none)"} selected.");
        details.SelectedIndex = 0;

        var smallIcons = new ListView
        {
            Bounds = new(340, 252, 300, 110),
            View = ListViewView.SmallIcon,
        };
        smallIcons.Items.AddRange([
            new ListViewItem("Alpha") { Image = this.DiscImage(Color.Crimson) },
            new ListViewItem("Beta") { Image = this.DiscImage(Color.MediumSeaGreen) },
            new ListViewItem("Gamma") { Image = this.DiscImage(Color.RoyalBlue) },
            new ListViewItem("Delta") { Image = this.DiscImage(Color.Gold) },
            new ListViewItem("Epsilon") { Image = this.DiscImage(Color.MediumOrchid) },
        ]);

        var plainList = new ListView
        {
            Bounds = new(340, 398, 300, 100),
            View = ListViewView.List,
        };
        plainList.Items.AddRange([
            new ListViewItem("First"),
            new ListViewItem("Second"),
            new ListViewItem("Third"),
            new ListViewItem("Fourth"),
        ]);

        page.Controls.AddRange(
            Caption("ListView (Details, icons)", 340, 12),
            details,
            Caption("ListView (SmallIcon)", 340, 228),
            smallIcons,
            Caption("ListView (List)", 340, 374),
            plainList);

        // --- Column 3: trees --------------------------------------------------------------------

        var tree = new TreeView
        {
            Bounds = new(664, 36, 300, 220),
            CheckBoxes = true,
            ImageList = _icons,
            ItemHeight = 22,
        };
        var solution = new TreeNode("Solution") { ImageIndex = _IconFolder };
        var core = new TreeNode("Core") { ImageIndex = _IconFolder };
        core.Nodes.Add(new TreeNode("Control.cs") { ImageIndex = _IconFile, Checked = true });
        core.Nodes.Add(new TreeNode("Form.cs") { ImageIndex = _IconFile });
        var backends = new TreeNode("Backends") { ImageIndex = _IconFolder };
        backends.Nodes.Add(new TreeNode("Win32") { ImageIndex = _IconFile });
        backends.Nodes.Add(new TreeNode("Gtk") { ImageIndex = _IconFile, Checked = true });
        solution.Nodes.Add(core);
        solution.Nodes.Add(backends);
        tree.Nodes.Add(solution);
        solution.Expand();
        core.Expand();
        tree.AfterCheck += (_, e)
            => this.SetStatus($"TreeView: \"{e.Node?.Text}\" is {(e.Node?.Checked == true ? "checked" : "unchecked")}.");
        tree.AfterSelect += (_, e) => this.SetStatus($"TreeView: \"{e.Node?.Text}\" selected.");

        var treeList = new TreeListView
        {
            Bounds = new(664, 292, 300, 220),
            ImageList = _icons,
            ItemHeight = 22,
        };
        treeList.Columns.AddRange([
            new TreeListViewColumn("Name", 140),
            new TreeListViewColumn("Kind", 70, static node => ((FsEntry?)node.Tag)?.Kind ?? string.Empty),
            new TreeListViewColumn("Size", 70, static node => ((FsEntry?)node.Tag)?.Size ?? string.Empty),
        ]);
        var roots = new FsEntry[]
        {
            new("src", "Folder", "", [
                new("app.cs", "Source", "9 KB", null),
                new("theme.cs", "Source", "3 KB", null),
            ]),
            new("assets", "Folder", "", [
                new("icons.dat", "Data", "22 KB", null),
            ]),
            new("build.sh", "Script", "1 KB", null),
        };
        treeList.SetDataSource(roots, static e => e.Name, static e => e.Children);
        treeList.Nodes[0].Expand();
        treeList.Nodes[1].Expand();
        treeList.AfterSelect += (_, e) => this.SetStatus($"TreeListView: \"{e.Node?.Text}\" selected.");

        page.Controls.AddRange(
            Caption("TreeView (icons + check boxes)", 664, 12),
            tree,
            Caption("TreeListView (SetDataSource, 3 columns)", 664, 268),
            treeList);

        return page;
    }
}
