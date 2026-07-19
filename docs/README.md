# NativeForms documentation

> Reference documentation for the NativeForms toolkit — start here, drill into the pages.

New to the library? Read the [architecture overview](architecture.md) first, then the
[`Control`](controls/control.md) base page; every control page builds on those two.

## Guides

| Page | Covers |
|---|---|
| [Architecture](architecture.md) | Core/peer split, backends, realization lifecycle, AOT rules |
| [MVVM & data binding](mvvm.md) | `ObservableObject`, `RelayCommand`, `PropertyBinding<T>`, `ObservableList<T>` |
| [Custom controls](custom-controls.md) | Authoring owner-drawn controls: `OwnerDrawnControl`, `IGraphics`, `ITheme` |
| [PRD](PRD.md) | The authoritative feature checklist and roadmap |

## Control reference

| Control | Strategy | Page |
|---|---|---|
| `Application` + `BackendRegistry` | — | [application.md](controls/application.md) |
| `Control` (base class) | — | [control.md](controls/control.md) |
| `Form` | native | [form.md](controls/form.md) |
| `Button` | native | [button.md](controls/button.md) |
| `Label` | native | [label.md](controls/label.md) |
| `Panel` | owner-drawn | [panel.md](controls/panel.md) |
| `GroupBox` | owner-drawn | [groupbox.md](controls/groupbox.md) |
| `CheckBox` | owner-drawn | [checkbox.md](controls/checkbox.md) |
| `RadioButton` | owner-drawn | [radiobutton.md](controls/radiobutton.md) |
| `ProgressBar` | owner-drawn | [progressbar.md](controls/progressbar.md) |
| `ListBox` | owner-drawn | [listbox.md](controls/listbox.md) |
| `ListView` | owner-drawn | [listview.md](controls/listview.md) |
| `DataGridView` | owner-drawn | [datagridview.md](controls/datagridview.md) |

Controls not listed here are not shipped yet — the full planned inventory, with per-control
acceptance criteria and milestones, lives in the [PRD](PRD.md) (§7 and §10). A page is added here
in the same change that ships its control; the coverage rule is PRD §11.
