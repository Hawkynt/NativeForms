using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The capture pass: walks the gallery a second time purely to photograph it, producing one PNG per
/// page plus one per interesting transient state — an open drop-down, an open menu, a collapsed
/// panel, a modal dialog.
/// </summary>
/// <remarks>
/// It is deliberately separate from the behavioral script and runs after it. The script's captures
/// are opportunistic — whatever the window happened to look like when a check finished — whereas
/// these are posed: each one puts the gallery into a named state, lets it settle, and shoots it. That
/// makes the set stable enough to compare across runs, and it means the numbering says what the
/// picture is of rather than when it was taken.
/// </remarks>
internal sealed partial class Autopilot
{
    /// <summary>Poses and photographs every page and every state worth a picture.</summary>
    private void CaptureGallery()
    {
        Section("Gallery captures");

        var tabs = _form.Part<TabControl>("chrome.tabs");
        var names = new[] { "01-basics", "02-input", "03-lists", "04-grid", "05-layout" };
        var count = Math.Min(names.Length, this.Read(() => tabs.TabPages.Count));
        for (var i = 0; i < count; ++i)
        {
            this.SelectTab(i);
            this.Pristine();
            this.Screenshot(names[i]);
        }

        this.CaptureComboDropDown();
        this.CaptureCalendarDropDown();
        this.CaptureFileMenu();
        this.CaptureExpander();
        this.CaptureScrolledPanel();
        this.CaptureMessageBox();
    }

    /// <summary>
    /// Puts the gallery back the way a user first sees it: any drop-down dismissed, the pointer taken
    /// off whatever it was last aimed at, and every page restored to its authored state.
    /// </summary>
    /// <remarks>
    /// Called before every posed shot, including the ones that then perform a single deliberate
    /// interaction — an open menu, a collapsed expander, a scrolled panel. That is what makes such a
    /// shot show exactly that one interaction instead of it plus the sediment of eighty-odd checks.
    /// </remarks>
    private void Pristine()
    {
        // A drop-down left open would photograph itself over the page and, while it holds the grab,
        // would swallow the gestures the next posed shot makes.
        for (var attempt = 0; attempt < 3 && this.Popups().Count > 0; ++attempt)
        {
            this.Key(KeySym.Escape);
            this.Settle(60);
        }

        this.ParkPointer();
        this.Do(_form.ResetToAuthoredState);
        this.Settle(120);
    }

    /// <summary>
    /// Slides the pointer to the empty stretch of menu bar right of the last item, which is where a
    /// shot wants it: over nothing that highlights and nothing that carries a tip.
    /// </summary>
    /// <remarks>
    /// Hover state is sticky under synthesized input. Crossing events are made by the display server,
    /// not by <c>gtk_main_do_event</c>, so a widget the pointer was over is never told the pointer
    /// went away and keeps painting itself hot — which is how a File menu ends up highlighted in a
    /// picture of the Grid page. Moving <em>within</em> the bar is enough: the strip does get the
    /// motion, and an x past every item resolves to no item at all.
    /// </remarks>
    private void ParkPointer()
    {
        var menu = _form.Part<MenuStrip>("chrome.menu");
        var geometry = this.Read(() => (menu.Width, menu.Height));
        this.Hover(menu, geometry.Width - 8, geometry.Height / 2);
    }

    /// <summary>The Lists page with the drop-down-list combo box open.</summary>
    private void CaptureComboDropDown()
    {
        this.SelectTab(2);
        this.Pristine();
        var combo = _form.Part<ComboBox>("lists.comboList");
        this.Do(combo.OpenDropDown);
        this.Settle(150);
        this.Screenshot("06-combobox-dropdown");
        this.Do(combo.CloseDropDown);
        this.Settle(80);
    }

    /// <summary>The Input page with the date-time picker's calendar down.</summary>
    private void CaptureCalendarDropDown()
    {
        this.SelectTab(1);
        this.Pristine();
        var picker = _form.Part<DateTimePicker>("input.picker");
        this.Do(picker.OpenDropDown);
        this.Settle(150);
        this.Screenshot("07-datetimepicker-calendar");
        this.Do(picker.CloseDropDown);
        this.Settle(80);
    }

    /// <summary>The window with the File menu pulled down.</summary>
    private void CaptureFileMenu()
    {
        this.SelectTab(0);
        this.Pristine();
        var menu = _form.Part<MenuStrip>("chrome.menu");
        this.Click(menu, 20, this.Read(() => menu.Height) / 2);

        // A click on an already-open bar toggles it shut, and the walkthrough may have left it that
        // way, so the shot is only taken once a popup is genuinely on screen.
        if (this.WaitForPopup(600) == 0)
        {
            this.Click(menu, 20, this.Read(() => menu.Height) / 2);
            this.WaitForPopup();
        }

        this.Settle(120);
        this.Screenshot("08-file-menu");
        this.Key(KeySym.Escape);
        this.Settle(80);
    }

    /// <summary>The Layout page with the expander collapsed, then expanded again.</summary>
    private void CaptureExpander()
    {
        this.SelectTab(4);
        this.Pristine();
        var expander = _form.Part<Expander>("layout.expander");
        if (this.Read(() => expander.Expanded))
        {
            this.Do(() => expander.Expanded = false);
            this.Settle(120);
        }

        this.Screenshot("09-expander-collapsed");
        this.Do(() => expander.Expanded = true);
        this.Settle(120);
        this.Screenshot("10-expander-expanded");
    }

    /// <summary>The Layout page with the auto-scrolling panel parked halfway down its content.</summary>
    private void CaptureScrolledPanel()
    {
        this.SelectTab(4);
        this.Pristine();
        var panel = _form.Part<Panel>("layout.scrollPanel");
        this.Do(() =>
        {
            // The children keep content coordinates whatever the scroll offset is, so their union
            // bottom is the scrollable extent and half the overshoot parks the view in the middle.
            var bottom = 0;
            for (var i = 0; i < panel.Controls.Count; ++i)
                bottom = Math.Max(bottom, panel.Controls[i].Bounds.Bottom);

            panel.AutoScrollPosition = new Point(0, Math.Max(0, bottom - panel.DisplayRectangle.Height) / 2);
        });

        this.Settle(120);
        this.Screenshot("11-autoscroll-panel-middle");
        this.Do(() => panel.AutoScrollPosition = Point.Empty);
        this.Settle(80);
    }

    /// <summary>The window behind a modal message box.</summary>
    private void CaptureMessageBox()
    {
        this.SelectTab(0);
        this.Pristine();
        var button = _form.Part<Button>("basics.dialog");
        var screen = this.ScreenOf(button, 150, 15);

        // The click must not be waited on: the dialog spins its own nested main loop and the gesture
        // only returns once the dialog is gone.
        this.Post(() =>
        {
            Injection.Move(_root, screen);
            Injection.Press(_root, screen, 1, 0);
            Injection.Release(_root, screen, 1, 0);
        });

        var dialog = this.WaitForPopup(4000);
        if (dialog == 0)
        {
            Console.WriteLine("      capture skipped: 12-messagebox — the dialog never appeared");
            _captureFailures.Add("12-messagebox");
            return;
        }

        this.Settle(120);
        this.Screenshot("12-messagebox");
        this.KeyInto(dialog, KeySym.Escape);
        this.Settle(150);
    }
}
