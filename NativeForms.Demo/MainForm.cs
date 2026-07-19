using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The demo window: a gallery with one captioned section per shipped control, each configured with
/// non-default property values. The button section keeps the original MVVM wiring — an <c>ICommand</c>
/// for the action and a one-way <see cref="PropertyBinding{T}"/> pushing the view-model's text onto a
/// label — and a second binding drives a progress bar from the same counter.
/// </summary>
/// <remarks>
/// There is no layout engine yet, so every control sits at absolute <see cref="Control.Bounds"/> in
/// three labelled columns. Only the form's direct children are realized, so the <see cref="Panel"/>
/// and <see cref="GroupBox"/> sections show the frames themselves with the other controls placed
/// beside them, and all radio buttons on the form share one exclusivity group. Icons are omitted
/// throughout because an <see cref="IImage"/> only exists once a backend is realized.
/// </remarks>
internal sealed class MainForm : Form
{
    private const int _Column1 = 20;
    private const int _Column2 = 340;
    private const int _Column3 = 680;
    private const int _ColumnWidth = 300;

    private readonly CounterViewModel _viewModel = new();

    // Held so the bindings are not garbage-collected for the window's lifetime.
    private readonly PropertyBinding<string> _labelBinding;
    private readonly PropertyBinding<int> _progressBinding;

    public MainForm()
    {
        this.Text = "NativeForms Gallery";
        this.Bounds = new(Point.Empty, new Size(1000, 700));

        _labelBinding = this.BuildButtonSection();
        this.BuildLabelSection();
        this.BuildCheckBoxSection();
        this.BuildRadioButtonSection();
        _progressBinding = this.BuildProgressBarSection();
        this.BuildContainerSection();
        this.BuildListBoxSection();
        this.BuildListViewSection();
        this.BuildDataGridViewSection();
    }

    /// <summary>Adds a section caption label at the given position.</summary>
    private void AddCaption(string text, int x, int y)
        => this.Controls.Add(new Label { Bounds = new(x, y, _ColumnWidth, 20), Text = text });

    /// <summary>
    /// Buttons: the MVVM click counter (command in, bound label out), a disabled button and a hidden
    /// one. Returns the label binding so the constructor can keep it alive.
    /// </summary>
    private PropertyBinding<string> BuildButtonSection()
    {
        this.AddCaption("Button", _Column1, 15);

        var counterLabel = new Label
        {
            Bounds = new(_Column1, 40, _ColumnWidth, 22),
            Text = _viewModel.Display,
        };

        var clickButton = new Button
        {
            Bounds = new(_Column1, 68, 140, 32),
            Text = "Click me",
        };
        clickButton.Click += (_, _) => _viewModel.Increment.Execute(null);

        var disabledButton = new Button
        {
            Bounds = new(_Column1 + 150, 68, 130, 32),
            Text = "Disabled",
            Enabled = false,
        };

        var hiddenButton = new Button
        {
            Bounds = new(_Column1, 108, 140, 32),
            Text = "Hidden",
            Visible = false,
        };

        var hiddenNote = new Label
        {
            Bounds = new(_Column1 + 150, 113, 130, 22),
            Text = "<- Visible = false",
        };

        this.Controls.AddRange(counterLabel, clickButton, disabledButton, hiddenButton, hiddenNote);

        return new(
            _viewModel,
            nameof(CounterViewModel.Count),
            () => _viewModel.Display,
            text => counterLabel.Text = text);
    }

    /// <summary>Labels: a plain one and a disabled one painted in the theme's disabled text color.</summary>
    private void BuildLabelSection()
    {
        this.AddCaption("Label", _Column1, 160);

        var plain = new Label
        {
            Bounds = new(_Column1, 185, _ColumnWidth, 22),
            Text = "A plain single-line label.",
        };

        var disabled = new Label
        {
            Bounds = new(_Column1, 209, _ColumnWidth, 22),
            Text = "A disabled label (Enabled = false).",
            Enabled = false,
        };

        this.Controls.AddRange(plain, disabled);
    }

    /// <summary>
    /// Check boxes: one reporting <see cref="CheckBox.CheckedChanged"/> into a feedback label, one
    /// pre-checked and one disabled.
    /// </summary>
    private void BuildCheckBoxSection()
    {
        this.AddCaption("CheckBox", _Column1, 250);

        var toggle = new CheckBox
        {
            Bounds = new(_Column1, 275, _ColumnWidth, 20),
            Text = "Toggle me",
        };

        var preChecked = new CheckBox
        {
            Bounds = new(_Column1, 301, _ColumnWidth, 20),
            Text = "Checked from the start",
            Checked = true,
        };

        var disabled = new CheckBox
        {
            Bounds = new(_Column1, 327, _ColumnWidth, 20),
            Text = "Disabled",
            Enabled = false,
        };

        var feedback = new Label
        {
            Bounds = new(_Column1, 353, _ColumnWidth, 22),
            Text = "The first box is unchecked.",
        };
        toggle.CheckedChanged += (_, _)
            => feedback.Text = toggle.Checked ? "The first box is checked." : "The first box is unchecked.";

        this.Controls.AddRange(toggle, preChecked, disabled, feedback);
    }

    /// <summary>
    /// Radio buttons: three exclusive options reporting the selection into a feedback label. They are
    /// siblings on the form, so selecting one unchecks the others.
    /// </summary>
    private void BuildRadioButtonSection()
    {
        this.AddCaption("RadioButton", _Column1, 395);

        var small = new RadioButton { Bounds = new(_Column1, 420, _ColumnWidth, 20), Text = "Small" };
        var medium = new RadioButton { Bounds = new(_Column1, 446, _ColumnWidth, 20), Text = "Medium" };
        var large = new RadioButton { Bounds = new(_Column1, 472, _ColumnWidth, 20), Text = "Large" };

        var feedback = new Label
        {
            Bounds = new(_Column1, 498, _ColumnWidth, 22),
            Text = "Selected size: (none)",
        };

        void Report(RadioButton radio) => radio.CheckedChanged += (_, _) =>
        {
            if (radio.Checked)
                feedback.Text = $"Selected size: {radio.Text}";
        };

        Report(small);
        Report(medium);
        Report(large);
        medium.Checked = true;

        this.Controls.AddRange(small, medium, large, feedback);
    }

    /// <summary>
    /// Progress bars: static values at 0, 50 and 100 percent plus a fourth bar bound to the click
    /// counter (ten percent per click). Returns the value binding so the constructor can keep it alive.
    /// </summary>
    private PropertyBinding<int> BuildProgressBarSection()
    {
        this.AddCaption("ProgressBar", _Column2, 15);

        var empty = new ProgressBar { Bounds = new(_Column2, 40, 230, 18), Value = 0 };
        var half = new ProgressBar { Bounds = new(_Column2, 64, 230, 18), Value = 50 };
        var full = new ProgressBar { Bounds = new(_Column2, 88, 230, 18), Value = 100 };
        var counterBar = new ProgressBar { Bounds = new(_Column2, 112, 230, 18) };

        var emptyLabel = new Label { Bounds = new(_Column2 + 240, 38, 60, 20), Text = "0 %" };
        var halfLabel = new Label { Bounds = new(_Column2 + 240, 62, 60, 20), Text = "50 %" };
        var fullLabel = new Label { Bounds = new(_Column2 + 240, 86, 60, 20), Text = "100 %" };
        var counterNote = new Label
        {
            Bounds = new(_Column2, 136, _ColumnWidth, 22),
            Text = "The last bar follows the click counter.",
        };

        this.Controls.AddRange(empty, half, full, counterBar, emptyLabel, halfLabel, fullLabel, counterNote);

        return new(
            _viewModel,
            nameof(CounterViewModel.Count),
            () => _viewModel.Count * 10,
            value => counterBar.Value = value);
    }

    /// <summary>
    /// Containers: one panel per <see cref="BorderStyle"/> and a captioned group box. Only the form's
    /// direct children are realized, so the frames are shown empty with everything else beside them.
    /// </summary>
    private void BuildContainerSection()
    {
        this.AddCaption("Panel + GroupBox", _Column2, 175);

        var single = new Panel { Bounds = new(_Column2, 200, 88, 64), BorderStyle = BorderStyle.FixedSingle };
        var threeD = new Panel { Bounds = new(_Column2 + 106, 200, 88, 64), BorderStyle = BorderStyle.Fixed3D };
        var borderless = new Panel { Bounds = new(_Column2 + 212, 200, 88, 64) };

        var singleLabel = new Label { Bounds = new(_Column2, 268, 100, 18), Text = "FixedSingle" };
        var threeDLabel = new Label { Bounds = new(_Column2 + 106, 268, 100, 18), Text = "Fixed3D" };
        var borderlessLabel = new Label { Bounds = new(_Column2 + 212, 268, 88, 18), Text = "None" };

        var group = new GroupBox
        {
            Bounds = new(_Column2, 294, _ColumnWidth, 76),
            Text = "Group caption",
        };

        var note = new Label
        {
            Bounds = new(_Column2, 378, _ColumnWidth, 22),
            Text = "Frames only; controls sit beside them.",
        };

        this.Controls.AddRange(single, threeD, borderless, singleLabel, threeDLabel, borderlessLabel, group, note);
    }

    /// <summary>ListBox: weekday items at a custom row height, selection echoed into a label.</summary>
    private void BuildListBoxSection()
    {
        this.AddCaption("ListBox", _Column2, 415);

        var listBox = new ListBox
        {
            Bounds = new(_Column2, 440, _ColumnWidth, 136),
            ItemHeight = 20,
        };
        listBox.Items.AddRange(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]);

        var feedback = new Label
        {
            Bounds = new(_Column2, 582, _ColumnWidth, 22),
            Text = "Selected: (none)",
        };
        listBox.SelectedIndexChanged += (_, _) => feedback.Text = $"Selected: {listBox.SelectedItem}";
        listBox.SelectedIndex = 2;

        this.Controls.AddRange(listBox, feedback);
    }

    /// <summary>
    /// List views: a Details layout with three columns, sub-item cells and a taller row height, plus a
    /// second instance switched to the List layout.
    /// </summary>
    private void BuildListViewSection()
    {
        this.AddCaption("ListView (Details)", _Column3, 15);

        var details = new ListView
        {
            Bounds = new(_Column3, 40, _ColumnWidth, 180),
            ItemHeight = 24,
        };
        details.Columns.AddRange([
            new ColumnHeader("Name", 120),
            new ColumnHeader("Size", 80),
            new ColumnHeader("Type", 96),
        ]);
        details.Items.AddRange([
            new ListViewItem("Readme.md", "4 KB", "Markdown"),
            new ListViewItem("MainForm.cs", "12 KB", "Source"),
            new ListViewItem("Logo.png", "48 KB", "Image"),
            new ListViewItem("Data.bin", "1 MB", "Binary"),
            new ListViewItem("Notes.txt", "2 KB", "Text"),
        ]);

        var feedback = new Label
        {
            Bounds = new(_Column3, 226, _ColumnWidth, 22),
            Text = "Selected: (none)",
        };
        details.SelectedIndexChanged += (_, _) => feedback.Text = $"Selected: {details.SelectedItem?.Text ?? "(none)"}";
        details.SelectedIndex = 0;

        this.AddCaption("ListView (List)", _Column3, 495);

        var list = new ListView
        {
            Bounds = new(_Column3, 520, _ColumnWidth, 110),
            View = ListViewView.List,
        };
        list.Items.AddRange([
            new ListViewItem("Alpha"),
            new ListViewItem("Beta"),
            new ListViewItem("Gamma"),
            new ListViewItem("Delta"),
        ]);

        this.Controls.AddRange(details, feedback, list);
    }

    /// <summary>
    /// Data grid: rows bound through the grid's <see cref="ObservableList{T}"/>, columns mapped by
    /// reflection-free selectors, with alternating row tint, a taller row height and a right-aligned
    /// numeric column. Selection is echoed into a label.
    /// </summary>
    private void BuildDataGridViewSection()
    {
        this.AddCaption("DataGridView", _Column3, 260);

        var grid = new DataGridView
        {
            Bounds = new(_Column3, 285, _ColumnWidth, 172),
            RowHeight = 24,
            AlternatingRows = true,
        };
        grid.Columns.Add(new DataGridViewColumn("Part", static row => ((Part)row!).Name) { Width = 140 });
        grid.Columns.Add(new DataGridViewColumn("Stock", static row => ((Part)row!).Stock)
        {
            Width = 80,
            Alignment = ContentAlignment.MiddleRight,
        });
        grid.Columns.Add(new DataGridViewColumn("Unit", static row => ((Part)row!).Unit) { Width = 76 });
        grid.Items.AddRange([
            new Part("Bolt M3", 240, "pcs"),
            new Part("Nut M3", 180, "pcs"),
            new Part("Washer", 500, "pcs"),
            new Part("Spring", 32, "pcs"),
            new Part("O-Ring", 75, "pcs"),
        ]);

        var feedback = new Label
        {
            Bounds = new(_Column3, 462, _ColumnWidth, 22),
            Text = "Selected: (none)",
        };
        grid.SelectionChanged += (_, _)
            => feedback.Text = grid.SelectedItem is Part part ? $"Selected: {part.Name}" : "Selected: (none)";
        grid.SelectedRowIndex = 1;

        this.Controls.AddRange(grid, feedback);
    }

    /// <summary>A row item for the data-grid section.</summary>
    private sealed record Part(string Name, int Stock, string Unit);
}
