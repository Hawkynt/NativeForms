namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The Pickers page walkthrough: the path pickers (typing, committing, browsing into a real native
/// file dialog and backing out of it again), the icon labels, and the Explorer-style drive tiles.
/// </summary>
internal sealed partial class Autopilot
{
    /// <summary>The index of the Pickers page in the gallery's tab strip.</summary>
    private const int _PickersTab = 5;

    /// <summary>Drives the file/folder pickers, the icon labels and the drive tiles.</summary>
    private void DrivePickers()
    {
        Section("Pickers, icon labels and drive tiles");
        this.SelectTab(_PickersTab);
        var status = _form.Part<ToolStripStatusLabel>("chrome.statusLabel");

        this.Check("FolderPicker: typing a path and pressing Enter commits it", () =>
        {
            var picker = _form.Part<FolderPicker>("pickers.folder");
            this.Click(picker, 40, this.Read(() => picker.Height) / 2);
            this.Do(() => picker.Text = string.Empty);
            this.Type("/usr");
            this.Expect("the committed path before Enter", this.Read(() => picker.SelectedPath), "/tmp");

            this.Key(KeySym.Return);
            this.Expect("FolderPicker.SelectedPath after Enter", this.Read(() => picker.SelectedPath), "/usr");
            this.ExpectTrue("PathExists did not notice that /usr is a real directory", this.Read(() => picker.PathExists));
            this.Expect("the status line", this.Read(() => status.Text), "FolderPicker: /usr");
        });

        this.Check("FolderPicker: a path that names nothing is reported as missing", () =>
        {
            var picker = _form.Part<FolderPicker>("pickers.folder");
            this.Do(() => picker.SelectedPath = "/no/such/place");

            this.ExpectTrue("a nonexistent directory was reported as existing", !this.Read(() => picker.PathExists));

            this.Do(() => picker.SelectedPath = "/tmp");
        });

        this.Check("FilePicker: the read-only field refuses typing but still reports its selection", () =>
        {
            var picker = _form.Part<FilePicker>("pickers.multi");
            var before = this.Read(() => picker.Text);
            this.Click(picker, 40, this.Read(() => picker.Height) / 2);
            this.Type("nope");

            this.Expect("the read-only field's text after typing", this.Read(() => picker.Text), before);
            this.Expect("SelectedPaths on an untouched picker", this.Read(() => picker.SelectedPaths.Length), 0);
        });

        this.Check("FilePicker: a committed path publishes itself as the whole selection", () =>
        {
            var picker = _form.Part<FilePicker>("pickers.open");
            this.Do(() => picker.SelectedPath = "/etc/hosts");

            this.Expect("SelectedPaths.Length", this.Read(() => picker.SelectedPaths.Length), 1);
            this.Expect("SelectedPaths[0]", this.Read(() => picker.SelectedPaths[0]), "/etc/hosts");
            this.ExpectTrue("/etc/hosts was not recognised as an existing file", this.Read(() => picker.PathExists));

            this.Do(() => picker.SelectedPath = string.Empty);
        });

        this.Check("IconLabel: renders its caption and keeps its image alongside", () =>
        {
            var label = _form.Part<IconLabel>("pickers.iconBefore");

            this.Expect("IconLabel.Text", this.Read(() => label.Text), "ImageBeforeText (the default)");
            this.ExpectTrue("the icon label lost its image", this.Read(() => label.Image) is not null);
            this.ExpectTrue(
                "the icon label is not wide enough to show both parts",
                this.Read(() => label.Width) > 100);
        });

        this.Check("IconLabel: AutoSize measured room for both the icon and the caption", () =>
        {
            var label = _form.Part<IconLabel>("pickers.iconAuto");
            var (width, height) = this.Read(() => (label.Width, label.Height));

            // 16 px icon + 4 px gap + the measured caption, so it must exceed the icon alone.
            this.ExpectTrue($"AutoSize left the label {width}×{height}, too small for icon plus text", width > 24 && height >= 16);
        });

        this.Check("ProgressTile: a click selects the tile and reports it", () =>
        {
            var media = _form.Part<ProgressTile>("pickers.driveMedia");
            var system = _form.Part<ProgressTile>("pickers.driveSystem");

            this.Click(media, this.Read(() => media.Width) / 2, this.Read(() => media.Height) / 2);

            this.ExpectTrue("the clicked tile did not become the selected one", this.Read(() => media.Selected));
            this.ExpectTrue("the previously selected tile stayed selected", !this.Read(() => system.Selected));
            this.Expect("the status line", this.Read(() => status.Text), "Drive tile: Media (E:) clicked.");
        });

        this.Check("ProgressTile: the nearly-full drive is past its warning threshold", () =>
        {
            var full = _form.Part<ProgressTile>("pickers.driveFull");

            this.ExpectTrue("the nearly-full drive tile is not painting its warning bar", this.Read(() => full.IsWarning));
            this.Expect("the warning tile's secondary caption", this.Read(() => full.SecondaryText), "2.1 GB free of 512 GB");
        });

        this.Check("ProgressTile: an inert tile ignores clicks", () =>
        {
            var quota = _form.Part<ProgressTile>("pickers.quota");
            this.Do(() => status.Text = "Ready.");

            this.Click(quota, this.Read(() => quota.Width) / 2, this.Read(() => quota.Height) / 2);

            this.Expect("the status line after clicking an inert tile", this.Read(() => status.Text), "Ready.");
            this.ExpectTrue("an inert tile reported itself selected", !this.Read(() => quota.Selected));
        });

        this.Check("ProgressTile: Space activates a focused clickable tile", () =>
        {
            var system = _form.Part<ProgressTile>("pickers.driveSystem");
            this.FocusOn(system);
            this.Key(KeySym.Space);

            this.Expect("the status line", this.Read(() => status.Text), "Drive tile: Windows (C:) clicked.");
            this.ExpectTrue("Space did not select the focused tile", this.Read(() => system.Selected));
        });

        this.HandKeyboardBackToANativeEditor();
    }

    /// <summary>
    /// Leaves the page with the keyboard on a real native editor rather than on an owner-drawn canvas.
    /// </summary>
    /// <remarks>
    /// The Space check above deliberately parks the focus on a bare canvas — that is what it tests.
    /// Switching tabs then hides the widget holding the toplevel's focus, and GTK does not re-home it:
    /// every later click still lands on the right entry, yet none of them moves the focus, so the
    /// whole text-entry sweep fails with "did not take the focus from a click". Every other page
    /// happens to end on a native editor, which is why the sweep passes after them.
    /// <para>
    /// A programmatic <see cref="Control.Focus"/> proved to only *mostly* fix it — the sweep failed
    /// about one run in two. The focus has to arrive the way a user's would, from a synthesized click
    /// that produces a real focus-in, and the walkthrough has to see it land before moving on rather
    /// than assume it did.
    /// </para>
    /// <para>
    /// This is a workaround for a backend-level gap, not a fix: a focused control whose page is being
    /// hidden should surrender the focus rather than strand it. The tab visibility/focus seam is not
    /// this change's to alter.
    /// </para>
    /// </remarks>
    private void HandKeyboardBackToANativeEditor()
    {
        var folder = _form.Part<FolderPicker>("pickers.folder");
        this.Click(folder, 40, this.Read(() => folder.Height) / 2);

        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && !this.HoldsFocus(folder))
            this.Settle(50);

        if (!this.HoldsFocus(folder))
            this.Fail("the keyboard could not be handed back to a native editor before leaving the Pickers page");
    }

    /// <summary>
    /// The browse button opening the platform's own file chooser. Like the MessageBox check, the
    /// click is posted rather than awaited — the native dialog nests its own main loop — and the
    /// dialog is dismissed from the harness side.
    /// </summary>
    /// <remarks>
    /// Run at the very end, for the reason <see cref="DriveModalDialog"/> spells out and then some: a
    /// GTK file chooser is a real toplevel that takes the keyboard focus with it, and on a
    /// focus-follows-pointer display it does not always hand that focus back to the gallery when it
    /// closes. Placed mid-script it left every later typing check with nowhere to type — nine of them
    /// failed at once — which is a property of the dialog, not of the controls they were testing.
    /// </remarks>
    private void DrivePickerDialog()
    {
        Section("Native file dialog");
        this.SelectTab(_PickersTab);

        this.Check("FilePicker: the browse button opens the native file dialog and Escape cancels it", () =>
        {
            var picker = _form.Part<FilePicker>("pickers.open");
            var before = this.Read(() => picker.SelectedPath);

            var screen = this.ScreenOf(picker, this.Read(() => picker.Width) - 14, this.Read(() => picker.Height) / 2);
            this.Post(() =>
            {
                Injection.Move(_root, screen);
                Injection.Press(_root, screen, 1, 0);
                Injection.Release(_root, screen, 1, 0);
            });

            var dialog = this.WaitForPopup(4000);
            if (dialog == 0)
            {
                this.Fail("no native file dialog appeared within 4 s of clicking the browse button");
                return;
            }

            this.Screenshot("state-dialog-filepicker");
            this.KeyInto(dialog, KeySym.Escape);

            var deadline = Environment.TickCount64 + 4000;
            while (Environment.TickCount64 < deadline && this.Popups().Count > 0)
                this.Settle(100);

            this.ExpectTrue("the native file dialog is still on screen after Escape", this.Popups().Count == 0);
            this.Expect("the cancelled browse changed the path", this.Read(() => picker.SelectedPath), before);
        });
    }
}
