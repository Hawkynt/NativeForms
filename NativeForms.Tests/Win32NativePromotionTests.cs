using System.Collections.Generic;
using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Windows;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The native-peer promotions of PRD §12, asserted against real Win32 widgets on a real desktop.
///
/// The headless fake answers whatever the core pushed into it, so it can only prove that the core asks
/// the right questions — never that <c>BS_AUTOCHECKBOX</c>, <c>msctls_trackbar32</c>, <c>SysLink</c> or a
/// stand-alone <c>SCROLLBAR</c> actually accept those messages and answer them. The peers are pure
/// interop; nothing but a live desktop proves they were built at all. This fixture therefore drives the
/// real backend: it realizes one of every promotable control, reads the state back out of the widgets and
/// asserts the promotion held, the gates held, and a mid-use property change swapped the peer with the
/// state intact.
///
/// Off Windows the whole fixture reports itself as ignored rather than passing vacuously.
/// </summary>
[TestFixture]
public sealed class Win32NativePromotionTests
{
    /// <summary>Everything the run on the Win32 message loop observed; the tests only assert against it.</summary>
    private static Observations? _observed;

    private static string? _skipReason;

    /// <summary>What one realized control reported about itself.</summary>
    private sealed class Observations
    {
        /// <summary>Per control name, whether it ended up on a real platform widget.</summary>
        public Dictionary<string, bool> Promoted { get; } = [];

        /// <summary>Per assertion name, whether driving the widget round-tripped.</summary>
        public Dictionary<string, bool> RoundTrips { get; } = [];

        /// <summary>Whether Direct2D was reachable at all on this machine.</summary>
        public bool ColorTextAvailable;

        /// <summary>How many colour draws the string with an emoji in it caused.</summary>
        public int ColorRunsForEmoji;

        /// <summary>How many it caused for a string of plain text.</summary>
        public int ColorRunsForPlainText;

        public string? Failure;
    }

    /// <summary>
    /// An owner-drawn control that paints one string with an emoji and one without, recording the
    /// colour-path counter around each so the divert can be observed rather than inferred.
    /// </summary>
    private sealed class ColorGlyphPainter : OwnerDrawnControl
    {
        public int ColorRunsBeforePlain;
        public int ColorRunsAfterPlain;
        public int ColorRunsBeforeEmoji;
        public int ColorRunsAfterEmoji;

        protected override void OnPaint(PaintEventArgs e)
        {
            this.ColorRunsBeforePlain = Win32ColorText.ColorRuns;
            e.Graphics.DrawText("plain", this.Font, Color.Black, new Rectangle(0, 0, 200, 20));
            this.ColorRunsAfterPlain = Win32ColorText.ColorRuns;

            this.ColorRunsBeforeEmoji = Win32ColorText.ColorRuns;
            e.Graphics.DrawText("emoji \U0001F423", this.Font, Color.Black, new Rectangle(0, 24, 200, 20));
            this.ColorRunsAfterEmoji = Win32ColorText.ColorRuns;
        }
    }

    [OneTimeSetUp]
    public void RunTheFormOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            _skipReason = "The Win32 peers are only exercised on Windows.";
            return;
        }

        BackendRegistry.Register(new Win32Backend());
        var observed = new Observations();

        var form = new Form { Text = "win32 promotion", Width = 700, Height = 620 };

        var check = new CheckBox { Bounds = new Rectangle(12, 12, 200, 20), Text = "CheckBox" };
        var radio = new RadioButton { Bounds = new Rectangle(12, 38, 200, 20), Text = "RadioButton" };
        var sibling = new RadioButton { Bounds = new Rectangle(12, 64, 200, 20), Text = "sibling" };
        var link = new LinkLabel { Bounds = new Rectangle(12, 90, 200, 20), Text = "LinkLabel" };
        var progress = new ProgressBar { Bounds = new Rectangle(12, 116, 200, 18), Value = 40 };
        var track = new TrackBar { Bounds = new Rectangle(12, 140, 200, 28), Maximum = 10, Value = 6 };
        var hscroll = new HScrollBar { Bounds = new Rectangle(12, 174, 200, 16), Maximum = 100, LargeChange = 10 };
        var vscroll = new VScrollBar { Bounds = new Rectangle(230, 12, 16, 180), Maximum = 100, LargeChange = 10 };

        var combo = new ComboBox { Bounds = new Rectangle(12, 200, 200, 26), DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(["alpha", "beta", "gamma"]);
        combo.SelectedIndex = 0;

        var list = new ListBox { Bounds = new Rectangle(12, 232, 200, 90) };
        list.Items.AddRange(["one", "two", "three"]);
        list.SelectedIndex = 0;

        var group = new GroupBox { Bounds = new Rectangle(270, 12, 260, 120), Text = "GroupBox" };
        var inside = new Button { Bounds = new Rectangle(14, 30, 120, 26), Text = "child" };
        group.Controls.Add(inside);

        // A group whose members do not share a rendering path: the gate is per control, so the image
        // keeps one of these on the painter while the other is a real BS_RADIOBUTTON. Grouping has to
        // reach across that split in both directions.
        var mixedHost = new GroupBox { Bounds = new Rectangle(540, 300, 140, 90), Text = "mixed" };
        var mixedNative = new RadioButton { Bounds = new Rectangle(10, 24, 120, 20), Text = "widget" };
        var mixedDrawn = new RadioButton { Bounds = new Rectangle(10, 50, 120, 20), Text = "painted", Image = Pixel() };
        mixedHost.Controls.AddRange(mixedNative, mixedDrawn);

        // Each of these puts one property outside its gate, so each must stay on the painter.
        var icon = Pixel();
        var gatedCheck = new CheckBox { Bounds = new Rectangle(270, 140, 240, 20), Text = "gated", Image = icon };
        var gatedRadio = new RadioButton { Bounds = new Rectangle(270, 166, 240, 20), Text = "gated", Image = icon };
        var gatedProgress = new ProgressBar { Bounds = new Rectangle(540, 12, 18, 90), Orientation = Orientation.Vertical };
        var gatedCombo = new ComboBox { Bounds = new Rectangle(270, 192, 240, 26), DropDownStyle = ComboBoxStyle.DropDown };
        var gatedList = new CheckedListBox { Bounds = new Rectangle(270, 224, 240, 70) };
        gatedList.Items.AddRange(["a", "b"]);
        var gatedGroup = new GroupBox { Bounds = new Rectangle(270, 300, 240, 60), Text = "gated", Image = icon };

        var tipped = new Button { Bounds = new Rectangle(540, 140, 140, 26), Text = "tipped" };
        var toolTip = new ToolTip();
        var painter = new ColorGlyphPainter { Bounds = new Rectangle(540, 400, 200, 60) };

        form.Controls.AddRange(
            check, radio, sibling, link, progress, track, hscroll, vscroll, combo, list, group, tipped,
            gatedCheck, gatedRadio, gatedProgress, gatedCombo, gatedList, gatedGroup, mixedHost, painter);

        // Observed from a timer rather than from Load: some of this needs the window to have been shown
        // and painted at least once — the colour-text divert is only visible after a paint has run.
        var timer = new Timer { Interval = 250 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                foreach (var (name, control) in new (string, Control)[]
                         {
                             ("CheckBox", check), ("RadioButton", radio), ("LinkLabel", link),
                             ("ProgressBar", progress), ("TrackBar", track), ("HScrollBar", hscroll),
                             ("VScrollBar", vscroll), ("ComboBox", combo), ("ListBox", list), ("GroupBox", group),
                             ("gated CheckBox", gatedCheck), ("gated RadioButton", gatedRadio),
                             ("vertical ProgressBar", gatedProgress), ("editable ComboBox", gatedCombo),
                             ("CheckedListBox", gatedList), ("gated GroupBox", gatedGroup),
                         })
                    observed.Promoted[name] = control.IsNativeWidget;

                check.Checked = true;
                observed.RoundTrips["CheckBox.Checked"] = check.Checked;

                radio.Checked = true;
                sibling.Checked = true;
                observed.RoundTrips["radio grouping"] = sibling.Checked && !radio.Checked;
                sibling.Checked = false;
                observed.RoundTrips["radio cleared"] = !sibling.Checked;

                link.LinkVisited = true;
                observed.RoundTrips["LinkLabel.LinkVisited"] = link.LinkVisited;

                progress.Value = 80;
                observed.RoundTrips["ProgressBar.Value"] = progress.Value == 80;

                track.Value = 3;
                observed.RoundTrips["TrackBar.Value"] = track.Value == 3;

                hscroll.Value = 55;
                observed.RoundTrips["ScrollBar.Value"] = hscroll.Value == 55;

                combo.SelectedIndex = 2;
                observed.RoundTrips["ComboBox.SelectedIndex"] = combo.SelectedIndex == 2 && combo.Text == "gamma";
                combo.Items.Add("delta");
                combo.SelectedIndex = 3;
                observed.RoundTrips["ComboBox late items"] = combo.Text == "delta";

                list.SelectedIndex = 2;
                observed.RoundTrips["ListBox.SelectedIndex"] = list.SelectedIndex == 2 && (string?)list.SelectedItem == "three";
                list.EnsureVisible(2);
                observed.RoundTrips["ListBox geometry"] = list.TopIndex >= 0 && list.IndexFromPoint(10, 4) >= 0;

                observed.RoundTrips["GroupBox child bounds"] = inside.Bounds == new Rectangle(14, 30, 120, 26);

                observed.Promoted["mixed widget half"] = mixedNative.IsNativeWidget;
                observed.Promoted["mixed painted half"] = mixedDrawn.IsNativeWidget;

                mixedNative.Checked = true;
                mixedDrawn.Checked = true;
                observed.RoundTrips["painted half takes the selection"] = mixedDrawn.Checked && !mixedNative.Checked;
                mixedNative.Checked = true;
                observed.RoundTrips["widget half takes it back"] = mixedNative.Checked && !mixedDrawn.Checked;

                // The swap has to be invisible to the application, in both directions.
                check.Image = icon;
                observed.RoundTrips["swap to painter"] = !check.IsNativeWidget && check.Checked;
                check.Image = null;
                observed.RoundTrips["swap back to widget"] = check.IsNativeWidget && check.Checked;

                combo.ImageSelector = static _ => null;
                observed.RoundTrips["ComboBox swap keeps selection"] = !combo.IsNativeWidget && combo.SelectedIndex == 3;

                // The platform tooltip: a real tooltips_class32 window has to exist once a tip is raised,
                // which is the half of this that a headless fake cannot see.
                observed.RoundTrips["no tooltip window before a tip"] =
                    NativeMethods.FindWindowExW(0, 0, NativeMethods.TOOLTIPS_CLASS, null) == 0;
                toolTip.SetToolTip(tipped, "a native tip");
                tipped.Peer?.ShowToolTip("a native tip");
                observed.RoundTrips["the platform tooltip window exists"] =
                    NativeMethods.FindWindowExW(0, 0, NativeMethods.TOOLTIPS_CLASS, null) != 0;

                // PRD §13: a string with an emoji is diverted to Direct2D, one without is not. Whether
                // the divert can succeed is a property of the machine, so the assertion is conditional on
                // the path being available at all rather than on it being present.
                observed.ColorTextAvailable = !Win32ColorText.Unavailable;
                observed.ColorRunsForEmoji = painter.ColorRunsAfterEmoji - painter.ColorRunsBeforeEmoji;
                observed.ColorRunsForPlainText = painter.ColorRunsAfterPlain - painter.ColorRunsBeforePlain;
            }
            catch (Exception exception)
            {
                observed.Failure = exception.ToString();
            }
            finally
            {
                form.Close();
            }
        };

        form.Load += (_, _) => timer.Start();

        // A failure to open a window at all (a runner without an interactive window station, say) must
        // arrive as a legible message on every test rather than as an opaque fixture error.
        try
        {
            Application.Run(form);
        }
        catch (Exception exception)
        {
            observed.Failure ??= $"the Win32 message loop could not run: {exception}";
        }

        _observed = observed;
    }

    private static Observations Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_observed, Is.Not.Null, "the Win32 loop never reached the observation point.");
        Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
        return _observed;
    }

    private static IImage Pixel()
    {
        var argb = new int[64];
        Array.Fill(argb, unchecked((int)0xFF3366CC));
        return BackendRegistry.Resolve().CreateImage(8, 8, argb);
    }

    [TestCase("CheckBox")]
    [TestCase("RadioButton")]
    [TestCase("ProgressBar")]
    [TestCase("TrackBar")]
    [TestCase("HScrollBar")]
    [TestCase("VScrollBar")]
    [TestCase("ComboBox")]
    [TestCase("ListBox")]
    [TestCase("GroupBox")]
    [TestCase("mixed widget half")]
    public void An_eligible_control_realizes_onto_a_real_widget(string control)
        => Assert.That(Result().Promoted[control], Is.True, $"{control} stayed on the owner-drawn painter");

    /// <summary>
    /// The one promotion whose window class can genuinely be missing: <c>SysLink</c> arrived with ComCtl32
    /// version 6, which a process only reaches through an application manifest. The rule is therefore not
    /// "always promoted" but "promoted exactly when the class resolved" — because the alternative, creating
    /// a window that fails and leaving the control invisible, is what this pins against.
    /// </summary>
    [Test]
    public void The_hyperlink_is_promoted_exactly_when_its_window_class_resolved()
    {
        var promoted = Result().Promoted["LinkLabel"];
        var available = NativeMethods.GetClassInfoExW(0, NativeMethods.WC_LINK, out _);

        Assert.That(
            promoted,
            Is.EqualTo(available),
            available
                ? "SysLink is registered, so the promotion should have been taken"
                : "SysLink is absent, so the backend must decline and leave the painter in charge");
    }

    [TestCase("gated CheckBox")]
    [TestCase("gated RadioButton")]
    [TestCase("vertical ProgressBar")]
    [TestCase("editable ComboBox")]
    [TestCase("CheckedListBox")]
    [TestCase("gated GroupBox")]
    [TestCase("mixed painted half")]
    public void A_control_outside_its_gate_stays_owner_drawn(string control)
        => Assert.That(Result().Promoted[control], Is.False, $"{control} was promoted despite its state");

    [TestCase("CheckBox.Checked")]
    [TestCase("radio grouping")]
    [TestCase("radio cleared")]
    [TestCase("LinkLabel.LinkVisited")]
    [TestCase("ProgressBar.Value")]
    [TestCase("TrackBar.Value")]
    [TestCase("ScrollBar.Value")]
    [TestCase("ComboBox.SelectedIndex")]
    [TestCase("ComboBox late items")]
    [TestCase("ListBox.SelectedIndex")]
    [TestCase("ListBox geometry")]
    [TestCase("GroupBox child bounds")]
    [TestCase("painted half takes the selection")]
    [TestCase("widget half takes it back")]
    [TestCase("no tooltip window before a tip")]
    [TestCase("the platform tooltip window exists")]
    public void Driving_the_real_widget_round_trips(string what)
        => Assert.That(Result().RoundTrips[what], Is.True, $"{what} did not survive the platform widget");

    [TestCase("swap to painter")]
    [TestCase("swap back to widget")]
    [TestCase("ComboBox swap keeps selection")]
    public void A_property_change_that_crosses_the_gate_swaps_the_peer_with_the_state_intact(string what)
        => Assert.That(Result().RoundTrips[what], Is.True, $"{what} lost state across the peer swap");

    /// <summary>
    /// PRD §13: a string carrying an emoji goes to Direct2D, because GDI would draw it in black. The
    /// machine decides whether that is possible; the toolkit decides whether it is attempted.
    /// </summary>
    [Test]
    public void A_string_with_an_emoji_is_drawn_by_the_colour_path()
    {
        var observed = Result();
        if (!observed.ColorTextAvailable)
            Assert.Ignore("Direct2D is not usable on this machine, so the fallback is the correct behaviour.");

        Assert.That(observed.ColorRunsForEmoji, Is.EqualTo(1), "the emoji string should have been diverted once");
    }

    /// <summary>
    /// The other half, and the one that protects every other string in the application: plain text must
    /// never reach the slower renderer.
    /// </summary>
    [Test]
    public void A_string_without_one_never_reaches_the_colour_path()
        => Assert.That(Result().ColorRunsForPlainText, Is.Zero);
}
