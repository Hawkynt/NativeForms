using System.Diagnostics;
using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The gallery's self-driving mode, switched on with <c>--autopilot</c>. It shows the real window on
/// the real desktop, then walks the whole gallery with synthesized-but-genuine GTK input — clicks,
/// drags, wheel notches, key strokes — and asserts the observable state of the controls after every
/// gesture. It prints a PASS/FAIL line per check plus a summary and leaves the process exit code at
/// zero only when every check passed, which makes the demo usable as an end-to-end smoke test.
/// </summary>
/// <remarks>
/// A normal launch never touches any of this: <see cref="Enabled"/> stays false, the gallery
/// publishes no control references and no autopilot thread is started.
/// <para>
/// The script runs on a worker thread and marshals every gesture and every read onto the UI thread
/// through <see cref="Control.BeginInvoke"/>, waiting with a timeout. That is what makes the run
/// survive a wedged step: a gesture that never returns — a modal dialog nesting its own main loop,
/// say — trips the watchdog, fails that one check and lets the walkthrough continue.
/// </para>
/// </remarks>
internal sealed partial class Autopilot
{
    /// <summary>The title the main window carries, used to find its <c>GdkWindow</c>.</summary>
    private const string _WindowTitle = "NativeForms Gallery";

    /// <summary>How long a single marshalled gesture or read may take before it counts as wedged.</summary>
    private const int _StepTimeoutMs = 6000;

    /// <summary>Where the screenshots taken at key moments are written.</summary>
    private static readonly string _ShotDirectory =
        Environment.GetEnvironmentVariable("NATIVEFORMS_AUTOPILOT_SHOTS") ?? "/tmp/nativeforms-autopilot";

    /// <summary>Whether the process was started with <c>--autopilot</c>.</summary>
    internal static bool Enabled { get; private set; }

    /// <summary>The exit code the walkthrough earned: zero only when every check passed.</summary>
    internal static int ExitCode { get; private set; }

    /// <summary>Turns the autopilot on. Must run before the gallery is constructed, because that is
    /// when the window publishes the control references the script drives.</summary>
    internal static void Enable() => Enabled = true;

    /// <summary>Starts the walkthrough once <paramref name="form"/> has been loaded.</summary>
    internal static void Attach(MainForm form)
    {
        if (!Enabled)
            return;

        form.Load += (_, _) =>
        {
            var autopilot = new Autopilot(form);
            var thread = new Thread(autopilot.Run) { IsBackground = true, Name = "autopilot" };
            thread.Start();
        };
    }

    private readonly MainForm _form;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _issues = [];
    private readonly List<string> _landings = [];
    private readonly List<string> _failedChecks = [];
    private readonly List<string> _captureFailures = [];
    private nint _root;
    private int _passed;
    private int _failed;
    private int _shots;
    private int[]? _tabHeaderX;

    private Autopilot(MainForm form) => _form = form;

    /// <summary>Thrown when a marshalled gesture does not come back inside the watchdog window.</summary>
    private sealed class WedgedException(string what)
        : Exception($"the UI thread did not come back within {_StepTimeoutMs} ms during {what}");

    // --- Lifecycle ------------------------------------------------------------------------------

    /// <summary>The worker-thread entry point: wait for the window to map, run the script, report.</summary>
    private void Run()
    {
        try
        {
            Thread.Sleep(1200);
            this.Settle(200);
            this.Pump("locating the main window", () => _root = Injection.MainWindow(_WindowTitle));
            if (_root == 0)
            {
                Console.Error.WriteLine("autopilot: the main window never mapped — nothing to drive.");
                ExitCode = 2;
                this.Quit();
                return;
            }

            Console.WriteLine($"autopilot: driving \"{_WindowTitle}\" at {Injection.WindowBounds(_root)}");
            Console.WriteLine();
            this.Screenshot("state-startup");
            this.RunScript();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"autopilot: the walkthrough aborted: {e}");
            ++_failed;
        }
        finally
        {
            this.Report();
            this.Quit();
        }
    }

    /// <summary>Prints the summary and settles the exit code.</summary>
    private void Report()
    {
        Console.WriteLine();
        if (_failedChecks.Count > 0)
        {
            Console.WriteLine("Failed checks:");
            foreach (var name in _failedChecks)
                Console.WriteLine($"  - {name}");

            Console.WriteLine();
        }

        if (_captureFailures.Count > 0)
        {
            Console.WriteLine($"Captures that produced no file: {string.Join(", ", _captureFailures)}");
            Console.WriteLine();
        }

        Console.WriteLine($"autopilot captures: {_shots} PNG(s) written to {_ShotDirectory}");
        Console.WriteLine(
            $"autopilot summary: {_passed + _failed} checks, {_passed} passed, {_failed} failed "
            + $"in {_clock.Elapsed.TotalSeconds:F1} s");
        ExitCode = _failed == 0 && _captureFailures.Count == 0 ? 0 : 1;
    }

    /// <summary>Closes the gallery, ending the process.</summary>
    private void Quit()
    {
        try
        {
            _form.BeginInvoke(Application.Exit);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"autopilot: could not close the window: {e.Message}");
        }
    }

    // --- Check bookkeeping ----------------------------------------------------------------------

    /// <summary>Runs one declarative check: the body performs a gesture and states expectations, and
    /// every unmet expectation — or a wedged UI thread — turns the check red without stopping the run.</summary>
    private void Check(string name, Action body)
    {
        _issues.Clear();
        _landings.Clear();
        var watch = Stopwatch.StartNew();
        try
        {
            body();
        }
        catch (WedgedException e)
        {
            _issues.Add(e.Message);
        }
        catch (Exception e)
        {
            _issues.Add($"{e.GetType().Name}: {e.Message}");
        }

        if (_issues.Count == 0)
        {
            ++_passed;
            Console.WriteLine($"PASS  {name}  [{watch.ElapsedMilliseconds} ms]");
            return;
        }

        ++_failed;
        _failedChecks.Add(name);
        Console.WriteLine($"FAIL  {name}  [{watch.ElapsedMilliseconds} ms]");
        foreach (var issue in _issues)
            Console.WriteLine($"        {issue}");

        // Which native widget each press actually reached; a gesture that misses its target looks
        // exactly like a control that ignores it until you see where the event went.
        if (_landings.Count > 0)
            Console.WriteLine($"        presses landed on: {string.Join(", ", _landings)}");
    }

    /// <summary>Prints a section heading into the log.</summary>
    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} " + new string('-', Math.Max(0, 70 - title.Length)));
    }

    /// <summary>Records an unmet expectation against the running check.</summary>
    private void Fail(string message) => _issues.Add(message);

    /// <summary>Expects two values to match, reporting both when they do not.</summary>
    private void Expect<T>(string what, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            this.Fail($"{what}: expected {Describe(expected)}, observed {Describe(actual)}");
    }

    /// <summary>Expects a condition to hold.</summary>
    private void ExpectTrue(string what, bool condition)
    {
        if (!condition)
            this.Fail(what);
    }

    /// <summary>Expects a value to have moved away from what it was.</summary>
    private void ExpectChanged<T>(string what, T before, T after)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
            this.Fail($"{what}: still {Describe(after)} after the gesture");
    }

    /// <summary>Expects an integer to sit inside a tolerance band around a target.</summary>
    private void ExpectNear(string what, int actual, int expected, int tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
            this.Fail($"{what}: expected {expected} ± {tolerance}, observed {actual}");
    }

    /// <summary>Renders a value for the log, quoting strings and naming nulls.</summary>
    private static string Describe<T>(T value) => value switch
    {
        null => "(null)",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? "(null)",
    };

    // --- UI-thread plumbing ---------------------------------------------------------------------

    /// <summary>Runs an action on the UI thread and waits for it, under the watchdog.</summary>
    private void Pump(string what, Action action)
    {
        var done = new ManualResetEventSlim(false);
        Exception? error = null;
        _form.BeginInvoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(_StepTimeoutMs))
            throw new WedgedException(what);

        if (error is not null)
            throw error;
    }

    /// <summary>Queues an action on the UI thread without waiting — the escape hatch for a gesture
    /// that deliberately blocks, such as the one opening a modal dialog.</summary>
    private void Post(Action action) => _form.BeginInvoke(action);

    /// <summary>
    /// The display index of the grid column with this header. Checks name their column instead of
    /// hard-coding an index, so inserting a column upstream cannot silently retarget a gesture at
    /// the wrong cell.
    /// </summary>
    private int GridColumn(DataGridView grid, string header) => this.Read(() =>
    {
        for (var i = 0; i < grid.Columns.Count; ++i)
            if (string.Equals(grid.Columns[i].HeaderText, header, StringComparison.Ordinal))
                return i;

        return -1;
    });

    /// <summary>Reads a value off the UI thread.</summary>
    private T Read<T>(Func<T> getter)
    {
        var value = default(T)!;
        this.Pump("a state read", () => value = getter());
        return value;
    }

    /// <summary>Performs an action on the UI thread and lets the result settle.</summary>
    private void Do(Action action)
    {
        this.Pump("a direct action", action);
        this.Settle();
    }

    /// <summary>Drains the main loop and gives timers a moment, so the next read sees a settled UI.</summary>
    private void Settle(int quietMs = 40)
    {
        this.Pump("settling", Injection.Drain);
        Thread.Sleep(quietMs);
        this.Pump("settling", Injection.Drain);
    }

    // --- Gestures -------------------------------------------------------------------------------

    /// <summary>The screen point at an offset inside a control.</summary>
    private Point ScreenOf(Control control, int dx, int dy)
        => this.Read(() => control.PointToScreen(new Point(dx, dy)));

    /// <summary>The screen point at the centre of a control.</summary>
    private Point CentreOf(Control control)
        => this.Read(() => control.PointToScreen(new Point(control.Width / 2, control.Height / 2)));

    /// <summary>Clicks at an offset inside a control and reports which widget the press landed on.</summary>
    private string Click(Control control, int dx, int dy, uint button = 1, uint modifiers = 0)
    {
        this.DropStrayTip();
        return this.ClickAt(this.ScreenOf(control, dx, dy), button, modifiers);
    }

    /// <summary>
    /// Takes down a tip left floating by an earlier hover, and disarms one still counting down, which
    /// is what aiming the pointer at another control is supposed to have done already.
    /// </summary>
    /// <remarks>
    /// Crossing events are made by the display server, not by <c>gtk_main_do_event</c>, so injected
    /// motion can never tell a control the pointer went away — and a tip armed by a hover three
    /// checks ago pops up, or stays up, while the walkthrough is somewhere else entirely. On a
    /// display whose focus follows the pointer that is not cosmetic: the floating surface holds the
    /// keyboard focus, the gallery reports itself inactive, and no widget in it will focus at all,
    /// from a click or from <see cref="Control.Focus"/> alike. This is the one thing in-process
    /// injection cannot synthesize, so the harness states it instead — it compensates for its own
    /// limitation, never for the toolkit's behavior, and the tooltip checks that <em>want</em> a tip
    /// hover rather than click.
    /// <para>
    /// Unconditional on purpose. Hiding only a tip that is already up leaves the pending delay
    /// running, and a tip that surfaces between the press and the assertion is exactly the race that
    /// makes a run look intermittently broken; <see cref="ToolTip.Hide"/> stops the delay too.
    /// </para>
    /// </remarks>
    private void DropStrayTip() => this.Do(_form.Part<ToolTip>("chrome.toolTip").Hide);

    /// <summary>
    /// Makes sure the gallery is the window the keyboard is pointed at before anything asks about
    /// focus. See <see cref="Injection.Present"/> for why this is the harness's job here.
    /// </summary>
    private void ActivateGallery()
    {
        for (var attempt = 0; attempt < 5 && !this.Read(() => Injection.IsActive(_root)); ++attempt)
        {
            this.Pump("activating the gallery", () => Injection.Present(_root));
            this.Settle(80);
        }
    }

    /// <summary>
    /// The toplevel an event at a screen point belongs to. Drop-downs, menus and dialogs are separate
    /// top-level windows stacked over the gallery, not descendants of it, so an event aimed at one of
    /// them has to start its descent there — pointing it at the main window would silently deliver
    /// the click to whatever sits underneath.
    /// </summary>
    private nint RootAt(Point screen)
    {
        foreach (var toplevel in Injection.OtherToplevels(_root))
            if (Injection.WindowBounds(toplevel).Contains(screen))
                return toplevel;

        return _root;
    }

    /// <summary>
    /// Presses and releases at a screen point, sampling a surface's open flag after each phase: right
    /// inside the press dispatch, once the loop has gone idle, and after the release. A drop-down that
    /// vanishes tells you far more when you know which of the three phases lost it.
    /// </summary>
    private (bool OnPress, bool AfterSettle, bool AfterRelease) ProbeOpen(Point screen, Func<bool> isOpen)
    {
        var onPress = false;
        this.Pump("a press", () =>
        {
            var root = this.RootAt(screen);
            Injection.Move(root, screen);
            _landings.Add(Injection.Press(root, screen, 1, 0).WidgetName);
            onPress = isOpen();
        });

        this.Settle();
        var afterSettle = this.Read(isOpen);
        this.ReleaseAt(screen);
        return (onPress, afterSettle, this.Read(isOpen));
    }

    /// <summary>Presses at a screen point without releasing, and records where it landed.</summary>
    private string PressAt(Point screen, uint button = 1, uint modifiers = 0)
    {
        var landed = "(unknown)";
        this.Pump("a press", () =>
        {
            var root = this.RootAt(screen);
            Injection.Move(root, screen);
            landed = Injection.Press(root, screen, button, modifiers).WidgetName;
        });

        _landings.Add(landed);
        this.Settle();
        return landed;
    }

    /// <summary>Releases the held button at a screen point.</summary>
    private void ReleaseAt(Point screen, uint button = 1, uint modifiers = 0)
    {
        this.Pump("a release", () => Injection.Release(this.RootAt(screen), screen, button, modifiers));
        this.Settle();
    }

    /// <summary>Clicks at a screen point.</summary>
    private string ClickAt(Point screen, uint button = 1, uint modifiers = 0)
    {
        var landed = this.PressAt(screen, button, modifiers);
        this.ReleaseAt(screen, button, modifiers);
        return landed;
    }

    /// <summary>Double-clicks at an offset inside a control.</summary>
    private void DoubleClick(Control control, int dx, int dy)
    {
        var screen = this.ScreenOf(control, dx, dy);
        this.ClickAt(screen);
        this.Pump("a double click", () =>
        {
            Injection.Press(_root, screen, 1, 0);
            Injection.Press(_root, screen, 1, 0, doubleClick: true);
            Injection.Release(_root, screen, 1, 0);
        });

        this.Settle();
    }

    /// <summary>Presses inside a control, drags in steps to a second offset and releases — under the
    /// implicit grab a real press takes, so the whole drag keeps reaching the pressed control.</summary>
    private void Drag(Control control, Point from, Point to, int steps = 8)
    {
        var start = this.ScreenOf(control, from.X, from.Y);
        var end = this.ScreenOf(control, to.X, to.Y);
        var root = this.RootAt(start);
        this.Pump("a drag press", () =>
        {
            Injection.Move(root, start);
            _landings.Add(Injection.Press(root, start, 1, 0).WidgetName);
        });

        for (var i = 1; i <= steps; ++i)
        {
            var point = new Point(
                start.X + ((end.X - start.X) * i / steps),
                start.Y + ((end.Y - start.Y) * i / steps));
            this.Pump("a drag move", () => Injection.Move(root, point, buttonHeld: true));
            Thread.Sleep(8);
        }

        this.Settle();
        this.Pump("a drag release", () => Injection.Release(root, end, 1, 0));
        this.Settle();
    }

    /// <summary>Moves the pointer over an offset inside a control without pressing.</summary>
    private void Hover(Control control, int dx, int dy)
    {
        var screen = this.ScreenOf(control, dx, dy);
        this.Pump("a hover", () => Injection.Move(this.RootAt(screen), screen));
        this.Settle();
    }

    /// <summary>Presses inside a control, holds for a while so auto-repeat can fire, then releases.</summary>
    private void PressAndHold(Control control, int dx, int dy, int holdMs)
    {
        var screen = this.ScreenOf(control, dx, dy);
        this.PressAt(screen);
        var deadline = Environment.TickCount64 + holdMs;
        while (Environment.TickCount64 < deadline)
        {
            this.Pump("holding", Injection.Drain);
            Thread.Sleep(20);
        }

        this.ReleaseAt(screen);
    }

    /// <summary>Sends wheel notches over an offset inside a control (positive scrolls up).</summary>
    private void Wheel(Control control, int dx, int dy, int notches)
    {
        var screen = this.ScreenOf(control, dx, dy);
        this.Pump("a wheel notch", () =>
        {
            var root = this.RootAt(screen);
            Injection.Move(root, screen);
            for (var i = 0; i < Math.Abs(notches); ++i)
                Injection.Wheel(root, screen, notches);
        });

        this.Settle();
    }

    /// <summary>Sends one key stroke to the main window's focus chain.</summary>
    private void Key(uint keyval, uint modifiers = 0)
    {
        this.Pump("a key stroke", () => Injection.Key(_root, keyval, modifiers));
        this.Settle(20);
    }

    /// <summary>Sends one key stroke to another toplevel — a drop-down or a modal dialog.</summary>
    private void KeyInto(nint toplevel, uint keyval, uint modifiers = 0)
    {
        this.Pump("a key stroke", () => Injection.Key(toplevel, keyval, modifiers));
        this.Settle(20);
    }

    /// <summary>Types printable text one key stroke at a time, shifting where the character needs it.</summary>
    private void Type(string text)
    {
        foreach (var c in text)
        {
            var shifted = char.IsUpper(c) || c is '(' or ')' or '_' or ':' or '?' or '!';
            this.Pump("typing", () => Injection.Key(_root, c, shifted ? Injection.ShiftMask : 0));
            Thread.Sleep(10);
        }

        this.Settle();
    }

    /// <summary>Gives a control the keyboard focus through the toolkit, then settles.</summary>
    private void FocusOn(Control control) => this.Do(control.Focus);

    // --- Toplevels ------------------------------------------------------------------------------

    /// <summary>The toplevel windows other than the gallery: drop-downs, menus and dialogs.</summary>
    private List<nint> Popups() => this.Read(() => Injection.OtherToplevels(_root));

    /// <summary>Waits for a second toplevel to appear, returning it or zero.</summary>
    private nint WaitForPopup(int timeoutMs = 1500)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var popups = this.Popups();
            if (popups.Count > 0)
                return popups[0];

            this.Settle(40);
        }

        return 0;
    }

    // --- Screenshots ----------------------------------------------------------------------------

    /// <summary>
    /// Writes a PNG of the gallery and every popup stacked over it, by asking the widgets to paint
    /// into a Cairo image surface on the UI thread. Nothing outside the process is involved, so the
    /// capture works on a display no screenshot tool can read — a rootless Xwayland server, say.
    /// </summary>
    private void Screenshot(string name)
    {
        var path = Path.Combine(_ShotDirectory, $"{name}.png");
        try
        {
            Size? size = null;
            this.Pump("a capture", () => size = Capture.Toplevels(_root, path));
            if (size is { } written)
            {
                ++_shots;
                Console.WriteLine($"      capture: {path} ({written.Width}×{written.Height})");
                return;
            }

            Console.WriteLine($"      capture failed: {path} — nothing mapped was drawable");
            _captureFailures.Add(name);
        }
        catch (Exception e)
        {
            Console.WriteLine($"      capture failed: {path} — {e.Message}");
            _captureFailures.Add(name);
        }
    }
}

/// <summary>The GDK key symbols the walkthrough sends (<c>gdkkeysyms.h</c>).</summary>
internal static class KeySym
{
    /// <summary>The Backspace key.</summary>
    internal const uint BackSpace = 0xff08;

    /// <summary>The Tab key.</summary>
    internal const uint Tab = 0xff09;

    /// <summary>The Return key.</summary>
    internal const uint Return = 0xff0d;

    /// <summary>The Escape key.</summary>
    internal const uint Escape = 0xff1b;

    /// <summary>The space bar.</summary>
    internal const uint Space = 0x020;

    /// <summary>The Home key.</summary>
    internal const uint Home = 0xff50;

    /// <summary>The Left arrow.</summary>
    internal const uint Left = 0xff51;

    /// <summary>The Up arrow.</summary>
    internal const uint Up = 0xff52;

    /// <summary>The Right arrow.</summary>
    internal const uint Right = 0xff53;

    /// <summary>The Down arrow.</summary>
    internal const uint Down = 0xff54;

    /// <summary>The Page Up key.</summary>
    internal const uint PageUp = 0xff55;

    /// <summary>The Page Down key.</summary>
    internal const uint PageDown = 0xff56;

    /// <summary>The End key.</summary>
    internal const uint End = 0xff57;

    /// <summary>The F2 key, which starts an in-place edit.</summary>
    internal const uint F2 = 0xffbf;

    /// <summary>The Delete key.</summary>
    internal const uint Delete = 0xffff;
}
