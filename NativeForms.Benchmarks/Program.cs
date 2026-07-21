using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Hawkynt.NativeForms;

namespace Hawkynt.NativeForms.Benchmarks;

/// <summary>
/// The §4 measuring stick: a dependency-free Stopwatch micro-harness covering construction cost per
/// control type, realize cost, steady-state repaint throughput and large-scale scroll traversal —
/// all headless. Emits one JSON line per metric to stdout (for machines) followed by an aligned
/// table (for humans), and exits non-zero when a §4 regression threshold is crossed, so the nightly
/// job fails loudly instead of drifting quietly. Thresholds carry 2× headroom over the PRD budgets
/// to stay stable across runners.
/// </summary>
internal static class Program
{
    /// <summary>2× the §4 unrealized-control budget (512 B), the nightly regression gate.</summary>
    private const int _PlainConstructionBudget = 1024;

    /// <summary>2× the §4 owner-drawn construction budget (768 B).</summary>
    private const int _OwnerDrawnConstructionBudget = 1536;

    /// <summary>2× the §4 hosted-editor composite budget (1024 B) — a shell plus a native editor.</summary>
    private const int _CompositeConstructionBudget = 2048;

    /// <summary>2× the §4 empty-form realization budget (8 KB).</summary>
    private const int _EmptyFormRealizeBudget = 16384;

    private const int _ConstructionCount = 4000;
    private const int _PaintAllocationFrames = 100;
    private const int _PaintThroughputFrames = 2000;
    private const int _TraversalRows = 100_000;

    private static readonly List<string> _failures = [];
    private static readonly List<(string Metric, string Value)> _table = [];

    private sealed record Row(string Name, int Value);

    private static int Main()
    {
        // Construction: what a bare `new` costs per §4-governed control class.
        Construct("Button", static () => new Button(), _PlainConstructionBudget);
        Construct("Label", static () => new Label(), _PlainConstructionBudget);
        Construct("TextBox", static () => new TextBox(), _PlainConstructionBudget);
        Construct("CheckBox", static () => new CheckBox(), _OwnerDrawnConstructionBudget);
        Construct("ListBox", static () => new ListBox(), _OwnerDrawnConstructionBudget);
        Construct("ListView", static () => new ListView(), _OwnerDrawnConstructionBudget);
        Construct("TreeView", static () => new TreeView(), _OwnerDrawnConstructionBudget);
        Construct("DataGridView", static () => new DataGridView(), _OwnerDrawnConstructionBudget);
        Construct("TabControl", static () => new TabControl(), _OwnerDrawnConstructionBudget);
        Construct("TimePicker", static () => new TimePicker(), _OwnerDrawnConstructionBudget);
        Construct("MonthCalendar", static () => new MonthCalendar(), _OwnerDrawnConstructionBudget);
        Construct("IconLabel", static () => new IconLabel(), _OwnerDrawnConstructionBudget);
        Construct("ProgressTile", static () => new ProgressTile(), _OwnerDrawnConstructionBudget);
        Construct("FilePicker", static () => new FilePicker(), _CompositeConstructionBudget);
        Construct("FolderPicker", static () => new FolderPicker(), _CompositeConstructionBudget);
        Construct("Accordion", static () => new Accordion(), _OwnerDrawnConstructionBudget);
        Construct("Ribbon", static () => new Ribbon(), _OwnerDrawnConstructionBudget);

        // Item models carry no peer, so they have no §4 control budget — but a ribbon is made of
        // hundreds of them, so their per-instance cost is tracked all the same.
        ConstructItem("AccordionPane", static () => new AccordionPane("Mail"));
        ConstructItem("RibbonTab", static () => new RibbonTab("Home"));
        ConstructItem("RibbonGroup", static () => new RibbonGroup("Clipboard"));
        ConstructItem("RibbonButton", static () => new RibbonButton("Paste"));
        ConstructItem("RibbonToggleButton", static () => new RibbonToggleButton("Bold", RibbonItemSize.Small));

        // The two structure-heavy containers, measured populated: what a real navigation pane and a
        // real ribbon cost to build, not what their empty shells do.
        ConstructAggregate("accordion3", static () => MakeAccordion());
        ConstructAggregate("ribbon3x2", static () => MakeRibbon());

        RealizeEmptyForm();
        RealizeHundredControlForm();

        // Steady-state repaint throughput of the data-heavy controls; a warmed frame must allocate 0.
        PaintThroughput("ListBox", MakeListBox(1000));
        PaintThroughput("ListView", MakeListView(1000));
        PaintThroughput("TreeView", MakeTreeView(1000));
        PaintThroughput("DataGridView", MakeDataGridView(1000));
        PaintThroughput("DataGridViewLists", MakeListColumnGrid(1000));
        PaintThroughput("TimePicker", MakeTimePicker());
        PaintThroughput("MonthCalendar", MakeMonthCalendar());
        PaintThroughput("IconLabel", MakeIconLabel());
        PaintThroughput("ProgressTile", MakeProgressTile());
        PaintThroughput("FilePicker", MakeFilePicker());
        PaintThroughput("Accordion", MakeAccordion());
        PaintThroughput("Ribbon", MakeRibbon());

        // Full traversal of a 100k-row control, painting every step — the "no GC in scroll" story.
        ScrollTraversal("ListView", MakeListView(_TraversalRows), Keys.PageDown);
        ScrollTraversal("DataGridView", MakeDataGridView(_TraversalRows), Keys.PageDown);
        ScrollTraversal("DataGridViewLists", MakeListColumnGrid(_TraversalRows), Keys.PageDown);
        ScrollTraversal("TreeView", MakeTreeView(_TraversalRows), key: null);

        PrintTable();

        if (_failures.Count == 0)
            return 0;

        Console.WriteLine();
        Console.WriteLine("§4 regression thresholds crossed:");
        foreach (var failure in _failures)
            Console.WriteLine($"  FAIL {failure}");

        return 1;
    }

    // ---- Metric: construction ----

    private static void Construct(string name, Func<Control> factory, int byteBudget)
    {
        var sink = new Control[_ConstructionCount];
        for (var i = 0; i < _ConstructionCount; ++i)
            sink[i] = factory(); // warm-up: JIT + static cctors stay out of the measurement

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < _ConstructionCount; ++i)
            sink[i] = factory();
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        var nsPerOp = watch.Elapsed.TotalNanoseconds / _ConstructionCount;
        var bytesPerOp = (double)bytes / _ConstructionCount;
        Emit($"construct.{name}", $"{{\"ns_per_op\":{F(nsPerOp)},\"bytes_per_op\":{F(bytesPerOp)}}}",
            $"{nsPerOp,8:F0} ns/op  {bytesPerOp,6:F0} B/op");

        if (bytesPerOp >= byteBudget)
            _failures.Add($"construct.{name}: {bytesPerOp:F0} B/op exceeds the {byteBudget} B budget");
    }

    /// <summary>Per-instance construction cost of a peerless item model.</summary>
    private static void ConstructItem(string name, Func<object> factory)
    {
        var sink = new object[_ConstructionCount];
        for (var i = 0; i < _ConstructionCount; ++i)
            sink[i] = factory(); // warm-up

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < _ConstructionCount; ++i)
            sink[i] = factory();
        watch.Stop();
        var bytes = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / _ConstructionCount;

        var nsPerOp = watch.Elapsed.TotalNanoseconds / _ConstructionCount;
        Emit($"construct.{name}", $"{{\"ns_per_op\":{F(nsPerOp)},\"bytes_per_op\":{F(bytes)}}}",
            $"{nsPerOp,8:F0} ns/op  {bytes,6:F0} B/op");
    }

    /// <summary>Construction cost of a whole populated control tree — reported, not gated, because
    /// the number is a design signal rather than a per-instance budget.</summary>
    private static void ConstructAggregate(string name, Func<Control> factory)
    {
        for (var i = 0; i < 64; ++i)
            factory(); // warm-up

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 64; ++i)
            factory();
        watch.Stop();
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 64.0;

        var nsPerOp = watch.Elapsed.TotalNanoseconds / 64;
        Emit($"construct.{name}", $"{{\"ns_per_op\":{F(nsPerOp)},\"bytes_per_op\":{F(bytes)}}}",
            $"{nsPerOp,8:F0} ns/op  {bytes,6:F0} B/op");
    }

    // ---- Metric: realization ----

    private static void RealizeEmptyForm()
    {
        // One warm-up pass, then a fresh form + backend per measured pass, like the unit test.
        Realize(new Form());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        Realize(new Form());
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        var micros = watch.Elapsed.TotalMicroseconds;
        Emit("realize.emptyForm", $"{{\"us\":{F(micros)},\"bytes\":{bytes}}}", $"{micros,8:F0} µs     {bytes,6} B");

        if (bytes >= _EmptyFormRealizeBudget)
            _failures.Add($"realize.emptyForm: {bytes} B exceeds the {_EmptyFormRealizeBudget} B budget");
    }

    private static void RealizeHundredControlForm()
    {
        Realize(MakeHundredControlForm()); // warm-up

        var form = MakeHundredControlForm();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        Realize(form);
        watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        var micros = watch.Elapsed.TotalMicroseconds;
        Emit("realize.form100", $"{{\"us\":{F(micros)},\"bytes\":{bytes}}}", $"{micros,8:F0} µs     {bytes,6} B");
    }

    private static Form MakeHundredControlForm()
    {
        var form = new Form { Bounds = new(0, 0, 1200, 800) };
        for (var i = 0; i < 25; ++i)
        {
            form.Controls.Add(new Button { Text = "Button", Bounds = new(0, i * 30, 100, 26) });
            form.Controls.Add(new Label { Text = "Label", Bounds = new(110, i * 30, 100, 20) });
            form.Controls.Add(new TextBox { Text = "Text", Bounds = new(220, i * 30, 100, 24) });
            form.Controls.Add(new CheckBox { Text = "Check", Bounds = new(330, i * 30, 100, 20) });
        }

        return form;
    }

    // ---- Metric: steady-state repaint ----

    private static void PaintThroughput(string name, OwnerDrawnControl control)
    {
        var canvas = RealizeOnCanvas(control);
        canvas.PerformPaint();
        canvas.PerformPaint(); // warm-up: JIT the paint path and build caches/reused args

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < _PaintAllocationFrames; ++i)
            canvas.PerformPaint();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < _PaintThroughputFrames; ++i)
            canvas.PerformPaint();
        watch.Stop();

        var paintsPerSecond = _PaintThroughputFrames / watch.Elapsed.TotalSeconds;
        Emit($"paint.{name}", $"{{\"paints_per_sec\":{F(paintsPerSecond)},\"bytes_per_frame\":{F((double)bytes / _PaintAllocationFrames)}}}",
            $"{paintsPerSecond,8:F0} paints/s  {(double)bytes / _PaintAllocationFrames,4:F0} B/frame");

        if (bytes != 0)
            _failures.Add($"paint.{name}: {bytes} B allocated over {_PaintAllocationFrames} steady-state frames (must be 0)");
    }

    // ---- Metric: scroll traversal ----

    private static void ScrollTraversal(string name, OwnerDrawnControl control, Keys? key)
    {
        var canvas = RealizeOnCanvas(control);
        canvas.PerformPaint(); // warm-up frame

        // Page keys cover a viewport per step; the wheel-driven fallback covers 3 rows per notch.
        var steps = key is null ? _TraversalRows / 3 + 1 : _TraversalRows / 10 + 1;
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < steps; ++i)
        {
            if (key is { } k)
                canvas.PerformKeyDown(k);
            else
                canvas.PerformMouseWheel(-120);

            canvas.PerformPaint();
        }

        watch.Stop();
        var rowsPerSecond = _TraversalRows / watch.Elapsed.TotalSeconds;
        Emit($"scroll.{name}", $"{{\"rows\":{_TraversalRows},\"ms\":{F(watch.Elapsed.TotalMilliseconds)},\"rows_per_sec\":{F(rowsPerSecond)}}}",
            $"{watch.Elapsed.TotalMilliseconds,8:F0} ms     {rowsPerSecond,10:F0} rows/s");
    }

    // ---- Control factories ----

    private static ListBox MakeListBox(int rows)
    {
        var list = new ListBox { Bounds = new(0, 0, 320, 440) };
        for (var i = 0; i < rows; ++i)
            list.Items.Add("Row " + i);
        list.SelectedIndex = 0;
        return list;
    }

    private static ListView MakeListView(int rows)
    {
        var list = new ListView { Bounds = new(0, 0, 480, 440) };
        list.Columns.Add(new ColumnHeader("Name", 240));
        list.Columns.Add(new ColumnHeader("Size", 120));
        for (var i = 0; i < rows; ++i)
            list.Items.Add(new ListViewItem("Row " + i, "1 KB"));
        list.SelectedIndex = 0;
        return list;
    }

    private static TreeView MakeTreeView(int rows)
    {
        var tree = new TreeView { Bounds = new(0, 0, 320, 440) };
        for (var i = 0; i < rows; ++i)
            tree.Nodes.Add("Node " + i);
        return tree;
    }

    /// <summary>The busiest time field: twelve-hour, seconds shown, so every part paints.</summary>
    private static TimePicker MakeTimePicker()
        => new() { Bounds = new(0, 0, 200, 26), Value = new(14, 30, 5), Use24HourClock = false };

    private static MonthCalendar MakeMonthCalendar()
        => new() { Bounds = new(0, 0, 240, 200), TodayDate = new(2026, 7, 19) };
    /// <summary>A three-pane navigation stack with real children in the open pane.</summary>
    private static Accordion MakeAccordion()
    {
        var accordion = new Accordion { Bounds = new(0, 0, 240, 440) };
        var mail = new AccordionPane("Mail");
        mail.Controls.Add(new CheckBox { Text = "Unread only", Bounds = new(8, 8, 160, 20) });
        mail.Controls.Add(new Button { Text = "Compose", Bounds = new(8, 34, 100, 26) });
        var calendar = new AccordionPane("Calendar");
        calendar.Controls.Add(new RadioButton { Text = "Week", Bounds = new(8, 8, 120, 20) });
        accordion.Panes.AddRange(mail, calendar, new AccordionPane("Contacts"));
        return accordion;
    }

    /// <summary>A three-tab, six-group ribbon mixing both item sizes and a hosted control.</summary>
    private static Ribbon MakeRibbon()
    {
        var ribbon = new Ribbon { Bounds = new(0, 0, 900, 120) };
        for (var t = 0; t < 3; ++t)
        {
            var tab = new RibbonTab("Tab " + t);
            for (var g = 0; g < 2; ++g)
            {
                var group = new RibbonGroup("Group " + g);
                group.Items.AddRange(
                    new RibbonButton("Large"),
                    new RibbonButton("One", RibbonItemSize.Small),
                    new RibbonButton("Two", RibbonItemSize.Small),
                    new RibbonToggleButton("Three", RibbonItemSize.Small));
                tab.Groups.Add(group);
            }

            ribbon.Tabs.Add(tab);
        }

        return ribbon;
    }

    private static DataGridView MakeDataGridView(int rows)
    {
        var grid = new DataGridView { Bounds = new(0, 0, 480, 440) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name));
        grid.Columns.Add(new DataGridViewColumn("Value", static o => ((Row)o!).Value) { Width = 80 });
        for (var i = 0; i < rows; ++i)
            grid.Items.Add(new Row("Row " + i, i));
        grid.SelectedRowIndex = 0;
        return grid;
    }

    /// <summary>
    /// A grid carrying the popup-list column kinds: a single-select list mapped through an item
    /// display selector, plus the two set-valued ones whose closed cells summarise a whole item
    /// collection. Their per-row work must stay behind the display-text cache, so this grid has to
    /// paint as cheaply — and as allocation-free — as the plain one above.
    /// </summary>
    private static DataGridView MakeListColumnGrid(int rows)
    {
        var choices = new object?[] { "Alpha", "Beta", "Gamma", "Delta" };
        var grid = new DataGridView { Bounds = new(0, 0, 480, 440) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name) { Width = 120 });
        grid.Columns.Add(new DataGridViewColumn("Pick", static o => ((Row)o!).Name)
        {
            Kind = DataGridViewColumnKind.ListBox,
            Width = 120,
            ItemsSelector = _ => choices,
            ItemDisplaySelector = static c => (string)c!,
            ValueSetter = static (_, _) => { },
        });
        grid.Columns.Add(new DataGridViewColumn("Picks", static _ => null)
        {
            Kind = DataGridViewColumnKind.ListBox,
            SelectionMode = SelectionMode.MultiExtended,
            Width = 120,
            ItemsSelector = _ => choices,
            CheckedItemsSelector = _ => choices,
            CheckedItemsSetter = static (_, _) => { },
        });
        grid.Columns.Add(new DataGridViewColumn("Tags", static _ => null)
        {
            Kind = DataGridViewColumnKind.CheckedListBox,
            Width = 120,
            ItemsSelector = _ => choices,
            CheckedItemsSelector = _ => choices,
            CheckedItemsSetter = static (_, _) => { },
        });
        for (var i = 0; i < rows; ++i)
            grid.Items.Add(new Row("Row " + i, i));
        grid.SelectedRowIndex = 0;
        return grid;
    }
    private static IconLabel MakeIconLabel()
        => new() { Text = "Documents", Image = new BenchImage(16, 16), Bounds = new(0, 0, 200, 24) };

    private static ProgressTile MakeProgressTile() => new()
    {
        Text = "Windows (C:)",
        SecondaryText = "45.2 GB free of 128 GB",
        Image = new BenchImage(24, 24),
        Bounds = new(0, 0, 220, 64),
        Maximum = 128,
        Value = 118,
        WarningThreshold = 115,
    };

    private static FilePicker MakeFilePicker()
        => new() { Bounds = new(0, 0, 240, 26), SelectedPath = "/tmp/missing.txt" };

    // ---- Plumbing ----

    private static void Realize(Form form) => Application.Run(form, new BenchBackend());

    private static BenchCanvasPeer RealizeOnCanvas(OwnerDrawnControl control)
    {
        var backend = new BenchBackend();
        var form = new Form { Bounds = new(0, 0, 640, 480) };
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Canvases[0];
    }

    private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static void Emit(string metric, string jsonBody, string human)
    {
        Console.WriteLine($"{{\"metric\":\"{metric}\",{jsonBody[1..]}");
        _table.Add((metric, human));
    }

    private static void PrintTable()
    {
        Console.WriteLine();
        Console.WriteLine($"{"metric",-22} value");
        Console.WriteLine(new string('-', 60));
        foreach (var (metric, value) in _table)
            Console.WriteLine($"{metric,-22} {value}");
    }
}
