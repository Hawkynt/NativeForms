using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Layout page: a flow layout panel wrapping a dozen buttons, a 3×3 table layout panel with
    /// mixed column/row styles, spans and visible cell borders, a splitter with two labelled panels,
    /// a collapsible expander, and an auto-scrolling panel whose content is larger than its frame.
    /// </summary>
    private TabPage BuildLayoutPage()
    {
        var page = new TabPage("Layout") { ImageIndex = _IconPurple };

        // --- Column 1: flow, table, splitter ----------------------------------------------------

        var flow = new FlowLayoutPanel
        {
            Bounds = new(16, 36, 460, 110),
            BorderStyle = BorderStyle.FixedSingle,
        };
        for (var i = 1; i <= 12; ++i)
        {
            // Wide enough for the two-digit captions once the GtkButton's own frame and padding are
            // paid for: "Flow 10" in the original 64 px box came out as "Fl…".
            var button = new Button { Size = new(92, 26), Margin = new(4), Text = $"Flow {i}" };
            flow.Controls.Add(button);
        }

        var table = new TableLayoutPanel
        {
            Bounds = new(16, 182, 460, 170),
            ColumnCount = 3,
            RowCount = 3,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
        };
        // 150 rather than 120: the RowSpan button lives in this column and a GtkButton needs its
        // caption plus its own frame, which 120 px did not cover ("RowSpan …").
        table.ColumnStyles.Add(new(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new(SizeType.Percent, 40));
        table.ColumnStyles.Add(new(SizeType.Percent, 60));
        table.RowStyles.Add(new(SizeType.Absolute, 40));
        table.RowStyles.Add(new(SizeType.Percent, 50));
        table.RowStyles.Add(new(SizeType.Percent, 50));
        var spanWide = new Button { Text = "ColumnSpan = 2", Margin = new(2) };
        var spanTall = new Button { Text = "RowSpan = 2", Margin = new(2) };
        table.Controls.Add(new Label { Text = "150 px", Margin = new(2), TextAlign = ContentAlignment.MiddleCenter });
        table.Controls.Add(spanWide);
        table.Controls.Add(spanTall);
        table.Controls.Add(new Button { Text = "40 %", Margin = new(2) });
        table.Controls.Add(new Button { Text = "60 %", Margin = new(2) });
        table.Controls.Add(new Label { Text = "bottom row", Margin = new(2), TextAlign = ContentAlignment.MiddleCenter });
        table.SetCellPosition(spanWide, 1, 0);
        table.SetColumnSpan(spanWide, 2);
        table.SetCellPosition(spanTall, 0, 1);
        table.SetRowSpan(spanTall, 2);

        var split = new SplitContainer
        {
            Bounds = new(16, 388, 460, 130),
            SplitterDistance = 180,
        };
        split.Panel1.BorderStyle = BorderStyle.FixedSingle;
        split.Panel2.BorderStyle = BorderStyle.FixedSingle;
        split.Panel1.Controls.Add(new Label { Bounds = new(8, 8, 160, 20), Text = "Panel1 (180 px)" });
        split.Panel2.Controls.Add(new Label { Bounds = new(8, 8, 170, 20), Text = "Panel2 (drag the bar)" });
        split.SplitterMoved += (_, _) => this.SetStatus($"SplitContainer: splitter at {split.SplitterDistance} px.");

        page.Controls.AddRange(
            Caption("FlowLayoutPanel (12 wrapping buttons)", 16, 12),
            flow,
            Caption("TableLayoutPanel (3×3, styles + spans + borders)", 16, 158, 460),
            table,
            Caption("SplitContainer", 16, 364),
            split);

        // --- Column 2: expander and auto-scroll panel -------------------------------------------

        var expander = new Expander
        {
            Bounds = new(500, 36, 300, 150),
            Text = "Connection details",
            Image = this.SquareImage(Color.MediumSeaGreen), // a header icon beside the caption
        };
        expander.Controls.Add(new Label { Bounds = new(12, 36, 80, 20), Text = "Host:" });
        expander.Controls.Add(new TextBox { Bounds = new(96, 34, 190, 24), Text = "localhost" });
        expander.Controls.Add(new Label { Bounds = new(12, 66, 80, 20), Text = "Port:" });
        expander.Controls.Add(new TextBox { Bounds = new(96, 64, 190, 24), Text = "8080" });
        expander.Controls.Add(new CheckBox { Bounds = new(12, 96, 270, 20), Text = "Use TLS", Checked = true });
        expander.ExpandedChanged += (_, _)
            => this.SetStatus($"Expander {(expander.Expanded ? "expanded" : "collapsed")} — click its header to toggle.");

        var scrollPanel = new Panel
        {
            Bounds = new(500, 222, 300, 180),
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
        };
        for (var row = 0; row < 5; ++row)
        for (var column = 0; column < 4; ++column)
            scrollPanel.Controls.Add(new Button
            {
                Bounds = new(8 + (column * 116), 8 + (row * 40), 108, 32),
                Text = $"Cell {row},{column}",
            });

        page.Controls.AddRange(
            Caption("Expander (click the header)", 500, 12),
            expander,
            Caption("Panel (AutoScroll, 464×200 content)", 500, 198),
            scrollPanel);

        // --- Column 3: tab-strip alignment ------------------------------------------------------

        TabControl AlignedTabs(TabAlignment alignment, int y, bool closable = false)
        {
            var tc = new TabControl { Bounds = new(812, y, 172, 104), Alignment = alignment, ShowCloseButtons = closable };
            tc.TabPages.AddRange(new TabPage("One"), new TabPage("Two"), new TabPage("Tri"));
            tc.TabPages[0].Controls.Add(new Label { Bounds = new(10, 10, 110, 20), Text = $"{alignment} strip" });
            if (closable)
                tc.TabClosed += (_, e) => this.SetStatus($"TabControl: closed tab \"{e.Page.Text}\".");

            return tc;
        }

        page.Controls.AddRange(
            Caption("TabControl.Alignment", 812, 12, 172),
            AlignedTabs(TabAlignment.Top, 36, closable: true),
            AlignedTabs(TabAlignment.Bottom, 152),
            AlignedTabs(TabAlignment.Left, 268),
            AlignedTabs(TabAlignment.Right, 384));

        // Snapshotted the instant each value was authored, so the restore cannot drift away from it.
        var splitterDistance = split.SplitterDistance;
        var expanderOpen = expander.Expanded;
        this.OnReset(() =>
        {
            split.SplitterDistance = splitterDistance;
            expander.Expanded = expanderOpen;
            scrollPanel.AutoScrollPosition = Point.Empty;
        });

        this.Publish("layout.page", page);
        this.Publish("layout.flow", flow);
        this.Publish("layout.table", table);
        this.Publish("layout.split", split);
        this.Publish("layout.expander", expander);
        this.Publish("layout.scrollPanel", scrollPanel);

        return page;
    }
}
