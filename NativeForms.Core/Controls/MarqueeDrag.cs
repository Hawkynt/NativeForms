using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>How a marquee drag combines the band it sweeps with the selection that already existed.</summary>
internal enum MarqueeCombine
{
    /// <summary>The band becomes the selection; whatever was selected before is dropped.</summary>
    Replace,

    /// <summary>The band is added to the selection, which is what Shift means everywhere else.</summary>
    Add,

    /// <summary>The band flips what it covers, which is what Ctrl means everywhere else.</summary>
    Toggle,
}

/// <summary>
/// One rubber-band selection gesture: where it started, where the pointer is now, what the selection
/// looked like before it began, and the edge auto-scroll that keeps it going past the viewport
/// (PRD §14).
/// </summary>
/// <remarks>
/// <para>
/// Shared by <see cref="ListView"/> and <see cref="DataGridView"/> rather than written twice, so both
/// answer to one set of keyboard semantics and one drag threshold. What differs between them is only
/// which indices a rectangle covers, which is the part each control has to answer for itself.
/// </para>
/// <para>
/// The baseline is snapshotted <em>before</em> the press that starts the drag applies its own
/// selection change. That is what makes Ctrl come out right: the press toggles the row under the
/// pointer, the band covers that same row, and combining against the pre-press selection toggles it
/// exactly once rather than back again.
/// </para>
/// <para>
/// A control holds one nullable reference to this and allocates it only while a drag is in flight, so
/// the gesture costs an idle control eight bytes. The two scratch lists are reused across every move
/// of a drag: a band re-evaluates on every mouse-move message, and a fresh list per message would
/// allocate through the whole gesture.
/// </para>
/// </remarks>
internal sealed class MarqueeDrag : IDisposable
{
    /// <summary>
    /// How far the pointer must travel before a press counts as a band rather than a click. Without
    /// it every click would sweep a zero-sized band and rewrite the selection the click just made.
    /// </summary>
    public const int Threshold = 4;

    /// <summary>Fast enough to feel continuous, slow enough not to overshoot a short list.</summary>
    private const int _AutoScrollIntervalMs = 60;

    /// <summary>The selection as it stood before the press, sorted so a lookup is a binary search.</summary>
    private readonly int[] _baseline;

    private Timer? _autoScroll;

    public MarqueeDrag(Point origin, MarqueeCombine combine, int[] baseline)
    {
        Array.Sort(baseline);
        _baseline = baseline;
        this.Origin = origin;
        this.Current = origin;
        this.Combine = combine;
    }

    /// <summary>Where the press landed, in client coordinates.</summary>
    public Point Origin { get; }

    /// <summary>Where the pointer is now, in client coordinates.</summary>
    public Point Current { get; private set; }

    /// <summary>What the band does to the selection it covers.</summary>
    public MarqueeCombine Combine { get; }

    /// <summary>
    /// Whether the pointer has travelled far enough for this to be a band. Until it has, the gesture
    /// is still a click and the selection is left exactly as the press made it.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>The indices the band currently covers, refilled by the owning control on every move.</summary>
    public List<int> Covered { get; } = [];

    /// <summary>The selection the band implies, rebuilt on every move and compared before it is applied.</summary>
    public List<int> Desired { get; } = [];

    /// <summary>The selection as it stood before the press that began this drag.</summary>
    public ReadOnlySpan<int> Baseline => _baseline;

    /// <summary>The swept rectangle, normalized so it is valid whichever way the drag runs.</summary>
    public Rectangle Band => Rectangle.FromLTRB(
        Math.Min(this.Origin.X, this.Current.X),
        Math.Min(this.Origin.Y, this.Current.Y),
        Math.Max(this.Origin.X, this.Current.X),
        Math.Max(this.Origin.Y, this.Current.Y));

    /// <summary>
    /// Moves the pointer, reporting whether the band is live and so whether the selection wants
    /// re-evaluating.
    /// </summary>
    public bool MoveTo(Point point)
    {
        this.Current = point;
        this.Active |= Math.Abs(point.X - this.Origin.X) >= Threshold
            || Math.Abs(point.Y - this.Origin.Y) >= Threshold;

        return this.Active;
    }

    /// <summary>Whether an index was selected before the drag began.</summary>
    public bool WasSelected(int index) => Array.BinarySearch(_baseline, index) >= 0;

    /// <summary>Whether an index ends up selected, given whether the band covers it.</summary>
    public bool Selects(int index, bool covered)
        => this.Combine switch
        {
            MarqueeCombine.Replace => covered,
            MarqueeCombine.Add => covered || this.WasSelected(index),
            _ => covered ^ this.WasSelected(index),
        };

    /// <summary>
    /// Rebuilds <see cref="Desired"/> from <see cref="Covered"/> and the baseline. Both are sorted, so
    /// the result is too and the caller can compare it against its own sorted selection directly.
    /// </summary>
    /// <param name="selectable">
    /// Whether an index from the baseline may still be selected — a row can have become hidden or
    /// unselectable since the press.
    /// </param>
    public void BuildDesired(Func<int, bool> selectable)
    {
        this.Covered.Sort();

        var desired = this.Desired;
        desired.Clear();

        foreach (var index in _baseline)
            if (this.Selects(index, this.Covered.BinarySearch(index) >= 0) && selectable(index))
                desired.Add(index);

        foreach (var index in this.Covered)
            if (!this.WasSelected(index) && this.Selects(index, covered: true))
                desired.Add(index);

        desired.Sort();
    }

    /// <summary>Whether <see cref="Desired"/> already matches a sorted selection, so nothing need change.</summary>
    public bool Matches(List<int> selection)
    {
        var desired = this.Desired;
        if (desired.Count != selection.Count)
            return false;

        for (var i = 0; i < desired.Count; ++i)
            if (desired[i] != selection[i])
                return false;

        return true;
    }

    /// <summary>
    /// Turns the edge auto-scroll on or off. The band keeps growing while the pointer sits outside the
    /// viewport, which is the whole point of dragging to the edge — a gesture that only scrolled on
    /// mouse-move would stop the moment the user held still.
    /// </summary>
    /// <param name="backend">The backend the timer source comes from; nothing happens without one.</param>
    /// <param name="step">Scrolls one row in the direction the caller works out from <see cref="Current"/>.</param>
    /// <param name="wanted">Whether the pointer is outside the viewport right now.</param>
    public void AutoScroll(IPlatformBackend? backend, EventHandler step, bool wanted)
    {
        if (!wanted || backend is null)
        {
            _autoScroll?.Stop();
            return;
        }

        if (_autoScroll is null)
        {
            _autoScroll = new(backend) { Interval = _AutoScrollIntervalMs };
            _autoScroll.Tick += step;
        }

        _autoScroll.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _autoScroll?.Dispose();
        _autoScroll = null;
    }
}
