using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Toolbars page: every <see cref="ToolStrip"/> item kind (button, toggle, drop-down, split,
    /// separator, disabled) plus hosted real controls (combo, date/time pickers, colour swatch, text
    /// box, check box), the <see cref="IconLabel"/> image/text orderings, and an animated image shown
    /// running next to a disabled (frozen, grayscale) copy.
    /// </summary>
    private TabPage BuildToolbarsPage()
    {
        var page = new TabPage("Tools") { ImageIndex = _IconGear };

        var strip = new ToolStrip { Bounds = new(16, 36, 948, 30) };
        var newButton = new ToolStripButton { ImageList = _icons, ImageIndex = _IconNew };
        newButton.Click += (_, _) => this.SetStatus("Toolbar: New.");
        var saveButton = new ToolStripButton("Save") { ImageList = _icons, ImageIndex = _IconSave };
        var bold = new ToolStripButton("Bold") { CheckOnClick = true };
        bold.CheckedChanged += (_, _) => this.SetStatus($"Toolbar: bold {(bold.Checked ? "on" : "off")}.");
        var disabled = new ToolStripButton("Off") { ImageList = _icons, ImageIndex = _IconRed, Enabled = false };

        var more = new ToolStripDropDownButton("More") { ImageList = _icons, ImageIndex = _IconGear };
        more.DropDownItems.Add(new ToolStripMenuItem("Option A"));
        more.DropDownItems.Add(new ToolStripMenuItem("Option B"));
        var split = new ToolStripSplitButton("Run") { ImageList = _icons, ImageIndex = _IconRun };
        split.Click += (_, _) => this.SetStatus("Toolbar: Run.");
        split.DropDownItems.Add(new ToolStripMenuItem("Run tests"));

        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(["Draft", "Final"]);
        combo.SelectedIndex = 0;
        var date = new DateTimePicker { Format = DateTimePickerFormat.Short };
        var time = new TimePicker { ShowSeconds = false };
        var color = new ColorPicker { SelectedColor = Color.RoyalBlue };
        var text = new TextBox { Text = "field" };
        var live = new CheckBox { Text = "Live" };

        strip.Items.AddRange(
            newButton, saveButton, bold, disabled, new ToolStripSeparator(),
            more, split, new ToolStripSeparator(),
            new ToolStripControlHost(combo) { HostWidth = 96 },
            new ToolStripControlHost(date) { HostWidth = 110 },
            new ToolStripControlHost(time) { HostWidth = 80 },
            new ToolStripControlHost(color) { HostWidth = 44 },
            new ToolStripControlHost(text) { HostWidth = 90 },
            new ToolStripControlHost(live) { HostWidth = 64 });

        // IconLabel: image before / after text, and disabled (greyed).
        var before = new IconLabel { Bounds = new(16, 96, 240, 24), Text = "Image before text", Image = this.DiscImage(Color.RoyalBlue) };
        var after = new IconLabel { Bounds = new(16, 126, 240, 24), Text = "Text before image", Image = this.DiscImage(Color.MediumSeaGreen), TextImageRelation = TextImageRelation.TextBeforeImage };
        var disabledLabel = new IconLabel { Bounds = new(16, 156, 240, 24), Text = "Disabled — greyed caption", Image = this.DiscImage(Color.Crimson), Enabled = false };

        // The same animation running and disabled (frozen on its current frame, painted grayscale).
        var running = new PictureBox { Bounds = new(340, 96, 110, 110), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, AnimatedImage = BuildSpinner() };
        var frozen = new PictureBox { Bounds = new(464, 96, 110, 110), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, AnimatedImage = BuildSpinner(), Enabled = false };

        // A custom bitmap cursor (Cursor.FromImage) — the same route a decoded .cur/.ani takes.
        var cursorArea = new GroupBox { Bounds = new(340, 230, 234, 60), Text = "Custom cursor", Cursor = BuildTargetCursor() };
        cursorArea.Controls.Add(new Label { Bounds = new(12, 26, 210, 24), Text = "Hover here — a bitmap cursor", Cursor = BuildTargetCursor() });

        page.Controls.AddRange(
            Caption("ToolStrip: buttons, toggle, drop-down, split, hosted controls", 16, 12, 720),
            strip,
            Caption("IconLabel: image/text order and disabled", 16, 72, 300),
            before, after, disabledLabel,
            Caption("Animated image: running vs disabled (frozen, grey)", 340, 72, 400),
            running, frozen, cursorArea);

        this.Publish("toolbars.strip", strip);
        this.Publish("toolbars.running", running);
        this.Publish("toolbars.frozen", frozen);
        return page;
    }
}
