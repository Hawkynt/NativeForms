using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The demo window: a tabbed gallery with one page per control family — basics, input, lists, the
/// data grid and layout containers — under a full set of window chrome (menu, tool and status
/// strips). Every shipped control appears with representative non-default property values, and the
/// original MVVM click counter lives on the Basics page: a <see cref="RelayCommand"/> drives the
/// view-model while two <see cref="PropertyBinding{T}"/>s push its state onto a label and a
/// progress bar.
/// </summary>
/// <remarks>
/// There is no dock/anchor engine yet, so the chrome rows and the tab control are re-docked by hand
/// from <see cref="Form.Resize"/> and everything inside a page sits at absolute
/// <see cref="Control.Bounds"/>. All icons are generated as ARGB pixel arrays — flat squares and
/// discs in distinct colors — so the demo ships without image files or decoders.
/// </remarks>
internal sealed partial class MainForm : Form
{
    private const int _MenuHeight = 24;
    private const int _ToolHeight = 28;
    private const int _StatusHeight = 24;
    private const int _IconSize = 16;

    // Indices into _icons, in the order BuildIcons adds them.
    private const int _IconNew = 0;
    private const int _IconOpen = 1;
    private const int _IconSave = 2;
    private const int _IconRun = 3;
    private const int _IconGear = 4;
    private const int _IconFolder = 5;
    private const int _IconFile = 6;
    private const int _IconRed = 7;
    private const int _IconGreen = 8;
    private const int _IconBlue = 9;
    private const int _IconYellow = 10;
    private const int _IconPurple = 11;

    private readonly CounterViewModel _viewModel = new();
    private readonly IPlatformBackend _backend = BackendRegistry.Resolve();
    private readonly ImageList _icons = new(_IconSize);
    private readonly ToolTip _toolTip = new();

    private readonly MenuStrip _menu;
    private readonly ToolStrip _tools;
    private readonly TabControl _tabs;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _statusLabel = new("Ready.");
    private readonly ToolStripProgressBarItem _statusProgress = new() { Width = 120, Value = 40 };

    // Held so the bindings are not garbage-collected for the window's lifetime.
    private PropertyBinding<string>? _labelBinding;
    private PropertyBinding<int>? _progressBinding;

    public MainForm()
    {
        this.Text = "NativeForms Gallery";
        this.Bounds = new(Point.Empty, new Size(1000, 720));
        this.MinimumSize = new(820, 560);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.SetIcon(_IconSize, _IconSize, DiscPixels(_IconSize, Color.RoyalBlue));

        this.BuildIcons();
        _menu = this.BuildMenuStrip();
        _tools = this.BuildToolStrip();
        _status = this.BuildStatusStrip();

        _tabs = new() { ImageList = _icons };
        _tabs.TabPages.AddRange(
            this.BuildBasicsPage(),
            this.BuildInputPage(),
            this.BuildListsPage(),
            this.BuildGridPage(),
            this.BuildLayoutPage());

        this.Controls.AddRange(_menu, _tools, _tabs, _status);
        this.LayoutChrome();
        this.Resize += (_, _) => this.LayoutChrome();
    }

    /// <summary>Re-docks the chrome rows and the tab control to the current window size.</summary>
    private void LayoutChrome()
    {
        var width = this.Width;
        var height = this.Height;
        _menu.Bounds = new(0, 0, width, _MenuHeight);
        _tools.Bounds = new(0, _MenuHeight, width, _ToolHeight);
        _status.Bounds = new(0, height - _StatusHeight, width, _StatusHeight);
        var top = _MenuHeight + _ToolHeight;
        _tabs.Bounds = new(0, top, width, Math.Max(0, height - top - _StatusHeight));
    }

    /// <summary>Reports a one-line message into the status strip.</summary>
    private void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>Adds a section caption label to a parent at the given position.</summary>
    private static Label Caption(string text, int x, int y)
        => new() { Bounds = new(x, y, 300, 18), Text = text };

    // --- Chrome -----------------------------------------------------------------------------------

    /// <summary>
    /// The menu bar: a File menu with icons, shortcut display, a checkable Autosave item and an Exit
    /// item calling <see cref="Application.Exit"/>, plus a Help menu opening a native message box.
    /// </summary>
    private MenuStrip BuildMenuStrip()
    {
        var strip = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        var newItem = new ToolStripMenuItem("&New") { ImageList = _icons, ImageIndex = _IconNew };
        newItem.Click += (_, _) => this.SetStatus("File → New clicked.");
        var open = new ToolStripMenuItem("&Open…")
        {
            ImageList = _icons,
            ImageIndex = _IconOpen,
            ShortcutKeys = Keys.Control | Keys.O,
        };
        open.Click += (_, _) => this.SetStatus("File → Open clicked.");
        var save = new ToolStripMenuItem("&Save")
        {
            ImageList = _icons,
            ImageIndex = _IconSave,
            ShortcutKeys = Keys.Control | Keys.S,
        };
        save.Click += (_, _) => this.SetStatus("File → Save clicked.");
        var autosave = new ToolStripMenuItem("&Autosave") { CheckOnClick = true, Checked = true };
        autosave.CheckedChanged += (_, _) => this.SetStatus($"Autosave is {(autosave.Checked ? "on" : "off")}.");
        var exit = new ToolStripMenuItem("E&xit");
        exit.Click += (_, _) => Application.Exit();
        file.DropDownItems.Add(newItem);
        file.DropDownItems.Add(open);
        file.DropDownItems.Add(save);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(autosave);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(exit);

        var help = new ToolStripMenuItem("&Help");
        var about = new ToolStripMenuItem("&About…") { ShortcutKeyDisplayString = "F1" };
        about.Click += (_, _) =>
            MessageBox.Show(
                "A WinForms-shaped UI toolkit rendering through native widgets.",
                "About NativeForms",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        help.DropDownItems.Add(about);

        strip.Items.AddRange(file, help);
        return strip;
    }

    /// <summary>
    /// The toolbar: three icon buttons, a checkable toggle button and a split button whose drop-down
    /// offers run variants. Every click is reported into the status strip.
    /// </summary>
    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip();

        var newButton = new ToolStripButton { ImageList = _icons, ImageIndex = _IconNew };
        newButton.Click += (_, _) => this.SetStatus("Toolbar: New clicked.");
        var openButton = new ToolStripButton { ImageList = _icons, ImageIndex = _IconOpen };
        openButton.Click += (_, _) => this.SetStatus("Toolbar: Open clicked.");
        var saveButton = new ToolStripButton { ImageList = _icons, ImageIndex = _IconSave };
        saveButton.Click += (_, _) => this.SetStatus("Toolbar: Save clicked.");

        var pin = new ToolStripButton("Pin")
        {
            ImageList = _icons,
            ImageIndex = _IconGear,
            CheckOnClick = true,
        };
        pin.CheckedChanged += (_, _) => this.SetStatus($"Toolbar: Pin is {(pin.Checked ? "on" : "off")}.");

        var run = new ToolStripSplitButton("Run") { ImageList = _icons, ImageIndex = _IconRun };
        run.Click += (_, _) => this.SetStatus("Toolbar: Run clicked.");
        var runTests = new ToolStripMenuItem("Run &tests");
        runTests.Click += (_, _) => this.SetStatus("Toolbar: Run tests picked.");
        var runProfiled = new ToolStripMenuItem("Run with &profiler");
        runProfiled.Click += (_, _) => this.SetStatus("Toolbar: Run with profiler picked.");
        run.DropDownItems.Add(runTests);
        run.DropDownItems.Add(runProfiled);

        strip.Items.AddRange(newButton, openButton, saveButton, new ToolStripSeparator(), pin, new ToolStripSeparator(), run);
        return strip;
    }

    /// <summary>The status bar: the message label, a spring filler and a progress item.</summary>
    private StatusStrip BuildStatusStrip()
    {
        var strip = new StatusStrip();
        strip.Items.AddRange(_statusLabel, new ToolStripStatusLabel { Spring = true }, _statusProgress);
        return strip;
    }

    // --- Generated icons --------------------------------------------------------------------------

    /// <summary>Fills the shared icon list; the <c>_Icon*</c> constants index into it.</summary>
    private void BuildIcons()
    {
        _icons.Add(SquarePixels(_IconSize, Color.CornflowerBlue)); // _IconNew
        _icons.Add(SquarePixels(_IconSize, Color.Orange));         // _IconOpen
        _icons.Add(SquarePixels(_IconSize, Color.MediumSeaGreen)); // _IconSave
        _icons.Add(DiscPixels(_IconSize, Color.ForestGreen));      // _IconRun
        _icons.Add(DiscPixels(_IconSize, Color.SlateGray));        // _IconGear
        _icons.Add(SquarePixels(_IconSize, Color.Goldenrod));      // _IconFolder
        _icons.Add(SquarePixels(_IconSize, Color.Silver));         // _IconFile
        _icons.Add(DiscPixels(_IconSize, Color.Crimson));          // _IconRed
        _icons.Add(DiscPixels(_IconSize, Color.MediumSeaGreen));   // _IconGreen
        _icons.Add(DiscPixels(_IconSize, Color.RoyalBlue));        // _IconBlue
        _icons.Add(DiscPixels(_IconSize, Color.Gold));             // _IconYellow
        _icons.Add(DiscPixels(_IconSize, Color.MediumOrchid));     // _IconPurple
    }

    /// <summary>A native square icon in the given fill color, for controls that take an image directly.</summary>
    private IImage SquareImage(Color fill) => _backend.CreateImage(_IconSize, _IconSize, SquarePixels(_IconSize, fill));

    /// <summary>A native disc icon in the given fill color, for controls that take an image directly.</summary>
    private IImage DiscImage(Color fill) => _backend.CreateImage(_IconSize, _IconSize, DiscPixels(_IconSize, fill));

    /// <summary>A filled square with a one-pixel darker border inside a one-pixel transparent inset.</summary>
    private static int[] SquarePixels(int size, Color fill)
    {
        var border = Darken(fill).ToArgb();
        var body = fill.ToArgb();
        var pixels = new int[size * size];
        for (var y = 1; y < size - 1; ++y)
        for (var x = 1; x < size - 1; ++x)
        {
            var edge = x == 1 || y == 1 || x == size - 2 || y == size - 2;
            pixels[(y * size) + x] = edge ? border : body;
        }

        return pixels;
    }

    /// <summary>A filled disc with a one-pixel darker rim on a transparent background.</summary>
    private static int[] DiscPixels(int size, Color fill)
    {
        var border = Darken(fill).ToArgb();
        var body = fill.ToArgb();
        var pixels = new int[size * size];
        var center = (size - 1) / 2f;
        var radius = (size / 2f) - 1f;
        for (var y = 0; y < size; ++y)
        for (var x = 0; x < size; ++x)
        {
            var dx = x - center;
            var dy = y - center;
            var distance = MathF.Sqrt((dx * dx) + (dy * dy));
            if (distance > radius)
                continue;

            pixels[(y * size) + x] = distance > radius - 1.2f ? border : body;
        }

        return pixels;
    }

    /// <summary>A horizontal blend between two colors, shaded darker toward the bottom.</summary>
    private static int[] GradientPixels(int width, int height, Color from, Color to)
    {
        var pixels = new int[width * height];
        for (var y = 0; y < height; ++y)
        {
            var shade = 255 - (y * 96 / Math.Max(1, height - 1));
            for (var x = 0; x < width; ++x)
            {
                var t = x * 255 / Math.Max(1, width - 1);
                var r = ((from.R * (255 - t)) + (to.R * t)) * shade / (255 * 255);
                var g = ((from.G * (255 - t)) + (to.G * t)) * shade / (255 * 255);
                var b = ((from.B * (255 - t)) + (to.B * t)) * shade / (255 * 255);
                pixels[(y * width) + x] = Color.FromArgb(255, r, g, b).ToArgb();
            }
        }

        return pixels;
    }

    /// <summary>The same hue at two thirds of the brightness, used for icon borders.</summary>
    private static Color Darken(Color color)
        => Color.FromArgb(color.A, color.R * 2 / 3, color.G * 2 / 3, color.B * 2 / 3);
}
