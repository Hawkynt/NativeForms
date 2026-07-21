using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The geometric half of the walkthrough: instead of driving a gesture and asserting a property, it
/// walks the realized control tree of every page and measures it, reporting anything a person would
/// call visually wrong — a child hanging out of its parent, two siblings on top of each other, a
/// caption wider than the room it was given.
/// </summary>
/// <remarks>
/// This exists because behavioral checks are blind to it. A caption that GTK elides to "Fl…" still
/// reports its full <see cref="Control.Text"/>, a button pushed past the page's edge is still
/// clickable through <see cref="Control.PointToScreen"/>, and both pass every assertion the script
/// makes — the defect only exists in pixels. Measuring the layout arithmetically finds those far
/// more reliably than looking at a screenshot does, and unlike a screenshot it says which control,
/// by how many pixels, in numbers that go straight into a fix.
/// </remarks>
internal sealed partial class Autopilot
{
    /// <summary>The gap a caption may overrun its box by before it counts as truncated. Text
    /// measurement and the native widget's own layout disagree by a pixel or two, and flagging that
    /// would bury the real findings in noise.</summary>
    private const int _TextSlack = 2;

    /// <summary>Runs the geometric audit over the form chrome and every page.</summary>
    private void RunAudit()
    {
        Section("Layout audit");
        var tabs = _form.Part<TabControl>("chrome.tabs");
        var count = this.Read(() => tabs.TabPages.Count);

        this.Check("Layout audit: the form chrome fits the window", () =>
        {
            var findings = new List<string>();
            this.Pump("auditing the chrome", () =>
            {
                foreach (var child in Children(_form))
                    if (child is not TabControl)
                        Inspect(_form, child, findings);
            });

            Report(findings);
        });

        for (var i = 0; i < count; ++i)
        {
            var index = i;
            this.SelectTab(index);
            this.Settle(80);
            var title = this.Read(() => tabs.TabPages[index].Text);
            this.Check($"Layout audit: the {title} page is laid out inside its frame", () =>
            {
                var findings = new List<string>();
                this.Pump("auditing a page", () => AuditContainer(tabs.TabPages[index], findings));
                Report(findings);
            });
        }
    }

    /// <summary>Files every finding against the running check, so one page reports all of its
    /// problems at once rather than stopping at the first.</summary>
    private void Report(List<string> findings)
    {
        foreach (var finding in findings)
            this.Fail(finding);
    }

    // --- The walk --------------------------------------------------------------------------------

    /// <summary>Audits one container and then recurses into each of its visible children.</summary>
    private static void AuditContainer(Control container, List<string> findings)
    {
        var children = Children(container);
        if (children.Count == 0)
            return;

        foreach (var child in children)
            Inspect(container, child, findings);

        // Overlap is a property of a pair, so it is checked once per container rather than per child.
        // A control laid out on top of a sibling hides it completely and no behavioral check notices,
        // because the buried control still answers every property the script asks it for.
        if (!ManagesItsOwnLayout(container))
            for (var i = 0; i < children.Count; ++i)
            for (var j = i + 1; j < children.Count; ++j)
            {
                var a = children[i];
                var b = children[j];
                var overlap = Rectangle.Intersect(a.Bounds, b.Bounds);
                if (overlap is { Width: > 0, Height: > 0 })
                    findings.Add(
                        $"overlap: {Name(a)} {a.Bounds} and {Name(b)} {b.Bounds} share {overlap}");
            }

        foreach (var child in children)
            AuditContainer(child, findings);
    }

    /// <summary>Checks one child against its parent: does it fit, and does its caption fit inside it.</summary>
    private static void Inspect(Control parent, Control child, List<string> findings)
    {
        // A scrolling viewport is *supposed* to hold content larger than itself — that is what makes
        // it scroll — so overhang there is the feature, not the defect.
        if (!Scrolls(parent))
        {
            var display = parent.DisplayRectangle;
            var bounds = child.Bounds;
            if (!display.Contains(bounds))
                findings.Add(
                    $"out of frame: {Name(child)} {bounds} escapes {Name(parent)}'s client area {display}"
                    + $" (overhang left {Math.Max(0, display.Left - bounds.Left)},"
                    + $" top {Math.Max(0, display.Top - bounds.Top)},"
                    + $" right {Math.Max(0, bounds.Right - display.Right)},"
                    + $" bottom {Math.Max(0, bounds.Bottom - display.Bottom)} px)");
        }

        CheckCaption(child, findings);
    }

    /// <summary>Measures a control's caption against the room its own box leaves for it.</summary>
    private static void CheckCaption(Control control, List<string> findings)
    {
        var text = control.Text;
        if (text.Length == 0 || !WearsItsCaption(control))
            return;

        var available = control.DisplayRectangle.Width - ReservedWidth(control);
        var needed = BackendRegistry.Resolve().MeasureText(text, control.Font).Width;
        if (needed > available + _TextSlack)
            findings.Add(
                $"truncated caption: {Name(control)} needs {needed} px for \"{text}\""
                + $" but its {control.Bounds.Width} px box leaves {available} px"
                + $" — short by {needed - available} px");
    }

    /// <summary>
    /// The horizontal strip of a control's client area that its own furniture claims before any
    /// caption is drawn — the check square, the radio ring, the switch track, the spinner or
    /// drop-down button. The numbers mirror what each control's <c>OnPaint</c> actually reserves.
    /// </summary>
    private static int ReservedWidth(Control control) => control switch
    {
        // GlyphRenderer.CheckBoxSize + CheckBox's text gap, and the image when one is set.
        CheckBox box => 14 + 6 + (box.Image is { } image ? image.Width + 6 : 0),

        // RadioButton's circle plus its text gap.
        RadioButton => 14 + 6,

        // ToggleSwitch.TrackWidth plus its text gap.
        ToggleSwitch => 36 + 6,

        // The arrow zone ComboBox and the up-down spinners keep at their right edge.
        ComboBox or UpDownBase => BackendRegistry.Resolve().Theme.ScrollBarSize + 1,

        // A native GtkButton's own frame and padding. Measured rather than assumed: a 76 px button
        // still elided "Flow 10", which the text engine measures at 53 px, so the chrome costs more
        // than 23 px — 34 is the figure at which the audit's verdict matches what the button
        // actually renders across the gallery.
        Button => 34,

        // A GroupBox writes its caption into the frame's top edge, inset from the corner.
        GroupBox => 16,

        _ => 0,
    };

    // --- Classification --------------------------------------------------------------------------

    /// <summary>Whether a control paints its <see cref="Control.Text"/> inside its own box, which is
    /// what makes a too-small box show up as an elided caption.</summary>
    private static bool WearsItsCaption(Control control) => control
        is Label or Button or CheckBox or RadioButton or ToggleSwitch or LinkLabel or GroupBox;

    /// <summary>Whether a container may legitimately hold children outside its visible frame.</summary>
    private static bool Scrolls(Control control) => control is Panel { AutoScroll: true };

    /// <summary>Whether a container places its own children, in which case their positions are the
    /// layout engine's business and a "wrong" one is a different bug than a hand-placed overlap.</summary>
    private static bool ManagesItsOwnLayout(Control control)
        => control is FlowLayoutPanel or TableLayoutPanel or SplitContainer or TabControl or Accordion or Ribbon;

    /// <summary>The visible children of a control, or an empty list when it hosts none.</summary>
    private static List<Control> Children(Control control)
    {
        var result = new List<Control>();
        var children = control.Controls;
        for (var i = 0; i < children.Count; ++i)
            if (children[i].Visible)
                result.Add(children[i]);

        return result;
    }

    /// <summary>A control's type and caption, enough to find it in the demo source.</summary>
    private static string Name(Control control)
    {
        var type = control.GetType().Name;
        var text = control.Text;
        return text.Length == 0 ? type : $"{type}(\"{Shorten(text)}\")";
    }

    /// <summary>Clips a caption so one long multiline body cannot swamp the report.</summary>
    private static string Shorten(string text)
    {
        var line = text.AsSpan();
        var breakAt = line.IndexOfAny('\r', '\n');
        if (breakAt >= 0)
            line = line[..breakAt];

        return line.Length <= 40 ? line.ToString() : string.Concat(line[..40], "…");
    }
}
