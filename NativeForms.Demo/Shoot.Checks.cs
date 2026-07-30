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

    /// <summary>Pointer moves that were injected through the OS and observed arriving.</summary>
    public static int Hovers { get; private set; }

    /// <summary>Editors an injected click moved the keyboard onto, as the toolkit reported it.</summary>
    /// <remarks>
    /// A separate count from <see cref="Clicks"/> because it is a separate claim, and on macOS it was
    /// a false one until now: no peer there raised the peer-level focus events at all, so
    /// <c>Control.Focused</c> was false however the keyboard had actually moved and everything in the
    /// toolkit that reasons about focus — a spin box committing its edit, a form's
    /// <c>ActiveControl</c> — was reasoning from it.
    /// </remarks>
    public static int Focuses { get; private set; }

    /// <summary>Whether the run has already said why a posted move reaches nothing.</summary>
    /// <remarks>Once is a finding; sixteen times is a log that buries the fifteen lines around it.</remarks>
    private static bool _moveReported;

    /// <summary>Whether the run has already said why a click focused nothing the toolkit heard about.</summary>
    /// <inheritdoc cref="_moveReported"/>
    private static bool _focusReported;

    /// <summary>
    /// What the key table made of two named keys posted at a focused editor, or
    /// <see langword="null"/> where the run never got to ask.
    /// </summary>
    /// <remarks>
    /// The seam this covers had never been exercised. A Mac numbers its keys by where they are, so
    /// <c>CocoaCanvasPeer.KeyOf</c> reads the named ones off the key code and everything else off what
    /// the key types — and the toolkit only gets to name a key at all because the backend's own loop
    /// stands ahead of the editor. The probe used to drain the queue itself, dispatching straight to
    /// AppKit, so every keystroke it counted had gone round that code rather than through it.
    /// </remarks>
    public static string? KeyTable { get; private set; }

    /// <summary>
    /// Posts two named keys at a focused editor and reports what the toolkit called them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Down and Home rather than Tab or Escape, which are the obvious two and are both spoken for: the
    /// form's dialog-key chain takes a Tab for focus navigation and an Escape for the cancel button
    /// before either reaches <c>Control.KeyDown</c>, so a check watching there would see nothing and be
    /// unable to say whether the table or the chain was the reason. Nothing consumes an arrow or Home,
    /// and both are named off the key code, which is the half of <c>KeyOf</c> that a letter never
    /// exercises.
    /// </para>
    /// <para>
    /// The character each key types is handed over as well as its position, because that is what a real
    /// event carries — AppKit spells an arrow with a code out of the Unicode private-use block — and the
    /// keys are marked handled, which is the other half of the seam's promise: a key the toolkit
    /// consumed is one the native editor never sees, so the caret does not move and nothing is typed.
    /// </para>
    /// </remarks>
    private static string NameKeys(TextBox box)
    {
        // NSDownArrowFunctionKey and NSHomeFunctionKey, with the key codes those keys have here.
        (ushort Code, char Types, Keys Expected)[] keys =
        [
            (0x7D, '', Keys.Down),
            (0x73, '', Keys.Home),
        ];

        var reports = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            var arrived = Keys.None;
            var count = 0;
            EventHandler<KeyEventArgs> watch = (_, e) =>
            {
                arrived = e.KeyCode;
                ++count;
                e.Handled = true;
            };

            box.KeyDown += watch;
            var posted = ShootInputMac.Press(key.Code, key.Types);
            if (posted)
                InjectDrain();

            box.KeyDown -= watch;

            reports.Add(!posted
                ? $"key code 0x{key.Code:X2} could not be posted"
                : count == 0
                    ? $"key code 0x{key.Code:X2} reached the box as nothing (expected Keys.{key.Expected})"
                    : arrived == key.Expected
                        ? $"key code 0x{key.Code:X2} arrived as Keys.{arrived}"
                        : $"key code 0x{key.Code:X2} arrived as Keys.{arrived}, NOT Keys.{key.Expected}");
        }

        return "key table: " + string.Join(", ", reports)
            + " — posted through the backend's own interception, which is the only place on this "
            + "platform that names a key before the native editor acts on it";
    }

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

            // Over the box glyph, not over the middle of the control. A check box laid out wider than
            // its caption needs carries empty space to the right of it, and an NSButton in its switch
            // type is sensitive over the box and the title and nowhere else in the frame — so the three
            // wide boxes in this walkthrough reported nothing arriving on every run while the two sized
            // to their captions worked. A BS_AUTOCHECKBOX and a GtkCheckButton take a press anywhere in
            // the rectangle, and so does the owner-drawn twin, so the box glyph is the one point all
            // four agree on, and it is where a person clicks anyway.
            var at = target.PointToScreen(new(Math.Min(8, target.Width / 2), target.Height / 2));
            if (InjectClick(at))
            {
                InjectDrain();
                if (target.Checked != before)
                    ++Clicks;
                else
                {
                    // What is under the point, said rather than guessed at: a control that does not
                    // toggle reads the same whether the press landed on another widget, on no widget, or
                    // on the right one somewhere it declines to answer.
                    note($"    input: a real click at {at.X},{at.Y} never reached CheckBox \"{target.Text}\" "
                        + $"({target.Width}x{target.Height}, "
                        + (target.IsNativeWidget ? "a platform widget" : "owner-drawn")
                        + (InjectAt(at) is { Length: > 0 } under ? $", {under} under the point" : string.Empty)
                        + ")");
                    failed += Fatal;
                }

                target.Checked = before;
            }
        }

        if (box is not null && InjectionAvailable)
        {
            var original = box.Text;
            box.Text = string.Empty;
            var centre = box.PointToScreen(new(box.Width / 2, box.Height / 2));

            // The event as well as the flag. A control can only be focused because its peer said so,
            // but the flag alone would also be satisfied by a control that was focused before this
            // click — the event is what says the arrival happened now, from this press.
            var arrived = 0;
            EventHandler gained = (_, _) => ++arrived;
            box.GotFocus += gained;

            if (InjectClick(centre))
            {
                InjectDrain();

                if (box.Focused && arrived > 0)
                    ++Focuses;
                else if (OperatingSystem.IsMacOS() && !_focusReported)
                {
                    // Named rather than guessed at, for the reason FirstResponder gives: a field being
                    // typed in answers with the window's borrowed field editor rather than with itself.
                    _focusReported = true;
                    note($"    input: a real click at {centre.X},{centre.Y} left {box.GetType().Name} "
                        + $"reporting Focused={box.Focused} after {arrived} GotFocus event(s), while the "
                        + $"window's first responder is {ShootInputMac.FirstResponder()}");
                }

                // Only a box the click actually focused can be typed into. An editor hosted inside a
                // composite — a search field, a token box — may hand focus to its shell instead, and
                // asserting a keystroke into something that never took focus invents a failure rather
                // than finding one. Said out loud so a skip is visible rather than silent.
                if (!box.Focused)
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

            // Once per run, and only into a box the keyboard is actually on. This is the check the
            // character keystroke above cannot make: a letter that arrives in the editor says the
            // event reached the first responder and nothing at all about how the toolkit named it.
            if (KeyTable is null && OperatingSystem.IsMacOS() && box.Focused)
                KeyTable = NameKeys(box);

            box.GotFocus -= gained;
            box.Text = original;
        }

        // Hover, which on macOS is the one input route this backend wires and nothing has shown
        // arriving. A press says nothing about it — a press carries its own location and the view under
        // it is handed the event whatever the tracking areas think — so a move is posted on its own.
        // The target is an owner-drawn control with no children of its own, which makes it the deepest
        // view at its own centre and so the one AppKit would hand the move to; aiming at the page
        // instead lands on whichever control happens to sit in the middle of it and reports the page
        // hearing nothing, which is correct behaviour read as a failure. macOS only, because this is the
        // gap that is macOS's: the Win32 pointer is already driven end to end by SendInput.
        //
        // Two moves rather than one, from outside the control to its middle, because what a tracking
        // area answers is a crossing: a lone move to a point AppKit already believes the pointer to be
        // at is not one, and entering is the half that lights a highlight up.
        if (OperatingSystem.IsMacOS()
            && Walk(page).OfType<OwnerDrawnControl>()
                .FirstOrDefault(c => !c.IsNativeWidget && c.Controls.Count == 0 && OnScreen(c)) is { } hovered)
        {
            var moved = 0;
            EventHandler<MouseEventArgs> seen = (_, _) => ++moved;
            hovered.MouseMove += seen;
            try
            {
                var outside = hovered.PointToScreen(new(hovered.Width / 2, -8));
                var centre = hovered.PointToScreen(new(hovered.Width / 2, hovered.Height / 2));
                ShootInputMac.Move(outside);
                if (!ShootInputMac.Move(centre))
                    return failed;

                InjectDrain();
                if (moved > 0)
                    ++Hovers;
                else if (!_moveReported)
                {
                    _moveReported = true;

                    // Once, and only when the first attempt found nothing. The question worth an
                    // answer is not "does hover work" but "which half of it did not run", and the two
                    // are told apart by taking the tracking area out of it: a window sends
                    // mouseMoved: to whichever view holds the keyboard as well as to the areas that
                    // asked for it, so a canvas given the keyboard hears the move by AppKit's other
                    // route. Reaching it that way says the toolkit's own plumbing is live and the
                    // tracking area is what a posted event does not drive; reaching it by neither says
                    // no moved event is delivered on this route at all.
                    var before = moved;
                    hovered.Focus();
                    ShootInputMac.Move(centre);
                    InjectDrain();

                    note($"    input: a posted move to {centre.X},{centre.Y} did not reach "
                        + $"{hovered.GetType().Name}, whose {InjectAt(centre)} is the view under the point"
                        + (moved > before
                            ? " — but the same move reached it once the canvas held the keyboard, so the "
                                + "toolkit's own mouse plumbing is live and it is the tracking area that "
                                + "a posted event does not drive"
                            : $" — nor with the canvas holding the keyboard (Focused={hovered.Focused}), "
                                + "so no moved event is delivered on this route at all"));
                }
            }
            finally
            {
                hovered.MouseMove -= seen;
            }
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

    /// <summary>What a press on an item of that popup did — the other half, and the harder one.</summary>
    /// <inheritdoc cref="_popupInDialog"/>
    private static string _itemInDialog = "not reached";

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

        var chosen = 0;
        menu.Items[0].Click += (_, _) => ++chosen;

        // Where the first row is, rather than where the menu roughly is. A drop-down puts a one-pixel
        // border above its first item and gives every item the theme's row height, so the middle of
        // row one is a point the layout defines rather than one this check guesses at — and a press
        // that landed on the border instead would report "the item was not chosen" for a reason that
        // has nothing to do with what is being asked.
        const int menuX = 200;
        const int menuY = 100;
        var row = Hawkynt.NativeForms.Backends.BackendRegistry.Resolve().Theme.RowHeight;
        var onFirstItem = new Point(menuX + 12, menuY + 1 + (row / 2));

        // Started before the dialog goes up, because ShowDialog does not come back until it comes
        // down: there is no later moment to arm anything from. One tick per gesture, because every
        // press has to be delivered by the modal loop and the modal loop is not turning while this
        // handler is inside it — so opening, pressing and reading the answer are separate visits, and
        // there are two presses to make.
        var step = 0;
        var pickPosted = false;
        var outsidePosted = false;
        var closer = new Timer { Interval = 200 };
        closer.Tick += (_, _) =>
        {
            switch (++step)
            {
                case 1:
                    menu.Show(dialog, new(menuX, menuY));
                    if (!menu.IsOpen)
                        _itemInDialog = "a popup opened inside the dialog did not report itself open";

                    return;

                case 2:
                    // Posted and left. A modal session dispatches its own events, so what has to
                    // happen next is the session's turn — which cannot come round until this handler
                    // has returned, because the loop drains this queue between turns.
                    if (!menu.IsOpen)
                        return;

                    pickPosted = ShootInputMac.Click(dialog.PointToScreen(onFirstItem));
                    if (!pickPosted)
                        _itemInDialog = "a press on the popup's first item could not be posted";

                    return;

                case 3:
                    if (pickPosted)
                        _itemInDialog = chosen > 0
                            ? "an item of a popup opened inside the dialog was chosen by a press on it"
                            : "an item of a popup opened inside the dialog was NOT chosen by a press on it "
                                + $"(the menu is {(menu.IsOpen ? "still open" : "closed")})";

                    // The second half needs the surface back: choosing an item closes the cascade.
                    menu.Show(dialog, new(menuX, menuY));
                    if (!menu.IsOpen)
                        _popupInDialog = "the popup would not open a second time";

                    return;

                case 4:
                    if (!menu.IsOpen)
                        return;

                    outsidePosted = ShootInputMac.Click(dialog.PointToScreen(new(10, 10)));
                    if (!outsidePosted)
                        _popupInDialog = "a press outside the popup could not be posted";

                    return;

                default:
                    if (outsidePosted)
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
            + Environment.NewLine + "  modal dialog: " + _itemInDialog
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
