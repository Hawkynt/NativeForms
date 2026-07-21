using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The walkthrough itself: one method per gallery page, each a list of declarative checks that
/// perform a gesture and then assert the observable state the gesture should have produced. Adding a
/// case is adding one <c>Check</c> block; nothing else in the harness needs to know about it.
/// </summary>
internal sealed partial class Autopilot
{
    /// <summary>The theme the gallery paints with — the source of the row and scrollbar metrics the
    /// script needs to aim at a menu row, a spinner button or a scrollbar thumb.</summary>
    private static ITheme Theme => BackendRegistry.Resolve().Theme;

    /// <summary>Runs every page of the walkthrough, in the order a user would.</summary>
    private void RunScript()
    {
        this.DriveTabs();
        this.DriveBasics();
        this.DriveInput();
        this.DriveLists();
        this.DriveGrid();
        this.DriveLayout();
        this.DrivePickers();
        this.DriveRibbonPage();
        this.DriveChrome();
        this.DriveModalDialog();
        this.DriveTextEntry();
        this.DrivePickerDialog();
        this.RunAudit();
        this.CaptureGallery();
    }

    // --- Tab navigation -------------------------------------------------------------------------

    /// <summary>
    /// Finds the x of every tab header by clicking along the header strip and watching
    /// <see cref="TabControl.SelectedIndex"/>, then leaves the gallery on the first page. Doing it by
    /// probing rather than by measuring keeps the harness honest: a header that cannot be clicked at
    /// all shows up right here instead of silently degrading every later page.
    /// </summary>
    private void DriveTabs()
    {
        Section("Tab navigation");
        var tabs = _form.Part<TabControl>("chrome.tabs");
        var count = this.Read(() => tabs.TabPages.Count);
        var headerY = this.Read(() => tabs.HeaderHeight) / 2;
        var width = this.Read(() => tabs.Width);
        var found = new int[count];
        Array.Fill(found, -1);

        this.Check("TabControl: every tab header is reachable by a click", () =>
        {
            for (var x = 6; x < width - 6; x += 14)
            {
                var before = this.Read(() => tabs.SelectedIndex);
                this.Pump("a header probe", () =>
                {
                    var screen = tabs.PointToScreen(new Point(x, headerY));
                    Injection.Move(_root, screen);
                    Injection.Press(_root, screen, 1, 0);
                    Injection.Release(_root, screen, 1, 0);
                    Injection.Drain();
                });

                var after = this.Read(() => tabs.SelectedIndex);
                if (after >= 0 && after < count && found[after] < 0)
                    found[after] = x;

                if (after == before && found[before] < 0)
                    found[before] = x;
            }

            for (var i = 0; i < count; ++i)
                if (found[i] < 0)
                    this.Fail($"tab {i} (\"{this.Read(() => tabs.TabPages[i].Text)}\") could not be selected by any click along the header strip");
        });

        _tabHeaderX = found;
        this.SelectTab(0);
    }

    /// <summary>Switches to a page by clicking its header, falling back to the property when the
    /// header proved unclickable so the rest of the walkthrough still runs.</summary>
    private void SelectTab(int index)
    {
        var tabs = _form.Part<TabControl>("chrome.tabs");
        var x = _tabHeaderX is { } map && index < map.Length ? map[index] : -1;
        if (x >= 0)
        {
            this.Click(tabs, x, this.Read(() => tabs.HeaderHeight) / 2);
            if (this.Read(() => tabs.SelectedIndex) == index)
                return;
        }

        this.Do(() => tabs.SelectedIndex = index);
    }

    /// <summary>Selects the tab whose page carries this header, so a page's driver never has to know
    /// its ordinal — inserting a tab upstream cannot silently point a walkthrough at the wrong page.</summary>
    private void SelectTab(string header)
    {
        var tabs = _form.Part<TabControl>("chrome.tabs");
        var index = this.Read(() =>
        {
            for (var i = 0; i < tabs.TabPages.Count; ++i)
                if (string.Equals(tabs.TabPages[i].Text, header, StringComparison.Ordinal))
                    return i;

            return -1;
        });

        this.SelectTab(index);
    }

    // --- Basics ---------------------------------------------------------------------------------

    /// <summary>Buttons, the MVVM counter, labels, links, checks, the toggle, radios and the picture
    /// box's context menu.</summary>
    private void DriveBasics()
    {
        Section("Basics");
        this.SelectTab(0);
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");
        var viewModel = _form.Part<CounterViewModel>("basics.viewModel");
        var counterLabel = _form.Part<Label>("basics.counterLabel");
        var counterBar = _form.Part<ProgressBar>("basics.counterBar");
        var click = _form.Part<Button>("basics.click");

        this.Check("Button: a click runs the RelayCommand and both bindings follow", () =>
        {
            var before = this.Read(() => viewModel.Count);
            var landed = this.Click(click, 60, 15);
            this.Expect("the press landed on", landed, "GtkButton");
            this.Expect("CounterViewModel.Count", this.Read(() => viewModel.Count), before + 1);
            this.Expect("the bound label", this.Read(() => counterLabel.Text), this.Read(() => viewModel.Display));
            this.Expect("the bound progress bar", this.Read(() => counterBar.Value), (before + 1) * 10);
        });

        this.Check("Button: three more clicks advance the counter three more times", () =>
        {
            var before = this.Read(() => viewModel.Count);
            for (var i = 0; i < 3; ++i)
                this.Click(click, 60, 15);

            this.Expect("CounterViewModel.Count", this.Read(() => viewModel.Count), before + 3);
        });

        this.Check("Button: a disabled button swallows the click", () =>
        {
            var disabled = _form.Part<Button>("basics.disabled");
            this.Do(() => status.Text = "sentinel");
            var count = this.Read(() => viewModel.Count);
            this.Click(disabled, 60, 15);
            this.Expect("the status line", this.Read(() => status.Text), "sentinel");
            this.Expect("CounterViewModel.Count", this.Read(() => viewModel.Count), count);
        });

        this.Check("LinkLabel: a click raises LinkClicked and marks the link visited", () =>
        {
            var link = _form.Part<LinkLabel>("basics.link");
            this.ExpectTrue("the link starts unvisited", !this.Read(() => link.LinkVisited));
            this.Click(link, 40, 10);
            this.ExpectTrue("LinkVisited was not set by the click", this.Read(() => link.LinkVisited));
            this.Expect("the status line", this.Read(() => status.Text), "LinkLabel clicked — now painted as visited.");
        });

        this.Check("CheckBox: clicking toggles Checked and reports it", () =>
        {
            var box = _form.Part<CheckBox>("basics.check");
            this.Click(box, 10, 10);
            this.ExpectTrue("the box did not tick", this.Read(() => box.Checked));
            this.Expect("the status line", this.Read(() => status.Text), "The plain check box is checked.");
            this.Click(box, 10, 10);
            this.ExpectTrue("the box did not untick", !this.Read(() => box.Checked));
        });

        this.Check("CheckBox: a disabled box ignores the click", () =>
        {
            var box = _form.Part<CheckBox>("basics.checkDisabled");
            this.Click(box, 10, 10);
            this.ExpectTrue("the disabled box ticked anyway", !this.Read(() => box.Checked));
        });

        this.Check("ToggleSwitch: clicking flips it", () =>
        {
            var toggle = _form.Part<ToggleSwitch>("basics.toggle");
            var before = this.Read(() => toggle.Checked);
            this.Click(toggle, 20, 12);
            this.Expect("ToggleSwitch.Checked", this.Read(() => toggle.Checked), !before);
            this.Expect("the status line", this.Read(() => status.Text), $"Notifications are {(before ? "off" : "on")}.");
        });

        this.Check("RadioButton: picking one clears the rest of the group", () =>
        {
            var small = _form.Part<RadioButton>("basics.radioSmall");
            var medium = _form.Part<RadioButton>("basics.radioMedium");
            var large = _form.Part<RadioButton>("basics.radioLarge");
            this.ExpectTrue("Medium starts checked", this.Read(() => medium.Checked));
            this.Click(large, 10, 10);
            this.ExpectTrue("Large did not take the selection", this.Read(() => large.Checked));
            this.ExpectTrue("Medium kept the selection", !this.Read(() => medium.Checked));
            this.ExpectTrue("Small became checked", !this.Read(() => small.Checked));
            this.Expect("the status line", this.Read(() => status.Text), "Selected size: Large");
            this.Click(small, 10, 10);
            this.ExpectTrue("Small did not take the selection", this.Read(() => small.Checked));
            this.ExpectTrue("Large kept the selection", !this.Read(() => large.Checked));
        });

        var picture = _form.Part<PictureBox>("basics.picture");
        var menu = _form.Part<ContextMenuStrip>("basics.pictureMenu");
        var cursor = Point.Empty;

        this.Check("PictureBox: a right click opens the context menu at the pointer", () =>
        {
            cursor = this.ScreenOf(picture, 150, 70);
            this.ClickAt(cursor, button: 3);
            this.ExpectTrue("the context menu did not open", this.Read(() => menu.IsOpen));
            var popups = this.Popups();
            if (popups.Count == 0)
            {
                this.Fail("no popup toplevel appeared for the context menu");
                return;
            }

            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            this.ExpectNear("the menu's left edge against the cursor", bounds.X, cursor.X, 4);
            this.ExpectNear("the menu's top edge against the cursor", bounds.Y, cursor.Y, 4);
        });

        this.Screenshot("state-basics-context-menu");

        this.Check("ContextMenuStrip: clicking an item runs it and closes the cascade", () =>
        {
            var popups = this.Popups();
            if (popups.Count == 0)
            {
                this.Fail("the context menu was not open, so no item could be clicked");
                return;
            }

            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            var rowHeight = this.Read(() => Theme.RowHeight);

            // Row 1 of cool / warm / separator / clear: the second gradient.
            this.ClickAt(new(bounds.X + 40, bounds.Y + 1 + rowHeight + (rowHeight / 2)));
            this.Expect("the status line", this.Read(() => status.Text), "PictureBox: green → purple gradient regenerated.");
            this.ExpectTrue("the menu stayed open after committing an item", !this.Read(() => menu.IsOpen));
        });
    }

    // --- Input ----------------------------------------------------------------------------------

    /// <summary>Text editors, spinners, the search box, both sliders, the picker and the calendar.</summary>
    private void DriveInput()
    {
        Section("Input");
        this.SelectTab(1);
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");

        this.Check("TextBox: typing into the single-line box appends to Text", () =>
        {
            var box = _form.Part<TextBox>("input.single");
            var before = this.Read(() => box.Text);
            this.FocusOn(box);
            this.Key(KeySym.End);
            this.Type("Typed");
            this.Expect("TextBox.Text", this.Read(() => box.Text), before + "Typed");
        });

        this.Check("TextBox: the placeholder box takes text and keeps it", () =>
        {
            var box = _form.Part<TextBox>("input.placeholder");
            this.FocusOn(box);
            this.Type("filled in");
            this.Expect("TextBox.Text", this.Read(() => box.Text), "filled in");
        });

        this.Check("TextBox: the password box records the typed characters", () =>
        {
            var box = _form.Part<TextBox>("input.password");
            var before = this.Read(() => box.Text);
            this.FocusOn(box);
            this.Key(KeySym.End);
            this.Type("42");
            this.Expect("TextBox.Text", this.Read(() => box.Text), before + "42");
        });

        this.Check("TextBox: the multiline box takes a new line and more text", () =>
        {
            var box = _form.Part<TextBox>("input.multiline");
            var before = this.Read(() => box.Text);
            this.FocusOn(box);
            this.Key(KeySym.End);
            this.Key(KeySym.Return);
            this.Type("Line four.");

            // End goes to the end of the first line, so the new line lands after it.
            var lines = before.Split('\n');
            this.Expect("TextBox.Text", this.Read(() => box.Text), $"{lines[0]}\nLine four.\n{lines[1]}\n{lines[2]}");
        });

        this.Check("MaskedTextBox: typing ten digits fills the mask and completes it", () =>
        {
            var masked = _form.Part<MaskedTextBox>("input.masked");
            this.Click(masked, 40, this.Read(() => masked.Height) / 2);

            // With the caret at the end and again at the very start, so a rejection cannot be blamed
            // on where the click happened to leave it.
            this.Key(KeySym.End);
            this.Type("5551234567");
            var atEnd = this.Read(() => masked.Text);
            this.Key(KeySym.Home);
            this.Type("5551234567");
            this.ExpectTrue($"typing at the end of the mask left it at \"{atEnd}\"", atEnd == "(555) 123-4567");
            this.Expect("MaskedTextBox.Text", this.Read(() => masked.Text), "(555) 123-4567");
            this.ExpectTrue("MaskCompleted stayed false", this.Read(() => masked.MaskCompleted));
            this.Expect("the status line", this.Read(() => status.Text), "Phone number completed: (555) 123-4567");
        });

        this.Check("RichTextBox: typing extends the document", () =>
        {
            var rich = _form.Part<RichTextBox>("input.rich");
            var before = this.Read(() => rich.Text.Length);
            this.Click(rich, 40, 12);
            this.Key(KeySym.End);
            this.Type("appended");
            this.Expect("the document length", this.Read(() => rich.Text.Length), before + 8);
        });

        var numeric = _form.Part<NumericUpDown>("input.numeric");

        this.Check("NumericUpDown: a click on the up button steps by Increment", () =>
        {
            var before = this.Read(() => numeric.Value);
            var landed = this.Click(numeric, this.Read(() => numeric.Width) - 5, this.Read(() => numeric.Height) / 4);
            this.Expect(
                "the widget five pixels inside the right edge, where the spinner buttons paint (the hosted editor should stop a scrollbar-width short of it)",
                landed,
                "GtkFixed");
            this.Expect("NumericUpDown.Value", this.Read(() => numeric.Value), before + this.Read(() => numeric.Increment));
        });

        this.Check("NumericUpDown: holding the down button auto-repeats", () =>
        {
            var before = this.Read(() => numeric.Value);
            var height = this.Read(() => numeric.Height);
            this.PressAndHold(numeric, this.Read(() => numeric.Width) - 5, height - (height / 4), 1200);
            var after = this.Read(() => numeric.Value);
            var steps = (before - after) / this.Read(() => numeric.Increment);
            this.ExpectTrue($"a 1.2 s hold produced {steps} step(s), which is not an auto-repeat", steps >= 3);
        });

        this.Check("DomainUpDown: a click on a spinner button moves the selection", () =>
        {
            var domain = _form.Part<DomainUpDown>("input.domain");
            var before = this.Read(() => domain.SelectedIndex);
            this.Click(domain, this.Read(() => domain.Width) - 5, this.Read(() => domain.Height) / 4);
            this.ExpectChanged("DomainUpDown.SelectedIndex", before, this.Read(() => domain.SelectedIndex));
            this.ExpectTrue(
                "the status line did not report the new item",
                this.Read(() => status.Text).StartsWith("DomainUpDown: ", StringComparison.Ordinal));
        });

        this.Check("SearchBox: Focus() routes the keyboard to the hosted editor", () =>
        {
            var search = _form.Part<SearchBox>("input.search");
            this.FocusOn(search);
            this.Type("focus");
            this.Expect("SearchBox.Text after Focus() and typing", this.Read(() => search.Text), "focus");
            this.Do(() => search.Text = string.Empty);
        });

        this.Check("SearchBox: typing then Enter commits, and the clear glyph empties it", () =>
        {
            var search = _form.Part<SearchBox>("input.search");
            this.Click(search, 60, this.Read(() => search.Height) / 2);
            this.Type("grid");
            this.Expect("SearchBox.Text", this.Read(() => search.Text), "grid");
            this.Key(KeySym.Return);
            this.Expect("the status line", this.Read(() => status.Text), "Search committed: \"grid\"");
            this.Click(search, this.Read(() => search.Width) - 6, this.Read(() => search.Height) / 2);
            this.Expect("SearchBox.Text after the clear glyph", this.Read(() => search.Text), string.Empty);
            this.Expect("the status line", this.Read(() => status.Text), "Search cleared.");
        });

        this.Check("TrackBar: dragging the thumb scrubs the value", () =>
        {
            var track = _form.Part<TrackBar>("input.track");
            var label = _form.Part<Label>("input.trackValue");
            var geometry = this.Read(() => (track.Width, track.Height, track.Minimum, track.Maximum, track.Value));
            var length = geometry.Width - 16;
            var thumbX = 8 + (length * (geometry.Value - geometry.Minimum) / (geometry.Maximum - geometry.Minimum));
            var targetX = 8 + (length * 3 / (geometry.Maximum - geometry.Minimum));
            this.Drag(track, new(thumbX, geometry.Height / 2), new(targetX, geometry.Height / 2));
            this.ExpectNear("TrackBar.Value after dragging the thumb to 3", this.Read(() => track.Value), 3, 1);
            this.Expect("the echoing label", this.Read(() => label.Text), this.Read(() => track.Value).ToString());
        });

        this.Check("HScrollBar: the increase arrow, the channel and a thumb drag all move Value", () =>
        {
            var bar = _form.Part<HScrollBar>("input.hscroll");
            var size = this.Read(() => bar.Size);
            var arrow = Math.Min(size.Height, size.Width / 2);

            var before = this.Read(() => bar.Value);
            this.Click(bar, size.Width - (arrow / 2), size.Height / 2);
            this.Expect("Value after the increase arrow", this.Read(() => bar.Value), before + this.Read(() => bar.SmallChange));

            before = this.Read(() => bar.Value);
            this.Click(bar, arrow + 4, size.Height / 2);
            this.ExpectTrue(
                $"clicking the channel left of the thumb should page down, observed {before} then {this.Read(() => bar.Value)}",
                this.Read(() => bar.Value) < before);

            before = this.Read(() => bar.Value);
            var thumb = ThumbCentre(size, vertical: false, this.Read(() => bar.Minimum), this.Read(() => bar.Maximum), before, this.Read(() => bar.LargeChange));
            this.Drag(bar, thumb, new(size.Width - arrow - 2, size.Height / 2));
            this.ExpectTrue(
                $"dragging the thumb to the far end should raise Value above {before}, observed {this.Read(() => bar.Value)}",
                this.Read(() => bar.Value) > before);
        });

        this.Check("VScrollBar: a thumb drag toward the top lowers Value", () =>
        {
            var bar = _form.Part<VScrollBar>("input.vscroll");
            var size = this.Read(() => bar.Size);
            var arrow = Math.Min(size.Width, size.Height / 2);
            var before = this.Read(() => bar.Value);
            var thumb = ThumbCentre(size, vertical: true, this.Read(() => bar.Minimum), this.Read(() => bar.Maximum), before, this.Read(() => bar.LargeChange));
            this.Drag(bar, thumb, new(size.Width / 2, arrow + 2));
            this.ExpectTrue(
                $"dragging the thumb to the top should lower Value below {before}, observed {this.Read(() => bar.Value)}",
                this.Read(() => bar.Value) < before);
        });

        var picker = _form.Part<DateTimePicker>("input.picker");

        this.Check("DateTimePicker: a click drops the calendar down and it stays down", () =>
        {
            var screen = this.ScreenOf(picker, this.Read(() => picker.Width) - 12, this.Read(() => picker.Height) / 2);
            var phases = this.ProbeOpen(screen, () => picker.DroppedDown);
            this.ExpectTrue("the press did not open the calendar at all", phases.OnPress);
            this.ExpectTrue(DropDownPhases("calendar", phases), phases.AfterRelease);
            this.ExpectTrue("no popup toplevel is on screen for the calendar", this.Popups().Count > 0);
            this.CheckProgrammaticOpen("the calendar", () => picker.DroppedDown, picker.OpenDropDown);
        });

        this.Check("DateTimePicker: the calendar surface lands directly below the field", () =>
            this.CheckPopupAnchored("the calendar", picker, () => picker.DroppedDown, picker.OpenDropDown));

        this.Screenshot("state-input-datetimepicker-dropdown");

        this.Check("DateTimePicker: picking a day in the drop-down commits it and closes up", () =>
        {
            var popups = this.Reopen(() => picker.DroppedDown, picker.OpenDropDown);
            if (popups.Count == 0)
            {
                this.Fail("the calendar drop-down could not be opened at all");
                return;
            }

            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            var rowHeight = this.Read(() => Theme.RowHeight);
            var before = this.Read(() => picker.Value);
            var cell = DayCell(bounds.Size, rowHeight, row: 2, column: 3);
            this.ClickAt(new(bounds.X + cell.X, bounds.Y + cell.Y));
            this.ExpectTrue("the drop-down stayed open after a day was clicked", !this.Read(() => picker.DroppedDown));
            this.ExpectChanged("DateTimePicker.Value", before.Date, this.Read(() => picker.Value).Date);
        });

        var calendar = _form.Part<MonthCalendar>("input.calendar");

        this.Check("MonthCalendar: clicking a day selects it and pages back a month", () =>
        {
            var size = this.Read(() => calendar.Size);
            var rowHeight = this.Read(() => Theme.RowHeight);
            var cell = DayCell(size, rowHeight, row: 2, column: 3);
            this.Click(calendar, cell.X, cell.Y);
            var first = this.Read(() => calendar.SelectionStart).Date;
            this.ExpectTrue(
                "the status line did not report a selection",
                this.Read(() => status.Text).StartsWith("MonthCalendar: ", StringComparison.Ordinal));

            this.Click(calendar, rowHeight / 2, rowHeight / 2);
            this.Click(calendar, cell.X, cell.Y);
            var second = this.Read(() => calendar.SelectionStart).Date;
            var delta = (int)(first - second).TotalDays;
            this.ExpectTrue(
                $"paging back one month should move the same cell 28..31 days earlier, observed {delta} ({first:yyyy-MM-dd} then {second:yyyy-MM-dd})",
                delta is >= 28 and <= 31);
        });

        this.Check("MonthCalendar: the title drills out to years and a cell drills back in", () =>
        {
            var size = this.Read(() => calendar.Size);
            var rowHeight = this.Read(() => Theme.RowHeight);
            var startYear = this.Read(() => calendar.SelectionStart).Year;
            var decadeStart = startYear - (startYear % 10);

            // Two title clicks reach the decade page, whose cells run decadeStart-1 .. decadeStart+10.
            this.Click(calendar, size.Width / 2, rowHeight / 2);
            this.Click(calendar, size.Width / 2, rowHeight / 2);
            this.Screenshot("state-input-monthcalendar-decade");

            var targetYear = decadeStart + 2;
            var yearCell = PeriodCell(size, rowHeight, targetYear - (decadeStart - 1));
            this.Click(calendar, yearCell.X, yearCell.Y); // the year page of targetYear
            this.Screenshot("state-input-monthcalendar-year");
            var juneCell = PeriodCell(size, rowHeight, 5);
            this.Click(calendar, juneCell.X, juneCell.Y); // June of it, back on the day page

            var dayCell = DayCell(size, rowHeight, row: 2, column: 3);
            this.Click(calendar, dayCell.X, dayCell.Y);
            var picked = this.Read(() => calendar.SelectionStart);
            this.ExpectTrue(
                $"drilling decade -> year -> month should land on June {targetYear}, observed {picked:yyyy-MM-dd}",
                picked.Year == targetYear && picked.Month == 6);
        });

        this.Check("MonthCalendar: the drilled-out page comes back to the day grid", () =>
        {
            var size = this.Read(() => calendar.Size);
            var rowHeight = this.Read(() => Theme.RowHeight);
            this.Do(() => calendar.SetSelectionRange(new(2026, 7, 15), new(2026, 7, 15)));
            var dayCell = DayCell(size, rowHeight, row: 2, column: 3);
            this.Click(calendar, dayCell.X, dayCell.Y);
            this.Expect("the day page selects whole days again", this.Read(() => calendar.SelectionStart).Year, 2026);
        });

        var time = _form.Part<TimePicker>("input.time");

        this.Check("TimePicker: a spinner click steps the part under the caret", () =>
        {
            var before = this.Read(() => time.Value);
            var geometry = this.Read(() => (time.Width, time.Height));
            this.Click(time, geometry.Width - 5, geometry.Height / 4); // the up button
            var afterHour = this.Read(() => time.Value);
            this.Expect("TimePicker.Value after stepping the hour", afterHour, before.Add(TimeSpan.FromHours(1)));
            this.ExpectTrue(
                "the status line did not report the new time",
                this.Read(() => status.Text).StartsWith("TimePicker: ", StringComparison.Ordinal));

            this.Click(time, geometry.Width - 5, geometry.Height - (geometry.Height / 4)); // the down button
            this.Expect("stepping back down", this.Read(() => time.Value), before);
        });

        this.Check("TimePicker: Right moves the caret so the spinner steps the minute", () =>
        {
            var geometry = this.Read(() => (time.Width, time.Height));
            this.Click(time, 10, geometry.Height / 2); // park the caret on the hour
            this.Key(KeySym.Right);
            this.Expect("the selected part after Right", this.Read(() => time.SelectedField), TimePickerField.Minute);

            var before = this.Read(() => time.Value);
            this.Click(time, geometry.Width - 5, geometry.Height / 4);
            this.Expect("TimePicker.Value after stepping the minute", this.Read(() => time.Value), before.Add(TimeSpan.FromMinutes(1)));
        });

        this.Check("TimePicker: a 12-hour field flips the half day from its AM/PM part", () =>
        {
            var time12 = _form.Part<TimePicker>("input.time12");
            var before = this.Read(() => time12.Value);
            this.Do(() => time12.SelectedField = TimePickerField.Meridiem);
            var geometry = this.Read(() => (time12.Width, time12.Height));
            this.Click(time12, geometry.Width - 5, geometry.Height / 4);
            var after = this.Read(() => time12.Value);
            this.ExpectTrue(
                $"stepping the meridiem should move the value 12 hours, observed {before} then {after}",
                (after - before).Duration() == TimeSpan.FromHours(12));
        });
    }

    // --- Lists ----------------------------------------------------------------------------------

    /// <summary>List box, checked list box, combo boxes, list views and both trees.</summary>
    private void DriveLists()
    {
        Section("Lists");
        this.SelectTab(2);
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");
        var list = _form.Part<ListBox>("lists.listBox");
        var itemHeight = this.Read(() => list.ItemHeight);

        this.Check("ListBox: a plain click selects exactly one row", () =>
        {
            this.Click(list, 40, (itemHeight * 1) + (itemHeight / 2));
            this.Expect("ListBox.SelectedIndex", this.Read(() => list.SelectedIndex), 1);
            this.Expect("the selection size", this.Read(() => list.SelectedIndices.Count), 1);
        });

        this.Check("ListBox: Ctrl+click adds a second row without dropping the first", () =>
        {
            this.Click(list, 40, (itemHeight * 4) + (itemHeight / 2), modifiers: Injection.ControlMask);
            this.Expect("the selection size", this.Read(() => list.SelectedIndices.Count), 2);
            this.ExpectTrue("row 1 lost its selection", this.Read(() => list.GetSelected(1)));
            this.ExpectTrue("row 4 was not selected", this.Read(() => list.GetSelected(4)));
        });

        this.Check("ListBox: Shift+click selects the whole range from the anchor", () =>
        {
            this.Click(list, 40, (itemHeight * 2) + (itemHeight / 2));
            this.Click(list, 40, (itemHeight * 5) + (itemHeight / 2), modifiers: Injection.ShiftMask);
            this.Expect("the selection size for rows 2..5", this.Read(() => list.SelectedIndices.Count), 4);
            this.ExpectTrue("row 3 is missing from the range", this.Read(() => list.GetSelected(3)));
        });

        this.Check("CheckedListBox: a click toggles the check and raises ItemCheck", () =>
        {
            var checkedList = _form.Part<CheckedListBox>("lists.checkedList");
            var height = this.Read(() => checkedList.ItemHeight);
            var before = this.Read(() => checkedList.GetItemChecked(2));
            this.Click(checkedList, 10, (height * 2) + (height / 2));
            this.Expect("the check state of row 2", this.Read(() => checkedList.GetItemChecked(2)), !before);
            this.Expect(
                "the status line",
                this.Read(() => status.Text),
                $"CheckedListBox: \"Publish\" will be {(before ? "unchecked" : "checked")}.");
        });

        var combo = _form.Part<ComboBox>("lists.comboList");

        this.Check("ComboBox: a click drops the list down and it stays down", () =>
        {
            var screen = this.ScreenOf(combo, this.Read(() => combo.Width) - 12, this.Read(() => combo.Height) / 2);
            var phases = this.ProbeOpen(screen, () => combo.DroppedDown);
            this.ExpectTrue("the press did not open the list at all", phases.OnPress);
            this.ExpectTrue(DropDownPhases("list", phases), phases.AfterRelease);
            this.ExpectTrue("no popup toplevel is on screen for the list", this.Popups().Count > 0);
            this.CheckProgrammaticOpen("the list", () => combo.DroppedDown, combo.OpenDropDown);
        });

        this.Check("ComboBox: the drop-down surface lands directly below the field", () =>
            this.CheckPopupAnchored("the list", combo, () => combo.DroppedDown, combo.OpenDropDown));

        this.Screenshot("state-lists-combobox-dropdown");

        this.Check("ComboBox: clicking a row picks it and closes the drop-down", () =>
        {
            var popups = this.Reopen(() => combo.DroppedDown, combo.OpenDropDown);
            if (popups.Count == 0)
            {
                this.Fail("the drop-down could not be opened at all");
                return;
            }

            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            var rowHeight = this.Read(() => Theme.RowHeight);
            this.ClickAt(new(bounds.X + 30, bounds.Y + (rowHeight * 3) + (rowHeight / 2)));
            this.Expect("ComboBox.SelectedIndex", this.Read(() => combo.SelectedIndex), 3);
            this.ExpectTrue("the drop-down stayed open", !this.Read(() => combo.DroppedDown));
            this.Expect("the status line", this.Read(() => status.Text), "ComboBox: Mars picked.");
        });

        var details = _form.Part<ListView>("lists.details");

        this.Check("ListView: a click selects the row under the pointer", () =>
        {
            var row = this.Read(() => details.GetItemBounds(2));
            this.Click(details, row.X + 40, row.Y + (row.Height / 2));
            this.Expect("ListView.SelectedIndex", this.Read(() => details.SelectedIndex), 2);
            this.Expect("the status line", this.Read(() => status.Text), "ListView: Logo.png selected.");
        });

        this.Check("ListView: clicking a column header raises ColumnClick for that column", () =>
        {
            var column = -1;
            this.Do(() => details.ColumnClick += (_, e) => column = e.Column);
            this.Click(details, 60, this.Read(() => details.ItemHeight) / 2);
            this.Expect("the column ColumnClick reported", column, 0);
        });

        this.Check("ListView: with Sorting on, a header click sorts and the next one reverses", () =>
        {
            this.Do(() => details.Sorting = SortOrder.Ascending);
            var header = this.Read(() => details.ItemHeight) / 2;
            this.Click(details, 60, header);
            this.Expect("the first row after sorting by Name", this.Read(() => details.Items[0].Text), "Data.bin");
            this.Click(details, 60, header);
            this.Expect("the first row after the reverse sort", this.Read(() => details.Items[0].Text), "Readme.md");
        });

        this.Check("ListView: check boxes toggle from a click on the box", () =>
        {
            this.Do(() => details.CheckBoxes = true);
            var row = this.Read(() => details.GetItemBounds(1));
            this.Click(details, row.X + 8, row.Y + (row.Height / 2));
            this.Expect("the checked rows", this.Read(() => details.CheckedIndices.Count), 1);
            this.Click(details, row.X + 8, row.Y + (row.Height / 2));
            this.Expect("the checked rows after a second click", this.Read(() => details.CheckedIndices.Count), 0);
            this.Do(() => details.CheckBoxes = false);
        });

        var tree = _form.Part<TreeView>("lists.tree");

        this.Check("TreeView: the expander glyph collapses and re-expands a node", () =>
        {
            var height = this.Read(() => tree.ItemHeight);
            var before = this.Read(() => tree.VisibleNodeCount);
            this.Click(tree, height + (height / 2), height + (height / 2));
            var collapsed = this.Read(() => tree.VisibleNodeCount);
            this.ExpectTrue($"collapsing \"Core\" should hide its two children, {before} rows became {collapsed}", collapsed == before - 2);
            this.Click(tree, height + (height / 2), height + (height / 2));
            this.Expect("the visible rows after re-expanding", this.Read(() => tree.VisibleNodeCount), before);
        });

        this.Check("TreeView: a click on a row selects that node", () =>
        {
            var height = this.Read(() => tree.ItemHeight);
            this.Click(tree, 140, (height * 3) + (height / 2));
            this.Expect("TreeView.SelectedNode", this.Read(() => tree.SelectedNode?.Text), "Form.cs");
            this.Expect("the status line", this.Read(() => status.Text), "TreeView: \"Form.cs\" selected.");
        });

        this.Check("TreeView: a click on the check cell ticks the node", () =>
        {
            var height = this.Read(() => tree.ItemHeight);
            var node = this.Read(() => tree.Nodes[0].Nodes[0].Nodes[1]);
            var before = this.Read(() => node.Checked);

            // "Form.cs" sits at level 2, so its glyph cell spans 2..3 indents and the check cell
            // starts one indent further right.
            this.Click(tree, (height * 3) + 6, (height * 3) + (height / 2));
            this.Expect("the node's Checked state", this.Read(() => node.Checked), !before);
            this.ExpectTrue(
                "the status line did not report the check",
                this.Read(() => status.Text).StartsWith("TreeView: \"Form.cs\" is ", StringComparison.Ordinal));
        });

        this.Check("TreeListView: a row click selects it and the columns carry their cell text", () =>
        {
            var treeList = _form.Part<TreeListView>("lists.treeList");
            var height = this.Read(() => treeList.ItemHeight);
            var header = this.Read(() => treeList.ShowColumnHeaders) ? height : 0;
            this.Click(treeList, 80, header + height + (height / 2));
            this.Expect("TreeListView.SelectedNode", this.Read(() => treeList.SelectedNode?.Text), "app.cs");
            this.Expect(
                "the Kind column's cell text for the selected node",
                this.Read(() => treeList.Columns[1].TextSelector?.Invoke(treeList.SelectedNode!)),
                "Source");

            var before = this.Read(() => treeList.VisibleNodeCount);
            this.Click(treeList, height / 2, header + (height / 2));
            this.ExpectTrue(
                $"collapsing the first root should hide its two children, {before} rows stayed {this.Read(() => treeList.VisibleNodeCount)}",
                this.Read(() => treeList.VisibleNodeCount) == before - 2);
        });
    }

    // --- Grid -----------------------------------------------------------------------------------

    /// <summary>The flagship: selection, sorting, frozen columns, cell kinds, editing and scrolling.</summary>
    private void DriveGrid()
    {
        Section("Grid");
        this.SelectTab(3);
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");
        var grid = _form.Part<DataGridView>("grid.grid");

        this.Check("DataGridView: a click selects the row under the pointer", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(3, 0));
            this.Click(grid, cell.X + 40, cell.Y + (cell.Height / 2));
            this.Expect("SelectedRowIndex", this.Read(() => grid.SelectedRowIndex), 3);
            this.ExpectTrue(
                "the status line did not report the selection",
                this.Read(() => status.Text).StartsWith("Grid: 1 row(s) selected", StringComparison.Ordinal));
        });

        this.Check("DataGridView: Ctrl+click and Shift+click build a multi-row selection", () =>
        {
            var sixth = this.Read(() => grid.GetCellBounds(6, 0));
            this.Click(grid, sixth.X + 40, sixth.Y + (sixth.Height / 2), modifiers: Injection.ControlMask);
            this.Expect("the selection size after Ctrl+click", this.Read(() => Count(grid.SelectedItems)), 2);

            var second = this.Read(() => grid.GetCellBounds(2, 0));
            this.Click(grid, second.X + 40, second.Y + (second.Height / 2));
            var fifth = this.Read(() => grid.GetCellBounds(5, 0));
            this.Click(grid, fifth.X + 40, fifth.Y + (fifth.Height / 2), modifiers: Injection.ShiftMask);
            this.Expect("the selection size for rows 2..5", this.Read(() => Count(grid.SelectedItems)), 4);
        });

        this.Check("DataGridView: clicking the Task header sorts and reverses", () =>
        {
            var headerHeight = this.Read(() => grid.ColumnHeaderHeight);
            var x = this.Read(() => grid.RowHeaderWidth) + 60;
            this.Click(grid, x, headerHeight / 2);
            this.Expect("SortedColumn", this.Read(() => grid.SortedColumn?.HeaderText), "Task");
            this.Expect("SortOrder", this.Read(() => grid.SortOrder), SortOrder.Ascending);
            this.Click(grid, x, headerHeight / 2);
            this.Expect("SortOrder after a second header click", this.Read(() => grid.SortOrder), SortOrder.Descending);
            this.Do(() => grid.Sort(null, SortOrder.None));
        });

        this.Check("DataGridView: a click on a check cell writes back into the bound item", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(3, 1));
            var item = this.Read(() => grid.Items[3]);
            var selector = this.Read(() => grid.Columns[1].CheckedSelector!);
            var before = this.Read(() => selector(item));
            this.Click(grid, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
            this.Expect("the bound item's Done flag", this.Read(() => selector(item)), !before);
        });

        this.Check("DataGridView: a button cell raises CellContentClick", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(3, 5));
            this.Click(grid, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
            this.ExpectTrue(
                $"the status line should report the Open column, observed \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).Contains("\"Open\" clicked", StringComparison.Ordinal));
        });

        this.Check("DataGridView: a link cell raises CellContentClick", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(3, 6));
            this.Click(grid, cell.X + 30, cell.Y + (cell.Height / 2));
            this.ExpectTrue(
                $"the status line should report the Docs column, observed \"{this.Read(() => status.Text)}\"",
                this.Read(() => status.Text).Contains("\"Docs\" clicked", StringComparison.Ordinal));
        });

        this.Check("DataGridView: a list cell opens its popup anchored under the cell and a click commits the pick", () =>
        {
            var owner = this.GridColumn(grid, "Owner");
            var cell = this.Read(() => grid.GetCellBounds(3, owner));
            var item = this.Read(() => grid.Items[3]);
            var selector = this.Read(() => grid.Columns[owner].ValueSelector);
            var before = this.Read(() => selector(item));

            var popup = this.OpenCellEditorPopup(grid, 3, owner);
            if (popup == 0)
                return;

            var anchor = this.ScreenOf(grid, cell.X, cell.Bottom);
            var origin = this.Read(() => Injection.WindowBounds(popup)).Location;
            if (Math.Abs(origin.X - anchor.X) > _AnchorTolerance || Math.Abs(origin.Y - anchor.Y) > _AnchorTolerance)
                this.Fail($"the list popup opened at {anchor} but its surface sits at {origin}");

            var bounds = this.Read(() => Injection.WindowBounds(popup));
            var rowHeight = this.Read(() => Theme.RowHeight);
            this.Screenshot("state-grid-listcolumn-popup");
            this.ClickAt(new(bounds.X + 20, bounds.Y + (rowHeight / 2))); // the first owner, "Alice"
            this.ExpectTrue("the list popup stayed open after a row was clicked", !this.Read(() => grid.IsEditing));
            this.ExpectChanged("the bound item's Owner", before, this.Read(() => selector(item)));
            this.Expect("the committed owner", this.Read(() => selector(item)), "Alice");
        });

        this.Check("DataGridView: a checked-list cell ticks several items and commits them as one set", () =>
        {
            var item = this.Read(() => grid.Items[3]);
            var labels = this.GridColumn(grid, "Labels");
            var selector = this.Read(() => grid.Columns[labels].CheckedItemsSelector!);
            var before = this.Read(() => Count(selector(item)));

            var popup = this.OpenCellEditorPopup(grid, 3, labels);
            if (popup == 0)
                return;

            var bounds = this.Read(() => Injection.WindowBounds(popup));
            var rowHeight = this.Read(() => Theme.RowHeight);
            // The row starts on "Docs" alone; ticking "UX" and "Risk" must leave the cell untouched
            // until the popup closes, and then arrive as one three-item set in ItemsSelector order.
            this.ClickAt(new(bounds.X + 20, bounds.Y + rowHeight + (rowHeight / 2)));       // tick "UX"
            this.ClickAt(new(bounds.X + 20, bounds.Y + (rowHeight * 3) + (rowHeight / 2))); // tick "Risk"
            this.Screenshot("state-grid-checkedlistcolumn-popup");
            this.Expect("the label count while the popup is still open", this.Read(() => Count(selector(item))), before);

            this.Key(KeySym.Return);
            this.ExpectTrue("the checked-list popup stayed open after Enter", !this.Read(() => grid.IsEditing));
            var after = this.Read(() => selector(item));
            this.Expect("the size of the committed label set", Count(after), 3);
            this.Expect("the first committed label", this.Read(() => after[0]), "Docs");
            this.Expect("the last committed label", this.Read(() => after[2]), "Risk");
        });

        this.Check("DataGridView: Escape abandons a checked-list edit instead of committing its ticks", () =>
        {
            // Escape has to reach the grid rather than merely dismissing the popup: for the
            // set-valued kinds dismissal *commits*, so a backend that swallowed Escape at the popup
            // top-level would silently turn "abandon" into "save".
            var labels = this.GridColumn(grid, "Labels");
            var item = this.Read(() => grid.Items[5]);
            var selector = this.Read(() => grid.Columns[labels].CheckedItemsSelector!);
            var before = this.Read(() => Count(selector(item)));

            var popup = this.OpenCellEditorPopup(grid, 5, labels);
            if (popup == 0)
                return;

            var bounds = this.Read(() => Injection.WindowBounds(popup));
            var rowHeight = this.Read(() => Theme.RowHeight);
            this.ClickAt(new(bounds.X + 20, bounds.Y + (rowHeight * 2) + (rowHeight / 2))); // tick "Perf"
            this.Key(KeySym.Escape);
            this.ExpectTrue("the popup stayed open after Escape", !this.Read(() => grid.IsEditing));
            this.Expect("the label count after Escape", this.Read(() => Count(selector(item))), before);
        });

        this.Check("DataGridView: typing into a numeric cell edits it and Enter commits", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(4, 3));
            var item = this.Read(() => grid.Items[4]);
            var selector = this.Read(() => grid.Columns[3].NumberSelector!);
            var before = this.Read(() => selector(item));
            this.Click(grid, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
            this.Type("7");
            this.ExpectTrue("typing a digit did not start an edit", this.Read(() => grid.IsEditing));
            this.Key(KeySym.Return);
            var after = this.Read(() => selector(item));
            this.ExpectChanged("the bound item's Hours", before, after);
            this.Expect("the committed value", after, 7m);
        });

        this.Check("DataGridView: a double click on a time cell hosts a TimePicker and Enter commits its step", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(4, 4));
            var item = this.Read(() => grid.Items[4]);
            var selector = this.Read(() => grid.Columns[4].TimeSelector!);
            var before = this.Read(() => selector(item));
            // Two plain clicks inside the double-click window, rather than the DoubleClick helper:
            // its trailing third press would land on the editor the second one just created.
            var x = cell.X + (cell.Width / 2);
            var y = cell.Y + (cell.Height / 2);
            this.Click(grid, x, y);
            this.Click(grid, x, y);
            this.ExpectTrue("the double click did not start an edit", this.Read(() => grid.IsEditing));
            this.ExpectTrue("the hosted editor is not a TimePicker", this.Read(() => grid.EditingControl) is TimePicker);
            this.Key(KeySym.Up);
            this.Key(KeySym.Return);
            var after = this.Read(() => selector(item));
            this.ExpectTrue("the edit did not leave edit mode", !this.Read(() => grid.IsEditing));
            this.Expect("the bound item's Start after stepping the hour", after, before.Add(TimeSpan.FromHours(1)));
        });

        this.Check("DataGridView: the wheel scrolls the rows", () =>
        {
            var before = this.Read(() => grid.TopRow);
            this.Wheel(grid, 300, 200, -4);
            var after = this.Read(() => grid.TopRow);
            this.ExpectTrue($"four wheel notches down should advance TopRow past {before}, observed {after}", after > before);
            this.Wheel(grid, 300, 200, 8);
            this.Expect("TopRow after scrolling back up", this.Read(() => grid.TopRow), 0);
        });

        this.Check("DataGridView: dragging the vertical scrollbar thumb scrolls the rows", () =>
        {
            if (!this.Read(() => grid.IsVerticalScrollBarVisible))
            {
                this.Fail("the grid shows no vertical scrollbar even though it holds 33 rows in 500 px");
                return;
            }

            // The grid's vertical strip starts under the column headers and opens with an arrow
            // button one strip-width tall, so the thumb begins below both.
            var size = this.Read(() => grid.Size);
            var thickness = this.Read(() => Theme.ScrollBarSize);
            var top = this.Read(() => grid.ColumnHeaderHeight) + thickness;
            var barX = size.Width - (thickness / 2);
            var before = this.Read(() => grid.TopRow);
            this.Drag(grid, new(barX, top + 20), new(barX, size.Height / 2));
            var after = this.Read(() => grid.TopRow);
            this.ExpectTrue($"dragging the thumb halfway down should advance TopRow past {before}, observed {after}", after > before);
            this.Do(() => grid.EnsureVisible(0));
        });

        this.Check("DataGridView: keyboard navigation steps over a merged section row", () =>
        {
            var cell = this.Read(() => grid.GetCellBounds(10, 0));
            this.Click(grid, cell.X + 40, cell.Y + (cell.Height / 2));
            this.Expect("the row the click selected", this.Read(() => grid.SelectedRowIndex), 10);
            this.Key(KeySym.Down);
            this.Expect(
                "the row below 10 — row 11 is the merged \"Backends\" section marker and must be skipped",
                this.Read(() => grid.SelectedRowIndex),
                12);
        });

        this.Check("DataGridView: widening a column by dragging its header edge shows the h-scrollbar", () =>
        {
            var headerHeight = this.Read(() => grid.ColumnHeaderHeight);
            var edge = this.Read(() =>
            {
                var x = grid.RowHeaderWidth;
                foreach (var column in grid.Columns)
                    x += column.Width;

                return x;
            });

            this.ExpectTrue("the grid already scrolls horizontally, so the frozen column cannot be proved", !this.Read(() => grid.IsHorizontalScrollBarVisible));
            var widened = this.GridColumn(grid, "Labels");
            this.Drag(grid, new(edge - 1, headerHeight / 2), new(edge + 320, headerHeight / 2));
            this.ExpectTrue(
                $"widening the last column by 320 px should make the grid scroll horizontally (Labels is now {this.Read(() => grid.Columns[widened].Width)} px wide)",
                this.Read(() => grid.IsHorizontalScrollBarVisible));
        });

        this.Check("DataGridView: the frozen first column stays put while the rest scrolls", () =>
        {
            if (!this.Read(() => grid.IsHorizontalScrollBarVisible))
            {
                this.Fail("the grid does not scroll horizontally, so nothing can be frozen against it");
                return;
            }

            var frozenBefore = this.Read(() => grid.GetCellBounds(3, 0));
            var looseBefore = this.Read(() => grid.GetCellBounds(3, 3));
            var size = this.Read(() => grid.Size);
            var thickness = this.Read(() => Theme.ScrollBarSize);
            var barY = size.Height - (thickness / 2);
            this.Drag(grid, new(thickness + 20, barY), new(size.Width / 2, barY));

            this.ExpectTrue("HorizontalOffset did not move with the scrollbar drag", this.Read(() => grid.HorizontalOffset) > 0);
            this.Expect("the frozen Task cell's x after scrolling", this.Read(() => grid.GetCellBounds(3, 0)).X, frozenBefore.X);
            this.ExpectTrue(
                $"the unfrozen Hours cell should have moved left of {looseBefore.X}, observed {this.Read(() => grid.GetCellBounds(3, 3)).X}",
                this.Read(() => grid.GetCellBounds(3, 3)).X < looseBefore.X);
        });

        this.Screenshot("state-grid-scrolled");
    }

    // --- Layout ---------------------------------------------------------------------------------

    /// <summary>Flow and table panels, the splitter, the expander and the auto-scrolling panel.</summary>
    private void DriveLayout()
    {
        Section("Layout");
        this.SelectTab(4);
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");

        this.Check("FlowLayoutPanel: a wrapped button is clickable where the panel laid it out", () =>
        {
            var flow = _form.Part<FlowLayoutPanel>("layout.flow");
            var clicked = 0;
            var button = this.Read(() => flow.Controls[5]);
            this.Do(() => button.Click += (_, _) => ++clicked);
            var landed = this.Click(button, this.Read(() => button.Width) / 2, this.Read(() => button.Height) / 2);
            this.Expect("the press landed on", landed, "GtkButton");
            this.Expect("the click count on the sixth flowed button", clicked, 1);
        });

        this.Check("TableLayoutPanel: a spanning cell is clickable where the table placed it", () =>
        {
            var table = _form.Part<TableLayoutPanel>("layout.table");
            var clicked = 0;
            var button = this.Read(() => table.Controls[1]);
            this.Do(() => button.Click += (_, _) => ++clicked);
            this.Click(button, this.Read(() => button.Width) / 2, this.Read(() => button.Height) / 2);
            this.Expect("the click count on the ColumnSpan = 2 button", clicked, 1);
        });

        var split = _form.Part<SplitContainer>("layout.split");

        this.Check("SplitContainer: dragging the splitter moves SplitterDistance", () =>
        {
            var before = this.Read(() => split.SplitterDistance);
            var height = this.Read(() => split.Height);
            var band = before + (this.Read(() => split.SplitterWidth) / 2);
            this.Drag(split, new(band, height / 2), new(band + 90, height / 2));
            var after = this.Read(() => split.SplitterDistance);
            this.ExpectNear("SplitterDistance after a 90 px drag", after, before + 90, 6);
            this.ExpectTrue(
                "the status line did not report the move",
                this.Read(() => status.Text).StartsWith("SplitContainer: splitter at ", StringComparison.Ordinal));
        });

        this.Check("SplitContainer: the splitter band shows a sizing cursor", () =>
        {
            var height = this.Read(() => split.Height);
            var distance = this.Read(() => split.SplitterDistance);
            var band = this.ScreenOf(split, distance + (this.Read(() => split.SplitterWidth) / 2), height / 2);
            this.Pump("hovering the band", () => Injection.Move(_root, band));
            this.Settle();
            this.ExpectTrue("no cursor was pushed onto the splitter band", this.Read(() => Injection.CursorAt(_root, band)) != 0);
        });

        var expander = _form.Part<Expander>("layout.expander");

        this.Check("Expander: the header collapses it and expands it back with every child", () =>
        {
            var childCount = this.Read(() => expander.Controls.Count);
            this.Expect("the expander's children", childCount, 5);
            this.ExpectTrue("the expander did not start expanded", this.Read(() => expander.Expanded));

            // The second child is the Host text box; whether a press at its place still reaches a
            // GtkEntry is the on-screen truth about a collapsed body, whatever Visible reports.
            var host = this.Read(() => expander.Controls[1]);
            var hostSpot = this.ScreenOf(host, this.Read(() => host.Width) / 2, this.Read(() => host.Height) / 2);

            this.Click(expander, 20, this.Read(() => expander.HeaderHeight) / 2);
            this.ExpectTrue("clicking the header did not collapse the expander", !this.Read(() => expander.Expanded));
            this.Expect("the status line", this.Read(() => status.Text), "Expander collapsed — click its header to toggle.");
            this.Expect("the children still reporting Visible while collapsed", this.Read(() => VisibleChildren(expander)), 0);
            var collapsedLanding = this.PressAt(hostSpot);
            this.ReleaseAt(hostSpot);
            this.ExpectTrue(
                $"a press where the collapsed body's Host text box used to be still reached a {collapsedLanding}",
                collapsedLanding != "GtkEntry");

            this.Click(expander, 20, this.Read(() => expander.HeaderHeight) / 2);
            this.ExpectTrue("clicking the header did not expand the expander again", this.Read(() => expander.Expanded));
            this.Expect("the visible children after expanding again", this.Read(() => VisibleChildren(expander)), childCount);
            var expandedLanding = this.PressAt(hostSpot);
            this.ReleaseAt(hostSpot);
            this.Expect("the widget a press on the re-expanded Host text box reaches", expandedLanding, "GtkEntry");
        });

        this.Screenshot("state-layout-expander");

        var panel = _form.Part<Panel>("layout.scrollPanel");

        this.Check("Panel: the wheel scrolls an AutoScroll panel and moves its children", () =>
        {
            var child = this.Read(() => panel.Controls[16]);
            var before = this.Read(() => child.PointToScreen(Point.Empty));
            this.Wheel(panel, 140, 90, -3);
            this.ExpectTrue(
                $"three wheel notches should push AutoScrollPosition.Y below zero, observed {this.Read(() => panel.AutoScrollPosition).Y}",
                this.Read(() => panel.AutoScrollPosition).Y < 0);

            var after = this.Read(() => child.PointToScreen(Point.Empty));
            this.ExpectTrue(
                $"the scrolled child should have moved up on screen, {before.Y} became {after.Y}",
                after.Y < before.Y);
        });

        this.Check("Panel: dragging the AutoScroll scrollbar scrolls the content", () =>
        {
            var size = this.Read(() => panel.Size);
            var thickness = this.Read(() => Theme.ScrollBarSize);
            var barX = size.Width - (thickness / 2);
            this.Do(() => panel.AutoScrollPosition = Point.Empty);
            var before = this.Read(() => panel.AutoScrollPosition).Y;
            this.Drag(panel, new(barX, 8), new(barX, size.Height - 20));
            this.ExpectTrue(
                $"dragging the panel's thumb down should scroll past {before}, observed {this.Read(() => panel.AutoScrollPosition).Y}",
                this.Read(() => panel.AutoScrollPosition).Y < before);
        });

        this.Check("Panel: the AutoScroll strip is not covered by a child that eats its presses", () =>
        {
            var size = this.Read(() => panel.Size);
            var thickness = this.Read(() => Theme.ScrollBarSize);
            var strip = this.ScreenOf(panel, size.Width - (thickness / 2), size.Height / 2);
            var landed = this.PressAt(strip);
            this.ReleaseAt(strip);
            this.Expect("the widget a press on the scrollbar strip reached", landed, "GtkFixed");
        });

        this.Check("Panel: scrolling moves a child out of the client area", () =>
        {
            this.Wheel(panel, 140, 90, -3);
            var child = this.Read(() => panel.Controls[0]);
            var panelTop = this.Read(() => panel.PointToScreen(Point.Empty)).Y;
            var childTop = this.Read(() => child.PointToScreen(Point.Empty)).Y;
            this.ExpectTrue(
                $"the first row should have scrolled above the panel's client top {panelTop}, observed {childTop}",
                childTop < panelTop);
        });
    }

    // --- Chrome ---------------------------------------------------------------------------------

    /// <summary>The menu bar, the tool strip, the status strip and the tooltip.</summary>
    private void DriveChrome()
    {
        Section("Chrome");
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");
        var menu = _form.Part<MenuStrip>("chrome.menu");
        var rowHeight = this.Read(() => Theme.RowHeight);

        this.Check("MenuStrip: clicking File opens its drop-down", () =>
        {
            this.Click(menu, 20, this.Read(() => menu.Height) / 2);
            this.Expect("MenuStrip.OpenIndex", this.Read(() => menu.OpenIndex), 0);
            this.ExpectTrue("no popup toplevel appeared for the File menu", this.WaitForPopup() != 0);
        });

        this.Screenshot("state-chrome-file-menu");

        this.Check("MenuStrip: clicking New runs the item and closes the menu", () =>
        {
            var popups = this.Popups();
            if (popups.Count == 0)
            {
                this.Fail("the File menu was not open");
                return;
            }

            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            this.ClickAt(new(bounds.X + 60, bounds.Y + 1 + (rowHeight / 2)));
            this.Expect("the status line", this.Read(() => status.Text), "File → New clicked.");
            this.Expect("MenuStrip.OpenIndex after the item ran", this.Read(() => menu.OpenIndex), -1);
        });

        this.Check("ToolStripMenuItem: a CheckOnClick item toggles from the menu", () =>
        {
            var autosave = _form.Part<ToolStripMenuItem>("menu.autosave");
            var before = this.Read(() => autosave.Checked);
            this.Click(menu, 20, this.Read(() => menu.Height) / 2);
            var popups = this.Popups();
            if (popups.Count == 0)
            {
                this.Fail("the File menu did not reopen");
                return;
            }

            // New, Open, Save, separator, Autosave — never Exit, which would quit the run.
            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            var top = 1 + (3 * rowHeight) + 5;
            this.ClickAt(new(bounds.X + 60, bounds.Y + top + (rowHeight / 2)));
            this.Expect("ToolStripMenuItem.Checked", this.Read(() => autosave.Checked), !before);
            this.Expect("the status line", this.Read(() => status.Text), $"Autosave is {(before ? "off" : "on")}.");
            this.Do(() => menu.CloseDropDown());
        });

        var tools = _form.Part<ToolStrip>("chrome.tools");

        this.Check("ToolStrip: the first button reports its click", () =>
        {
            this.Click(tools, 14, this.Read(() => tools.Height) / 2);
            this.Expect("the status line", this.Read(() => status.Text), "Toolbar: New clicked.");
        });

        this.Check("ToolStrip: the CheckOnClick button toggles", () =>
        {
            var pin = _form.Part<ToolStripButton>("tool.pin");
            var before = this.Read(() => pin.Checked);
            var x = this.ScanToolStrip(tools, () => this.Read(() => pin.Checked) != before);
            if (x < 0)
            {
                this.Fail("no x along the tool strip toggled the Pin button");
                return;
            }

            this.Expect("ToolStripButton.Checked", this.Read(() => pin.Checked), !before);
            this.Expect("the status line", this.Read(() => status.Text), $"Toolbar: Pin is {(before ? "off" : "on")}.");
        });

        this.Check("ToolStripSplitButton: the drop-down arrow opens the menu and an item runs", () =>
        {
            var width = this.Read(() => tools.Width);
            var height = this.Read(() => tools.Height) / 2;
            var opened = false;

            // Walk along the strip until a press pops a drop-down rather than running an item; the
            // press has to be observed before the release, which some surfaces dismiss on.
            for (var x = 6; x < width - 6 && !opened; x += 6)
            {
                var screen = this.ScreenOf(tools, x, height);
                this.PressAt(screen);
                opened = this.Popups().Count > 0;
                this.ReleaseAt(screen);
            }

            if (!opened)
            {
                this.Fail("no x along the tool strip opened the split button's drop-down");
                return;
            }

            var stillOpen = this.Popups();
            this.ExpectTrue("the drop-down was closed again by the mouse-up of the same click", stillOpen.Count > 0);
            if (stillOpen.Count == 0)
                return;

            var bounds = this.Read(() => Injection.WindowBounds(stillOpen[0]));
            this.ClickAt(new(bounds.X + 60, bounds.Y + 1 + (rowHeight / 2)));
            this.Expect("the status line", this.Read(() => status.Text), "Toolbar: Run tests picked.");
        });

        this.Check("StatusStrip: the label carries the last reported message", () =>
        {
            this.Do(() => status.Text = "sentinel");
            this.Click(tools, 14, this.Read(() => tools.Height) / 2);
            this.Expect("the status line", this.Read(() => status.Text), "Toolbar: New clicked.");
        });

        this.Check("ToolTip: hovering an owner-drawn control raises the tip after its initial delay", () =>
        {
            this.SelectTab(0);
            var tip = _form.Part<ToolTip>("chrome.toolTip");
            var toggle = _form.Part<ToggleSwitch>("basics.toggle");
            this.ExpectTrue("the toggle carries no registered tip", this.Read(() => tip.GetToolTip(toggle).Length) > 0);
            this.ExpectTrue($"no tip appeared over the ToggleSwitch", this.WaitForTip(tip, toggle, 200, 12));
            this.Do(tip.Hide);
        });

        this.Check("ToolTip: hovering a native-peer control raises the tip too", () =>
        {
            var tip = _form.Part<ToolTip>("chrome.toolTip");
            var click = _form.Part<Button>("basics.click");
            this.ExpectTrue("the button carries no registered tip", this.Read(() => tip.GetToolTip(click).Length) > 0);
            this.ExpectTrue("no tip appeared over the Button, whose peer is a native GtkButton", this.WaitForTip(tip, click, 60, 15));
            this.Do(tip.Hide);
        });
    }

    // --- The modal dialog, deliberately last ----------------------------------------------------

    /// <summary>
    /// The MessageBox button nests its own main loop, so the click is posted rather than awaited and
    /// the dialog is dismissed from the harness side. Run last: a dialog that refuses to close would
    /// otherwise strand every check behind it.
    /// </summary>
    private void DriveModalDialog()
    {
        Section("Modal dialog");
        this.SelectTab(0);
        var button = _form.Part<Button>("basics.dialog");
        var result = _form.Part<Label>("basics.dialogResult");

        this.Check("MessageBox: the button opens a modal dialog and Escape closes it with a result", () =>
        {
            this.Expect("the result label before the dialog", this.Read(() => result.Text), "Result: (not asked yet)");

            var screen = this.ScreenOf(button, 150, 15);
            this.Post(() =>
            {
                Injection.Move(_root, screen);
                Injection.Press(_root, screen, 1, 0);
                Injection.Release(_root, screen, 1, 0);
            });

            var dialog = this.WaitForPopup(4000);
            if (dialog == 0)
            {
                this.Fail("no modal dialog appeared within 4 s of clicking the button");
                return;
            }

            this.Screenshot("state-dialog-messagebox");
            this.KeyInto(dialog, KeySym.Escape);

            var deadline = Environment.TickCount64 + 4000;
            while (Environment.TickCount64 < deadline && this.Read(() => result.Text) == "Result: (not asked yet)")
                this.Settle(100);

            this.Expect("the echoed DialogResult", this.Read(() => result.Text), "Result: Cancel");
            this.ExpectTrue("the dialog is still on screen after Escape", this.Popups().Count == 0);
        });
    }

    // --- Small shared helpers -------------------------------------------------------------------

    /// <summary>
    /// Returns the open popup toplevels, reopening the surface through its own API first when a
    /// preceding gesture left it closed. The closing itself is reported by the check that drove the
    /// gesture; this only keeps the follow-up check — can an item be picked at all? — running.
    /// </summary>
    private List<nint> Reopen(Func<bool> isOpen, Action open)
    {
        if (!this.Read(isOpen))
        {
            this.Do(open);
            this.Settle(120);
        }

        return this.Popups();
    }

    /// <summary>
    /// Opens a surface straight through its own API — no input whatsoever — and reports when it does
    /// not survive the next turn of the main loop. That separates a routing problem in the harness
    /// from a surface that simply refuses to stay open.
    /// </summary>
    private void CheckProgrammaticOpen(string what, Func<bool> isOpen, Action open)
    {
        var immediately = false;
        this.Pump("a programmatic open", () =>
        {
            open();
            immediately = isOpen();
        });

        this.Settle(150);
        if (this.Read(isOpen))
            return;

        this.Fail(immediately
            ? $"{what}: opening it directly through its own API, with no input at all, still leaves it closed one main-loop iteration later"
            : $"{what}: opening it directly through its own API does not open it");
    }

    /// <summary>How far a popup's surface may sit from the anchor it was opened at before the
    /// placement counts as wrong. Not zero: a compositor may nudge a surface to keep it on screen.</summary>
    private const int _AnchorTolerance = 4;

    /// <summary>
    /// Opens a drop-down and reports whether its surface actually landed under the control that owns
    /// it, comparing the popup toplevel's own screen origin against the anchor the control hands
    /// <c>ShowAt</c> — directly below its bottom-left corner.
    /// </summary>
    /// <remarks>
    /// The rest of the walkthrough cannot see this. It aims its clicks at computed coordinates rather
    /// than at wherever the surface really is, so a drop-down that the display server parked somewhere
    /// else entirely still takes every gesture and every check still passes, while the user looks at a
    /// list floating off the side of the window. Asking the surface where it is is the only assertion
    /// that notices — it is what caught popups being created without a transient parent, which leaves
    /// a display server free to place them wherever it likes.
    /// </remarks>
    private void CheckPopupAnchored(string what, Control owner, Func<bool> isOpen, Action open)
    {
        var popups = this.Reopen(isOpen, open);
        if (popups.Count == 0)
        {
            this.Fail($"{what}: no popup toplevel to measure — it could not be opened");
            return;
        }

        var anchor = this.ScreenOf(owner, 0, this.Read(() => owner.Height));
        var origin = this.Read(() => Injection.WindowBounds(popups[0])).Location;
        if (Math.Abs(origin.X - anchor.X) > _AnchorTolerance || Math.Abs(origin.Y - anchor.Y) > _AnchorTolerance)
            this.Fail($"{what}: opened at {anchor} but its surface sits at {origin}");
    }

    /// <summary>
    /// Opens a grid cell's editor by double-clicking it — the classic grid's own open gesture, and
    /// the one that works on every backend — and returns the popup toplevel that appeared, or 0
    /// having already reported the failure. The two popup-list column kinds are driven through this,
    /// so a cell that refuses to edit at all is told apart from a popup that opened somewhere
    /// unexpected.
    /// </summary>
    private nint OpenCellEditorPopup(DataGridView grid, int rowIndex, int columnIndex)
    {
        var cell = this.Read(() => grid.GetCellBounds(rowIndex, columnIndex));
        var screen = this.ScreenOf(grid, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));

        // Two ordinary clicks, never a synthetic GDK double-press on top of them: the grid times the
        // double-click itself, and a third press inside the gesture would land on the grid while the
        // editor is already up — which the grid rightly reads as a press outside the editor and closes it.
        this.ClickAt(screen);
        var phases = this.ProbeOpen(screen, () => grid.IsEditing);
        if (!this.Read(() => grid.IsEditing))
        {
            this.Fail($"a double-click on cell ({rowIndex},{columnIndex}) did not leave an edit open — {DropDownPhases("editor", phases)}");
            return 0;
        }

        var popups = this.Popups();
        if (popups.Count != 0)
            return popups[0];

        this.Fail($"cell ({rowIndex},{columnIndex}) edits, but no popup toplevel is on screen for it");
        return 0;
    }

    /// <summary>Names the phase in which a drop-down lost its open state, for the failure line.</summary>
    private static string DropDownPhases(string what, (bool OnPress, bool AfterSettle, bool AfterRelease) phases) => phases switch
    {
        { OnPress: false } => $"the {what} never opened",
        { AfterSettle: false } => $"the {what} opened inside the mouse-down dispatch and was gone again as soon as the loop went idle, before any further input",
        { AfterRelease: false } => $"the {what} opened on mouse-down and was closed again by the mouse-up of the very same click",
        _ => $"the {what} is open",
    };

    /// <summary>Hovers a control and waits out the tip's initial delay, reporting whether it showed.</summary>
    private bool WaitForTip(ToolTip tip, Control control, int dx, int dy)
    {
        this.Hover(control, dx, dy);
        var deadline = Environment.TickCount64 + this.Read(() => tip.InitialDelay) + 1500;
        while (Environment.TickCount64 < deadline)
        {
            if (this.Read(() => tip.Active))
                return true;

            this.Settle(50);
        }

        return false;
    }

    /// <summary>Clicks along a tool strip until a condition holds, returning the x that did it.</summary>
    private int ScanToolStrip(ToolStrip strip, Func<bool> until)
    {
        var width = this.Read(() => strip.Width);
        var y = this.Read(() => strip.Height) / 2;
        for (var x = 6; x < width - 6; x += 6)
        {
            this.Click(strip, x, y);
            if (until())
                return x;
        }

        return -1;
    }

    /// <summary>The centre of a scrollbar's thumb, mirroring the renderer's own geometry.</summary>
    private static Point ThumbCentre(Size size, bool vertical, int minimum, int maximum, int value, int largeChange)
    {
        var length = vertical ? size.Height : size.Width;
        var thickness = vertical ? size.Width : size.Height;
        var arrow = Math.Min(thickness, length / 2);
        var track = length - (2 * arrow);
        var range = maximum - minimum + 1;
        var thumb = range <= 0 ? track : Math.Clamp(track * largeChange / range, Math.Min(8, track), track);
        var maximumValue = Math.Max(minimum, maximum - largeChange + 1);
        var travel = track - thumb;
        var offset = maximumValue > minimum && travel > 0 ? travel * (value - minimum) / (maximumValue - minimum) : 0;
        var centre = arrow + offset + (thumb / 2);
        return vertical ? new(thickness / 2, centre) : new(centre, thickness / 2);
    }

    /// <summary>The centre of a calendar day cell, mirroring <c>CalendarCore</c>'s own hit test.</summary>
    /// <summary>The centre of a cell on a drilled-out calendar page: a 4×3 grid under the title
    /// row, indexed 0–11 in reading order.</summary>
    private static Point PeriodCell(Size size, int rowHeight, int index)
    {
        var cellWidth = size.Width / 4;
        var cellHeight = (size.Height - rowHeight) / 3;
        return new(((index % 4) * cellWidth) + (cellWidth / 2), rowHeight + ((index / 4) * cellHeight) + (cellHeight / 2));
    }

    private static Point DayCell(Size size, int rowHeight, int row, int column)
    {
        var top = 2 * rowHeight;
        var cellWidth = size.Width / 7;
        var cellHeight = (size.Height - top) / 6;
        return new((column * cellWidth) + (cellWidth / 2), top + (row * cellHeight) + (cellHeight / 2));
    }

    /// <summary>How many of a container's children report themselves visible.</summary>
    private static int VisibleChildren(Control parent)
    {
        var count = 0;
        foreach (var child in parent.Controls)
            if (child.Visible)
                ++count;

        return count;
    }

    /// <summary>Counts a sequence without pulling LINQ into the demo.</summary>
    private static int Count(IEnumerable<object?> items)
    {
        var count = 0;
        foreach (var _ in items)
            ++count;

        return count;
    }
}
