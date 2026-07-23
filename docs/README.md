# NativeForms documentation

> Reference documentation for the NativeForms toolkit — start here, drill into the pages.

New to the library? Read the [architecture overview](architecture.md) first, then the
[`Control`](controls/control.md) base page; every control page builds on those two.

**WinForms compatibility.** The API is Windows-Forms-shaped, close enough that porting is mostly a
namespace swap — but not a clone: reflection-bound surfaces are replaced by delegates, a few
defaults differ, and some legacy corners are deliberate non-goals. The base-class deviations are
collected in
[Control — Differences from System.Windows.Forms.Control](controls/control.md#differences-from-systemwindowsformscontrol);
each control page carries its own "Differences from WinForms" section where behavior diverges.

## Gallery

The `NativeForms.Demo` app is a tabbed showcase of every control, captured on Linux/GTK by its
headless autopilot. Each page below is one tab of the demo:

| | | |
|:---:|:---:|:---:|
| [![Basics](screenshots/01-basics.png)](screenshots/01-basics.png)<br>Basics | [![Input](screenshots/02-input.png)](screenshots/02-input.png)<br>Input | [![Lists](screenshots/03-lists.png)](screenshots/03-lists.png)<br>Lists & trees |
| [![Grid](screenshots/04-grid.png)](screenshots/04-grid.png)<br>DataGridView | [![Layout](screenshots/05-layout.png)](screenshots/05-layout.png)<br>Layout | [![Docking](screenshots/06-docking.png)](screenshots/06-docking.png)<br>Docking |
| [![Pickers](screenshots/07-pickers.png)](screenshots/07-pickers.png)<br>Pickers | [![Ribbon](screenshots/08-ribbon.png)](screenshots/08-ribbon.png)<br>Ribbon | [![Calendar](screenshots/09-calendar.png)](screenshots/09-calendar.png)<br>Calendar |

Interaction states: [docking drag overlays](screenshots/docking-drag.png),
[the month scheduler](screenshots/calendar-month.png), [a modal MessageBox](screenshots/messagebox.png),
[a context menu](screenshots/context-menu.png).

## Guides

| Page | Covers |
|---|---|
| [Architecture](architecture.md) | Core/peer split, backends, realization lifecycle, containers, popups, timers, modal loops, AOT rules |
| [MVVM & data binding](mvvm.md) | `ObservableObject`, `RelayCommand`, `PropertyBinding<T>`, `ObservableList<T>`, command wiring |
| [Custom controls](custom-controls.md) | Authoring owner-drawn controls: `OwnerDrawnControl`, `IGraphics`, `ITheme`, shared paint primitives |
| [Images, animation & cursors](imaging.md) | Supported formats (PNG/BMP/PCX/GIF/ICO/CUR/ANI), `AnimatedImage` + the shared clock, custom cursors from a `.cur`/`.ani` |
| [PRD](PRD.md) | The authoritative feature checklist and roadmap |

## Control reference

| Control | Strategy | Page |
|---|---|---|
| `Accordion` / `AccordionPane` | owner-drawn | [accordion.md](controls/accordion.md) |
| `Application` + `BackendRegistry` | — | [application.md](controls/application.md) |
| `Breadcrumb` | owner-drawn | [breadcrumb.md](controls/breadcrumb.md) |
| `Button` | native | [button.md](controls/button.md) |
| `CalendarView` | owner-drawn | [calendarview.md](controls/calendarview.md) |
| `CheckBox` | owner-drawn | [checkbox.md](controls/checkbox.md) |
| `CheckedListBox` | owner-drawn | [checkedlistbox.md](controls/checkedlistbox.md) |
| `ClockFace` | owner-drawn + popup dial | [clockface.md](controls/clockface.md) |
| `ColorPicker` | owner-drawn | [colorpicker.md](controls/colorpicker.md) |
| `ComboBox` | owner-drawn + popup | [combobox.md](controls/combobox.md) |
| `ContextMenuStrip` | owner-drawn (popup engine) | [contextmenustrip.md](controls/contextmenustrip.md) |
| `Control` (base class) | — | [control.md](controls/control.md) |
| `DataGridView` | owner-drawn | [datagridview.md](controls/datagridview.md) |
| `DateTimePicker` | owner-drawn + popup calendar | [datetimepicker.md](controls/datetimepicker.md) |
| `DockPanel` / `DockContent` | owner-drawn + windows | [dockpanel.md](controls/dockpanel.md) |
| `DomainUpDown` | owner-drawn + hosted editor | [domainupdown.md](controls/domainupdown.md) |
| `Expander` | owner-drawn | [expander.md](controls/expander.md) |
| `FilePicker` | owner-drawn + hosted editor | [filepicker.md](controls/filepicker.md) |
| `FlowLayoutPanel` | owner-drawn layout | [flowlayoutpanel.md](controls/flowlayoutpanel.md) |
| `FolderPicker` | owner-drawn + hosted editor | [folderpicker.md](controls/folderpicker.md) |
| `Form` (incl. modal + window management) | native | [form.md](controls/form.md) |
| `GridPicker` (Office table-size chooser) | owner-drawn | [gridpicker.md](controls/gridpicker.md) |
| `GroupBox` | owner-drawn | [groupbox.md](controls/groupbox.md) |
| `HScrollBar` / `VScrollBar` | owner-drawn | [scrollbar.md](controls/scrollbar.md) |
| `IconLabel` | owner-drawn | [iconlabel.md](controls/iconlabel.md) |
| `ImageList` | — | [imagelist.md](controls/imagelist.md) |
| `Label` | native | [label.md](controls/label.md) |
| `LinkLabel` | owner-drawn | [linklabel.md](controls/linklabel.md) |
| `ListBox` | owner-drawn | [listbox.md](controls/listbox.md) |
| `ListView` | owner-drawn | [listview.md](controls/listview.md) |
| `MaskedTextBox` | native + core mask engine | [maskedtextbox.md](controls/maskedtextbox.md) |
| `MenuStrip` + menu items | owner-drawn (popup engine) | [menustrip.md](controls/menustrip.md) |
| `MessageBox` + common dialogs | native | [dialogs.md](controls/dialogs.md) |
| `MonthCalendar` | owner-drawn | [monthcalendar.md](controls/monthcalendar.md) |
| `NotifyIcon` | native (Windows) | [notifyicon.md](controls/notifyicon.md) |
| `NumericUpDown` | owner-drawn + hosted editor | [numericupdown.md](controls/numericupdown.md) |
| `Panel` (incl. `AutoScroll`) | owner-drawn | [panel.md](controls/panel.md) |
| `PictureBox` | owner-drawn | [picturebox.md](controls/picturebox.md) |
| `ProgressBar` | owner-drawn | [progressbar.md](controls/progressbar.md) |
| `ProgressTile` | owner-drawn | [progresstile.md](controls/progresstile.md) |
| `RadioButton` | owner-drawn | [radiobutton.md](controls/radiobutton.md) |
| `Ribbon` (tabs, groups, items) | owner-drawn + popup | [ribbon.md](controls/ribbon.md) |
| `RichTextBox` | native | [richtextbox.md](controls/richtextbox.md) |
| `SearchBox` | owner-drawn + hosted editor | [searchbox.md](controls/searchbox.md) |
| `SplitButton` / `DropDownButton` | owner-drawn | [splitbutton.md](controls/splitbutton.md) |
| `SplitContainer` | owner-drawn | [splitcontainer.md](controls/splitcontainer.md) |
| `StatusStrip` | owner-drawn | [statusstrip.md](controls/statusstrip.md) |
| `TabControl` / `TabPage` | owner-drawn | [tabcontrol.md](controls/tabcontrol.md) |
| `TableLayoutPanel` | owner-drawn layout | [tablelayoutpanel.md](controls/tablelayoutpanel.md) |
| `TextBox` | native | [textbox.md](controls/textbox.md) |
| `TimePicker` | owner-drawn + popup clock | [timepicker.md](controls/timepicker.md) |
| `Timer` | — | [timer.md](controls/timer.md) |
| `ToggleSwitch` | owner-drawn | [toggleswitch.md](controls/toggleswitch.md) |
| `ToolStrip` | owner-drawn | [toolstrip.md](controls/toolstrip.md) |
| `ToolTip` | owner-drawn (popup) | [tooltip.md](controls/tooltip.md) |
| `TrackBar` | owner-drawn | [trackbar.md](controls/trackbar.md) |
| `TreeListView` | owner-drawn | [treelistview.md](controls/treelistview.md) |
| `TreeView` | owner-drawn | [treeview.md](controls/treeview.md) |

Controls not listed here are not shipped yet — the full planned inventory, with per-control
acceptance criteria and milestones, lives in the [PRD](PRD.md) (§7 and §10). A page is added here
in the same change that ships its control; the coverage rule is PRD §11.
