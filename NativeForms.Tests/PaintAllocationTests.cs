using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The zero-per-frame-allocation guarantee for owner-drawn painting: once realized and warmed up, a
/// repaint driven through the peer's reused <see cref="PaintEventArgs"/> must allocate no managed
/// memory at all. Painted onto <see cref="NullGraphics"/> so the only bytes the measurement could
/// see are the control's own — a regression here means a paint path started newing per frame.
/// Every owner-drawn control is swept with a representative instance (filled lists, expanded trees,
/// populated strips, active containers) so no paint path escapes the guarantee.
/// </summary>
[TestFixture]
internal sealed class PaintAllocationTests
{
    private const int _Frames = 100;

    private sealed record Person(string Name, int Age);

    /// <summary>Realizes the control, warms the paint paths up, then measures a steady-state run
    /// over every canvas the control tree realized (containers own several).</summary>
    private static long MeasureSteadyStatePaint(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 640, 480) };
        form.Controls.Add(control);
        Application.Run(form, backend);
        var canvases = backend.Created.OfType<HeadlessCanvasPeer>().ToArray();
        var graphics = new NullGraphics();

        // Warm up: JIT the paint paths and let each peer build its reusable event args.
        for (var pass = 0; pass < 2; ++pass)
            foreach (var canvas in canvases)
                canvas.RaisePaint(graphics);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < _Frames; ++i)
            foreach (var canvas in canvases)
                canvas.RaisePaint(graphics);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // ---- Toggles, indicators, labels ----

    [Test]
    public void CheckBox_steady_state_repaint_allocates_nothing()
    {
        var check = new CheckBox { Text = "Remember me", Bounds = new(0, 0, 160, 20), Checked = true };

        Assert.That(MeasureSteadyStatePaint(check), Is.Zero);
    }

    [Test]
    public void RadioButton_steady_state_repaint_allocates_nothing()
    {
        var radio = new RadioButton { Text = "Option A", Bounds = new(0, 0, 160, 20), Checked = true };

        Assert.That(MeasureSteadyStatePaint(radio), Is.Zero);
    }

    [Test]
    public void ToggleSwitch_steady_state_repaint_allocates_nothing()
    {
        var toggle = new ToggleSwitch { Text = "Run", Bounds = new(0, 0, 120, 24), Checked = true };

        Assert.That(MeasureSteadyStatePaint(toggle), Is.Zero);
    }

    [Test]
    public void ProgressBar_steady_state_repaint_allocates_nothing()
    {
        var bar = new ProgressBar { Bounds = new(0, 0, 160, 20), Value = 40 };

        Assert.That(MeasureSteadyStatePaint(bar), Is.Zero);
    }

    [Test]
    public void LinkLabel_steady_state_repaint_allocates_nothing()
    {
        var link = new LinkLabel { Text = "Read more", Bounds = new(0, 0, 120, 24) };

        Assert.That(MeasureSteadyStatePaint(link), Is.Zero);
    }

    [Test]
    public void PictureBox_steady_state_repaint_allocates_nothing()
    {
        var picture = new PictureBox { Bounds = new(0, 0, 100, 100), Image = new HeadlessImage(16, 16) };

        Assert.That(MeasureSteadyStatePaint(picture), Is.Zero);
    }

    // ---- Buttons and drop-downs ----

    [Test]
    public void DropDownButton_steady_state_repaint_allocates_nothing()
    {
        var button = new DropDownButton { Text = "Open", Bounds = new(0, 0, 120, 26) };

        Assert.That(MeasureSteadyStatePaint(button), Is.Zero);
    }

    [Test]
    public void SplitButton_steady_state_repaint_allocates_nothing()
    {
        var button = new SplitButton { Text = "Save", Bounds = new(0, 0, 120, 26) };

        Assert.That(MeasureSteadyStatePaint(button), Is.Zero);
    }

    [Test]
    public void ComboBox_steady_state_repaint_allocates_nothing()
    {
        var combo = new ComboBox { Bounds = new(0, 0, 140, 24) };
        combo.Items.AddRange(["Alpha", "Beta", "Gamma"]);
        combo.SelectedIndex = 1;

        Assert.That(MeasureSteadyStatePaint(combo), Is.Zero);
    }

    [Test]
    public void SearchBox_steady_state_repaint_allocates_nothing()
    {
        var search = new SearchBox { Bounds = new(0, 0, 180, 24), Text = "query" };

        Assert.That(MeasureSteadyStatePaint(search), Is.Zero);
    }

    // ---- Spinners and pickers ----

    [Test]
    public void NumericUpDown_steady_state_repaint_allocates_nothing()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24), Maximum = 1000, Value = 42, DecimalPlaces = 2 };

        Assert.That(MeasureSteadyStatePaint(upDown), Is.Zero);
    }

    [Test]
    public void DomainUpDown_steady_state_repaint_allocates_nothing()
    {
        var upDown = new DomainUpDown { Bounds = new(0, 0, 120, 24) };
        upDown.Items.AddRange(["Red", "Green", "Blue"]);
        upDown.SelectedIndex = 1;

        Assert.That(MeasureSteadyStatePaint(upDown), Is.Zero);
    }

    [Test]
    public void DateTimePicker_steady_state_repaint_allocates_nothing()
    {
        var picker = new DateTimePicker { Bounds = new(0, 0, 200, 24), Value = new(2026, 7, 19, 14, 30, 5) };

        Assert.That(MeasureSteadyStatePaint(picker), Is.Zero);
    }

    [Test]
    public void MonthCalendar_steady_state_repaint_allocates_nothing()
    {
        var calendar = new MonthCalendar { Bounds = new(0, 0, 210, 180), TodayDate = new(2026, 7, 19) };

        Assert.That(MeasureSteadyStatePaint(calendar), Is.Zero);
    }

    // ---- Range controls ----

    [Test]
    public void TrackBar_steady_state_repaint_allocates_nothing()
    {
        var track = new TrackBar { Bounds = new(0, 0, 200, 32), Maximum = 100, Value = 30 };

        Assert.That(MeasureSteadyStatePaint(track), Is.Zero);
    }

    [Test]
    public void HScrollBar_steady_state_repaint_allocates_nothing()
    {
        var bar = new HScrollBar { Bounds = new(0, 0, 200, 16), Maximum = 100, Value = 30 };

        Assert.That(MeasureSteadyStatePaint(bar), Is.Zero);
    }

    [Test]
    public void VScrollBar_steady_state_repaint_allocates_nothing()
    {
        var bar = new VScrollBar { Bounds = new(0, 0, 16, 200), Maximum = 100, Value = 30 };

        Assert.That(MeasureSteadyStatePaint(bar), Is.Zero);
    }

    // ---- Item lists ----

    [Test]
    public void ListBox_steady_state_repaint_allocates_nothing()
    {
        var list = new ListBox { Bounds = new(0, 0, 160, 110) };
        list.Items.AddRange(["One", "Two", "Three", "Four", "Five"]);
        list.SelectedIndex = 1;

        Assert.That(MeasureSteadyStatePaint(list), Is.Zero);
    }

    [Test]
    public void CheckedListBox_steady_state_repaint_allocates_nothing()
    {
        var list = new CheckedListBox { Bounds = new(0, 0, 160, 110) };
        list.Items.AddRange(["One", "Two", "Three"]);
        list.SetItemChecked(1, true);

        Assert.That(MeasureSteadyStatePaint(list), Is.Zero);
    }

    [Test]
    public void ListView_steady_state_repaint_allocates_nothing()
    {
        var list = new ListView { Bounds = new(0, 0, 320, 160) };
        list.Columns.AddRange([new ColumnHeader("Name", 140), new ColumnHeader("Size", 80)]);
        list.Items.AddRange(
        [
            new ListViewItem("File1", "1 KB"),
            new ListViewItem("File2", "2 KB"),
            new ListViewItem("File3", "3 KB"),
        ]);
        list.SelectedIndex = 0;

        Assert.That(MeasureSteadyStatePaint(list), Is.Zero);
    }

    [Test]
    public void TreeView_steady_state_repaint_allocates_nothing()
    {
        var tree = new TreeView { Bounds = new(0, 0, 240, 200) };
        var root = tree.Nodes.Add("root");
        var child = root.Nodes.Add("child");
        child.Nodes.Add("leaf");
        tree.Nodes.Add("sibling");
        root.ExpandAll();
        tree.SelectedNode = child;

        Assert.That(MeasureSteadyStatePaint(tree), Is.Zero);
    }

    [Test]
    public void TreeListView_steady_state_repaint_allocates_nothing()
    {
        var tree = new TreeListView { Bounds = new(0, 0, 320, 200) };
        tree.Columns.AddRange(
        [
            new TreeListViewColumn("Name", 200),
            new TreeListViewColumn("Size", 80, static n => n.Tag as string ?? string.Empty),
        ]);
        var root = tree.Nodes.Add("root");
        root.Nodes.Add("child").Tag = "1 KB";
        root.ExpandAll();

        Assert.That(MeasureSteadyStatePaint(tree), Is.Zero);
    }

    [Test]
    public void DataGridView_steady_state_repaint_allocates_nothing()
    {
        // The Age column exercises the boxing/formatting path the display-text pipeline must cache:
        // a value-typed cell with a FormatSelector may only pay for its string when the row changes.
        var grid = new DataGridView { Bounds = new(0, 0, 320, 160) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Person)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Person)o!).Age)
        {
            Width = 60,
            FormatSelector = static v => $"{v} yrs",
        });
        grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25), new Person("Carol", 40)]);
        grid.SelectedRowIndex = 1;

        Assert.That(MeasureSteadyStatePaint(grid), Is.Zero);
    }

    // ---- Strips and menus ----

    [Test]
    public void MenuStrip_steady_state_repaint_allocates_nothing()
    {
        var strip = new MenuStrip { Bounds = new(0, 0, 320, 24) };
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("Open"));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Exit"));
        strip.Items.AddRange(file, new ToolStripMenuItem("&Edit"));

        Assert.That(MeasureSteadyStatePaint(strip), Is.Zero);
    }

    [Test]
    public void MenuStrip_open_drop_down_steady_state_repaint_allocates_nothing()
    {
        // Covers the popup-hosted menu paint path (MenuDropDown): open the File menu first, then
        // sweep every surface — bar and drop-down — like a hover-repaint storm would.
        var strip = new MenuStrip { Bounds = new(0, 0, 320, 24) };
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("Open"));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Exit"));
        strip.Items.Add(file);

        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 640, 480) };
        form.Controls.Add(strip);
        Application.Run(form, backend);
        backend.Created.OfType<HeadlessCanvasPeer>().First().RaiseMouseDown(5, 5); // open the menu

        var canvases = backend.Created.OfType<HeadlessCanvasPeer>().ToArray();
        var graphics = new NullGraphics();
        for (var pass = 0; pass < 2; ++pass)
            foreach (var canvas in canvases)
                canvas.RaisePaint(graphics);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < _Frames; ++i)
            foreach (var canvas in canvases)
                canvas.RaisePaint(graphics);

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    [Test]
    public void ToolStrip_steady_state_repaint_allocates_nothing()
    {
        var strip = new ToolStrip { Bounds = new(0, 0, 320, 28) };
        strip.Items.Add(new ToolStripButton("Run"));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripDropDownButton("Menu"));
        strip.Items.Add(new ToolStripSplitButton("Split"));

        Assert.That(MeasureSteadyStatePaint(strip), Is.Zero);
    }

    [Test]
    public void StatusStrip_steady_state_repaint_allocates_nothing()
    {
        var strip = new StatusStrip { Bounds = new(0, 0, 320, 24) };
        strip.Items.AddRange(
            new ToolStripStatusLabel("Ready"),
            new ToolStripStatusLabel { Spring = true },
            new ToolStripProgressBarItem { Value = 50 });

        Assert.That(MeasureSteadyStatePaint(strip), Is.Zero);
    }

    // ---- Containers ----

    [Test]
    public void Panel_steady_state_repaint_allocates_nothing()
    {
        var panel = new Panel { Bounds = new(0, 0, 200, 150) };
        panel.Controls.Add(new CheckBox { Text = "Child", Bounds = new(10, 10, 120, 20) });

        Assert.That(MeasureSteadyStatePaint(panel), Is.Zero);
    }

    [Test]
    public void GroupBox_steady_state_repaint_allocates_nothing()
    {
        var box = new GroupBox { Text = "Options", Bounds = new(0, 0, 200, 120) };
        box.Controls.Add(new RadioButton { Text = "Choice", Bounds = new(10, 24, 120, 20) });

        Assert.That(MeasureSteadyStatePaint(box), Is.Zero);
    }

    [Test]
    public void Expander_steady_state_repaint_allocates_nothing()
    {
        var expander = new Expander { Text = "Details", Bounds = new(0, 0, 200, 150), Expanded = true };
        expander.Controls.Add(new CheckBox { Text = "Inner", Bounds = new(10, 30, 120, 20) });

        Assert.That(MeasureSteadyStatePaint(expander), Is.Zero);
    }

    [Test]
    public void TabControl_steady_state_repaint_allocates_nothing()
    {
        var tabs = new TabControl { Bounds = new(0, 0, 320, 200) };
        var one = new TabPage("One");
        one.Controls.Add(new CheckBox { Text = "Inner", Bounds = new(10, 10, 120, 20) });
        tabs.TabPages.AddRange(one, new TabPage("Two"));

        Assert.That(MeasureSteadyStatePaint(tabs), Is.Zero);
    }

    [Test]
    public void SplitContainer_steady_state_repaint_allocates_nothing()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 320, 160), SplitterDistance = 120 };
        split.Panel1.Controls.Add(new CheckBox { Text = "Left", Bounds = new(10, 10, 100, 20) });
        split.Panel2.Controls.Add(new CheckBox { Text = "Right", Bounds = new(10, 10, 100, 20) });

        Assert.That(MeasureSteadyStatePaint(split), Is.Zero);
    }

    [Test]
    public void FlowLayoutPanel_steady_state_repaint_allocates_nothing()
    {
        var panel = new FlowLayoutPanel { Bounds = new(0, 0, 300, 100) };
        panel.Controls.Add(new CheckBox { Text = "A", Bounds = new(0, 0, 60, 20) });
        panel.Controls.Add(new CheckBox { Text = "B", Bounds = new(0, 0, 60, 20) });

        Assert.That(MeasureSteadyStatePaint(panel), Is.Zero);
    }

    [Test]
    public void TableLayoutPanel_steady_state_repaint_allocates_nothing()
    {
        var table = new TableLayoutPanel { Bounds = new(0, 0, 300, 120), ColumnCount = 2, RowCount = 2 };
        table.ColumnStyles.Add(new(SizeType.Percent, 50));
        table.ColumnStyles.Add(new(SizeType.Percent, 50));
        table.Controls.Add(new CheckBox { Text = "Cell", Bounds = new(0, 0, 60, 20) });

        Assert.That(MeasureSteadyStatePaint(table), Is.Zero);
    }
}
