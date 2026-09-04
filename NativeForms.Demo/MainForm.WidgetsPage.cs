using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using System.Linq;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm {
  /// <summary>The Widgets page: the app-shell controls (§7.10) — segmented control, range slider, token
  /// box, info bar, navigation view, property grid and a zoomable canvas.</summary>
  private TabPage BuildWidgetsPage() {
    var page = new TabPage("Widgets") { ImageIndex = _IconBlue };

    var segmented = new SegmentedControl { Bounds = new(16, 36, 300, 28) };
    segmented.SetSegments("Day", "Week", "Month");
    segmented.SelectedIndexChanged += (_, _) => this.SetStatus($"SegmentedControl: {segmented.SelectedSegment}.");

    var range = new RangeSlider { Bounds = new(16, 100, 300, 26), Minimum = 0, Maximum = 100, LowerValue = 25, UpperValue = 75 };
    range.RangeChanged += (_, _) => this.SetStatus($"RangeSlider: {range.LowerValue}–{range.UpperValue}.");

    var info = new InfoBar {
      Bounds = new(16, 164, 500, 40),
      Severity = InfoBarSeverity.Warning,
      Title = "Heads up",
      Message = "Unsaved changes will be lost.",
      ActionText = "Save",
    };
    info.ActionClicked += (_, _) => this.SetStatus("InfoBar: action clicked.");
    info.Closed += (_, _) => this.SetStatus("InfoBar: dismissed.");

    var toastButton = new Button { Bounds = new(16, 220, 160, 30), Text = "Show a toast" };
    var toastCount = 0;
    toastButton.Click += (_, _) => {
      var severity = (InfoBarSeverity)(toastCount % 4);
      Toast.Show(this, $"Toast #{++toastCount}", $"A {severity} notification.", severity, 4000);
    };

    var tokens = new TokenBox { Bounds = new(16, 300, 500, 60), PlaceholderText = "Add a tag…" };
    tokens.AddToken("design");
    tokens.AddToken("urgent");
    tokens.AutoCompleteSource = prefix => new[] { "backend", "bug", "design", "docs", "feature", "urgent" }
        .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    tokens.ChipStyleProvider = t => t switch {
      "urgent" => new TokenChipStyle { BackColor = Color.FromArgb(0xFF, 0xE8, 0x11, 0x23), ForeColor = Color.White, FontStyle = FontStyle.Bold },
      "bug" => new TokenChipStyle { BackColor = Color.FromArgb(0xFF, 0xE8, 0x8A, 0x00), ForeColor = Color.Black, FontStyle = FontStyle.Italic },
      _ => default,
    };
    tokens.TokensChanged += (_, _) => this.SetStatus($"TokenBox: {tokens.Tokens.Count} tag(s).");

    var virtualList = new ListView { Bounds = new(16, 400, 500, 205), View = ListViewView.Details, VirtualMode = true, VirtualListSize = 1_000_000 };
    virtualList.Columns.Add(new ColumnHeader { Text = "#", Width = 90 });
    virtualList.Columns.Add(new ColumnHeader { Text = "Generated row", Width = 380 });
    virtualList.RetrieveVirtualItem += (_, e) =>
        e.Item = new ListViewItem(e.ItemIndex.ToString("N0"), $"Row {e.ItemIndex:N0} — served on demand");
    virtualList.SelectedIndexChanged += (_, _) => this.SetStatus($"Virtual ListView: row {virtualList.SelectedIndex:N0} of 1,000,000.");

    var nav = new NavigationView { Bounds = new(560, 36, 170, 240), ImageList = _icons };
    nav.AddItem("Home", _IconBlue);
    nav.AddItem("Files", _IconFolder);
    nav.AddItem("Settings", _IconOpen);
    var navContent = new Label { Bounds = new(742, 44, 260, 22), Text = "NavigationView content: Home" };
    nav.SelectedIndexChanged += (_, _) => navContent.Text = $"NavigationView content: {nav.Items[nav.SelectedIndex]}";

    var zoom = new ZoomPanel { Bounds = new(560, 300, 440, 268), ShowRulers = true, GridSize = 16 };
    zoom.Image = _backend.CreateImage(320, 200, GradientPixels(320, 200, Color.RoyalBlue, Color.Orange));
    zoom.ZoomChanged += (_, _) => this.SetStatus($"ZoomPanel: {zoom.Zoom * 100:F0}%.");
    var fitButton = new Button { Bounds = new(560, 576, 90, 26), Text = "Fit" };
    fitButton.Click += (_, _) => zoom.FitToWindow();
    var actualButton = new Button { Bounds = new(658, 576, 90, 26), Text = "100%" };
    actualButton.Click += (_, _) => zoom.ActualSize();

    page.Controls.AddRange(
        Caption("SegmentedControl (exclusive toggle group)", 16, 12, 400),
        segmented,
        Caption("RangeSlider (two-thumb range)", 16, 76, 360),
        range,
        Caption("InfoBar (inline banner · severity · action · dismiss)", 16, 140, 500),
        info,
        toastButton,
        Caption("TokenBox (chips · × / Backspace delete · autocomplete)", 16, 276, 500),
        tokens,
        Caption("Virtual ListView (1,000,000 rows served on demand)", 16, 376, 500),
        virtualList,
        Caption("NavigationView (side rail · hamburger collapses to icons)", 560, 12, 420),
        nav,
        navContent,
        Caption("ZoomPanel (wheel-zoom · drag-pan · rulers · fit/actual)", 560, 276, 440),
        zoom,
        fitButton,
        actualButton);

    return page;
  }
}
