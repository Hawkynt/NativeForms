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

        var range = new RangeSlider { Bounds = new(16, 100, 300, 26), Minimum = 0, Maximum = 100, LowerValue = 25, UpperValue = 75 };
        range.RangeChanged += (_, _) => this.SetStatus($"RangeSlider: {range.LowerValue}–{range.UpperValue}.");

        page.Controls.AddRange(
            Caption("SegmentedControl (mutually-exclusive toggle group)", 16, 12, 360),
            segmented,
            Caption("RangeSlider (two-thumb range)", 16, 76, 360),
            range);

        return page;
    }
}
