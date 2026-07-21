using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>
    /// The Ribbon page: an Office-style <see cref="Ribbon"/> across the top — three tabs, groups of
    /// large and small items, a latching toggle and a hosted combo box — over an Outlook-style
    /// <see cref="Accordion"/> whose three panes carry real nested controls. The two switches beside
    /// the accordion drive the ribbon's minimized state and squeeze its width until the trailing
    /// group collapses into its drop-down button.
    /// </summary>
    private TabPage BuildRibbonPage()
    {
        var page = new TabPage("Ribbon") { ImageIndex = _IconYellow };

        // --- The ribbon -------------------------------------------------------------------------

        var ribbon = new Ribbon
        {
            Bounds = new(16, 36, 800, 140),
            ImageList = _icons,
        };

        var home = new RibbonTab("Home");

        var clipboard = new RibbonGroup("Clipboard") { ImageIndex = _IconOpen };
        var paste = new RibbonButton("Paste") { ImageList = _icons, ImageIndex = _IconOpen };
        var pasteRuns = 0;
        paste.Command = new RelayCommand(() =>
        {
            ++pasteRuns;
            this.SetStatus($"Ribbon: the Paste command ran {pasteRuns} time(s).");
        });
        var cut = new RibbonButton("Cut", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconRed };
        cut.Click += (_, _) => this.SetStatus("Ribbon: Cut clicked.");
        var copy = new RibbonButton("Copy", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconGreen };
        copy.Click += (_, _) => this.SetStatus("Ribbon: Copy clicked.");
        var format = new RibbonButton("Format", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconBlue };
        format.Click += (_, _) => this.SetStatus("Ribbon: Format clicked.");
        clipboard.Items.AddRange(paste, cut, copy, format);

        var font = new RibbonGroup("Font") { ImageIndex = _IconGear };
        var bold = new RibbonToggleButton("Bold", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconPurple };
        bold.CheckedChanged += (_, _) => this.SetStatus($"Ribbon: Bold is {(bold.Checked ? "on" : "off")}.");
        var italic = new RibbonToggleButton("Italic", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconYellow };
        italic.CheckedChanged += (_, _) => this.SetStatus($"Ribbon: Italic is {(italic.Checked ? "on" : "off")}.");
        var underline = new RibbonToggleButton("Underline", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconFile };
        font.Items.AddRange(bold, italic, underline);

        var styles = new RibbonGroup("Styles") { ImageIndex = _IconFolder };
        var styleCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        styleCombo.Items.AddRange(["Normal", "Heading 1", "Heading 2", "Quote"]);
        styleCombo.SelectedIndex = 0;
        styleCombo.SelectedIndexChanged += (_, _) => this.SetStatus($"Ribbon: style \"{styleCombo.Text}\" picked.");
        styles.Items.Add(new RibbonHostItem(styleCombo) { HostWidth = 140 });
        var clearStyle = new RibbonButton("Clear", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconNew };
        clearStyle.Click += (_, _) => this.SetStatus("Ribbon: Clear formatting clicked.");
        styles.Items.Add(clearStyle);

        home.Groups.AddRange(clipboard, font, styles);

        var insert = new RibbonTab("Insert");
        var tables = new RibbonGroup("Tables") { ImageIndex = _IconFile };
        var table = new RibbonButton("Table") { ImageList = _icons, ImageIndex = _IconFile };
        table.Click += (_, _) => this.SetStatus("Ribbon: Table clicked.");
        tables.Items.Add(table);
        var illustrations = new RibbonGroup("Illustrations") { ImageIndex = _IconBlue };
        illustrations.Items.AddRange(
            new RibbonButton("Picture") { ImageList = _icons, ImageIndex = _IconBlue },
            new RibbonButton("Chart", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconGreen },
            new RibbonButton("Shape", RibbonItemSize.Small) { ImageList = _icons, ImageIndex = _IconRed });
        insert.Groups.AddRange(tables, illustrations);

        var view = new RibbonTab("View");
        var zoom = new RibbonGroup("Zoom") { ImageIndex = _IconGear };
        zoom.Items.AddRange(
            new RibbonButton("Zoom") { ImageList = _icons, ImageIndex = _IconGear },
            new RibbonButton("100 %", RibbonItemSize.Small),
            new RibbonButton("Fit", RibbonItemSize.Small));
        view.Groups.Add(zoom);

        ribbon.Tabs.AddRange(home, insert, view);
        ribbon.SelectedIndexChanged += (_, _)
            => this.SetStatus($"Ribbon: the {ribbon.SelectedTab?.Text} tab is showing.");

        // --- The accordion ----------------------------------------------------------------------

        var accordion = new Accordion
        {
            Bounds = new(16, 210, 300, 330),
            ImageList = _icons,
        };

        var mail = new AccordionPane("Mail") { ImageIndex = _IconBlue };
        mail.Controls.Add(new Label { Bounds = new(12, 10, 260, 18), Text = "Folders" });
        var folders = new ListBox { Bounds = new(12, 32, 264, 120) };
        folders.Items.AddRange(["Inbox", "Drafts", "Sent", "Archive"]);
        folders.SelectedIndex = 0;
        folders.SelectedIndexChanged += (_, _) => this.SetStatus($"Accordion: folder \"{folders.SelectedItem}\" selected.");
        mail.Controls.Add(folders);
        mail.Controls.Add(new CheckBox { Bounds = new(12, 160, 264, 20), Text = "Unread messages only" });
        var compose = new Button { Bounds = new(12, 188, 120, 28), Text = "Compose" };
        compose.Click += (_, _) => this.SetStatus("Accordion: Compose clicked.");
        mail.Controls.Add(compose);

        var calendar = new AccordionPane("Calendar") { ImageIndex = _IconGreen };
        calendar.Controls.Add(new Label { Bounds = new(12, 10, 260, 18), Text = "Show" });
        calendar.Controls.Add(new RadioButton { Bounds = new(12, 34, 264, 20), Text = "Day", Checked = true });
        calendar.Controls.Add(new RadioButton { Bounds = new(12, 58, 264, 20), Text = "Work week" });
        calendar.Controls.Add(new RadioButton { Bounds = new(12, 82, 264, 20), Text = "Month" });
        // Kept inside 124 px so the Calendar body is complete even when a second pane is open beside
        // it and the two split the height the headers leave.
        calendar.Controls.Add(new CheckBox { Bounds = new(12, 104, 264, 20), Text = "Show declined events" });

        var contacts = new AccordionPane("Contacts") { ImageIndex = _IconPurple };
        contacts.Controls.Add(new Label { Bounds = new(12, 10, 80, 18), Text = "Search:" });
        contacts.Controls.Add(new TextBox { Bounds = new(96, 8, 180, 24), Text = "Hawkynt" });
        contacts.Controls.Add(new CheckBox { Bounds = new(12, 40, 264, 20), Text = "Favourites first", Checked = true });

        accordion.Panes.AddRange(mail, calendar, contacts);
        accordion.SelectedIndexChanged += (_, _)
            => this.SetStatus($"Accordion: the {accordion.SelectedPane?.Text} pane is open.");

        // --- The switches beside the accordion --------------------------------------------------

        var minimize = new CheckBox { Bounds = new(340, 214, 240, 20), Text = "Minimize the ribbon" };
        minimize.CheckedChanged += (_, _) => ribbon.Minimized = minimize.Checked;

        // Wide enough for the caption plus the check square once GTK measures it in the real font;
        // the layout audit fails a box that would elide its own text.
        var multiple = new CheckBox { Bounds = new(340, 240, 280, 20), Text = "Accordion: allow several open" };
        multiple.CheckedChanged += (_, _) => accordion.ExpandMode =
            multiple.Checked ? AccordionExpandMode.Multiple : AccordionExpandMode.Single;

        var narrow = new Button { Bounds = new(340, 270, 200, 28), Text = "Narrow the ribbon" };
        narrow.Click += (_, _) =>
        {
            ribbon.Width = ribbon.Width > 420 ? 380 : 800;
            this.SetStatus($"Ribbon: width is {ribbon.Width} px — groups collapse as it shrinks.");
        };

        page.Controls.AddRange(
            Caption("Ribbon (tabs → groups → large and small items)", 16, 12, 420),
            ribbon,
            Caption("Accordion (Outlook-style navigation pane)", 16, 186, 380),
            accordion,
            minimize,
            multiple,
            narrow);

        // Snapshotted the instant each value was authored, so the restore cannot drift away from it.
        var ribbonTab = ribbon.SelectedIndex;
        var ribbonWidth = ribbon.Width;
        var accordionPane = accordion.SelectedIndex;
        var boldChecked = bold.Checked;
        var italicChecked = italic.Checked;
        var folderIndex = folders.SelectedIndex;
        var styleIndex = styleCombo.SelectedIndex;
        this.OnReset(() =>
        {
            minimize.Checked = false;
            multiple.Checked = false;
            ribbon.Minimized = false;
            ribbon.Width = ribbonWidth;
            ribbon.SelectedIndex = ribbonTab;
            accordion.ExpandMode = AccordionExpandMode.Single;
            accordion.SelectedIndex = accordionPane;
            bold.Checked = boldChecked;
            italic.Checked = italicChecked;
            folders.SelectedIndex = folderIndex;
            styleCombo.SelectedIndex = styleIndex;
        });

        this.Publish("ribbon.page", page);
        this.Publish("ribbon.control", ribbon);
        this.Publish("ribbon.accordion", accordion);
        this.Publish("ribbon.paste", paste);
        this.Publish("ribbon.bold", bold);
        this.Publish("ribbon.clipboard", clipboard);
        this.Publish("ribbon.styles", styles);
        this.Publish("ribbon.styleCombo", styleCombo);
        this.Publish("ribbon.minimize", minimize);
        this.Publish("ribbon.multiple", multiple);
        this.Publish("ribbon.narrow", narrow);
        this.Publish("ribbon.mailPane", mail);
        this.Publish("ribbon.calendarPane", calendar);
        this.Publish("ribbon.contactsPane", contacts);
        this.Publish("ribbon.folders", folders);
        this.Publish("ribbon.compose", compose);

        return page;
    }
}
