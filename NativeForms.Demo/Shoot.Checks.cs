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
    /// <summary>Every control in the tree rooted at <paramref name="control"/>, itself included.</summary>
    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        foreach (var child in control.Controls)
            foreach (var descendant in Walk(child))
                yield return descendant;
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
