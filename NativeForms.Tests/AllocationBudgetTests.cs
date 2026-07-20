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
}
