using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>The Widgets page: the app-shell controls (§7.10) — segmented control, range slider, token
    /// box, info bar, navigation view, property grid and a zoomable canvas.</summary>
    private TabPage BuildWidgetsPage()
    {
        var page = new TabPage("Widgets") { ImageIndex = _IconBlue };

        var segmented = new SegmentedControl { Bounds = new(16, 36, 300, 28) };
        segmented.SetSegments("Day", "Week", "Month");
        segmented.SelectedIndexChanged += (_, _) => this.SetStatus($"SegmentedControl: {segmented.SelectedSegment}.");

        page.Controls.AddRange(
            Caption("SegmentedControl (mutually-exclusive toggle group)", 16, 12, 360),
            segmented);

        return page;
    }
}
