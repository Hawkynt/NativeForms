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

## Guides

| Page | Covers |
|---|---|
| [Architecture](architecture.md) | Core/peer split, backends, realization lifecycle, containers, popups, timers, modal loops, AOT rules |
| [MVVM & data binding](mvvm.md) | `ObservableObject`, `RelayCommand`, `PropertyBinding<T>`, `ObservableList<T>`, command wiring |
| [Custom controls](custom-controls.md) | Authoring owner-drawn controls: `OwnerDrawnControl`, `IGraphics`, `ITheme`, shared paint primitives |
| [PRD](PRD.md) | The authoritative feature checklist and roadmap |

## Control reference

| Control | Strategy | Page |
|---|---|---|
| `Application` + `BackendRegistry` | — | [application.md](controls/application.md) |
| `Control` (base class) | — | [control.md](controls/control.md) |
| `Form` (incl. modal + window management) | native | [form.md](controls/form.md) |
| `Button` | native | [button.md](controls/button.md) |
| `Label` | native | [label.md](controls/label.md) |
| `LinkLabel` | owner-drawn | [linklabel.md](controls/linklabel.md) |
| `TextBox` | native | [textbox.md](controls/textbox.md) |
| `MaskedTextBox` | native + core mask engine | [maskedtextbox.md](controls/maskedtextbox.md) |
| `RichTextBox` | native | [richtextbox.md](controls/richtextbox.md) |
| `SearchBox` | owner-drawn + hosted editor | [searchbox.md](controls/searchbox.md) |
| `CheckBox` | owner-drawn | [checkbox.md](controls/checkbox.md) |
| `RadioButton` | owner-drawn | [radiobutton.md](controls/radiobutton.md) |
| `ToggleSwitch` | owner-drawn | [toggleswitch.md](controls/toggleswitch.md) |
| `SplitButton` / `DropDownButton` | owner-drawn | [splitbutton.md](controls/splitbutton.md) |
| `NumericUpDown` | owner-drawn + hosted editor | [numericupdown.md](controls/numericupdown.md) |
| `DomainUpDown` | owner-drawn + hosted editor | [domainupdown.md](controls/domainupdown.md) |
| `TrackBar` | owner-drawn | [trackbar.md](controls/trackbar.md) |
| `HScrollBar` / `VScrollBar` | owner-drawn | [scrollbar.md](controls/scrollbar.md) |
| `ProgressBar` | owner-drawn | [progressbar.md](controls/progressbar.md) |
| `DateTimePicker` | owner-drawn + popup calendar | [datetimepicker.md](controls/datetimepicker.md) |
| `MonthCalendar` | owner-drawn | [monthcalendar.md](controls/monthcalendar.md) |
| `TimePicker` | owner-drawn | [timepicker.md](controls/timepicker.md) |
| `Panel` (incl. `AutoScroll`) | owner-drawn | [panel.md](controls/panel.md) |
| `GroupBox` | owner-drawn | [groupbox.md](controls/groupbox.md) |
| `TabControl` / `TabPage` | owner-drawn | [tabcontrol.md](controls/tabcontrol.md) |
| `SplitContainer` | owner-drawn | [splitcontainer.md](controls/splitcontainer.md) |
| `Expander` | owner-drawn | [expander.md](controls/expander.md) |
| `FlowLayoutPanel` | owner-drawn layout | [flowlayoutpanel.md](controls/flowlayoutpanel.md) |
| `TableLayoutPanel` | owner-drawn layout | [tablelayoutpanel.md](controls/tablelayoutpanel.md) |
| `ListBox` | owner-drawn | [listbox.md](controls/listbox.md) |
| `CheckedListBox` | owner-drawn | [checkedlistbox.md](controls/checkedlistbox.md) |
| `ComboBox` | owner-drawn + popup | [combobox.md](controls/combobox.md) |
| `ListView` | owner-drawn | [listview.md](controls/listview.md) |
| `TreeView` | owner-drawn | [treeview.md](controls/treeview.md) |
| `TreeListView` | owner-drawn | [treelistview.md](controls/treelistview.md) |
| `DataGridView` | owner-drawn | [datagridview.md](controls/datagridview.md) |
| `PictureBox` | owner-drawn | [picturebox.md](controls/picturebox.md) |
| `ImageList` | — | [imagelist.md](controls/imagelist.md) |
| `Timer` | — | [timer.md](controls/timer.md) |
| `MenuStrip` + menu items | owner-drawn (popup engine) | [menustrip.md](controls/menustrip.md) |
| `ContextMenuStrip` | owner-drawn (popup engine) | [contextmenustrip.md](controls/contextmenustrip.md) |
| `ToolStrip` | owner-drawn | [toolstrip.md](controls/toolstrip.md) |
| `StatusStrip` | owner-drawn | [statusstrip.md](controls/statusstrip.md) |
| `ToolTip` | owner-drawn (popup) | [tooltip.md](controls/tooltip.md) |
| `NotifyIcon` | native (Windows) | [notifyicon.md](controls/notifyicon.md) |
| `MessageBox` + common dialogs | native | [dialogs.md](controls/dialogs.md) |

Controls not listed here are not shipped yet — the full planned inventory, with per-control
acceptance criteria and milestones, lives in the [PRD](PRD.md) (§7 and §10). A page is added here
in the same change that ships its control; the coverage rule is PRD §11.
