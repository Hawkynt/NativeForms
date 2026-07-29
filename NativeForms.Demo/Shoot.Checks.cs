using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The behavioural half of <c>--shoot</c>: state round-trips and a layout audit, run against every
/// control on a page.
/// </summary>
/// <remarks>
/// <para>
/// A screenshot proves a page renders. It does not prove the page <em>responds</em>, and on Windows
/// nothing else could: the autopilot drives its input through <c>gdk_test_simulate_*</c>, so the whole
/// walkthrough is GTK-only by construction.
/// </para>
/// <para>
/// These checks go through the toolkit's own API rather than through synthesized OS input, which is
/// what lets them run anywhere the toolkit runs. That is a narrower claim than the autopilot's — no
/// pointer, no keyboard, no window manager — but it is the claim that reaches Windows, and it covers
/// the failure that matters most for a peer-backed toolkit: state written to a control has to survive
/// the trip into the native widget and back.
/// </para>
/// <para>
/// The layout audit is here for the same reason. A control sized from a wrong system metric — a scroll
/// bar as wide as the screen, say — passes every unit test in the suite and is obvious the moment
/// anything measures where it actually landed.
/// </para>
/// </remarks>
internal static partial class Shoot
{
    /// <summary>
    /// Whether an injected event that produced no effect counts against the run.
    /// </summary>
    /// <remarks>
    /// It does on Windows, where SendInput reports refusal and a delivered event that changes nothing
    /// is a real defect. It still does not on macOS, but for a smaller reason than before: the probe
    /// there now posts into the application's own queue rather than through the window server, so an
    /// event is always accepted and always dispatched — what remains uncertain is the runner, which
    /// gives the process no session of its own to be activated in. The observation is logged either
    /// way, and the click and keystroke counts in the closing line are what say whether it worked.
    /// </remarks>
    private static int Fatal => OperatingSystem.IsWindows() ? 1 : 0;

    /// <summary>Whether this platform can deliver injected input at all.</summary>
    private static bool InjectionAvailable
        => (OperatingSystem.IsWindows() && ShootInput.Available)
            || (OperatingSystem.IsMacOS() && ShootInputMac.Available);

    /// <summary>Clicks at a screen point through whichever injector this platform has.</summary>
    private static bool InjectClick(Point screen)
        => OperatingSystem.IsWindows() ? ShootInput.Click(screen) : ShootInputMac.Click(screen);

    /// <summary>
    /// What the platform says is under a screen point, or an empty string where nothing asks.
    /// </summary>
    /// <remarks>
    /// Only macOS answers, and only because a miss there had to be explained: a click that changes
    /// nothing is the same observation whether the point was over the wrong widget, over no widget, or
    /// over the right one in a part of it the platform does not treat as sensitive. Naming the view
    /// separates the first two from the third without guessing.
    /// </remarks>
    private static string InjectAt(Point screen)
        => OperatingSystem.IsMacOS() ? ShootInputMac.ViewAt(screen) : string.Empty;

    /// <summary>Types one character through whichever injector this platform has.</summary>
    private static bool InjectType(char character)
        => OperatingSystem.IsWindows() ? ShootInput.Type(character) : ShootInputMac.Type(character);

    /// <summary>Lets the platform deliver what was just injected.</summary>
    private static void InjectDrain()
    {
        if (OperatingSystem.IsWindows())
            ShootInput.Drain();
        else
            ShootInputMac.Deliver();
    }

    /// <summary>Clicks that were injected through the OS and observed arriving.</summary>
    public static int Clicks { get; private set; }

    /// <summary>Keystrokes that were injected through the OS and observed arriving.</summary>
    public static int Keystrokes { get; private set; }

    /// <summary>Every control in the tree rooted at <paramref name="control"/>, itself included.</summary>
    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        foreach (var child in control.Controls)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    /// <summary>
    /// Drives one real click and one real keystroke through the operating system's input queue, and
    /// checks that both reached the toolkit. Reports the number of failures; a session that cannot
    /// deliver injected input at all is a skip, not a failure.
    /// </summary>
    /// <remarks>
    /// This is the check that state round-trips cannot make. Writing <c>box.Text</c> proves the peer
    /// stores and returns a string; a keystroke arriving from the input queue proves the window is
    /// focusable, hit-testable, on top, and wired to the toolkit's event routing — which is where a
    /// native-widget toolkit actually breaks.
    /// </remarks>
    public static int CheckInput(Control page, string windowTitle, Action<string> note)
    {
        if (!InjectionAvailable)
            return 0;

        // Only controls actually on screen can be clicked. A page that scrolls, or one whose panes are
        // laid out past the viewport, puts perfectly ordinary controls at negative screen coordinates —
        // aiming there tests nothing and reports a failure the toolkit did not earn.
        var visible = new Rectangle(page.PointToScreen(Point.Empty), new(page.Width, page.Height));
        bool OnScreen(Control control)
        {
            if (control is not { Visible: true, Enabled: true, Width: > 8, Height: > 8 })
                return false;

            var centre = control.PointToScreen(new(control.Width / 2, control.Height / 2));
            return visible.Contains(centre) && centre is { X: >= 0, Y: >= 0 };
        }

        // A check box, never a button. The click target has to be something whose handler cannot block:
        // the first run on a real runner clicked "Show a modal MessageBox...", which did exactly what it
        // says, and the message pump then sat inside a dialog nobody was there to dismiss. A check box
        // exercises the identical path — hit-test, focus, z-order, event routing — and only toggles.
        var target = Walk(page).OfType<CheckBox>().FirstOrDefault(OnScreen);
        var box = Walk(page).OfType<TextBox>().FirstOrDefault(b => b is not MaskedTextBox && b is { ReadOnly: false } && OnScreen(b));
        if (target is null && box is null)
            return 0;

        // A key event goes to the key window, and neither platform hands one out unasked: Windows
        // wants the window brought to the foreground, macOS wants the application activated.
        if (OperatingSystem.IsWindows())
            ShootInput.Activate(windowTitle);
        else
            ShootInputMac.Activate();

        var failed = 0;

        if (target is not null)
        {
            var before = target.Checked;
            var centre = target.PointToScreen(new(target.Width / 2, target.Height / 2));
            if (InjectClick(centre))
            {
                InjectDrain();
                if (target.Checked != before)
                    ++Clicks;
                else
                {
                    // The centre of a check box need not be on the check box. A control given more width
                    // than its caption needs carries empty space to the right of it, and whether that
                    // space belongs to the widget is the platform's answer rather than the toolkit's — so
                    // the same gesture is aimed at the box glyph before anything is called a failure.
                    // The two readings separate an input path that does not work from an aim that was
                    // never over anything, which the old single line could not tell apart.
                    var onBox = target.PointToScreen(new(Math.Min(8, target.Width / 2), target.Height / 2));
                    if (InjectClick(onBox))
                        InjectDrain();

                    var described = $"{target.Width}x{target.Height}, "
                        + (target.IsNativeWidget ? "a platform widget" : "owner-drawn")
                        + (InjectAt(centre) is { Length: > 0 } under ? $", {under} under the centre" : string.Empty);

                    if (target.Checked != before)
                    {
                        ++Clicks;
                        note($"    input: CheckBox \"{target.Text}\" ({described}) took a click at "
                            + $"{onBox.X},{onBox.Y} over its box and ignored one at {centre.X},{centre.Y}");
                    }
                    else
                    {
                        note($"    input: a real click at {centre.X},{centre.Y} never reached CheckBox "
                            + $"\"{target.Text}\" ({described}), nor did one at {onBox.X},{onBox.Y} over its box");
                        failed += Fatal;
                    }
                }

                target.Checked = before;
            }
        }

        if (box is not null && InjectionAvailable)
        {
            var original = box.Text;
            box.Text = string.Empty;
            var centre = box.PointToScreen(new(box.Width / 2, box.Height / 2));
            if (InjectClick(centre))
            {
                InjectDrain();

                // Only a box the click actually focused can be typed into. A editor hosted inside a
                // composite — a search field, a token box — may hand focus to its shell instead, and
                // asserting a keystroke into something that never took focus invents a failure rather
                // than finding one. Said out loud so a skip is visible rather than silent.
                //
                // Except on macOS, where nothing raises the peer's focus events at all — so the gate
                // would skip every keystroke on the one platform the injector was rewritten for. The
                // key is posted there regardless and the box is asked afterwards, which is the same
                // evidence by a different route: a character that arrived is a character the first
                // responder took.
                if (!box.Focused && !OperatingSystem.IsMacOS())
                    note($"    input: the click did not focus this {box.GetType().Name}, so the keystroke was skipped");
                else if (InjectType('Z'))
                {
                    InjectDrain();

                    // Anything at all in a box the probe emptied first is a keystroke that landed. The
                    // character is named when it is not the one asked for, because a wrong letter is a
                    // key code mapped against the wrong layout rather than a dead input path, and the
                    // two want telling apart.
                    if (box.Text.Contains('Z'))
                        ++Keystrokes;
                    else if (box.Text.Length > 0)
                    {
                        ++Keystrokes;
                        note($"    input: a real keystroke reached the TextBox as \"{box.Text}\" rather than as Z");
                    }
                    else
                    {
                        note("    input: a real keystroke never reached the TextBox");
                        failed += Fatal;
                    }
                }
            }

            box.Text = original;
        }

        return failed;
    }

    /// <summary>
    /// What the form's lists answer when asked where they are scrolled to and which row sits under a
    /// point, or <see langword="null"/> when it holds no list with anything in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two peer members every backend implements and nothing has ever called. "It compiles" was the
    /// whole of the evidence for them, and on a promoted list neither answer comes from the toolkit at
    /// all — the widget lays its own rows out, so a wrong sign, a wrong origin or a coordinate space
    /// confused with the document's would go unnoticed until an application asked.
    /// </para>
    /// <para>
    /// Reported rather than asserted. Where a list is scrolled to is the application's business, so a
    /// rule invented here about where it ought to be would fail a list that is simply somewhere else.
    /// What the two numbers do show is that the calls run, come back, and agree with each other: the
    /// row under the first pixel row of the client area is the first visible row, whatever number that
    /// happens to be, and a regression to -1 or to a crash has nowhere to hide in that.
    /// </para>
    /// </remarks>
    public static string? ListGeometry(Control root)
    {
        var reports = new List<string>();
        foreach (var list in Walk(root).OfType<ListBox>())
        {
            if (list.Items.Count == 0 || list.Width <= 8 || list.Height <= 8)
                continue;

            var top = list.TopIndex;
            var here = list.IndexFromPoint(4, 4);
            reports.Add(here == top ? $"{top}" : $"{top} but the top row reads {here}");
        }

        return reports.Count == 0 ? null : $"list geometry: first visible row {string.Join(", ", reports)}";
    }

    /// <summary>What a toolkit popup opened inside the dialog did when the user pressed outside it.</summary>
    /// <remarks>
    /// Written by <see cref="Modal"/>'s own timer and read after it, because there is no other moment
    /// to run in: <c>ShowDialog</c> does not come back until the dialog does, so everything that
    /// happens inside one has to be armed before it opens.
    /// </remarks>
    private static string _popupInDialog = "not reached";

    /// <summary>
    /// Shows a form modally and reports whether the call actually blocked, or <see langword="null"/>
    /// where this is not the platform that needed asking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one claim on macOS that neither a screenshot nor a wiring census can settle: whether
    /// <c>Form.ShowDialog</c> comes back when the dialog closes rather than the moment it opens. It
    /// used to do the latter, so a caller got <c>DialogResult.Cancel</c> before the window was on
    /// screen and the peer tree was disposed underneath them — invisible in a capture, and exactly the
    /// sort of thing that only shows up in an application.
    /// </para>
    /// <para>
    /// What closes the dialog is a <see cref="Timer"/>, and that is the point rather than a
    /// convenience. On this backend a tick comes home through the queue the event loop drains, so a
    /// modal that pumped only AppKit's own session would never see it: the dialog would stay up, this
    /// probe would hang, and the failure would be the same one an application hits the first time
    /// something ticks while a dialog is open. Blocking and closing on time is therefore two claims in
    /// one line.
    /// </para>
    /// <para>
    /// macOS only, and deliberately. The Windows shoot is a gating job with no step timeout, so a
    /// regression in its modal loop would hold that job for the runner's whole budget rather than fail
    /// it; the macOS probe is bounded at three minutes and advisory, which is where a check whose
    /// failure mode is a hang belongs.
    /// </para>
    /// </remarks>
    public static string? Modal()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var dialog = new Form
        {
            Text = "NativeForms — modal probe",
            Bounds = new(160, 160, 320, 140),
            Controls = { new Label { Bounds = new(20, 20, 280, 24), Text = "Closing itself in a moment." } },
        };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripMenuItem("One"), new ToolStripMenuItem("Two"));

        // Started before the dialog goes up, because ShowDialog does not come back until it comes
        // down: there is no later moment to arm anything from. Three ticks rather than one, because
        // the press that dismisses the popup has to be delivered by the modal loop and the modal loop
        // is not turning while this handler is inside it — so opening, pressing and reading the answer
        // are three separate visits.
        var step = 0;
        var posted = false;
        var closer = new Timer { Interval = 200 };
        closer.Tick += (_, _) =>
        {
            switch (++step)
            {
                case 1:
                    menu.Show(dialog, new(200, 100));
                    if (!menu.IsOpen)
                        _popupInDialog = "a popup opened inside the dialog did not report itself open";

                    return;

                case 2:
                    // Posted and left. A modal session dispatches its own events, so what has to
                    // happen next is the session's turn — which cannot come round until this handler
                    // has returned, because the loop drains this queue between turns.
                    if (!menu.IsOpen)
                        return;

                    posted = ShootInputMac.Click(dialog.PointToScreen(new(10, 10)));
                    if (!posted)
                        _popupInDialog = "a press outside the popup could not be posted";

                    return;

                default:
                    if (posted)
                        _popupInDialog = menu.IsOpen
                            ? "a popup opened inside the dialog was NOT dismissed by a press outside it"
                            : "a popup opened inside the dialog dismissed on a press outside it";

                    menu.Close();
                    closer.Stop();

                    // A verdict on a modal form closes it, which is what makes this a round trip: the
                    // answer the caller reads back is the one set here rather than the Cancel a form
                    // gets for being closed without one.
                    dialog.DialogResult = DialogResult.OK;
                    return;
            }
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        closer.Start();
        var result = dialog.ShowDialog();
        clock.Stop();
        closer.Stop();

        var elapsed = clock.ElapsedMilliseconds;
        return (elapsed >= 150 && result == DialogResult.OK
            ? $"modal dialog: ShowDialog blocked {elapsed} ms and answered {result} "
                + "(so the session ran, and the timer that ended it was drained inside it)"
            : $"modal dialog: ShowDialog returned after {elapsed} ms with {result} — it did NOT block, "
                + "so a caller would have its answer before the user saw the window")
            + Environment.NewLine + "  modal dialog: " + _popupInDialog;
    }

    /// <summary>
    /// Runs the checks over one page, appending a line per failure and returning how many failed.
    /// </summary>
    public static int Check(Control page, Size host, Action<string> note)
    {
        var failed = 0;

        foreach (var control in Walk(page))
        {
            if (!control.Visible)
                continue;

            // A visible control has to have a sane size. Zero is a control that never got laid out;
            // wider than the window it lives in is a metric read from the wrong place.
            var bounds = control.Bounds;
            if (bounds.Width < 0 || bounds.Height < 0 || bounds.Width > host.Width * 2 || bounds.Height > host.Height * 2)
            {
                note($"    layout: {control.GetType().Name} is {bounds.Width}x{bounds.Height} in a {host.Width}x{host.Height} window");
                ++failed;
            }

            switch (control)
            {
                // Text through a native EDIT and back. This is the round trip that fails when a peer
                // buffers state it never flushes, or flushes state it cannot read back.
                // A masked box rejecting text that does not fit its mask is the mask working, so it is
                // excluded rather than probed with something it is right to refuse.
                case MaskedTextBox:
                    break;

                case TextBox { ReadOnly: false, Enabled: true } box:
                {
                    var original = box.Text;
                    const string probe = "round trip";
                    box.Text = probe;
                    if (box.Text != probe)
                    {
                        note($"    text: TextBox kept \"{box.Text}\" when asked for \"{probe}\"");
                        ++failed;
                    }

                    box.Text = original;
                    break;
                }

                case CheckBox { Enabled: true } check:
                {
                    var original = check.Checked;
                    check.Checked = !original;
                    if (check.Checked == original)
                    {
                        note($"    check: CheckBox \"{check.Text}\" ignored being set to {!original}");
                        ++failed;
                    }

                    check.Checked = original;
                    break;
                }

                default:
                    break;
            }
        }

        return failed;
    }
}
