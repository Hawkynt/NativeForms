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

        var info = new InfoBar
        {
            Bounds = new(16, 164, 500, 40),
            Severity = InfoBarSeverity.Warning,
            Title = "Heads up",
            Message = "Unsaved changes will be lost.",
            ActionText = "Save",
        };
        info.ActionClicked += (_, _) => this.SetStatus("InfoBar: action clicked.");
        info.Closed += (_, _) => this.SetStatus("InfoBar: dismissed.");

        var toastButton = new Button { Bounds = new(16, 220, 160, 30), Text = "Show a toast" };
        toastButton.Click += (_, _) => Toast.Show(this, "Saved", "Your changes are saved.", InfoBarSeverity.Success);

        page.Controls.AddRange(
            Caption("SegmentedControl (mutually-exclusive toggle group)", 16, 12, 360),
            segmented,
            Caption("RangeSlider (two-thumb range)", 16, 76, 360),
            range,
            Caption("InfoBar (inline banner · severity · action · dismiss)", 16, 140, 500),
            info,
            toastButton);

        return page;
    }
}
