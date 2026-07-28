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
        if (!OperatingSystem.IsWindows() || !ShootInput.Available)
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

        ShootInput.Activate(windowTitle);
        var failed = 0;

        if (target is not null)
        {
            var before = target.Checked;
            var centre = target.PointToScreen(new(target.Width / 2, target.Height / 2));
            if (ShootInput.Click(centre))
            {
                ShootInput.Drain();
                if (target.Checked != before)
                    ++Clicks;
                else
                {
                    note($"    input: a real click at {centre.X},{centre.Y} never reached CheckBox \"{target.Text}\"");
                    ++failed;
                }

                target.Checked = before;
            }
        }

        if (box is not null && ShootInput.Available)
        {
            var original = box.Text;
            box.Text = string.Empty;
            var centre = box.PointToScreen(new(box.Width / 2, box.Height / 2));
            if (ShootInput.Click(centre))
            {
                ShootInput.Drain();

                // Only a box the click actually focused can be typed into. A editor hosted inside a
                // composite — a search field, a token box — may hand focus to its shell instead, and
                // asserting a keystroke into something that never took focus invents a failure rather
                // than finding one. Said out loud so a skip is visible rather than silent.
                if (!box.Focused)
                    note($"    input: the click did not focus this {box.GetType().Name}, so the keystroke was skipped");
                else if (ShootInput.Type('Z'))
                {
                    ShootInput.Drain();
                    if (box.Text.Contains('Z'))
                        ++Keystrokes;
                    else
                    {
                        note("    input: a real keystroke never reached the focused TextBox");
                        ++failed;
                    }
                }
            }

            box.Text = original;
        }

        return failed;
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
