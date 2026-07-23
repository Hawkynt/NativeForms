using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// An owner-drawn, virtualized Outlook-style scheduling surface painted in the native theme — the
/// scheduling counterpart of <see cref="MonthCalendar"/>, which is a date <em>picker</em>. It shows
/// bound <see cref="Appointment"/>s in a <see cref="CalendarViewMode.Day"/>, <see cref="CalendarViewMode.WorkWeek"/>,
/// <see cref="CalendarViewMode.Week"/> or <see cref="CalendarViewMode.Month"/> view: the first three
/// paint a vertical time grid (hour rows on a configurable <see cref="TimeScale"/>, the
/// <see cref="WorkDayStart"/>–<see cref="WorkDayEnd"/> band shaded, a "now" line, and overlapping
/// appointments laid side by side the way Outlook packs a column); the month view paints a six-week
/// day grid with a chip per appointment and a "+n more" overflow marker.
/// </summary>
/// <remarks>
/// <para>
/// The control does not own the caller's storage. Appointments are bound reflection-free through
/// <see cref="SetAppointments{T}(IEnumerable{T}, Func{T, Appointment})"/> — the same selector idiom
/// the <see cref="DataGridView"/> uses — which projects the source into one flat, start-sorted
/// snapshot. Only the appointments intersecting the visible days are ever laid out: a binary search
/// over that snapshot (bounded by the widest appointment) pulls each visible day's items, so a set of
/// a hundred thousand appointments costs the same per frame as a set of ten.
/// </para>
/// <para>
/// The overlap packing and the pixel geometry are cached and rebuilt only when the data, the shown
/// period, the view mode or the size changes — never on a plain repaint — so the "now" line ticking,
/// a hover, a focus change or a vertical scroll all repaint allocation-free.
/// </para>
/// </remarks>
public class CalendarView : OwnerDrawnControl
{
    private const int _GutterWidth = 52;
    private const int _ChipPadding = 3;
    private const int _AllDayMaxRows = 3;
    private const int _MonthMaxChips = 4;
    private const int _WheelMinutes = 60;
    private const int _MoveThreshold = 4;
    private const int _EdgeGrab = 5;

    /// <summary>One painted appointment: an index into the start-sorted snapshot, the rectangle it
    /// occupies and its pre-formatted chip caption. Timed boxes carry a content-space y (translated by
    /// the scroll offset when painted); fixed boxes (the all-day band, month chips) carry an absolute
    /// client rectangle. The caption is built here, on rebuild, never on the paint path.</summary>
    private struct ApptBox
    {
        public int Index;
        public Rectangle Rect;
        public bool Timed;
        public string Text;

        /// <summary>The day column this box belongs to — the base date its clamped top/bottom edges
        /// are measured from, so a resize on a multi-day box anchors to the right day.</summary>
        public DateTime Day;

        /// <summary>Whether the top edge is a clamped continuation from an earlier day (the real start
        /// is out of view), not the appointment's actual start — so it offers no start-resize.</summary>
        public bool ClipStart;

        /// <summary>Whether the bottom edge is a clamped continuation into a later day (the real end is
        /// out of view), not the appointment's actual end — so it offers no end-resize.</summary>
        public bool ClipEnd;
    }

    /// <summary>What an armed appointment drag is doing: sliding the whole body to a new time, or
    /// pulling one edge to change the start or the end (Outlook-style resize, timed views only).</summary>
    private enum DragKind
    {
        Move,
        ResizeStart,
        ResizeEnd,
    }

    /// <summary>The invariant hour captions "12 AM".."11 PM", materialized once so the time gutter
    /// paints allocation-free.</summary>
    private static string[]? _hourLabels;

    /// <summary>The day-of-month strings "1".."31", materialized once so month painting stays
    /// allocation-free.</summary>
    private static string[]? _dayNumbers;

    // The start-sorted snapshot of the bound appointments and its bounding duration.
    private Appointment[] _appointments = [];
    private int _count;
    private long _maxDurationTicks;

    // The reused layout buffers, rebuilt off the paint path.
    private readonly List<ApptBox> _layout = [];
    private int[] _monthOverflow = [];
    private string?[] _monthOverflowText = [];
    private string[] _dayHeaders = [];
    private int _dayHeaderCount;
    private bool _layoutDirty = true;
    private Size _layoutSize;
    private int _bandRows;

    // Packing scratch, grown on demand and reused across day columns.
    private readonly List<int> _dayScratch = [];
    private long[] _colEnds = [];
    private int[] _colIndex = [];
    private int[] _colCount = [];
    private int[] _bandRowEnd = [];
    private int[] _monthTimed = [];

    private CalendarViewMode _viewMode = CalendarViewMode.Week;
    private DateTime _selectedDate = DateTime.Today;
    private int _timeScale = 30;
    private TimeSpan _workDayStart = new(8, 0, 0);
    private TimeSpan _workDayEnd = new(17, 0, 0);
    private DayOfWeek _firstDayOfWeek = DayOfWeek.Monday;
    private DateTime? _now;

    private int _scrollY;
    private bool _scrollInitialized;
    private int _selectedIndex = -1;
    private long _lastClickTime;
    private int _lastClickIndex = -1;

    // Empty-time drag → time-range selection.
    private bool _rangeDragging;
    private DateTime _rangeAnchor;
    private DateTime _rangeStart;
    private DateTime _rangeEnd;

    // Vertical scrollbar thumb drag.
    private bool _scrollDragging;
    private int _scrollDragOffset;

    // Appointment reschedule drag (move the body / resize an edge). The control proposes the change
    // through the AppointmentMoving/AppointmentMoved events and never mutates the snapshot itself. All
    // preview geometry is recomputed from these value-type fields on the paint path, so a live drag
    // preview allocates nothing — only the drop allocates the one event-args object.
    private DragKind _dragKind;
    private bool _movePrimed;
    private bool _moving;
    private int _moveIndex = -1;
    private bool _moveFixed;
    private Point _movePress;
    private int _moveGrabPixels;
    private int _moveGrabDays;
    private int _moveDurationMinutes;
    private string? _moveSubject;
    private Color _moveAccent;
    private DateTime _previewStart;
    private DateTime _previewEnd;

    /// <summary>Creates a week-view scheduler showing the current week.</summary>
    public CalendarView() { }

    /// <summary>Which scheduling surface is shown. Changing it re-lays-out and repaints.</summary>
    public CalendarViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value)
                return;

            _viewMode = value;
            _scrollInitialized = false;
            this.InvalidateLayout();
        }
    }

    /// <summary>The date the view is centred on: the shown day, the day whose week is shown, or the
    /// day whose month is shown. Assigning navigates there.</summary>
    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            var date = value.Date;
            if (_selectedDate == date)
                return;

            _selectedDate = date;
            this.InvalidateLayout();
        }
    }

    /// <summary>The size of one time-grid slot in minutes (5–120). Hour lines are always drawn every
    /// 60 minutes; the slot governs the finer grid and the resolution a drag snaps to. Defaults to 30.</summary>
    public int TimeScale
    {
        get => _timeScale;
        set
        {
            var scale = Math.Clamp(value, 5, 120);
            if (_timeScale == scale)
                return;

            _timeScale = scale;
            this.InvalidateLayout();
        }
    }

    /// <summary>When the shaded work day begins. Defaults to 08:00.</summary>
    public TimeSpan WorkDayStart
    {
        get => _workDayStart;
        set
        {
            _workDayStart = value;
            this.Invalidate();
        }
    }

    /// <summary>When the shaded work day ends. Defaults to 17:00.</summary>
    public TimeSpan WorkDayEnd
    {
        get => _workDayEnd;
        set
        {
            _workDayEnd = value;
            this.Invalidate();
        }
    }

    /// <summary>The day of week in the leftmost column of the week and month views. Defaults to Monday.</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => _firstDayOfWeek;
        set
        {
            if (_firstDayOfWeek == value)
                return;

            _firstDayOfWeek = value;
            this.InvalidateLayout();
        }
    }

    /// <summary>The instant the "now" line and the today highlight read. Defaults to
    /// <see cref="DateTime.Now"/>; settable, like <see cref="MonthCalendar.TodayDate"/>, so long-running
    /// views and tests stay deterministic.</summary>
    public DateTime Now
    {
        get => _now ?? DateTime.Now;
        set
        {
            _now = value;
            this.Invalidate();
        }
    }

    /// <summary>The colour of the "now" line. Defaults to the shared alert red — no desktop publishes a
    /// themed "now" colour to query — so an application can match its own palette.</summary>
    public Color NowLineColor
    {
        get => field;
        set
        {
            field = value;
            this.Invalidate();
        }
    } = GlyphRenderer.Warning;

    /// <summary>The number of appointments currently bound.</summary>
    public int AppointmentCount => _count;

    /// <summary>The selected appointment, or <see langword="null"/>.</summary>
    public Appointment? SelectedAppointment
        => _selectedIndex >= 0 && _selectedIndex < _count ? _appointments[_selectedIndex] : null;

    /// <summary>The index of the selected appointment into the start-sorted snapshot, or -1.</summary>
    public int SelectedAppointmentIndex => _selectedIndex;

    /// <summary>Raised when <see cref="SelectedAppointment"/> changes, by click or navigation.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the user asks to open an appointment for edit — a double-click or Enter on a
    /// selected appointment. The control hosts no editor; it only reports the model.</summary>
    public event EventHandler<AppointmentEventArgs>? AppointmentActivate;

    /// <summary>Raised when the user click-drags across empty time (or empty month days), carrying the
    /// selected span as a <see cref="DateRangeEventArgs"/> — the "new appointment here" hook.</summary>
    public event EventHandler<DateRangeEventArgs>? TimeRangeSelected;

    /// <summary>Raised while a drag proposes moving or resizing a <see cref="Appointment.Movable"/>
    /// appointment, before it is applied — <em>cancelable</em>. Carries the appointment, its original
    /// bounds and the snapped proposed bounds. The control mutates nothing: a handler validates the
    /// proposal, updates its own model and re-binds through <see cref="SetAppointments{T}"/>. Setting
    /// <see cref="AppointmentMoveEventArgs.Cancel"/> vetoes the move and the snapshot stays put.</summary>
    public event EventHandler<AppointmentMoveEventArgs>? AppointmentMoving;

    /// <summary>Raised after <see cref="AppointmentMoving"/> was not cancelled — the reschedule stands.
    /// The application applies the proposed <see cref="AppointmentMoveEventArgs.Start"/>/<see cref="AppointmentMoveEventArgs.End"/>
    /// to its model item and re-binds; the control does not move the snapshot itself.</summary>
    public event EventHandler<AppointmentMoveEventArgs>? AppointmentMoved;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>Enter opens the selected appointment, so it stays out of the form's AcceptButton routing.</summary>
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter;

    /// <summary>Replaces the bound appointments by projecting each source item through
    /// <paramref name="selector"/> — the reflection-free binding the grid uses. Takes a start-sorted
    /// snapshot; mutating the source afterwards has no effect until the next call.</summary>
    public void SetAppointments<T>(IEnumerable<T> items, Func<T, Appointment> selector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(selector);

        var buffer = _appointments;
        var index = 0;
        foreach (var item in items)
        {
            if (index >= buffer.Length)
                Array.Resize(ref buffer, Math.Max(4, buffer.Length * 2));

            buffer[index++] = selector(item);
        }

        _appointments = buffer;
        this.FinishBinding(index);
    }

    /// <summary>Replaces the bound appointments from a ready sequence of <see cref="Appointment"/>s.</summary>
    public void SetAppointments(IEnumerable<Appointment> appointments)
        => this.SetAppointments(appointments, static a => a);

    /// <summary>Sorts the fresh snapshot by start, measures its widest span (the bounded-lookup window)
    /// and drops the selection and layout.</summary>
    private void FinishBinding(int count)
    {
        _count = count;
        Array.Sort(_appointments, 0, count, AppointmentStartComparer.Instance);

        var max = 0L;
        for (var i = 0; i < count; ++i)
        {
            var span = _appointments[i].End.Ticks - _appointments[i].Start.Ticks;
            if (span > max)
                max = span;
        }

        _maxDurationTicks = max;
        _selectedIndex = -1;
        this.InvalidateLayout();
    }

    /// <summary>Steps the view forward one of its own units — a day, a week, a month.</summary>
    public void Next() => this.SelectedDate = this.Step(+1);

    /// <summary>Steps the view back one of its own units.</summary>
    public void Previous() => this.SelectedDate = this.Step(-1);

    /// <summary>Jumps to today.</summary>
    public void GoToToday() => this.SelectedDate = this.Now.Date;

    private DateTime Step(int direction) => _viewMode switch
    {
        CalendarViewMode.Day => _selectedDate.AddDays(direction),
        CalendarViewMode.Month => _selectedDate.AddMonths(direction),
        _ => _selectedDate.AddDays(7 * direction),
    };

    /// <summary>The client rectangle of the appointment at <paramref name="index"/> in the current
    /// view, already translated for scroll — the hook the demo and the autopilot aim clicks at.
    /// Returns <see langword="false"/> when the appointment is not currently laid out.</summary>
    public bool TryGetAppointmentBounds(int index, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        this.EnsureLayout();
        var body = this.BodyBounds;
        for (var i = 0; i < _layout.Count; ++i)
        {
            var box = _layout[i];
            if (box.Index != index)
                continue;

            bounds = box.Timed ? new(box.Rect.X, box.Rect.Y + body.Y - _scrollY, box.Rect.Width, box.Rect.Height) : box.Rect;
            return true;
        }

        return false;
    }

    /// <summary>The continuation-clamp flags of the first laid-out timed box for an appointment index —
    /// <c>ClipStart</c>/<c>ClipEnd</c> mark a box whose top/bottom edge is a multi-day continuation
    /// rather than the real start/end. For tests of the out-of-view multi-day handling.</summary>
    internal (bool ClipStart, bool ClipEnd) BoxClipFlags(int index)
    {
        this.EnsureLayout();
        for (var i = 0; i < _layout.Count; ++i)
            if (_layout[i].Index == index && _layout[i].Timed)
                return (_layout[i].ClipStart, _layout[i].ClipEnd);

        return (false, false);
    }

    /// <summary>The client x at the horizontal centre of a visible day's column, or -1 when that day
    /// is off view. For tests of cross-day edge dragging.</summary>
    internal int DayColumnCenterX(DateTime day)
    {
        var col = (int)(day.Date - this.FirstVisibleDate.Date).TotalDays;
        if (col < 0 || col >= this.VisibleDayCount)
            return -1;

        var w = this.DayColumnWidth;
        return _GutterWidth + (col * w) + (w / 2);
    }

    // --- Geometry ----------------------------------------------------------------------------------

    private bool IsMonth => _viewMode == CalendarViewMode.Month;

    /// <summary>The number of day columns the current view spans.</summary>
    public int VisibleDayCount => _viewMode switch
    {
        CalendarViewMode.Day => 1,
        CalendarViewMode.WorkWeek => 5,
        _ => 7,
    };

    /// <summary>The first date the current view shows.</summary>
    public DateTime FirstVisibleDate => _viewMode switch
    {
        CalendarViewMode.Day => _selectedDate,
        CalendarViewMode.WorkWeek => StartOfWeek(_selectedDate, DayOfWeek.Monday),
        CalendarViewMode.Week => StartOfWeek(_selectedDate, _firstDayOfWeek),
        _ => CalendarCore.FirstGridDate(new(_selectedDate.Year, _selectedDate.Month, 1), _firstDayOfWeek),
    };

    private static DateTime StartOfWeek(DateTime date, DayOfWeek first)
        => date.Date.AddDays(-(((int)date.DayOfWeek - (int)first + 7) % 7));

    private int HeaderHeight => this.Theme.RowHeight;

    private int SlotHeight => Math.Max(8, this.Theme.RowHeight);

    private int ChipHeight => Math.Max(14, this.Theme.RowHeight - 4);

    /// <summary>The content-space y (0 = midnight) of a minute-of-day.</summary>
    private int YForMinutes(int minutes) => minutes * this.SlotHeight / _timeScale;

    /// <summary>The minute-of-day a content-space y falls on, clamped to the day.</summary>
    private int MinutesForY(int y) => Math.Clamp(y * _timeScale / this.SlotHeight, 0, 1440);

    /// <summary>The full-day content height of the time grid.</summary>
    private int ContentHeight => this.YForMinutes(1440);

    /// <summary>The pixel height of the all-day band under the day headers (0 when nothing is all-day).</summary>
    private int BandHeight => _bandRows <= 0 ? 0 : Math.Min(_AllDayMaxRows, _bandRows) * (this.ChipHeight + 2) + 4;

    private int BodyTop => this.HeaderHeight + this.BandHeight;

    /// <summary>The body height, independent of the scrollbar — a vertical scrollbar steals width, not
    /// height, so the scroll decision can read this without recursing through <see cref="BodyBounds"/>.</summary>
    private int BodyHeight => Math.Max(0, this.Height - this.BodyTop);

    private bool NeedsScrollBar => !this.IsMonth && this.ContentHeight > this.BodyHeight;

    private int ScrollBarWidth => this.NeedsScrollBar ? this.Theme.ScrollBarSize : 0;

    /// <summary>The scrollable time-grid body, right of the hour gutter and below the header + band.</summary>
    private Rectangle BodyBounds
        => new(_GutterWidth, this.BodyTop, Math.Max(0, this.Width - _GutterWidth - this.ScrollBarWidth), this.BodyHeight);

    private int DayColumnWidth
    {
        get
        {
            var count = this.VisibleDayCount;
            return count <= 0 ? 0 : this.BodyBounds.Width / count;
        }
    }

    private int MaxScroll => Math.Max(0, this.ContentHeight - this.BodyHeight);

    // --- Layout ------------------------------------------------------------------------------------

    /// <summary>Marks the cached geometry stale and repaints; the rebuild happens lazily off the paint
    /// path in <see cref="EnsureLayout"/>.</summary>
    private void InvalidateLayout()
    {
        _layoutDirty = true;
        this.Invalidate();
    }

    /// <summary>Rebuilds the appointment layout when it is stale or the surface resized; a no-op — and
    /// allocation-free — on a steady-state frame.</summary>
    private void EnsureLayout()
    {
        if (!_layoutDirty && _layoutSize == this.Size)
            return;

        _layoutDirty = false;
        _layoutSize = this.Size;
        _layout.Clear();

        if (this.IsMonth)
            this.BuildMonthLayout();
        else
            this.BuildTimeGridLayout();

        this.ClampScroll();
    }

    /// <summary>The first snapshot index whose start is at or after <paramref name="ticks"/>.</summary>
    private int LowerBound(long ticks)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_appointments[mid].Start.Ticks < ticks)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    /// <summary>Gathers into <see cref="_dayScratch"/> the snapshot indices of the appointments that
    /// intersect the day starting at <paramref name="day"/> and match the all-day filter, using the
    /// bounded window so the scan never walks the whole snapshot.</summary>
    private void GatherDay(DateTime day, bool allDay)
    {
        _dayScratch.Clear();
        if (_count == 0)
            return;

        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        var from = this.LowerBound(dayStart.Ticks - _maxDurationTicks);
        for (var i = from; i < _count; ++i)
        {
            var appt = _appointments[i];
            if (appt.Start >= dayEnd)
                break;

            if (appt.AllDay != allDay)
                continue;

            var overlaps = allDay
                ? appt.Start.Date <= dayStart && appt.End.Date >= dayStart
                : appt.End > dayStart && appt.Start < dayEnd;
            if (overlaps)
                _dayScratch.Add(i);
        }
    }

    /// <summary>Lays out the Day/Week/WorkWeek time grid: timed appointments column-packed per day and
    /// all-day appointments stacked into the band.</summary>
    private void BuildTimeGridLayout()
    {
        var dayCount = this.VisibleDayCount;
        var dayWidth = this.DayColumnWidth;
        var first = this.FirstVisibleDate;
        var left = _GutterWidth;

        // Cache the day-header captions here, so painting never formats them.
        if (_dayHeaders.Length < dayCount)
            _dayHeaders = new string[dayCount];

        var names = Strings.AbbreviatedDayNames;
        for (var d = 0; d < dayCount; ++d)
        {
            var day = first.AddDays(d);
            _dayHeaders[d] = $"{names[(int)day.DayOfWeek]} {day.Day}";
        }

        _dayHeaderCount = dayCount;

        // The all-day band first, so its row count is known before the body geometry is used.
        this.BuildAllDayBand(first, dayCount, dayWidth, left);

        for (var d = 0; d < dayCount; ++d)
        {
            var day = first.AddDays(d);
            this.GatherDay(day, allDay: false);
            this.PackDayColumn(day, d, dayWidth, left);
        }
    }

    /// <summary>The pre-formatted caption for an appointment chip: the subject, prefixed with its start
    /// time for a timed appointment. Built on rebuild so the paint path never formats a time.</summary>
    private static string ChipText(Appointment appt)
        => appt.AllDay ? appt.Subject : $"{appt.Start:HH:mm} {appt.Subject}";

    /// <summary>Column-packs one day's timed appointments (Outlook side-by-side) into content-space
    /// boxes.</summary>
    private void PackDayColumn(DateTime day, int columnIndex, int dayWidth, int left)
    {
        var n = _dayScratch.Count;
        if (n == 0)
            return;

        this.EnsurePackCapacity(n);
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);

        // Sweep clusters of mutually overlapping appointments; within a cluster each appointment takes
        // the first free column, and every member shares the cluster's column count as its width.
        var i = 0;
        while (i < n)
        {
            var clusterEnd = _appointments[_dayScratch[i]].End;
            var cols = 0;
            var groupStart = i;
            while (i < n)
            {
                var appt = _appointments[_dayScratch[i]];
                if (appt.Start >= clusterEnd)
                    break;

                var placed = -1;
                for (var c = 0; c < cols; ++c)
                    if (appt.Start.Ticks >= _colEnds[c])
                    {
                        placed = c;
                        break;
                    }

                if (placed < 0)
                {
                    placed = cols++;
                }

                _colEnds[placed] = appt.End.Ticks;
                _colIndex[i] = placed;
                if (appt.End > clusterEnd)
                    clusterEnd = appt.End;

                ++i;
            }

            for (var g = groupStart; g < i; ++g)
                _colCount[g] = cols;
        }

        var columnLeft = left + (columnIndex * dayWidth);
        for (var k = 0; k < n; ++k)
        {
            var appt = _appointments[_dayScratch[k]];
            var clipStart = appt.Start < dayStart;
            var clipEnd = appt.End > dayEnd;
            var startMinutes = clipStart ? 0 : (int)(appt.Start - dayStart).TotalMinutes;
            var endMinutes = clipEnd ? 1440 : (int)(appt.End - dayStart).TotalMinutes;
            if (endMinutes <= startMinutes)
                endMinutes = startMinutes + _timeScale;

            var top = this.YForMinutes(startMinutes);
            var bottom = this.YForMinutes(endMinutes);
            var count = Math.Max(1, _colCount[k]);
            var slotWidth = Math.Max(1, (dayWidth - _ChipPadding) / count);
            var x = columnLeft + (_colIndex[k] * slotWidth) + 1;
            _layout.Add(new ApptBox
            {
                Index = _dayScratch[k],
                Timed = true,
                Text = ChipText(appt),
                Rect = new(x, top + 1, Math.Max(1, slotWidth - 2), Math.Max(this.ChipHeight, bottom - top - 2)),
                Day = dayStart,
                ClipStart = clipStart,
                ClipEnd = clipEnd,
            });
        }
    }

    /// <summary>Stacks the all-day appointments across their day columns into the band, one bar per
    /// appointment on the first free row.</summary>
    private void BuildAllDayBand(DateTime first, int dayCount, int dayWidth, int left)
    {
        _bandRows = 0;
        if (_count == 0)
            return;

        // Collect the distinct all-day appointments overlapping the visible span, in start order.
        _dayScratch.Clear();
        var spanEnd = first.AddDays(dayCount);
        var from = this.LowerBound(first.Ticks - _maxDurationTicks);
        for (var i = from; i < _count; ++i)
        {
            var appt = _appointments[i];
            if (appt.Start >= spanEnd)
                break;

            if (appt.AllDay && appt.End.Date >= first.Date && appt.Start.Date < spanEnd.Date)
                _dayScratch.Add(i);
        }

        var n = _dayScratch.Count;
        if (n == 0)
            return;

        this.EnsureBandCapacity(dayCount);
        for (var r = 0; r < dayCount; ++r)
            _bandRowEnd[r] = -1;

        var chipH = this.ChipHeight;
        for (var k = 0; k < n; ++k)
        {
            var appt = _appointments[_dayScratch[k]];
            var startCol = Math.Max(0, (int)(appt.Start.Date - first.Date).TotalDays);
            var endCol = Math.Min(dayCount - 1, (int)(appt.End.Date - first.Date).TotalDays);
            if (endCol < startCol)
                endCol = startCol;

            var row = 0;
            while (row < dayCount && _bandRowEnd[row] >= startCol)
                ++row;

            if (row >= _AllDayMaxRows)
                continue;

            _bandRowEnd[row] = endCol;
            if (row + 1 > _bandRows)
                _bandRows = row + 1;

            var x = left + (startCol * dayWidth) + 1;
            var width = ((endCol - startCol + 1) * dayWidth) - 2;
            var y = this.HeaderHeight + 2 + (row * (chipH + 2));
            _layout.Add(new ApptBox
            {
                Index = _dayScratch[k],
                Timed = false,
                Text = ChipText(appt),
                Rect = new(x, y, Math.Max(1, width), chipH),
            });
        }
    }

    /// <summary>Lays out the month grid: a chip per appointment per day cell, up to
    /// <see cref="_MonthMaxChips"/>, with the overflow counted for a "+n more" marker.</summary>
    private void BuildMonthLayout()
    {
        _bandRows = 0;
        var first = this.FirstVisibleDate;
        var cellWidth = this.Width / 7;
        var top = this.HeaderHeight;
        var cellHeight = (this.Height - top) / 6;
        if (cellWidth < 1 || cellHeight < 1)
            return;

        if (_monthOverflow.Length < 42)
        {
            _monthOverflow = new int[42];
            _monthOverflowText = new string?[42];
        }

        var chipH = this.ChipHeight;
        for (var cell = 0; cell < 42; ++cell)
        {
            _monthOverflow[cell] = 0;
            var day = first.AddDays(cell);
            this.GatherDay(day, allDay: false);
            var timedCount = _dayScratch.Count;
            if (_monthTimed.Length < timedCount)
                _monthTimed = new int[Math.Max(timedCount, 8)];

            for (var t = 0; t < timedCount; ++t)
                _monthTimed[t] = _dayScratch[t];

            this.GatherDay(day, allDay: true);

            var col = cell % 7;
            var row = cell / 7;
            var cellX = col * cellWidth;
            var cellY = top + (row * cellHeight);
            var chipTop = cellY + this.Theme.RowHeight; // below the day-number line
            var shown = 0;

            void Place(int apptIndex)
            {
                if (chipTop + chipH > cellY + cellHeight - 2 || shown >= _MonthMaxChips)
                {
                    ++_monthOverflow[cell];
                    return;
                }

                _layout.Add(new ApptBox
                {
                    Index = apptIndex,
                    Timed = false,
                    Text = ChipText(_appointments[apptIndex]),
                    Rect = new(cellX + 2, chipTop, Math.Max(1, cellWidth - 4), chipH),
                });
                chipTop += chipH + 2;
                ++shown;
            }

            for (var a = 0; a < _dayScratch.Count; ++a)
                Place(_dayScratch[a]);

            for (var t = 0; t < timedCount; ++t)
                Place(_monthTimed[t]);

            _monthOverflowText[cell] = _monthOverflow[cell] > 0 ? $"+{_monthOverflow[cell]} more" : null;
        }
    }

    private void EnsurePackCapacity(int n)
    {
        if (_colEnds.Length < n)
        {
            _colEnds = new long[n];
            _colIndex = new int[n];
            _colCount = new int[n];
        }
    }

    private void EnsureBandCapacity(int n)
    {
        if (_bandRowEnd.Length < n)
            _bandRowEnd = new int[n];
    }

    private void ClampScroll()
    {
        if (this.IsMonth)
        {
            _scrollY = 0;
            return;
        }

        if (!_scrollInitialized && this.MaxScroll > 0)
        {
            _scrollInitialized = true;
            _scrollY = Math.Min(this.MaxScroll, this.YForMinutes((int)_workDayStart.TotalMinutes));
        }

        _scrollY = Math.Clamp(_scrollY, 0, this.MaxScroll);
    }

    // --- Painting ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        this.EnsureLayout();
        var g = e.Graphics;
        var theme = this.Theme;
        g.FillRectangle(theme.FieldBackground, new(0, 0, this.Width, this.Height));

        if (this.IsMonth)
            this.PaintMonth(g, theme);
        else
            this.PaintTimeGrid(g, theme);

        g.DrawRectangle(theme.Border, new(0, 0, this.Width - 1, this.Height - 1));
    }

    private void PaintTimeGrid(IGraphics g, ITheme theme)
    {
        var dayCount = this.VisibleDayCount;
        var dayWidth = this.DayColumnWidth;
        var first = this.FirstVisibleDate;
        var body = this.BodyBounds;
        var today = this.Now.Date;

        // Day headers, from the captions cached during layout.
        for (var d = 0; d < dayCount; ++d)
        {
            var day = first.AddDays(d);
            var x = _GutterWidth + (d * dayWidth);
            var header = new Rectangle(x, 0, dayWidth, this.HeaderHeight);
            if (day == today)
                g.FillRectangle(theme.SelectionBackground, header);

            var label = d < _dayHeaderCount ? _dayHeaders[d] : string.Empty;
            g.DrawText(label, theme.DefaultFont, day == today ? theme.SelectionText : theme.HeaderText, header, ContentAlignment.MiddleCenter);
            g.DrawLine(theme.Border, x, 0, x, this.Height);
        }

        // The all-day band.
        var bandHeight = this.BandHeight;
        if (bandHeight > 0)
        {
            var band = new Rectangle(0, this.HeaderHeight, this.Width, bandHeight);
            g.FillRectangle(theme.HeaderBackground, band);
            g.DrawLine(theme.Border, 0, band.Bottom, this.Width, band.Bottom);
            for (var i = 0; i < _layout.Count; ++i)
                if (!_layout[i].Timed)
                    this.PaintChip(g, theme, _layout[i], _layout[i].Rect, compact: true);
        }

        // The ghost of an all-day appointment being dragged across day columns (day granularity).
        if (_moving && _moveFixed)
        {
            var col = (int)(_previewStart.Date - first.Date).TotalDays;
            if (col >= 0 && col < dayCount)
            {
                var spanDays = Math.Max(1, (int)(_previewEnd.Date - _previewStart.Date).TotalDays);
                var endCol = Math.Min(dayCount - 1, col + spanDays - 1);
                this.PaintGhost(g, theme, new(_GutterWidth + (col * dayWidth) + 1, this.HeaderHeight + 2, Math.Max(1, ((endCol - col + 1) * dayWidth) - 2), this.ChipHeight));
            }
        }

        // The scrollable body: hour lines, work-hour shading, appointments and the now line. The clip
        // spans the hour gutter too (so the hour labels show) but stops short of the scrollbar column.
        g.PushClip(new Rectangle(0, body.Y, body.Right, body.Height));
        var originY = body.Y - _scrollY;

        // Off-hours vs work-hours shading, per day column so weekends could differ later.
        var workTop = originY + this.YForMinutes((int)_workDayStart.TotalMinutes);
        var workBottom = originY + this.YForMinutes((int)_workDayEnd.TotalMinutes);
        var offHours = Blend(theme.FieldBackground, theme.ControlBackground);
        g.FillRectangle(offHours, new(body.X, body.Y, body.Width, body.Height));
        if (workBottom > workTop)
            g.FillRectangle(theme.FieldBackground, new(body.X, Math.Max(body.Y, workTop), body.Width, Math.Min(body.Bottom, workBottom) - Math.Max(body.Y, workTop)));

        for (var minutes = 0; minutes <= 1440; minutes += _timeScale)
        {
            var y = originY + this.YForMinutes(minutes);
            if (y < body.Y - 1 || y > body.Bottom)
                continue;

            var onHour = minutes % 60 == 0;
            g.DrawLine(onHour ? theme.Border : theme.GridLine, body.X, y, this.Width, y);
            if (onHour && minutes < 1440)
            {
                var labels = _hourLabels ??= CreateHourLabels();
                g.DrawText(labels[minutes / 60], theme.DefaultFont, theme.HeaderText, new(0, y, _GutterWidth - 4, this.SlotHeight), ContentAlignment.TopRight);
            }
        }

        for (var i = 0; i < _layout.Count; ++i)
        {
            var box = _layout[i];
            if (!box.Timed)
                continue;

            var rect = new Rectangle(box.Rect.X, box.Rect.Y + originY, box.Rect.Width, box.Rect.Height);
            if (rect.Bottom < body.Y || rect.Y > body.Bottom)
                continue;

            this.PaintChip(g, theme, box, rect, compact: false);
        }

        // The drag range highlight.
        if (_rangeDragging && _rangeEnd > _rangeStart)
        {
            var col = (int)(_rangeStart.Date - first.Date).TotalDays;
            if (col >= 0 && col < dayCount)
            {
                var ry = originY + this.YForMinutes((int)_rangeStart.TimeOfDay.TotalMinutes);
                var rb = originY + this.YForMinutes((int)(_rangeEnd - _rangeStart.Date).TotalMinutes);
                g.FillRectangle(Blend(theme.SelectionBackground, theme.FieldBackground), new(_GutterWidth + (col * dayWidth), ry, dayWidth, Math.Max(2, rb - ry)));
            }
        }

        // The live move/resize ghost — where a timed appointment will land, snapped, translucent, the
        // original left in place until the drop. Recomputed from the value-type preview fields, so this
        // costs no allocation even mid-drag.
        if (_moving && !_moveFixed)
        {
            var col = (int)(_previewStart.Date - first.Date).TotalDays;
            if (col >= 0 && col < dayCount)
            {
                var startMin = (int)(_previewStart - _previewStart.Date).TotalMinutes;
                var endMin = _previewEnd.Date > _previewStart.Date ? 1440 : (int)(_previewEnd - _previewStart.Date).TotalMinutes;
                var top = originY + this.YForMinutes(startMin);
                var bottom = originY + this.YForMinutes(endMin);
                this.PaintGhost(g, theme, new(_GutterWidth + (col * dayWidth) + 1, top + 1, Math.Max(1, dayWidth - 2), Math.Max(this.ChipHeight, bottom - top - 2)));
            }
        }

        // The now line, if today is on screen.
        for (var d = 0; d < dayCount; ++d)
        {
            var day = first.AddDays(d);
            if (day != today)
                continue;

            var y = originY + this.YForMinutes((int)this.Now.TimeOfDay.TotalMinutes);
            if (y >= body.Y && y <= body.Bottom)
            {
                var x = _GutterWidth + (d * dayWidth);
                g.FillEllipse(this.NowLineColor, new(x - 2, y - 3, 6, 6));
                g.DrawLine(this.NowLineColor, x, y, x + dayWidth, y, 2);
            }
        }

        g.PopClip();

        g.DrawLine(theme.Border, _GutterWidth, 0, _GutterWidth, this.Height);
        this.PaintScrollBar(g, theme);
    }

    private void PaintMonth(IGraphics g, ITheme theme)
    {
        var first = this.FirstVisibleDate;
        var cellWidth = this.Width / 7;
        var top = this.HeaderHeight;
        var cellHeight = (this.Height - top) / 6;
        if (cellWidth < 1 || cellHeight < 1)
            return;

        var names = Strings.AbbreviatedDayNames;
        var today = this.Now.Date;
        var month = _selectedDate.Month;

        for (var c = 0; c < 7; ++c)
        {
            var day = first.AddDays(c);
            g.DrawText(names[(int)day.DayOfWeek], theme.DefaultFont, theme.HeaderText, new(c * cellWidth, 0, cellWidth, this.HeaderHeight), ContentAlignment.MiddleCenter);
        }

        for (var cell = 0; cell < 42; ++cell)
        {
            var day = first.AddDays(cell);
            var col = cell % 7;
            var row = cell / 7;
            var rect = new Rectangle(col * cellWidth, top + (row * cellHeight), cellWidth, cellHeight);
            var inMonth = day.Month == month;
            if (!inMonth)
                g.FillRectangle(Blend(theme.FieldBackground, theme.ControlBackground), rect);

            if (day == today)
                g.FillRectangle(theme.SelectionBackground, new(rect.X, rect.Y, rect.Width, this.Theme.RowHeight));

            var numbers = _dayNumbers ??= CreateDayNumbers();
            g.DrawText(
                numbers[day.Day - 1],
                theme.DefaultFont,
                day == today ? theme.SelectionText : inMonth ? theme.ControlText : theme.DisabledText,
                new(rect.X + 4, rect.Y, rect.Width - 8, this.Theme.RowHeight),
                ContentAlignment.MiddleLeft);

            g.DrawLine(theme.Border, rect.X, rect.Bottom, rect.Right, rect.Bottom);
            g.DrawLine(theme.Border, rect.Right, rect.Y, rect.Right, rect.Bottom);

            if (_monthOverflowText.Length > cell && _monthOverflowText[cell] is { } more)
                g.DrawText(
                    more,
                    theme.DefaultFont,
                    theme.HeaderText,
                    new(rect.X + 4, rect.Bottom - this.ChipHeight, rect.Width - 8, this.ChipHeight),
                    ContentAlignment.MiddleLeft);
        }

        for (var i = 0; i < _layout.Count; ++i)
            this.PaintChip(g, theme, _layout[i], _layout[i].Rect, compact: true);

        // The ghost of an appointment being dragged to another day cell (day granularity).
        if (_moving && _moveFixed)
        {
            var cell = (int)(_previewStart.Date - first.Date).TotalDays;
            if (cell >= 0 && cell < 42)
                this.PaintGhost(g, theme, new(((cell % 7) * cellWidth) + 2, top + ((cell / 7) * cellHeight) + this.Theme.RowHeight, Math.Max(1, cellWidth - 4), this.ChipHeight));
        }
    }

    /// <summary>Paints one appointment chip: a category-coloured face, an accent bar down its left,
    /// its subject and — when tall enough — its start time and location, and a selection outline.</summary>
    private void PaintChip(IGraphics g, ITheme theme, ApptBox box, Rectangle rect, bool compact)
    {
        if (rect.Width <= 1 || rect.Height <= 1)
            return;

        var appt = _appointments[box.Index];
        var accent = appt.Color.IsEmpty ? theme.Accent : appt.Color;
        var face = Blend(accent, theme.FieldBackground);
        var selected = box.Index == _selectedIndex;

        g.FillRectangle(face, rect);
        g.FillRectangle(accent, new(rect.X, rect.Y, 3, rect.Height));
        g.PushClip(rect);

        var textLeft = rect.X + 6;
        var textWidth = rect.Width - 8;
        if (compact || rect.Height < this.ChipHeight * 2)
        {
            g.DrawText(box.Text, theme.DefaultFont, theme.ControlText, new(textLeft, rect.Y, textWidth, rect.Height), ContentAlignment.MiddleLeft);
        }
        else
        {
            var rowH = this.Theme.RowHeight;
            g.DrawText(box.Text, theme.DefaultFont, theme.ControlText, new(textLeft, rect.Y + 2, textWidth, rowH), ContentAlignment.TopLeft);
            if (appt.Location.Length > 0 && rect.Height > rowH * 2)
                g.DrawText(appt.Location, theme.DefaultFont, theme.DisabledText, new(textLeft, rect.Y + rowH + 2, textWidth, rowH), ContentAlignment.TopLeft);
        }

        // A locked appointment carries no move affordance — a small padlock in its top-right corner
        // signals it will not drag.
        if (!appt.Movable)
            PaintLock(g, new(rect.Right - 12, rect.Y + 2, 8, 8), theme.DisabledText);

        // A multi-day box continues off this day: a small chevron at the clamped edge says so, and
        // marks the edge that carries no resize grab.
        if (box.ClipStart && rect.Width > 12)
            Glyphs.PaintTriangle(g, accent, new(rect.X + (rect.Width / 2) - 4, rect.Y + 1, 8, 4), GlyphDirection.Up);

        if (box.ClipEnd && rect.Width > 12)
            Glyphs.PaintTriangle(g, accent, new(rect.X + (rect.Width / 2) - 4, rect.Bottom - 5, 8, 4), GlyphDirection.Down);

        g.PopClip();

        if (selected)
            g.DrawRectangle(theme.Accent, new(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), 2);
        else
            g.DrawRectangle(Blend(accent, theme.Border), new(rect.X, rect.Y, rect.Width - 1, rect.Height - 1));
    }

    /// <summary>A tiny padlock glyph — a shackle arc over a body — drawn from primitives so it costs no
    /// image asset and no per-frame allocation. Marks a non-movable appointment.</summary>
    private static void PaintLock(IGraphics g, Rectangle bounds, Color color)
    {
        if (bounds.Width < 5 || bounds.Height < 5)
            return;

        var shackleWidth = Math.Max(3, bounds.Width - 2);
        var shackle = new Rectangle(bounds.X + ((bounds.Width - shackleWidth) / 2), bounds.Y, shackleWidth, bounds.Height / 2 + 1);
        g.DrawEllipse(color, shackle);
        g.FillRectangle(color, new(bounds.X, bounds.Y + (bounds.Height / 2), bounds.Width, bounds.Height - (bounds.Height / 2)));
    }

    /// <summary>Paints the translucent ghost of a dragged appointment at its snapped landing — a
    /// see-through accent face, the accent bar and outline, and the subject. Uses a real alpha colour
    /// so it reads as a preview over whatever it covers; allocates nothing.</summary>
    private void PaintGhost(IGraphics g, ITheme theme, Rectangle rect)
    {
        if (rect.Width <= 1 || rect.Height <= 1)
            return;

        var ghost = Color.FromArgb(120, _moveAccent);
        g.FillRectangle(ghost, rect);
        g.FillRectangle(_moveAccent, new(rect.X, rect.Y, 3, rect.Height));
        if (_moveSubject is { } subject)
        {
            g.PushClip(rect);
            g.DrawText(subject, theme.DefaultFont, theme.ControlText, new(rect.X + 6, rect.Y, rect.Width - 8, rect.Height), ContentAlignment.MiddleLeft);
            g.PopClip();
        }

        g.DrawRectangle(_moveAccent, new(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), 2);
    }

    private void PaintScrollBar(IGraphics g, ITheme theme)
    {
        if (!this.NeedsScrollBar)
            return;

        var body = this.BodyBounds;
        var bounds = new Rectangle(this.Width - this.Theme.ScrollBarSize, body.Y, this.Theme.ScrollBarSize, body.Height);
        ScrollBarRenderer.Paint(g, theme, bounds, vertical: true, 0, this.MaxScroll, _scrollY, Math.Max(1, body.Height),
            _scrollDragging ? ScrollBarPart.Thumb : ScrollBarPart.None);
    }

    private static string[] CreateHourLabels()
    {
        var labels = new string[24];
        for (var h = 0; h < 24; ++h)
            labels[h] = h switch
            {
                0 => "12 AM",
                12 => "12 PM",
                < 12 => $"{h} AM",
                _ => $"{h - 12} PM",
            };

        return labels;
    }

    private static string[] CreateDayNumbers()
    {
        var numbers = new string[31];
        for (var i = 0; i < numbers.Length; ++i)
            numbers[i] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return numbers;
    }

    /// <summary>Mixes two opaque colours 50:50, channel-wise.</summary>
    private static Color Blend(Color a, Color b)
        => Color.FromArgb(0xFF, (a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);

    // --- Input -------------------------------------------------------------------------------------

    /// <summary>Hit-tests the laid-out appointments, returning the position in <see cref="_layout"/>
    /// under the point or -1 — the drag path needs the box (its rectangle and whether it is timed), not
    /// only the snapshot index.</summary>
    private int HitTestBox(int x, int y)
    {
        var body = this.BodyBounds;
        var point = new Point(x, y);

        // Fixed boxes (band / month chips) first, then timed boxes translated for scroll.
        for (var i = _layout.Count - 1; i >= 0; --i)
        {
            var box = _layout[i];
            if (box.Timed)
                continue;

            if (box.Rect.Contains(point))
                return i;
        }

        if (!body.Contains(point))
            return -1;

        for (var i = _layout.Count - 1; i >= 0; --i)
        {
            var box = _layout[i];
            if (!box.Timed)
                continue;

            var rect = new Rectangle(box.Rect.X, box.Rect.Y + body.Y - _scrollY, box.Rect.Width, box.Rect.Height);
            if (rect.Contains(point))
                return i;
        }

        return -1;
    }

    /// <summary>Hit-tests the laid-out appointments, returning the snapshot index under the point or -1.</summary>
    private int HitTestAppointment(int x, int y)
    {
        var box = this.HitTestBox(x, y);
        return box < 0 ? -1 : _layout[box].Index;
    }

    /// <summary>The instant an empty-body point maps to: the day column plus the time-of-day snapped to
    /// the slot, or <see langword="null"/> off the body.</summary>
    private DateTime? TimeAt(int x, int y)
    {
        var body = this.BodyBounds;
        if (!body.Contains(new Point(x, y)))
            return null;

        var dayWidth = this.DayColumnWidth;
        if (dayWidth <= 0)
            return null;

        var col = Math.Clamp((x - _GutterWidth) / dayWidth, 0, this.VisibleDayCount - 1);
        var minutes = this.MinutesForY(y - body.Y + _scrollY);
        minutes = (minutes / _timeScale) * _timeScale;
        return this.FirstVisibleDate.AddDays(col).AddMinutes(minutes);
    }

    /// <summary>The day cell a month point maps to, or <see langword="null"/>.</summary>
    private DateTime? MonthDayAt(int x, int y)
    {
        var cellWidth = this.Width / 7;
        var top = this.HeaderHeight;
        var cellHeight = (this.Height - top) / 6;
        if (cellWidth < 1 || cellHeight < 1 || y < top)
            return null;

        var col = Math.Clamp(x / cellWidth, 0, 6);
        var row = Math.Clamp((y - top) / cellHeight, 0, 5);
        return this.FirstVisibleDate.AddDays((row * 7) + col);
    }

    /// <summary>The date of the day column an x maps to, in a timed view.</summary>
    private DateTime DayColumnDate(int x)
    {
        var dayWidth = this.DayColumnWidth;
        if (dayWidth <= 0)
            return this.FirstVisibleDate;

        var col = Math.Clamp((x - _GutterWidth) / dayWidth, 0, this.VisibleDayCount - 1);
        return this.FirstVisibleDate.AddDays(col);
    }

    /// <summary>Floors a minute-of-day to the <see cref="TimeScale"/> slot a drag snaps to.</summary>
    private int SnapMinutes(int minutes) => Math.Clamp(minutes / _timeScale * _timeScale, 0, 1440);

    /// <summary>Arms a reschedule on a movable appointment: records the grab and seeds the preview. A
    /// non-movable appointment is refused here, so it never drags and shows no move affordance.</summary>
    /// <summary>Which edge (if any) a y falls on for a timed box, honoring the resizable-height floor
    /// and the continuation clamps: a clamped edge is a continuation, not a real start/end, so it never
    /// resizes — a multi-day box's off-view edge stays put.</summary>
    private static DragKind EdgeKindOf(ApptBox box, int screenTop, int y)
    {
        if (box.Rect.Height < _EdgeGrab * 3)
            return DragKind.Move;

        var screenBottom = screenTop + box.Rect.Height;
        if (!box.ClipStart && y - screenTop <= _EdgeGrab)
            return DragKind.ResizeStart;

        if (!box.ClipEnd && screenBottom - y <= _EdgeGrab)
            return DragKind.ResizeEnd;

        return DragKind.Move;
    }

    /// <summary>The resize edge under a client point, or <see cref="DragKind.Move"/> when the point is
    /// not on a movable timed box's real edge — the basis for the resize cursor.</summary>
    private DragKind EdgeKindAt(int x, int y)
    {
        var hit = this.HitTestBox(x, y);
        if (hit < 0)
            return DragKind.Move;

        var box = _layout[hit];
        if (!box.Timed || !_appointments[box.Index].Movable)
            return DragKind.Move;

        var screenTop = box.Rect.Y + this.BodyBounds.Y - _scrollY;
        return EdgeKindOf(box, screenTop, y);
    }

    private void PrimeMove(ApptBox box, int index, MouseEventArgs e)
    {
        var appt = _appointments[index];
        if (!appt.Movable)
            return;

        _movePrimed = true;
        _moving = false;
        _moveIndex = index;
        _movePress = e.Location;
        _moveFixed = !box.Timed;
        _moveSubject = appt.Subject;
        _moveAccent = appt.Color.IsEmpty ? this.Theme.Accent : appt.Color;
        _moveDurationMinutes = Math.Max(_timeScale, (int)(appt.End - appt.Start).TotalMinutes);

        if (box.Timed)
        {
            var body = this.BodyBounds;
            var screenTop = box.Rect.Y + body.Y - _scrollY;
            _dragKind = EdgeKindOf(box, screenTop, e.Y);
            _moveGrabPixels = e.Y - screenTop;
        }
        else
        {
            _dragKind = DragKind.Move;
            var pressDay = this.IsMonth ? this.MonthDayAt(e.X, e.Y) : this.DayColumnDate(e.X);
            _moveGrabDays = pressDay is { } day ? (int)(day.Date - appt.Start.Date).TotalDays : 0;
        }

        this.UpdateMovePreview(e);
    }

    /// <summary>Recomputes the snapped landing bounds of the dragged appointment from the pointer — the
    /// value-type state the paint path reads to draw the ghost. Allocates nothing.</summary>
    private void UpdateMovePreview(MouseEventArgs e)
    {
        var appt = _appointments[_moveIndex];
        if (_moveFixed)
        {
            // Day granularity: the whole appointment shifts by whole days, preserving its time-of-day
            // and duration — the same for the all-day band and for month chips.
            var target = this.IsMonth ? this.MonthDayAt(e.X, e.Y) : this.DayColumnDate(e.X);
            var landDay = (target ?? appt.Start).Date.AddDays(-_moveGrabDays);
            var delta = landDay - appt.Start.Date;
            _previewStart = appt.Start + delta;
            _previewEnd = appt.End + delta;
            return;
        }

        var grid = this.BodyBounds;
        switch (_dragKind)
        {
            case DragKind.ResizeStart:
            {
                // The start follows the pointer's day column and minute, so a drag can carry it onto
                // another day; it never reaches the end (a one-slot minimum span is kept).
                var day = this.DayColumnDate(e.X);
                var min = this.SnapMinutes(this.MinutesForY(e.Y - grid.Y + _scrollY));
                var start = day.AddMinutes(min);
                var latest = appt.End.AddMinutes(-_timeScale);
                _previewStart = start > latest ? latest : start;
                _previewEnd = appt.End;
                break;
            }

            case DragKind.ResizeEnd:
            {
                // The end follows the pointer's day column and minute, so a drag can carry it onto a
                // later (or earlier) day; it never crosses back before the start.
                var day = this.DayColumnDate(e.X);
                var min = this.SnapMinutes(this.MinutesForY(e.Y - grid.Y + _scrollY));
                var end = day.AddMinutes(min);
                var earliest = appt.Start.AddMinutes(_timeScale);
                _previewEnd = end < earliest ? earliest : end;
                _previewStart = appt.Start;
                break;
            }

            default:
            {
                var startMin = this.SnapMinutes(this.MinutesForY(e.Y - grid.Y + _scrollY - _moveGrabPixels));
                _previewStart = this.DayColumnDate(e.X).AddMinutes(startMin);
                _previewEnd = _previewStart.AddMinutes(_moveDurationMinutes);
                break;
            }
        }
    }

    /// <summary>Proposes the dropped reschedule: raises the cancelable <see cref="AppointmentMoving"/>
    /// and, unless a handler vetoes it, <see cref="AppointmentMoved"/>. A no-op move raises nothing.
    /// The one event-args object is the move's only allocation.</summary>
    private void CommitMove(int index)
    {
        var appt = _appointments[index];
        if (_previewEnd <= _previewStart)
            return;

        if (_previewStart == appt.Start && _previewEnd == appt.End)
            return;

        var args = new AppointmentMoveEventArgs(appt, _previewStart, _previewEnd);
        this.AppointmentMoving?.Invoke(this, args);
        if (args.Cancel)
            return;

        this.AppointmentMoved?.Invoke(this, args);
    }

    /// <summary>Clears the reschedule-drag state.</summary>
    private void ResetMove()
    {
        _movePrimed = false;
        _moving = false;
        _moveIndex = -1;
        _moveSubject = null;
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        this.EnsureLayout();

        // A press on the scrollbar thumb arms a drag.
        if (this.NeedsScrollBar)
        {
            var body = this.BodyBounds;
            var bounds = new Rectangle(this.Width - this.Theme.ScrollBarSize, body.Y, this.Theme.ScrollBarSize, body.Height);
            if (bounds.Contains(e.Location))
            {
                switch (ScrollBarRenderer.HitTest(bounds, true, 0, this.MaxScroll, _scrollY, Math.Max(1, body.Height), e.Location))
                {
                    case ScrollBarPart.DecreaseArrow: this.ScrollBy(-this.SlotHeight); break;
                    case ScrollBarPart.IncreaseArrow: this.ScrollBy(this.SlotHeight); break;
                    case ScrollBarPart.DecreaseChannel: this.ScrollBy(-body.Height); break;
                    case ScrollBarPart.IncreaseChannel: this.ScrollBy(body.Height); break;
                    case ScrollBarPart.Thumb:
                        var thumb = ScrollBarRenderer.ThumbRect(bounds, true, 0, this.MaxScroll, _scrollY, Math.Max(1, body.Height));
                        _scrollDragging = true;
                        _scrollDragOffset = e.Y - thumb.Y;
                        this.Invalidate();
                        break;
                }

                return;
            }
        }

        var hitBox = this.HitTestBox(e.X, e.Y);
        if (hitBox >= 0)
        {
            var box = _layout[hitBox];
            var hit = box.Index;

            // A second press on the same appointment inside the double-click window opens it for edit,
            // the honest double-click moment on a surface the core owns (like the grid's cell double
            // click) — there is no separate native double-click event to wait for.
            var now = Environment.TickCount64;
            var isDouble = hit == _lastClickIndex && now - _lastClickTime <= this.Theme.DoubleClickTime;
            _lastClickTime = isDouble ? 0 : now;
            _lastClickIndex = hit;
            this.SelectIndex(hit);
            if (isDouble)
            {
                this.AppointmentActivate?.Invoke(this, new AppointmentEventArgs(_appointments[hit]));
                return;
            }

            // Arm a move: past a small threshold the press turns into a reschedule drag; a click that
            // never crosses the threshold falls through to the plain selection made just above.
            this.PrimeMove(box, hit, e);
            return;
        }

        _lastClickIndex = -1;

        // Empty space: begin a range gesture, and drop any appointment selection.
        this.SelectIndex(-1);
        if (this.IsMonth)
        {
            if (this.MonthDayAt(e.X, e.Y) is { } day)
            {
                _rangeDragging = true;
                _rangeAnchor = day;
                _rangeStart = day;
                _rangeEnd = day.AddDays(1);
            }

            return;
        }

        if (this.TimeAt(e.X, e.Y) is { } time)
        {
            _rangeDragging = true;
            _rangeAnchor = time;
            _rangeStart = time;
            _rangeEnd = time.AddMinutes(_timeScale);
            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_scrollDragging)
        {
            var body = this.BodyBounds;
            var bounds = new Rectangle(this.Width - this.Theme.ScrollBarSize, body.Y, this.Theme.ScrollBarSize, body.Height);
            var track = ScrollBarRenderer.TrackRect(bounds, true);
            var offset = e.Y - _scrollDragOffset - track.Y;
            _scrollY = ScrollBarRenderer.ValueFromThumbOffset(bounds, true, 0, this.MaxScroll, Math.Max(1, body.Height), offset);
            this.ClampScroll();
            this.Invalidate();
            return;
        }

        // An armed appointment drag becomes a live move/resize once it clears the threshold; before
        // that a press-and-release is a plain click and keeps the selection it already made.
        if (_movePrimed)
        {
            if (!_moving)
            {
                if (Math.Abs(e.X - _movePress.X) <= _MoveThreshold && Math.Abs(e.Y - _movePress.Y) <= _MoveThreshold)
                    return;

                _moving = true;
            }

            this.UpdateMovePreview(e);
            this.Invalidate();
            return;
        }

        if (!_rangeDragging)
        {
            // Idle: show the north-south resize cursor over a real (non-continuation) timed edge.
            this.SetRegionCursor(this.EdgeKindAt(e.X, e.Y) != DragKind.Move ? Cursors.SizeNS : null);
            return;
        }

        if (this.IsMonth)
        {
            if (this.MonthDayAt(e.X, e.Y) is { } day)
            {
                var lo = day < _rangeAnchor ? day : _rangeAnchor;
                var hi = day < _rangeAnchor ? _rangeAnchor : day;
                _rangeStart = lo;
                _rangeEnd = hi.AddDays(1);
                this.Invalidate();
            }

            return;
        }

        if (this.TimeAt(e.X, e.Y) is { } time)
        {
            if (time >= _rangeAnchor)
            {
                _rangeStart = _rangeAnchor;
                _rangeEnd = time.AddMinutes(_timeScale);
            }
            else
            {
                _rangeStart = time;
                _rangeEnd = _rangeAnchor.AddMinutes(_timeScale);
            }

            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_scrollDragging)
        {
            _scrollDragging = false;
            this.Invalidate();
            return;
        }

        // A reschedule drag drops here: propose the snapped move, then clear the drag state. A press
        // that never moved (sub-threshold) resets silently — its selection already stood.
        if (_movePrimed)
        {
            var moved = _moving;
            var index = _moveIndex;
            this.ResetMove();
            if (moved)
            {
                this.Invalidate();
                this.CommitMove(index);
            }

            return;
        }

        if (!_rangeDragging)
            return;

        _rangeDragging = false;
        this.Invalidate();
        if (_rangeEnd > _rangeStart)
            this.TimeRangeSelected?.Invoke(this, new DateRangeEventArgs(_rangeStart, _rangeEnd));
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (this.IsMonth)
        {
            if (e.Delta > 0)
                this.Previous();
            else if (e.Delta < 0)
                this.Next();

            return;
        }

        this.ScrollBy(e.Delta > 0 ? -this.YForMinutes(_WheelMinutes) : this.YForMinutes(_WheelMinutes));
    }

    private void ScrollBy(int delta)
    {
        this.EnsureLayout();
        _scrollInitialized = true;
        _scrollY = Math.Clamp(_scrollY + delta, 0, this.MaxScroll);
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
                this.SelectedDate = _selectedDate.AddDays(-1);
                e.Handled = true;
                break;

            case Keys.Right:
                this.SelectedDate = _selectedDate.AddDays(+1);
                e.Handled = true;
                break;

            case Keys.Up when this.IsMonth:
                this.SelectedDate = _selectedDate.AddDays(-7);
                e.Handled = true;
                break;

            case Keys.Down when this.IsMonth:
                this.SelectedDate = _selectedDate.AddDays(+7);
                e.Handled = true;
                break;

            case Keys.Up:
                this.ScrollBy(-this.SlotHeight);
                e.Handled = true;
                break;

            case Keys.Down:
                this.ScrollBy(this.SlotHeight);
                e.Handled = true;
                break;

            case Keys.PageUp:
                this.Previous();
                e.Handled = true;
                break;

            case Keys.PageDown:
                this.Next();
                e.Handled = true;
                break;

            case Keys.Home:
                this.GoToToday();
                e.Handled = true;
                break;

            case Keys.Enter:
                if (this.SelectedAppointment is { } appt)
                    this.AppointmentActivate?.Invoke(this, new AppointmentEventArgs(appt));

                e.Handled = true;
                break;

            case Keys.Escape when _movePrimed:
                // Bail out of an in-flight reschedule: the preview vanishes and the snapshot is left
                // exactly where it was, so a subsequent mouse-up proposes nothing.
                this.ResetMove();
                this.Invalidate();
                e.Handled = true;
                break;
        }
    }

    /// <summary>The number of appointment boxes currently laid out — the visible-window size the
    /// virtualization keeps bounded however large the bound set is. A test/benchmark hook.</summary>
    internal int LaidOutBoxCount
    {
        get
        {
            this.EnsureLayout();
            return _layout.Count;
        }
    }

    /// <summary>The vertical scroll offset of the time grid, in pixels. A test hook.</summary>
    internal int ScrollOffset => _scrollY;

    /// <summary>The appointment at <paramref name="index"/> in the start-sorted snapshot. A test hook.</summary>
    internal Appointment SnapshotAppointment(int index) => _appointments[index];

    private void SelectIndex(int index)
    {
        if (index == _selectedIndex)
            return;

        _selectedIndex = index;
        this.Invalidate();
        this.SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Orders appointments by start instant for the snapshot's binary-search window.</summary>
    private sealed class AppointmentStartComparer : IComparer<Appointment>
    {
        public static readonly AppointmentStartComparer Instance = new();

        public int Compare(Appointment x, Appointment y) => x.Start.CompareTo(y.Start);
    }
}
