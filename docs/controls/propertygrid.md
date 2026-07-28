# PropertyGrid

> An owner-drawn two-column inspector: name/value rows grouped under collapsible category headers, each value cell edited by a typed inline editor — text, number, check box, tri-state, drop-down, flags flyout, 3×3 spatial picker, colour, date, time. Reflection-free: rows are delegate-driven, and an optional **source generator** builds them from attributes at compile time.

![PropertyGrid in the NativeForms demo](../screenshots/29-editors.png)

`Hawkynt.NativeForms.PropertyGrid` · strategy: **owner-drawn + hosted editors/pickers** · peer: `ICanvasPeer`

## Usage

Three ways to fill a grid, from most explicit to most automatic.

**1 — Typed rows (no attributes, no generator).** The editor, formatting and parsing are inferred from `T` at compile time:

```csharp
var grid = new PropertyGrid { Bounds = new(16, 36, 380, 380) };

grid.AddRow("Name",     () => model.Name,    v => model.Name = v,    category: "Appearance");
grid.AddRow("Accent",   () => model.Accent,  v => model.Accent = v,  category: "Appearance"); // Color -> ColorPicker
grid.AddRow("Visible",  () => model.Visible, v => model.Visible = v);                          // bool? -> tri-state
grid.AddRow("Width",    () => model.Width,   v => model.Width = v,   minimum: 0, maximum: 400);
grid.AddEnumRow("Mode", () => model.Mode,    v => model.Mode = v);                             // enum -> drop-down
```

**2 — Attribute-driven via the source generator.** Mark a non-nested `partial` class `[GridEditable]` and the generator emits a `PopulateGrid(PropertyGrid)` method — one row per public settable property, editor inferred from the type, metadata read from the attributes:

```csharp
[GridEditable]
public partial class WidgetSettings
{
    [GridCategory("Appearance")]
    [GridDescription("The caption shown on the widget.")]
    public string Name { get; set; } = "Save button";

    [GridCategory("Layout")]
    [GridRange(0, 400)]
    public int Width { get; set; } = 120;

    [GridDisplayName("Anchored edges")]
    public WidgetEdges Anchor { get; set; }   // [Flags] enum -> check-box flyout

    [GridIgnore]
    public string InternalKey { get; set; } = "";
}

// ...
var grid = new PropertyGrid();
settings.PopulateGrid(grid);       // generated — no reflection at run time
```

**3 — Hand-built rows** for anything the inference cannot know, composed onto the same grid:

```csharp
grid.AddGridEnumRow("Dock", () => m.Dock, v => m.Dock = v,
    new[] { "", "Top", "", "Left", "Fill", "Right", "None", "Bottom", "" });   // 3x3 spatial flyout

grid.AddRow(new PropertyGridRow("Align", () => m.Align, v => m.Align = v)
{
    Editor = PropertyGridEditor.Align,   // 3x3 ContentAlignment picker
    Category = "Layout",
});
```

## Attributes

All six live in `Hawkynt.NativeForms` (declared in Core, so they compile with or without the generator). They are **inert metadata unless the generator is present** — see [Getting the generator](#getting-the-generator).

| Attribute | Target | Argument | Effect on the generated row |
|---|---|---|---|
| `[GridCategory]` | property | `string category` | The category header the row groups under. Rows without one land in `Misc`. |
| `[GridDescription]` | property | `string description` | Text shown in the grid's description strip while the row is selected. |
| `[GridDisplayName]` | property | `string name` | Overrides the row's displayed name (the property name is used otherwise). |
| `[GridEditable]` | class | — | Marks the model. The class must be **`partial`** and **non-nested**, or the generator reports `NFG001` and emits nothing. |
| `[GridIgnore]` | property | — | Excludes the member from the generated grid. |
| `[GridRange]` | property | `double minimum, double maximum` | Clamps a numeric row's committed value to the inclusive bounds. |

### Grid-column attributes

The same `[GridEditable]` marker also emits `PopulateColumns(DataGridView)`, so one annotated model
drives an inspector **and** a [`DataGridView`](datagridview.md). These attributes cover what a grid has
and an inspector does not. Following the `WindowsFormsExtensions` convention a dynamic rule names another
member rather than taking a delegate — but the generator resolves that name at **compile time**, so a
typo is a build error (`NFG002`) and a wrong-typed member is `NFG003`, not a silent no-op.

| Attribute | Target | Argument | Effect on the generated column |
|---|---|---|---|
| `[GridColumnKind]` | property | `DataGridViewColumnKind kind` | Overrides the kind inferred from the property type. |
| `[GridColumnReadOnlyWhen]` | property | `string propertyName` | Cells are read-only while the named `bool` property is true (`ReadOnlyCellSelector`). |
| `[GridColumnSortMode]` | property | `DataGridViewColumnSortMode mode` | The column's sort mode; `Automatic` makes the header clickable. |
| `[GridColumnWidth]` | property | `int width` | The column's starting pixel width. |
| `[GridRowHeightFrom]` | class | `string propertyName` | Row height from the named `int` property (`RowHeightSelector`). |
| `[GridRowHiddenWhen]` | class | `string propertyName` | Hides the row while the named `bool` property is true (`RowHiddenSelector`). |
| `[GridRowSelectableWhen]` | class | `string propertyName` | Row is selectable only while the named `bool` property is true (`RowSelectableSelector`). |

Column kinds are inferred from the property type: `bool` → `Check`, any numeric → `NumericUpDown`,
`DateTime`/`DateOnly` → `DateTime`, `TimeOnly` → `TimePicker`, `Color` → `Color`, an `enum` → `ComboBox`,
a `[Flags]` enum → `CheckedListBox`, anything else → `Text`. A settable property gets a `ValueSetter` so
grid edits write back; a get-only property yields a read-only column.

```csharp
[GridEditable]
[GridRowHiddenWhen(nameof(IsArchived))]
public partial class Order
{
    // A gate can be hidden from the UI and still be referenced by name.
    [GridIgnore] public bool IsArchived { get; set; }

    [GridColumnWidth(90)]
    [GridColumnSortMode(DataGridViewColumnSortMode.Automatic)]
    [GridColumnReadOnlyWhen(nameof(IsArchived))]
    public int Quantity { get; set; }
}

Order.PopulateColumns(grid);   // generated — static, since columns describe the type
```

Only **public, settable, non-static, non-indexer properties** become rows. The member attributes also *compile* on fields (their `AttributeUsage` permits it), but **the generator only walks properties** — a field carrying `[GridCategory]` is silently skipped. Expose it as a property, or add the row by hand.

### Getting the generator

The generator ships **inside the `Hawkynt.NativeForms` NuGet package** as an analyzer asset (`analyzers/dotnet/cs`). Referencing the package is all it takes:

```xml
<PackageReference Include="Hawkynt.NativeForms" Version="..." />
```

Nothing else to add, and no runtime cost — the emitted `PopulateGrid` is ordinary code calling the typed `AddRow` overloads below. Without the package's analyzer (for example a bare project reference to Core inside this repo), the attributes still compile and the typed builders still work; only `PopulateGrid` is not generated.

## API

### Editors

`PropertyGridEditor` selects the value cell's editing affordance. The typed `AddRow<T>` overload infers it from `T`:

| Member | Inferred from | Behavior |
|---|---|---|
| `Align` | — (explicit) | 3×3 flyout of `ContentAlignment` names, or of `PropertyGridRow.GridValues` when supplied. |
| `Boolean` | `bool` | Inline check box; a click toggles it. |
| `Choice` | `enum` (via `AddEnumRow`) | Drop-down list of `Choices`. |
| `Color` | `Color` | Hosts the real [`ColorPicker`](colorpicker.md) over the cell — full mixer, numeric tabs, eyedropper. |
| `Date` | `DateOnly` | Hosts a [`DateTimePicker`](datetimepicker.md) with a calendar drop-down. |
| `DateTime` | `DateTime` | Hosts a [`DateTimePicker`](datetimepicker.md) in a date+time format. |
| `Flags` | `[Flags]` enum (via `AddFlagsEnumRow`) | Check-box flyout; the value is the comma-separated member set. |
| `Number` | any numeric type | Numeric text field, clamped to `Minimum`/`Maximum`; empty allowed when `AllowNull`. |
| `Text` | anything else | Free-text field. |
| `Time` | `TimeOnly` | Hosts a [`TimePicker`](timepicker.md). |
| `TriState` | `bool?` | Three-state check box cycling `True` → `False` → *(null)*. |

### `PropertyGrid` methods

| Name | Description |
|---|---|
| `AddEnumRow<TEnum>(name, get, set, category?, description?)` | Drop-down row over the enum's names. Reflection-free (`Enum.GetNames<TEnum>()`). |
| `AddFlagsEnumRow<TEnum>(name, get, set, category?, description?)` | Check-box flyout over the members of a `[Flags]` enum, excluding the zero member. |
| `AddGridEnumRow<TEnum>(name, get, set, gridValues, category?, description?)` | 3×3 spatial flyout; `gridValues` maps the nine cells row-major, an empty string disabling a cell. |
| `AddRow(PropertyGridRow row)` | Appends a fully described row. |
| `AddRow<T>(name, get, set, category?, description?, minimum?, maximum?)` | Typed row: infers the editor and the value's formatting/parsing from `T`. Returns the created row. |
| `ClearRows()` | Removes every row and collapses state. |

### `PropertyGrid` properties & events

| Name | Type | Default | Description |
|---|---|---|---|
| `PropertyValueChanged` | `event EventHandler<PropertyValueChangedEventArgs>` | — | Raised after an editor commits a value that actually changed; carries `Row`, `OldValue`, `NewValue`. |
| `Rows` | `IReadOnlyList<PropertyGridRow>` | empty | The rows in insertion order; categories form from their `Category`. |
| `SelectedRow` | `PropertyGridRow?` | `null` | The selected row, or `null` when a category header or nothing is selected. |

### `PropertyGridRow`

| Name | Type | Default | Description |
|---|---|---|---|
| `AllowNull` | `bool` | `false` | Whether an empty value is allowed (a null number, or the third state of a `TriState`). |
| `Category` | `string` | `"Misc"` | The category header the row groups under. |
| `Choices` | `IReadOnlyList<string>?` | `null` | Options for a `Choice` or `Flags` row. |
| `Description` | `string?` | `null` | Shown in the description strip while the row is selected. |
| `Editor` | `PropertyGridEditor` | `Text` | Which editor the value cell uses. |
| `Get` | `Func<string>` | (ctor) | Reads the current value as display text. |
| `GridValues` | `IReadOnlyList<string>?` | `null` | The nine cell values of an `Align` row; `null` uses the `ContentAlignment` names. |
| `Maximum` | `double?` | `null` | Inclusive upper bound for a `Number` row. |
| `Minimum` | `double?` | `null` | Inclusive lower bound for a `Number` row. |
| `Name` | `string` | (ctor) | The name shown in the left column. |
| `Set` | `Action<string>?` | (ctor) | Commits an edited value; `null` makes the row read-only. |

## Notes

- **Reflection-free by construction.** Rows are delegates; the generator resolves attributes from the Roslyn symbol model at compile time. Nothing in the running app touches `System.Reflection`, so the grid is trim- and NativeAOT-safe.
- Category headers collapse on click. Rows are keyboard-navigable (arrows), and Enter or F2 activates the selected row's editor.
- The splitter between the name and value columns is draggable.
- A row whose `Set` is `null` renders in the disabled text colour and never opens an editor.
- A `Number` row rejects unparseable input by keeping the previous value rather than throwing.
- Colour, date and time rows **reuse the toolkit's own controls** rather than reimplementing pickers — one colour UI, one calendar, one clock across the library.

## Differences from WinForms

`System.Windows.Forms.PropertyGrid` reflects over `SelectedObject` and drives editing through `TypeDescriptor`, `TypeConverter` and `UITypeEditor`. None of that exists here: it is reflection-based and therefore incompatible with the trim/AOT goal. The replacements are the typed `AddRow<T>` builders and the compile-time generator. Consequently there is no `SelectedObject`, no `TypeConverter` lookup and no `UITypeEditor` extensibility point; a custom editor is a `PropertyGridEditor` member plus its cell handling.

## Not yet implemented

See [docs/PRD.md](../PRD.md) §7.10: multiline-string editing (Alt+Enter drop-out to a multi-line editor), nested/expandable object rows, and per-row validation feedback.

**Driving a [`DataGridView`](datagridview.md) from the same attributes** is planned but not built — specified in [PRD §14](../PRD.md#14-attribute-driven-grids--lists--extend-the-generator-to-datagridview). The intent is that one `[GridEditable]` model emits both a `PopulateGrid(PropertyGrid)` and a `PopulateColumns(DataGridView)`, so the same annotations give you an inspector and a grid. Today the attributes drive the `PropertyGrid` only; grid columns are added by hand.
