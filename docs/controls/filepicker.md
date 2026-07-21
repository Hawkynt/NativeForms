# FilePicker

> A file field: a hosted native [`TextBox`](textbox.md) holding the path plus a browse button that opens the platform's own [`OpenFileDialog` or `SaveFileDialog`](dialogs.md) and writes the choice back.

`Hawkynt.NativeForms.FilePicker` · strategy: **owner-drawn** (frame over a hosted native `TextBox`) · peer: `ICanvasPeer` + `ITextBoxPeer`

## Usage

```csharp
var picker = new FilePicker
{
    Bounds = new(20, 20, 300, 26),
    Filter = "Text files|*.txt|All files|*.*",
    FilterIndex = 2,
    Title = "Pick a file to open",
};
picker.PathChanged += (_, _) => Load(picker.SelectedPath);
form.Controls.Add(picker);
```

Saving instead of opening is one property:

```csharp
var save = new FilePicker { Mode = FilePickerMode.Save, Filter = "CSV|*.csv" };
```

## API

### Properties

| Name | Type | Default | Description |
|---|---|---|---|
| `SelectedPath` | `string` | `""` | The committed path. Assigning rewrites the editor, re-evaluates `PathExists` and raises `PathChanged`. |
| `SelectedPaths` | `string[]` | `[]` | Every path the current selection covers — the whole set after a multi-pick, one element after any other commit. Never `null`. |
| `Mode` | `FilePickerMode` | `Open` | Which dialog the browse button opens: `Open` or `Save`. |
| `Filter` | `string` | `""` | Type filter in WinForms syntax (`"Text files\|*.txt\|All files\|*.*"`). Validated on assignment. |
| `FilterIndex` | `int` | `1` | 1-based index of the initially selected filter entry. |
| `Multiselect` | `bool` | `false` | Whether the dialog lets the user pick several files (`Open` mode only). |
| `InitialDirectory` | `string` | `""` | Where the dialog starts; empty starts at the committed path's own folder. |
| `Title` | `string` | `""` | The dialog's caption; empty picks the platform default. |
| `ReadOnlyText` | `bool` | `false` | Makes the field display-only — still selectable and copyable, but only the browse button changes it. |
| `PathExists` | `bool` | `false` | Whether the committed path was real when last evaluated. See Notes. |
| `PlaceholderText` | `string` | `""` | Greyed hint shown while the field is empty. |

The inherited `Text` is the hosted editor's *live* content, including an edit not committed yet.

### Events

| Name | Description |
|---|---|
| `PathChanged` | Raised after `SelectedPath` changed, however it was committed. |

### Methods

| Name | Description |
|---|---|
| `PerformBrowse()` | Opens the browse dialog exactly as a click on the button would, and commits an OK. |

Inherits the common members of [`Control`](control.md) plus the owner-drawn surface of `OwnerDrawnControl`.

## Notes

- Built like [`SearchBox`](searchbox.md) and the [`UpDownBase`](numericupdown.md) spinners: the native editor fills the field so caret, selection, clipboard and IME stay platform-native, and a 28 px trailing zone carries the themed browse button (drawn with the shared button face, captioned `…`).
- **Committed path vs. live text.** `SelectedPath` is the value the control stands behind; the editor may hold an uncommitted edit until a *commit point* promotes it — Enter, focus leaving the field, a dialog result, or a programmatic assignment. Enter is claimed back from the native editor through `ITextBoxPeer.KeyDown`, so it commits whether it was typed inside the editor or on the surface around it.
- **`PathExists` is evaluated at commit points only**, never from `OnPaint` and never per keystroke — the paint path may not touch the filesystem (PRD §4), and a stat per typed character would block the UI thread. A committed path that names nothing is framed in the warning colour; an empty field is not an error and keeps the plain themed border.
- **What "exists" means depends on `Mode`.** `Open` demands the file itself. `Save` demands only that the *folder* exists — naming a file that is not there yet is the entire point of saving — and a bare file name with no folder resolves against the working directory and is accepted.
- `Multiselect` publishes the whole set in `SelectedPaths` while `SelectedPath` keeps the first. Any later commit that is not a browse narrows the set back to the single committed path, so a stale multi-pick is never reported.
- Construction costs ~984 B, inside the 1024 B hosted-editor composite tier (PRD §4); a steady-state repaint allocates 0 bytes.
- `FilePickerTests` pin the surface headlessly: editor placement, the dialog options handed over, multi-select bookkeeping, Enter commit, both existence semantics, the warning frame and the disabled state.

## Differences from WinForms

Windows Forms has no file-picker control — only the dialogs. This is a composite in the shape the dialogs imply, so `Filter`, `FilterIndex`, `Multiselect` and `Title` keep their `FileDialog` names and syntax exactly.
