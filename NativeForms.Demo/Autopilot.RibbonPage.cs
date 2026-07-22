using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class Autopilot
{
    /// <summary>
    /// The Ribbon page: tab switching, a command-backed ribbon button, a latching toggle, the group
    /// overflow that folds a group into its drop-down button when the width runs out, the minimized
    /// state — and the accordion's single-expand behaviour, keyboard navigation and child visibility.
    /// </summary>
    private void DriveRibbonPage()
    {
        Section("Ribbon and Accordion");
        this.SelectTab("Ribbon");
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");
        var ribbon = _form.Part<Ribbon>("ribbon.control");
        var accordion = _form.Part<Accordion>("ribbon.accordion");

        // --- Ribbon -----------------------------------------------------------------------------

        this.Check("Ribbon: clicking a tab header switches the shown groups", () =>
        {
            var stripY = this.Read(() => ribbon.TabStripHeight) / 2;
            this.Expect("the tab the page starts on", this.Read(() => ribbon.SelectedIndex), 0);

            // "Home" is the first caption, so a click well past it lands on "Insert".
            this.Click(ribbon, 80, stripY);
            this.Expect("the selected tab after clicking the second header", this.Read(() => ribbon.SelectedIndex), 1);
            this.Expect("the status line", this.Read(() => status.Text), "Ribbon: the Insert tab is showing.");

            this.Click(ribbon, 20, stripY);
            this.Expect("the selected tab after clicking back on the first header", this.Read(() => ribbon.SelectedIndex), 0);
        });

        this.Check("Ribbon: the arrow keys walk the tab strip", () =>
        {
            this.FocusOn(ribbon);
            this.Key(KeySym.Right);
            this.Expect("the selected tab after Right", this.Read(() => ribbon.SelectedIndex), 1);
            this.Key(KeySym.Right);
            this.Expect("the selected tab after a second Right", this.Read(() => ribbon.SelectedIndex), 2);
            this.Key(KeySym.Left);
            this.Expect("the selected tab after Left", this.Read(() => ribbon.SelectedIndex), 1);
            this.Do(() => ribbon.SelectedIndex = 0);
        });

        this.Check("Ribbon: clicking a large item runs its ICommand", () =>
        {
            var clipboard = _form.Part<RibbonGroup>("ribbon.clipboard");
            var bounds = this.Read(() => clipboard.Bounds);
            var contentHeight = this.Read(() => ribbon.GroupAreaHeight) - 24;

            // The large Paste column is the first one inside the group's padding.
            this.Click(ribbon, bounds.X + 20, bounds.Y + 6 + (contentHeight / 2));

            this.ExpectTrue(
                $"the Paste command should have reported into the status line, which reads \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).StartsWith("Ribbon: the Paste command ran", StringComparison.Ordinal));
        });

        this.Check("Ribbon: a small toggle item latches and unlatches", () =>
        {
            var bold = _form.Part<RibbonToggleButton>("ribbon.bold");
            this.Expect("Bold before the click", this.Read(() => bold.Checked), false);

            this.ClickRibbonRow(ribbon, "Font", 0);
            this.Expect("Bold after one click", this.Read(() => bold.Checked), true);
            this.Expect("the status line", this.Read(() => status.Text), "Ribbon: Bold is on.");

            this.ClickRibbonRow(ribbon, "Font", 0);
            this.Expect("Bold after a second click", this.Read(() => bold.Checked), false);
        });

        this.Check("Ribbon: shrinking the width folds the trailing group into a drop-down button", () =>
        {
            var styles = _form.Part<RibbonGroup>("ribbon.styles");
            var combo = _form.Part<ComboBox>("ribbon.styleCombo");
            this.Expect("the Styles group before the squeeze", this.Read(() => styles.IsCollapsed), false);
            this.ExpectTrue("the hosted combo box should start visible", this.Read(() => combo.Visible));

            this.Click(_form.Part<Button>("ribbon.narrow"), 60, 14);

            this.Expect("the Styles group after the squeeze", this.Read(() => styles.IsCollapsed), true);
            this.ExpectTrue(
                "a collapsed group must take its hosted control off screen with it",
                !this.Read(() => combo.Visible));

            this.Click(_form.Part<Button>("ribbon.narrow"), 60, 14);
            this.Expect("the Styles group once the width came back", this.Read(() => styles.IsCollapsed), false);
            this.ExpectTrue("the hosted combo box should come back too", this.Read(() => combo.Visible));
        });

        this.Check("Ribbon: minimizing collapses it onto the strip and lifts the content below", () =>
        {
            var minimize = _form.Part<CheckBox>("ribbon.minimize");
            var combo = _form.Part<ComboBox>("ribbon.styleCombo");
            var accordionTop = this.Read(() => accordion.Top);
            var strip = this.Read(() => ribbon.TabStripHeight);

            this.Do(() => ribbon.SelectedIndex = 0);
            this.Click(minimize, 8, 10);
            this.Expect("the group area height while minimized", this.Read(() => ribbon.GroupAreaHeight), 0);
            this.Expect("the ribbon collapses to just its strip", this.Read(() => ribbon.Height), strip);
            this.ExpectTrue("a minimized ribbon must hide its hosted controls", !this.Read(() => combo.Visible));
            this.ExpectTrue(
                "minimizing must lift the content below the ribbon — no dead gap where the group area was",
                this.Read(() => accordion.Top) < accordionTop);
            this.Screenshot("state-ribbon-minimized");

            this.Click(minimize, 8, 10);
            this.ExpectTrue("restoring the ribbon must bring the group area back", this.Read(() => ribbon.GroupAreaHeight) > 0);
            this.Expect("and drop the content back where it was", this.Read(() => accordion.Top), accordionTop);
            this.ExpectTrue("and the hosted combo box with it", this.Read(() => combo.Visible));
        });

        this.Check("Ribbon: a tab click while minimized opens a flyout whose items fire", () =>
        {
            var minimize = _form.Part<CheckBox>("ribbon.minimize");
            var strip = this.Read(() => ribbon.TabStripHeight);

            this.Click(minimize, 8, 10); // minimize
            this.Click(ribbon, 80, strip / 2); // click the Insert header
            this.Expect("the selected tab while minimized", this.Read(() => ribbon.SelectedIndex), 1);

            var flyout = this.WaitForPopup();
            this.ExpectTrue("a tab click while minimized must float that tab's groups under the strip", flyout != 0);
            this.Screenshot("state-ribbon-flyout");

            // The large Picture button fills the first column of Insert's first group, so a click near
            // the flyout's top-left corner lands on it.
            var spot = this.Read(() => ribbon.PointToScreen(new Point(24, strip + 40)));
            this.ClickAt(spot);
            this.Expect("the status line after activating a flyout item", this.Read(() => status.Text), "Ribbon: Picture clicked.");
            this.ExpectTrue("activating a flyout item must close the flyout", this.Popups().Count == 0);

            this.Click(minimize, 8, 10); // restore
            this.Do(() => ribbon.SelectedIndex = 0);
        });

        this.Check("Ribbon: double-clicking a tab toggles minimize", () =>
        {
            var strip = this.Read(() => ribbon.TabStripHeight);
            this.Do(() => ribbon.Minimized = false);
            this.Settle(700); // clear the double-click window

            this.DoubleClick(ribbon, 20, strip / 2);
            this.Expect("double-clicking a tab minimizes the ribbon", this.Read(() => ribbon.Minimized), true);

            this.Settle(700);
            this.DoubleClick(ribbon, 20, strip / 2);
            this.Expect("double-clicking again restores it", this.Read(() => ribbon.Minimized), false);
        });

        this.Check("Grid picker: hovering reports the dimensions and a click commits them", () =>
        {
            var picker = _form.Part<GridPicker>("ribbon.gridPicker");

            // GridPickerCore: 18px cells, 6px padding. Cell (col 3, row 2) centres at 6+(n-1)*18+9.
            this.Hover(picker, 6 + (2 * 18) + 9, 6 + 18 + 9);
            this.Expect("the hovered column count", this.Read(() => picker.Columns), 3);
            this.Expect("the hovered row count", this.Read(() => picker.Rows), 2);
            this.Screenshot("state-gridpicker");

            this.Click(picker, 6 + (2 * 18) + 9, 6 + 18 + 9);
            this.Expect("the status line after committing a block", this.Read(() => status.Text), "Grid: insert 3 × 2 table.");
        });

        this.Check("Ribbon: the Table button opens the grid picker and inserts a table", () =>
        {
            this.Do(() =>
            {
                ribbon.Minimized = false;
                ribbon.SelectedIndex = 1; // Insert
            });

            var tables = this.Read(() => GroupBounds(ribbon, "Tables"));
            var contentMid = (this.Read(() => ribbon.GroupAreaHeight) - 24) / 2;
            this.Click(ribbon, tables.X + 20, tables.Y + 6 + contentMid); // the large Table button

            var popup = this.WaitForPopup();
            this.ExpectTrue("the Table button must open a grid picker popup", popup != 0);

            // The popup is anchored under the group; commit cell (col 2, row 1) → a 2×1 table.
            var anchor = this.Read(() => ribbon.PointToScreen(new Point(tables.X, tables.Bottom)));
            this.ClickAt(new Point(anchor.X + 6 + 18 + 9, anchor.Y + 6 + 9));
            this.ExpectTrue(
                $"the picked size should have reached the status line, which reads \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).StartsWith("Ribbon: insert 2 × 1 table", StringComparison.Ordinal));

            this.Do(() => ribbon.SelectedIndex = 0);
        });

        this.Screenshot("state-ribbon");

        // --- Accordion --------------------------------------------------------------------------

        this.Check("Accordion: clicking a header opens that pane and closes the others", () =>
        {
            var mail = _form.Part<AccordionPane>("ribbon.mailPane");
            var calendar = _form.Part<AccordionPane>("ribbon.calendarPane");
            var contacts = _form.Part<AccordionPane>("ribbon.contactsPane");

            this.ExpectTrue("the Mail pane should start open", this.Read(() => mail.Expanded));

            this.ClickAccordionHeader(accordion, 1);
            this.Expect("the Calendar pane after clicking its header", this.Read(() => calendar.Expanded), true);
            this.Expect("the Mail pane after another pane opened", this.Read(() => mail.Expanded), false);
            this.Expect("the Contacts pane", this.Read(() => contacts.Expanded), false);
            this.Expect("SelectedIndex", this.Read(() => accordion.SelectedIndex), 1);
            this.Expect("the status line", this.Read(() => status.Text), "Accordion: the Calendar pane is open.");

            this.ClickAccordionHeader(accordion, 2);
            this.Expect("the Contacts pane after clicking its header", this.Read(() => contacts.Expanded), true);
            this.Expect("the Calendar pane after Contacts opened", this.Read(() => calendar.Expanded), false);
        });

        this.Check("Accordion: a closed pane's children are genuinely off screen", () =>
        {
            // Aimed at the Compose button rather than the folder list: a ListBox is owner-drawn, so
            // a press on it lands on the same GtkFixed as the pane behind it and open and closed
            // would be indistinguishable. A Button is a native GtkButton, which makes the landing
            // the on-screen truth about the closed drawer, whatever the flags say.
            var compose = _form.Part<Button>("ribbon.compose");
            this.ClickAccordionHeader(accordion, 0);
            this.ExpectTrue("the Mail pane's button should be visible while its pane is open", this.Read(() => compose.Visible));

            var spot = this.ScreenOf(compose, this.Read(() => compose.Width) / 2, this.Read(() => compose.Height) / 2);
            var openLanding = this.PressAt(spot);
            this.ReleaseAt(spot);
            this.Expect("the widget a press on the open pane's button reaches", openLanding, "GtkButton");

            this.ClickAccordionHeader(accordion, 1);
            this.ExpectTrue("a closed pane's child must report itself invisible", !this.Read(() => compose.Visible));

            var closedLanding = this.PressAt(spot);
            this.ReleaseAt(spot);
            this.ExpectTrue(
                $"a press where the closed pane's button used to be still reached a {closedLanding}",
                closedLanding != "GtkButton");

            this.ClickAccordionHeader(accordion, 0);
            this.ExpectTrue("reopening the pane must bring its button back", this.Read(() => compose.Visible));
            this.Expect("the widget a press on the reopened button reaches", this.PressAt(spot), "GtkButton");
            this.ReleaseAt(spot);
        });

        this.Check("Accordion: the arrow keys walk the headers and Enter toggles one", () =>
        {
            var calendar = _form.Part<AccordionPane>("ribbon.calendarPane");
            this.ClickAccordionHeader(accordion, 0);
            this.FocusOn(accordion);

            this.Key(KeySym.Down);
            this.Key(KeySym.Return);

            this.Expect("the Calendar pane after Down + Enter", this.Read(() => calendar.Expanded), true);
            this.ClickAccordionHeader(accordion, 0);
        });

        this.Check("Accordion: Multiple mode keeps several panes open at once", () =>
        {
            var mail = _form.Part<AccordionPane>("ribbon.mailPane");
            var calendar = _form.Part<AccordionPane>("ribbon.calendarPane");
            var multiple = _form.Part<CheckBox>("ribbon.multiple");

            this.Click(multiple, 8, 10);
            this.ClickAccordionHeader(accordion, 1);

            this.Expect("the Mail pane in Multiple mode", this.Read(() => mail.Expanded), true);
            this.Expect("the Calendar pane in Multiple mode", this.Read(() => calendar.Expanded), true);
            this.ExpectTrue(
                "two open panes must share the height the headers leave",
                this.Read(() => mail.Bounds.Height) > 0 && this.Read(() => calendar.Bounds.Height) > 0);

            this.Click(multiple, 8, 10);
            this.Expect("the mode switch back to Single leaves one pane open", this.Read(() => mail.Expanded), false);
        });

        this.Screenshot("state-accordion");
    }

    /// <summary>Clicks the middle of an accordion header row, which the control itself locates.</summary>
    private void ClickAccordionHeader(Accordion accordion, int index)
    {
        var header = this.Read(() => accordion.GetHeaderBounds(index));
        this.Click(accordion, 30, header.Y + (header.Height / 2));
    }

    /// <summary>
    /// Clicks the stacked row at <paramref name="row"/> of the group captioned
    /// <paramref name="caption"/> on the selected tab. Aiming by caption rather than by a literal
    /// coordinate keeps the check honest when the group layout shifts.
    /// </summary>
    private void ClickRibbonRow(Ribbon ribbon, string caption, int row)
    {
        var bounds = this.Read(() => GroupBounds(ribbon, caption));
        var rowHeight = (this.Read(() => ribbon.GroupAreaHeight) - 24) / 3;
        this.Click(ribbon, bounds.X + 30, bounds.Y + 6 + (row * rowHeight) + (rowHeight / 2));
    }

    /// <summary>The bounds of the selected tab's group with the given caption.</summary>
    private static Rectangle GroupBounds(Ribbon ribbon, string caption)
    {
        if (ribbon.SelectedTab is not { } tab)
            return Rectangle.Empty;

        for (var i = 0; i < tab.Groups.Count; ++i)
            if (tab.Groups[i].Text == caption)
                return tab.Groups[i].Bounds;

        return Rectangle.Empty;
    }
}
