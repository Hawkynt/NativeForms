# NativeForms

[![License](https://img.shields.io/github/license/Hawkynt/NativeForms)](LICENSE)
[![Language](https://img.shields.io/github/languages/top/Hawkynt/NativeForms?color=8957D5)](https://github.com/Hawkynt/NativeForms)

[![CI](https://img.shields.io/github/actions/workflow/status/Hawkynt/NativeForms/ci.yml?branch=main&label=CI)](https://github.com/Hawkynt/NativeForms/actions/workflows/ci.yml)
[![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/NativeForms)](https://github.com/Hawkynt/NativeForms/commits/main)
[![Activity](https://img.shields.io/github/commit-activity/m/Hawkynt/NativeForms)](https://github.com/Hawkynt/NativeForms/pulse)

[![Stars](https://img.shields.io/github/stars/Hawkynt/NativeForms?color=FFD700)](https://github.com/Hawkynt/NativeForms/stargazers)
[![Forks](https://img.shields.io/github/forks/Hawkynt/NativeForms?color=008080)](https://github.com/Hawkynt/NativeForms/network/members)
[![Issues](https://img.shields.io/github/issues/Hawkynt/NativeForms)](https://github.com/Hawkynt/NativeForms/issues)
[![Code Size](https://img.shields.io/github/languages/code-size/Hawkynt/NativeForms)](https://github.com/Hawkynt/NativeForms)
[![Repo Size](https://img.shields.io/github/repo-size/Hawkynt/NativeForms)](https://github.com/Hawkynt/NativeForms)

> A fast, tiny, trim/AOT-compatible UI toolkit with a Windows Forms-shaped API that renders through
> platform-native widgets (Win32, GTK, Cocoa) via P/Invoke — and paints the controls no platform
> offers natively in that platform's own visual style.

## ✨ What it is

NativeForms lets you write desktop UI with the ergonomics of `System.Windows.Forms` — `Form`,
`Button`, `Label`, a `Controls` collection, `Click` events — but each control is a **real native
widget** on the host OS, so your app looks and behaves like every other app on that desktop. Controls
no platform ships natively (a `DataGridView`, a rich `ListView`, an icon `ComboBox`) are **owner-drawn
in the platform's own theme**, so they still look native.

It is built to be **small and quick**: reflection-free, `IsAotCompatible`, buffered peer state,
value-type geometry, and no per-frame allocation — kilobytes of managed overhead, not megabytes.

## 🧩 Architecture

```
Hawkynt.NativeForms                     Core: controls, layout, events, data-binding (no native code)
Hawkynt.NativeForms.Backends.Windows    Win32   via [LibraryImport]
Hawkynt.NativeForms.Backends.Gtk        GTK 3   via [LibraryImport]
Hawkynt.NativeForms.Backends.MacOS      Cocoa   (placeholder — on the roadmap)
```

Core never calls a native API; it drives platform **peers** through `IPlatformBackend`. An app
registers the backends it ships — all of them for "one binary, every platform", or just one to
shrink a single-platform build.

## 🚀 Quick start

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.Windows;

BackendRegistry.Register(new Win32Backend());
BackendRegistry.Register(new GtkBackend());

var form = new Form { Text = "Hello", Bounds = new(0, 0, 320, 160) };
var button = new Button { Text = "Click me", Bounds = new(20, 20, 140, 36) };
button.Click += (_, _) => button.Text = "Clicked!";
form.Controls.Add(button);

Application.Run(form);
```

MVVM, MVC and MVP are all first-class: `ObservableObject`, `RelayCommand`/`RelayCommand<T>` and a
reflection-free two-way `PropertyBinding<T>` live in `Hawkynt.NativeForms.ComponentModel`. See
`NativeForms.Demo` for a bound counter.

## 📖 Documentation & supported controls

The full reference lives under **[`docs/`](docs/README.md)** — an [architecture
overview](docs/architecture.md), an [MVVM & data-binding guide](docs/mvvm.md), a [custom-control
authoring guide](docs/custom-controls.md), and one reference page per control (usage example, API
tables, behavior notes). What ships today:

| Control | Strategy | Reference |
|---|---|---|
| `Form` | native | [docs/controls/form.md](docs/controls/form.md) |
| `Button` | native | [docs/controls/button.md](docs/controls/button.md) |
| `Label` | native | [docs/controls/label.md](docs/controls/label.md) |
| `Panel` | owner-drawn | [docs/controls/panel.md](docs/controls/panel.md) |
| `GroupBox` | owner-drawn | [docs/controls/groupbox.md](docs/controls/groupbox.md) |
| `CheckBox` | owner-drawn | [docs/controls/checkbox.md](docs/controls/checkbox.md) |
| `RadioButton` | owner-drawn | [docs/controls/radiobutton.md](docs/controls/radiobutton.md) |
| `ProgressBar` | owner-drawn | [docs/controls/progressbar.md](docs/controls/progressbar.md) |
| `ListBox` | owner-drawn | [docs/controls/listbox.md](docs/controls/listbox.md) |
| `ListView` | owner-drawn | [docs/controls/listview.md](docs/controls/listview.md) |
| `DataGridView` | owner-drawn | [docs/controls/datagridview.md](docs/controls/datagridview.md) |

Plus the non-visual foundation: [`Application` & backend registration](docs/controls/application.md),
the [`Control` base class](docs/controls/control.md), and the reflection-free MVVM primitives.
`NativeForms.Demo` doubles as a gallery showing every shipped control with representative property
settings.

## 📋 Status

This is an actively growing toolkit. **`docs/PRD.md`** is the authoritative checklist of every
control and feature — per-control acceptance criteria (§7), the milestone roadmap M0–M9 (§10), and
the tested/demo-ed/documented coverage matrix (§11). Everything not in the table above (TextBox,
ComboBox, TreeView, TreeListView, TabControl, menus, dialogs, …) is tracked there box-by-box.

## 🛠️ Build

```sh
dotnet build NativeForms.sln -c Release
dotnet test  NativeForms.sln -c Release
dotnet run  --project NativeForms.Demo         # needs GTK 3 on Linux; native on Windows
```

## ❤️ Support

If NativeForms is useful to you, consider supporting development:

[![GitHub Sponsors](https://img.shields.io/badge/Sponsor-Hawkynt-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see [LICENSE](LICENSE).
