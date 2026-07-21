using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Basics page: the MVVM click counter (command in, bound label and progress bar out), a
    /// button opening a modal message box and echoing its <see cref="DialogResult"/>, labels with
    /// <see cref="Label.AutoSize"/> and <see cref="Label.TextAlign"/>, a link label, check boxes
    /// (one with a generated icon), a toggle switch, a radio-button group nested in a real
    /// <see cref="GroupBox"/>, progress bars at 0/50/100 plus a marquee, and a picture box showing a
    /// generated ARGB gradient with a context menu.
    /// </summary>
    private TabPage BuildBasicsPage()
    {
        var page = new TabPage("Basics") { ImageIndex = _IconBlue };

        // --- Column 1: buttons and the MVVM counter ---------------------------------------------

        var counterLabel = new Label { Bounds = new(16, 36, 300, 22), Text = _viewModel.Display };
        var counterBar = new ProgressBar { Bounds = new(16, 62, 300, 16) };
        var clickButton = new Button { Bounds = new(16, 88, 145, 30), Text = "Click me" };
        clickButton.Click += (_, _) => _viewModel.Increment.Execute(null);
        var disabledButton = new Button { Bounds = new(171, 88, 145, 30), Text = "Disabled", Enabled = false };
        _toolTip.SetToolTip(clickButton, "Executes the view-model's RelayCommand.");

        _labelBinding = new(
            _viewModel,
            nameof(CounterViewModel.Count),
            () => _viewModel.Display,
            text => counterLabel.Text = text);
        _progressBinding = new(
            _viewModel,
            nameof(CounterViewModel.Count),
            () => _viewModel.Count * 10,
            value => counterBar.Value = value);

        var dialogButton = new Button { Bounds = new(16, 158, 300, 30), Text = "Show a modal MessageBox…" };
        var dialogResultLabel = new Label { Bounds = new(16, 194, 300, 22), Text = "Result: (not asked yet)" };
        dialogButton.Click += (_, _) =>
        {
            var result = MessageBox.Show(
                "Keep the unsaved gallery changes?",
                "NativeForms",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            dialogResultLabel.Text = $"Result: {result}";
        };

        page.Controls.AddRange(
            Caption("Button + MVVM counter", 16, 12),
            counterLabel, counterBar, clickButton, disabledButton,
            Caption("Button → modal dialog", 16, 134),
            dialogButton, dialogResultLabel);

        // --- Column 2: labels, link, check boxes, toggle switch ---------------------------------

        var autoLabel = new Label
        {
            Bounds = new(340, 36, 10, 10),
            Text = "AutoSize measures this label.",
            AutoSize = true,
        };
        var centeredLabel = new Label
        {
            Bounds = new(340, 62, 300, 26),
            Text = "TextAlign = MiddleCenter",
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
        };

        var link = new LinkLabel { Bounds = new(340, 114, 300, 20), Text = "Open the project page" };
        link.LinkClicked += (_, _) =>
        {
            link.LinkVisited = true;
            this.SetStatus("LinkLabel clicked — now painted as visited.");
        };

        var plainCheck = new CheckBox { Bounds = new(340, 166, 300, 20), Text = "Plain check box" };
        plainCheck.CheckedChanged += (_, _)
            => this.SetStatus($"The plain check box is {(plainCheck.Checked ? "checked" : "unchecked")}.");
        var preChecked = new CheckBox { Bounds = new(340, 192, 300, 20), Text = "Checked from the start", Checked = true };
        var iconCheck = new CheckBox
        {
            Bounds = new(340, 218, 300, 20),
            Text = "With a generated icon",
            Image = this.DiscImage(Color.MediumOrchid),
        };
        var disabledCheck = new CheckBox { Bounds = new(340, 244, 300, 20), Text = "Disabled", Enabled = false };

        var toggle = new ToggleSwitch { Bounds = new(340, 296, 300, 24), Text = "Notifications", Checked = true };
        toggle.CheckedChanged += (_, _)
            => this.SetStatus($"Notifications are {(toggle.Checked ? "on" : "off")}.");
        _toolTip.SetToolTip(toggle, "An owner-drawn on/off switch.");

        page.Controls.AddRange(
            Caption("Label", 340, 12),
            autoLabel, centeredLabel,
            Caption("LinkLabel", 340, 90),
            link,
            Caption("CheckBox", 340, 142),
            plainCheck, preChecked, iconCheck, disabledCheck,
            Caption("ToggleSwitch", 340, 272),
            toggle);

        // --- Column 3: grouped radios, progress bars, picture box -------------------------------

        var group = new GroupBox { Bounds = new(664, 36, 300, 108), Text = "Size" };
        var small = new RadioButton { Bounds = new(16, 26, 260, 20), Text = "Small" };
        var medium = new RadioButton { Bounds = new(16, 52, 260, 20), Text = "Medium" };
        var large = new RadioButton { Bounds = new(16, 78, 260, 20), Text = "Large" };
        void Report(RadioButton radio) => radio.CheckedChanged += (_, _) =>
        {
            if (radio.Checked)
                this.SetStatus($"Selected size: {radio.Text}");
        };
        Report(small);
        Report(medium);
        Report(large);
        group.Controls.AddRange(small, medium, large);
        medium.Checked = true;

        var empty = new ProgressBar { Bounds = new(664, 182, 240, 16), Value = 0 };
        var half = new ProgressBar { Bounds = new(664, 204, 240, 16), Value = 50 };
        var full = new ProgressBar { Bounds = new(664, 226, 240, 16), Value = 100 };
        var marquee = new ProgressBar { Bounds = new(664, 248, 240, 16), Style = ProgressBarStyle.Marquee };
        var emptyLabel = new Label { Bounds = new(914, 180, 50, 18), Text = "0 %" };
        var halfLabel = new Label { Bounds = new(914, 202, 50, 18), Text = "50 %" };
        var fullLabel = new Label { Bounds = new(914, 224, 50, 18), Text = "100 %" };
        _toolTip.SetToolTip(marquee, "Style = Marquee sweeps forever.");

        var picture = new PictureBox
        {
            Bounds = new(664, 296, 300, 140),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BorderStyle = BorderStyle.FixedSingle,
            Image = _backend.CreateImage(150, 70, GradientPixels(150, 70, Color.RoyalBlue, Color.Orange)),
        };
        var pictureMenu = new ContextMenuStrip();
        var cool = new ToolStripMenuItem("Blue → orange gradient");
        cool.Click += (_, _) =>
        {
            picture.Image = _backend.CreateImage(150, 70, GradientPixels(150, 70, Color.RoyalBlue, Color.Orange));
            this.SetStatus("PictureBox: blue → orange gradient regenerated.");
        };
        var warm = new ToolStripMenuItem("Green → purple gradient");
        warm.Click += (_, _) =>
        {
            picture.Image = _backend.CreateImage(150, 70, GradientPixels(150, 70, Color.SeaGreen, Color.MediumOrchid));
            this.SetStatus("PictureBox: green → purple gradient regenerated.");
        };
        var clear = new ToolStripMenuItem("Clear image");
        clear.Click += (_, _) =>
        {
            picture.Image = null;
            this.SetStatus("PictureBox: image cleared.");
        };
        pictureMenu.Items.AddRange(cool, warm, new ToolStripSeparator(), clear);
        picture.ContextMenuStrip = pictureMenu;
        _toolTip.SetToolTip(picture, "Right-click for gradient options.");

        page.Controls.AddRange(
            Caption("RadioButton (nested in a GroupBox)", 664, 12),
            group,
            Caption("ProgressBar", 664, 158),
            empty, half, full, marquee, emptyLabel, halfLabel, fullLabel,
            Caption("PictureBox (generated gradient)", 664, 272),
            picture);

        this.Publish("basics.page", page);
        this.Publish("basics.counterLabel", counterLabel);
        this.Publish("basics.counterBar", counterBar);
        this.Publish("basics.click", clickButton);
        this.Publish("basics.disabled", disabledButton);
        this.Publish("basics.dialog", dialogButton);
        this.Publish("basics.dialogResult", dialogResultLabel);
        this.Publish("basics.link", link);
        this.Publish("basics.check", plainCheck);
        this.Publish("basics.checkDisabled", disabledCheck);
        this.Publish("basics.toggle", toggle);
        this.Publish("basics.radioSmall", small);
        this.Publish("basics.radioMedium", medium);
        this.Publish("basics.radioLarge", large);
        this.Publish("basics.picture", picture);
        this.Publish("basics.pictureMenu", pictureMenu);
        this.Publish("basics.viewModel", _viewModel);

        return page;
    }
}
