using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Input page: text boxes (single-line, placeholder, password, multiline), a masked text box
    /// with a phone mask, a rich text box pre-styled through <see cref="RichTextBox.Rtf"/>, both
    /// up-down spinners, a search box, a track bar and both scroll bars each echoing their value
    /// into a label, a date-time picker and a month calendar.
    /// </summary>
    private TabPage BuildInputPage()
    {
        var page = new TabPage("Input") { ImageIndex = _IconGreen };

        // --- Column 1: text editors -------------------------------------------------------------

        var single = new TextBox { Bounds = new(16, 36, 300, 26), Text = "A single-line text box." };
        var placeholder = new TextBox { Bounds = new(16, 68, 300, 26), PlaceholderText = "Placeholder until you type…" };
        var password = new TextBox { Bounds = new(16, 100, 300, 26), UseSystemPasswordChar = true, Text = "hunter2" };
        var multiline = new TextBox
        {
            Bounds = new(16, 132, 300, 72),
            Multiline = true,
            Text = "A multiline text box.\nLine two.\nLine three.",
        };

        var masked = new MaskedTextBox { Bounds = new(16, 234, 300, 26), Mask = "(000) 000-0000" };
        masked.MaskedTextChanged += (_, _) =>
        {
            if (masked.MaskCompleted)
                this.SetStatus($"Phone number completed: {masked.Text}");
        };

        var rich = new RichTextBox
        {
            Bounds = new(16, 290, 300, 110),
            Rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil;}}{\colortbl ;\red200\green80\blue0;}\pard\ql {\b Bold}, {\i italic} and {\cf1\fs28 colored} rich text.}",
        };

        page.Controls.AddRange(
            Caption("TextBox", 16, 12),
            single, placeholder, password, multiline,
            Caption("MaskedTextBox (phone mask)", 16, 210),
            masked,
            Caption("RichTextBox (pre-styled Rtf)", 16, 266),
            rich);

        // --- Column 2: spinners, search, sliders ------------------------------------------------

        var numeric = new NumericUpDown
        {
            Bounds = new(340, 36, 160, 26),
            Minimum = 0,
            Maximum = 100,
            Increment = 0.5m,
            DecimalPlaces = 1,
            Value = 42.5m,
        };
        numeric.ValueChanged += (_, _) => this.SetStatus($"NumericUpDown: {numeric.Value}");

        var domain = new DomainUpDown { Bounds = new(340, 92, 160, 26), Wrap = true };
        domain.Items.AddRange(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]);
        domain.SelectedIndex = 2;
        domain.SelectedItemChanged += (_, _) => this.SetStatus($"DomainUpDown: {domain.SelectedItem}");

        var search = new SearchBox { Bounds = new(340, 148, 300, 26), PlaceholderText = "Search controls…" };
        search.SearchCommitted += (_, _) => this.SetStatus($"Search committed: \"{search.Text}\"");
        search.SearchCleared += (_, _) => this.SetStatus("Search cleared.");
        _toolTip.SetToolTip(search, "Enter commits, Escape clears.");

        var track = new TrackBar
        {
            Bounds = new(340, 204, 240, 30),
            Minimum = 0,
            Maximum = 10,
            TickFrequency = 1,
            Value = 7,
        };
        var trackValue = new Label { Bounds = new(590, 210, 50, 18), Text = "7" };
        track.ValueChanged += (_, _) => trackValue.Text = $"{track.Value}";

        var horizontal = new HScrollBar { Bounds = new(340, 264, 240, 16), Maximum = 100, LargeChange = 10, Value = 30 };
        var horizontalValue = new Label { Bounds = new(590, 262, 50, 18), Text = "30" };
        horizontal.ValueChanged += (_, _) => horizontalValue.Text = $"{horizontal.Value}";

        var vertical = new VScrollBar { Bounds = new(340, 310, 16, 120), Maximum = 100, LargeChange = 10, Value = 60 };
        var verticalValue = new Label { Bounds = new(366, 310, 50, 18), Text = "60" };
        vertical.ValueChanged += (_, _) => verticalValue.Text = $"{vertical.Value}";

        page.Controls.AddRange(
            Caption("NumericUpDown", 340, 12),
            numeric,
            Caption("DomainUpDown", 340, 68),
            domain,
            Caption("SearchBox", 340, 124),
            search,
            Caption("TrackBar", 340, 180),
            track, trackValue,
            Caption("HScrollBar", 340, 240),
            horizontal, horizontalValue,
            Caption("VScrollBar", 340, 286),
            vertical, verticalValue);

        // --- Column 3: date and time ------------------------------------------------------------

        var shortPicker = new DateTimePicker
        {
            Bounds = new(664, 36, 220, 26),
            Format = DateTimePickerFormat.Short,
            ShowCheckBox = true,
            Checked = true,
        };
        shortPicker.ValueChanged += (_, _) => this.SetStatus($"DateTimePicker: {shortPicker.Value:yyyy-MM-dd}");
        var customPicker = new DateTimePicker
        {
            Bounds = new(664, 68, 220, 26),
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
        };

        var calendar = new MonthCalendar
        {
            Bounds = new(664, 124, 240, 200),
            FirstDayOfWeek = DayOfWeek.Monday,
            MaxSelectionCount = 7,
        };
        calendar.DateSelected += (_, e)
            => this.SetStatus($"MonthCalendar: {e.Start:yyyy-MM-dd} … {e.End:yyyy-MM-dd}");

        page.Controls.AddRange(
            Caption("DateTimePicker (Short + Custom)", 664, 12),
            shortPicker, customPicker,
            Caption("MonthCalendar (week selections)", 664, 100),
            calendar);

        // Snapshotted the instant each value was authored, so the restore cannot drift away from it.
        var singleText = single.Text;
        var placeholderText = placeholder.Text;
        var passwordText = password.Text;
        var multilineText = multiline.Text;
        var maskedText = masked.Text;
        var richRtf = rich.Rtf;
        var numericValue = numeric.Value;
        var domainIndex = domain.SelectedIndex;
        var searchText = search.Text;
        var trackValueNumber = track.Value;
        var horizontalValueNumber = horizontal.Value;
        var verticalValueNumber = vertical.Value;
        var pickerValue = shortPicker.Value;
        var pickerChecked = shortPicker.Checked;
        var customValue = customPicker.Value;
        var calendarStart = calendar.SelectionStart;
        var calendarEnd = calendar.SelectionEnd;
        this.OnReset(() =>
        {
            single.Text = singleText;
            placeholder.Text = placeholderText;
            password.Text = passwordText;
            multiline.Text = multilineText;
            masked.Text = maskedText;
            rich.Rtf = richRtf;
            numeric.Value = numericValue;
            domain.SelectedIndex = domainIndex;
            search.Text = searchText;
            track.Value = trackValueNumber;
            horizontal.Value = horizontalValueNumber;
            vertical.Value = verticalValueNumber;
            shortPicker.Value = pickerValue;
            shortPicker.Checked = pickerChecked;
            customPicker.Value = customValue;

            // Restoring the range also pages the calendar back to the month that holds it, so a
            // walkthrough that browsed backwards does not leave the shot on the wrong month.
            calendar.SetSelectionRange(calendarStart, calendarEnd);

            // The echoing labels follow their sliders through ValueChanged, which a restore to the
            // authored value does not raise when the walkthrough happened to leave it there.
            trackValue.Text = $"{track.Value}";
            horizontalValue.Text = $"{horizontal.Value}";
            verticalValue.Text = $"{vertical.Value}";
        });

        this.Publish("input.page", page);
        this.Publish("input.single", single);
        this.Publish("input.placeholder", placeholder);
        this.Publish("input.password", password);
        this.Publish("input.multiline", multiline);
        this.Publish("input.masked", masked);
        this.Publish("input.rich", rich);
        this.Publish("input.numeric", numeric);
        this.Publish("input.domain", domain);
        this.Publish("input.search", search);
        this.Publish("input.track", track);
        this.Publish("input.trackValue", trackValue);
        this.Publish("input.hscroll", horizontal);
        this.Publish("input.vscroll", vertical);
        this.Publish("input.picker", shortPicker);
        this.Publish("input.calendar", calendar);

        return page;
    }
}
