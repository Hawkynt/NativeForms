using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Pickers page: a <see cref="FilePicker"/> in open and save mode plus a read-only,
    /// multi-select one, a <see cref="FolderPicker"/>, a column of <see cref="IconLabel"/>s showing
    /// every text/image relation the shared content layout offers, and a row of
    /// <see cref="ProgressTile"/>s in the Explorer drive shape — one of them past its warning
    /// threshold so the red bar is on screen.
    /// </summary>
    private TabPage BuildPickersPage()
    {
        var page = new TabPage("Pickers") { ImageIndex = _IconFolder };

        // --- Column 1: file and folder pickers ---------------------------------------------------

        var openPicker = new FilePicker
        {
            Bounds = new(16, 36, 300, 26),
            Filter = "Text files|*.txt|C# files|*.cs|All files|*.*",
            FilterIndex = 3,
            Title = "Pick a file to open",
            PlaceholderText = "No file chosen…",
        };
        openPicker.PathChanged += (_, _) => this.SetStatus($"FilePicker: {openPicker.SelectedPath}");

        var savePicker = new FilePicker
        {
            Bounds = new(16, 92, 300, 26),
            Mode = FilePickerMode.Save,
            Filter = "CSV|*.csv|All files|*.*",
            Title = "Pick a save target",
            SelectedPath = "/tmp/export.csv",
        };

        // Read-only + multi-select: the field cannot be typed into, only browsed.
        var multiPicker = new FilePicker
        {
            Bounds = new(16, 148, 300, 26),
            Multiselect = true,
            ReadOnlyText = true,
            PlaceholderText = "Browse to pick several…",
        };
        var multiCount = new Label { Bounds = new(16, 178, 300, 18), Text = "0 files selected" };
        multiPicker.PathChanged += (_, _)
            => multiCount.Text = $"{multiPicker.SelectedPaths.Length} file(s) selected";

        // A deliberately broken path, so the warning frame is visible in the gallery and the shot.
        var brokenPicker = new FilePicker
        {
            Bounds = new(16, 232, 300, 26),
            SelectedPath = "/does/not/exist.txt",
        };
        var brokenNote = new Label { Bounds = new(16, 262, 300, 18), Text = "PathExists = false → warning frame" };

        var folderPicker = new FolderPicker
        {
            Bounds = new(16, 316, 300, 26),
            SelectedPath = "/tmp",
            Title = "Pick a folder",
        };
        folderPicker.PathChanged += (_, _) => this.SetStatus($"FolderPicker: {folderPicker.SelectedPath}");
        _toolTip.SetToolTip(folderPicker, "Type a path and press Enter, or browse with the … button.");

        var breadcrumb = new Breadcrumb { Bounds = new(16, 368, 320, 26), Editable = true };
        breadcrumb.Items.AddRange("Computer", "Documents", "Projects", "NativeForms", "docs");
        breadcrumb.ItemClicked += (_, e) => this.SetStatus($"Breadcrumb: navigated to \"{e.Item.Text}\".");

        // Folder walk: each chevron drops down that segment's children — a virtual listing here, so it
        // would serve an archive or a remote tree just as well.
        breadcrumb.SubItemsProvider = parent =>
        {
            var stem = parent?.Text ?? "root";
            return [new BreadcrumbItem($"{stem}·one"), new BreadcrumbItem($"{stem}·two"), new BreadcrumbItem($"{stem}·three")];
        };
        breadcrumb.SubItemSelected += (_, e) => this.SetStatus($"Breadcrumb: opened \"{e.Item.Text}\".");

        // Click the empty space to type a path; a delegate autocompletes it against a small virtual set.
        breadcrumb.AutoCompleteSource = text =>
        {
            var pool = new[] { "Computer/Documents", "Computer/Downloads", "Computer/Music", "Computer/Pictures" };
            var hits = new List<string>();
            foreach (var candidate in pool)
                if (candidate.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    hits.Add(candidate);

            return hits;
        };
        breadcrumb.PathEntered += (_, e) => this.SetStatus($"Breadcrumb: entered path \"{e.Path}\".");

        page.Controls.AddRange(
            Caption("FilePicker (open, filtered)", 16, 12),
            openPicker,
            Caption("FilePicker (save mode)", 16, 68),
            savePicker,
            Caption("FilePicker (read-only, multi-select)", 16, 124),
            multiPicker, multiCount,
            Caption("FilePicker (a path that does not exist)", 16, 208),
            brokenPicker, brokenNote,
            Caption("FolderPicker", 16, 292),
            folderPicker,
            Caption("Breadcrumb (segment · chevron · edit)", 16, 344, 320),
            breadcrumb);

        // --- Column 2: icon labels ---------------------------------------------------------------

        var iconBefore = new IconLabel
        {
            Bounds = new(340, 36, 300, 24),
            Text = "ImageBeforeText (the default)",
            Image = this.DiscImage(Color.RoyalBlue),
        };
        var iconAfter = new IconLabel
        {
            Bounds = new(340, 66, 300, 24),
            Text = "TextBeforeImage",
            Image = this.DiscImage(Color.MediumSeaGreen),
            TextImageRelation = TextImageRelation.TextBeforeImage,
        };
        var iconAbove = new IconLabel
        {
            Bounds = new(340, 96, 300, 48),
            Text = "ImageAboveText",
            Image = this.SquareImage(Color.Goldenrod),
            TextImageRelation = TextImageRelation.ImageAboveText,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var iconCentered = new IconLabel
        {
            Bounds = new(340, 150, 300, 24),
            Text = "TextAlign = MiddleCenter",
            Image = this.DiscImage(Color.MediumOrchid),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var iconAuto = new IconLabel
        {
            Bounds = new(340, 180, 10, 10),
            Text = "AutoSize fits both parts",
            Image = this.DiscImage(Color.Crimson),
            AutoSize = true,
        };
        var iconDisabled = new IconLabel
        {
            Bounds = new(340, 210, 300, 24),
            Text = "Disabled greys the caption",
            Image = this.DiscImage(Color.SlateGray),
            Enabled = false,
        };

        page.Controls.AddRange(
            Caption("IconLabel (image and text together)", 340, 12),
            iconBefore, iconAfter, iconAbove, iconCentered, iconAuto, iconDisabled);

        // --- Column 3: drive tiles ---------------------------------------------------------------

        var systemDrive = new ProgressTile
        {
            Bounds = new(664, 36, 300, 76),
            Text = "Windows (C:)",
            SecondaryText = "45.2 GB free of 128 GB",
            Image = this.SquareImage(Color.Silver),
            Maximum = 128,
            Value = 83,
            Clickable = true,
            Selected = true,
        };
        systemDrive.Click += (_, _) => this.SetStatus("Drive tile: Windows (C:) clicked.");

        // Nearly full: past the threshold, so the bar paints in the alert red Explorer uses.
        var fullDrive = new ProgressTile
        {
            Bounds = new(664, 124, 300, 76),
            Text = "Backup (D:)",
            SecondaryText = "2.1 GB free of 512 GB",
            Image = this.SquareImage(Color.Goldenrod),
            Maximum = 512,
            Value = 510,
            WarningThreshold = 460,
            Clickable = true,
        };
        fullDrive.Click += (_, _) => this.SetStatus("Drive tile: Backup (D:) clicked — nearly full.");
        _toolTip.SetToolTip(fullDrive, "Past WarningThreshold, so the bar turns red.");

        var mediaDrive = new ProgressTile
        {
            Bounds = new(664, 212, 300, 76),
            Text = "Media (E:)",
            SecondaryText = "1.4 TB free of 2 TB",
            Image = this.SquareImage(Color.MediumSeaGreen),
            Maximum = 2000,
            Value = 600,
            WarningThreshold = 1800,
            Clickable = true,
        };
        mediaDrive.Click += (_, _) => this.SetStatus("Drive tile: Media (E:) clicked.");

        // An inert tile: no hover, no focus, no click — the same shape used as a read-out.
        var quotaTile = new ProgressTile
        {
            Bounds = new(664, 328, 300, 76),
            Text = "Mailbox quota",
            SecondaryText = "8.7 GB of 10 GB used",
            Image = this.DiscImage(Color.CornflowerBlue),
            Maximum = 100,
            Value = 87,
            WarningThreshold = 85,
        };

        // The tiles behave as one exclusive set, like an Explorer drive list.
        void Select(ProgressTile picked)
        {
            systemDrive.Selected = ReferenceEquals(picked, systemDrive);
            fullDrive.Selected = ReferenceEquals(picked, fullDrive);
            mediaDrive.Selected = ReferenceEquals(picked, mediaDrive);
        }

        systemDrive.Click += (_, _) => Select(systemDrive);
        fullDrive.Click += (_, _) => Select(fullDrive);
        mediaDrive.Click += (_, _) => Select(mediaDrive);

        // A compact tile: icon left, caption over the bar on its right, both matching the icon height.
        var compactTile = new ProgressTile
        {
            Bounds = new(664, 428, 300, 52),
            Text = "Downloads",
            SecondaryText = "ignored in compact mode",
            Image = this.SquareImage(Color.SteelBlue),
            Maximum = 100,
            Value = 62,
            Compact = true,
        };

        page.Controls.AddRange(
            Caption("ProgressTile (Explorer-style drives)", 664, 12),
            systemDrive, fullDrive, mediaDrive,
            Caption("ProgressTile (inert read-out)", 664, 304),
            quotaTile,
            Caption("ProgressTile (compact)", 664, 408, 300),
            compactTile);

        // Snapshotted the instant each value was authored, so the restore cannot drift away from it.
        var openPath = openPicker.SelectedPath;
        var savePath = savePicker.SelectedPath;
        var multiPath = multiPicker.SelectedPath;
        var multiText = multiCount.Text;
        var folderPath = folderPicker.SelectedPath;
        var systemSelected = systemDrive.Selected;
        var fullSelected = fullDrive.Selected;
        var mediaSelected = mediaDrive.Selected;
        this.OnReset(() =>
        {
            openPicker.SelectedPath = openPath;
            savePicker.SelectedPath = savePath;
            multiPicker.SelectedPath = multiPath;
            multiCount.Text = multiText;
            folderPicker.SelectedPath = folderPath;
            systemDrive.Selected = systemSelected;
            fullDrive.Selected = fullSelected;
            mediaDrive.Selected = mediaSelected;
        });

        this.Publish("pickers.page", page);
        this.Publish("pickers.open", openPicker);
        this.Publish("pickers.save", savePicker);
        this.Publish("pickers.multi", multiPicker);
        this.Publish("pickers.multiCount", multiCount);
        this.Publish("pickers.broken", brokenPicker);
        this.Publish("pickers.folder", folderPicker);
        this.Publish("pickers.iconBefore", iconBefore);
        this.Publish("pickers.iconAfter", iconAfter);
        this.Publish("pickers.iconAuto", iconAuto);
        this.Publish("pickers.driveSystem", systemDrive);
        this.Publish("pickers.driveFull", fullDrive);
        this.Publish("pickers.driveMedia", mediaDrive);
        this.Publish("pickers.quota", quotaTile);

        return page;
    }
}
