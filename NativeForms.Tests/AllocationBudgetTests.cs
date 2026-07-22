using Hawkynt.NativeForms;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Footprint guards. The toolkit promises "bytes, not megabytes", so these assert that constructing
/// controls and mutating bound collections stays within a tight managed-allocation budget. They use
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> with a warm-up pass to keep JIT noise out.
/// Budgets are generous enough to be stable across runtimes yet catch any order-of-magnitude bloat
/// (a stray per-control dictionary, boxing, reflection cache, …).
/// </summary>
[TestFixture]
internal sealed class AllocationBudgetTests
{
    private const int _Count = 4000;

    private static long Measure(Action action)
    {
        action(); // warm up the JIT so first-call compilation isn't counted
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void Unrealized_control_stays_within_byte_budget()
    {
        var sink = new Control[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = new Button();
        });

        var perControl = (double)bytes / _Count;
        Assert.That(perControl, Is.LessThan(512), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void Owner_drawn_control_construction_stays_within_budget()
    {
        var sink = new Control[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = new CheckBox();
        });

        var perControl = (double)bytes / _Count;
        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    /// <summary>Measures the per-instance construction cost of one control class.</summary>
    private static double PerInstance(Func<Control> factory)
    {
        var sink = new Control[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = factory();
        });

        return (double)bytes / _Count;
    }

    [Test]
    public void IconLabel_construction_stays_within_the_owner_drawn_budget()
    {
        var perControl = PerInstance(static () => new IconLabel());

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void ProgressTile_construction_stays_within_the_owner_drawn_budget()
    {
        var perControl = PerInstance(static () => new ProgressTile());

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void GridPicker_construction_stays_within_the_owner_drawn_budget()
    {
        // The whole state is the shared grid engine and the three callback delegates wiring it back —
        // no collection, no hosted editor — so it must sit inside the single-surface budget.
        var perControl = PerInstance(static () => new GridPicker());

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    /// <summary>
    /// §4's second tier. A control that hosts a native editor inside an owner-drawn shell pays for
    /// three things a plain owner-drawn control does not: the child <see cref="TextBox"/> (~296 B on
    /// its own), the child collection that holds it, and the delegates wiring the editor's text and
    /// key events back to the shell. That lands the whole family — <see cref="SearchBox"/>,
    /// <see cref="NumericUpDown"/>, <see cref="DomainUpDown"/>, <see cref="FilePicker"/>,
    /// <see cref="FolderPicker"/> — above the 768 B single-surface budget by construction, so they
    /// get their own ceiling rather than a blanket claim none of them ever met.
    /// </summary>
    private const int _HostedEditorCompositeBudget = 1024;

    [TestCase("SearchBox")]
    [TestCase("NumericUpDown")]
    [TestCase("DomainUpDown")]
    [TestCase("FilePicker")]
    [TestCase("FolderPicker")]
    public void Hosted_editor_composites_stay_within_their_budget(string which)
    {
        Func<Control> factory = which switch
        {
            "SearchBox" => static () => new SearchBox(),
            "NumericUpDown" => static () => new NumericUpDown(),
            "DomainUpDown" => static () => new DomainUpDown(),
            "FilePicker" => static () => new FilePicker(),
            "FolderPicker" => static () => new FolderPicker(),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
        };

        var perControl = PerInstance(factory);

        Assert.That(perControl, Is.LessThan(_HostedEditorCompositeBudget), $"{which}: ~{perControl:F0} bytes/control");
    }

    [Test]
    public void TimePicker_construction_stays_within_the_owner_drawn_budget()
    {
        // The time field owns no collections and no hosted editor: its whole state is the value, the
        // window, the caret and the layout flags, so it must sit well inside the owner-drawn budget.
        var sink = new Control[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = new TimePicker();
        });

        var perControl = (double)bytes / _Count;
        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void ClockFace_construction_stays_within_the_owner_drawn_budget()
    {
        // The analog dial's whole state is the value, the stage, the layout flags and the cached hand
        // endpoint: no collections, no hosted editor, and the string/trig tables are shared statics —
        // so a bare dial sits well inside the owner-drawn budget.
        var perControl = PerInstance(static () => new ClockFace());

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void MonthCalendar_construction_stays_within_the_owner_drawn_budget()
    {
        // The drill-down levels must not cost an unused calendar anything: the period captions are
        // allocated on the first drill-out, never in the constructor.
        var sink = new Control[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = new MonthCalendar();
        });

        var perControl = (double)bytes / _Count;
        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void CalendarView_construction_stays_within_the_owner_drawn_budget()
    {
        // The scheduler holds no appointment buffers until bound — an empty control is the layout
        // lists and the value-type view state, nothing per-appointment — so it must sit inside the
        // owner-drawn budget like every other single-surface control.
        var sink = new Control[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = new CalendarView();
        });

        var perControl = (double)bytes / _Count;
        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void An_appointment_is_a_small_value_type()
    {
        // Appointments are stored in one flat array, so their per-item cost is the struct's own size,
        // not a heap object each. Measuring a big array isolates that: total bytes over the count is
        // the element size the 100k case pays.
        const int count = 100_000;
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        Appointment[] sink = [];
        var bytes = Measure(() =>
        {
            sink = new Appointment[count];
            for (var i = 0; i < count; ++i)
                sink[i] = new Appointment("Item", start, start.AddMinutes(30));
        });

        var perItem = (double)bytes / count;
        Assert.That(perItem, Is.LessThan(64), $"~{perItem:F0} bytes/appointment");
    }

    [Test]
    public void Empty_form_realization_stays_within_byte_budget()
    {
        // Warm-up pass: JIT the whole construct-realize-flush path so the measured pass sees only
        // the per-form cost. Each pass gets a fresh backend so peer recording doesn't accumulate.
        var bytes = Measure(() =>
        {
            var form = new Form();
            Application.Run(form, new Fakes.HeadlessBackend());
        });

        // §4: an empty realized Form costs < 8 KB of managed memory beyond the native window. The
        // measurement even includes the fake backend and its recording peer — the stand-ins for the
        // native window — so the real toolkit-side cost is strictly below the asserted number.
        Assert.That(bytes, Is.LessThan(8192), $"{bytes} bytes to realize an empty form");
    }

    [Test]
    public void ObservableList_append_does_not_allocate_per_item_beyond_growth()
    {
        var list = new ObservableList<int>();
        // Pre-grow so the measured appends hit no resize, then appends should be allocation-free.
        for (var i = 0; i < _Count; ++i)
            list.Add(i);

        var before = GC.GetAllocatedBytesForCurrentThread();
        list.Add(1);
        list.Add(2);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // The null-conditional in OnListChanged must short-circuit before the event args are
        // constructed, so appends without a subscriber allocate nothing at all.
        Assert.That(bytes, Is.Zero, $"{bytes} bytes for two appends");
    }
    /// <summary>Measures the per-instance cost of a non-control type — the item models the strips and
    /// the ribbon hang off, which carry no peer and so have no owner-drawn budget of their own.</summary>
    private static double MeasurePerInstance(Func<object> factory)
    {
        var sink = new object[_Count];
        var bytes = Measure(() =>
        {
            for (var i = 0; i < _Count; ++i)
                sink[i] = factory();
        });

        return (double)bytes / _Count;
    }

    [Test]
    public void Accordion_construction_stays_within_the_owner_drawn_budget()
    {
        var perControl = MeasurePerInstance(static () => new Accordion());

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void AccordionPane_construction_stays_within_the_owner_drawn_budget()
    {
        var perControl = MeasurePerInstance(static () => new AccordionPane("Mail"));

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void Ribbon_construction_stays_within_the_owner_drawn_budget()
    {
        var perControl = MeasurePerInstance(static () => new Ribbon());

        Assert.That(perControl, Is.LessThan(768), $"~{perControl:F0} bytes/control");
    }

    [Test]
    public void Ribbon_structure_types_stay_tiny_per_instance()
    {
        // Tabs, groups and items are plain objects, not controls: a hundred ribbon buttons must cost
        // a hundred small allocations rather than a hundred native widgets.
        var tab = MeasurePerInstance(static () => new RibbonTab("Home"));
        var group = MeasurePerInstance(static () => new RibbonGroup("Clipboard"));
        var button = MeasurePerInstance(static () => new RibbonButton("Paste"));
        var toggle = MeasurePerInstance(static () => new RibbonToggleButton("Bold", RibbonItemSize.Small));

        // The floor is the shared ToolStripItem field set every strip item already pays for; a ribbon
        // button must not cost meaningfully more than the toolbar button it is modelled on.
        var baseline = MeasurePerInstance(static () => new ToolStripButton("Paste"));

        Assert.Multiple(() =>
        {
            Assert.That(tab, Is.LessThan(256), $"RibbonTab ~{tab:F0} bytes");
            Assert.That(group, Is.LessThan(256), $"RibbonGroup ~{group:F0} bytes");
            Assert.That(button, Is.LessThan(baseline + 16), $"RibbonButton ~{button:F0} bytes vs ToolStripButton ~{baseline:F0}");
            Assert.That(toggle, Is.LessThan(baseline + 32), $"RibbonToggleButton ~{toggle:F0} bytes vs ToolStripButton ~{baseline:F0}");
        });
    }

    [Test]
    public void A_populated_ribbon_stays_within_a_few_kilobytes()
    {
        // The structural worst case the toolkit ships: three tabs, six groups, twenty-four items.
        // If a ribbon of that size cost tens of kilobytes the design would be wrong, not the budget.
        var bytes = Measure(static () =>
        {
            var ribbon = new Ribbon();
            for (var t = 0; t < 3; ++t)
            {
                var tab = new RibbonTab("Tab");
                for (var g = 0; g < 2; ++g)
                {
                    var group = new RibbonGroup("Group");
                    group.Items.AddRange(
                        new RibbonButton("Large"),
                        new RibbonButton("One", RibbonItemSize.Small),
                        new RibbonButton("Two", RibbonItemSize.Small),
                        new RibbonToggleButton("Three", RibbonItemSize.Small));
                    tab.Groups.Add(group);
                }

                ribbon.Tabs.Add(tab);
            }
        });

        Assert.That(bytes, Is.LessThan(8192), $"{bytes} bytes for a 3-tab, 6-group, 24-item ribbon");
    }

    [Test]
    public void A_populated_accordion_stays_within_a_few_kilobytes()
    {
        var bytes = Measure(static () =>
        {
            var accordion = new Accordion();
            accordion.Panes.AddRange(
                new AccordionPane("Mail"),
                new AccordionPane("Calendar"),
                new AccordionPane("Contacts"));
        });

        Assert.That(bytes, Is.LessThan(4096), $"{bytes} bytes for a three-pane accordion");
    }

}
