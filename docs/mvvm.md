# MVVM and data binding

> Pattern-agnostic binding primitives in `Hawkynt.NativeForms.ComponentModel` — `ObservableObject`,
> `RelayCommand`, `PropertyBinding<T>`, `ObservableList<T>` — built entirely on delegates so they
> survive trimming and NativeAOT.

## Design stance

NativeForms does not impose a UI pattern. MVVM, MVC and MVP all sit on the same two mechanisms:
plain .NET events on controls (`Click`, `TextChanged`, `SelectedIndexChanged`, …) and delegate-based
bindings. A view-model binds through `PropertyBinding<T>` and `ICommand`; a controller or presenter
subscribes events and pushes state back through the same properties. Nothing in Core knows which
pattern you chose.

The binding layer is reflection-free by design. Classic WinForms binding resolves `"PropertyName"`
strings through `TypeDescriptor`/`PropertyDescriptor` at runtime — reflection metadata the trimmer
cannot see, so it either breaks under trimming or forces everything to be kept. NativeForms instead
takes compiled delegates: you hand the binding a `Func<T>` to read and an `Action<T>` to write, both
checked by the compiler and both visible to the linker. Strings appear in exactly one place — the
`INotifyPropertyChanged` event filter, supplied via `nameof(...)` so it is compile-checked too — and
are never used to look up a member. `Expression<>` trees are equally off the table: they are
interpreted under NativeAOT. Plain `Func<>`/`Action<>` only.

## ObservableObject

The base class for view-models and models. It implements both `INotifyPropertyChanged` and
`INotifyPropertyChanging`; property names arrive through `[CallerMemberName]` at compile time. An
instance costs its own fields plus two (usually null) event slots.

`SetProperty` assigns only when the value differs, raising `PropertyChanging` before and
`PropertyChanged` after the write, and returns whether a change occurred — useful for chaining
notifications for derived properties, as the demo's counter does:

```csharp
using System.Windows.Input;
using Hawkynt.NativeForms.ComponentModel;

public sealed class CounterViewModel : ObservableObject
{
    private int _count;

    public CounterViewModel() => this.Increment = new RelayCommand(() => ++this.Count);

    public int Count
    {
        get => _count;
        set
        {
            if (!this.SetProperty(ref _count, value))
                return;

            // Display derives from Count — notify it so bindings watching it re-read.
            this.OnPropertyChanged(nameof(this.Display));
        }
    }

    public string Display => _count == 0 ? "Click the button." : $"Clicked {_count} time(s).";

    public ICommand Increment { get; }
}
```

| Member | Description |
|---|---|
| `event PropertyChangedEventHandler? PropertyChanged` | Raised after a property changes. |
| `event PropertyChangingEventHandler? PropertyChanging` | Raised before a property changes. |
| `protected void OnPropertyChanging([CallerMemberName] string? propertyName = null)` | Raises `PropertyChanging` for the calling property. |
| `protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)` | Raises `PropertyChanged` for the calling property. |
| `protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)` | Assigns if different, raising changing/changed around the write; returns whether a change occurred. |

## RelayCommand and RelayCommand&lt;T&gt;

An `ICommand` that forwards to delegates — the standard way a view-model exposes an action to a
button. `RelayCommand` ignores the command parameter; `RelayCommand<T>` casts it to `T?` and passes
it to both delegates. `CanExecute` returns `true` when no guard was supplied. When the guard's
answer may have changed, call `RaiseCanExecuteChanged()` so bound controls re-query it.

Menu and tool-strip items take a command directly — `ToolStripItem.Command` executes on activation
and its `CanExecute` gates the item's effective `Enabled`, re-evaluated on `CanExecuteChanged`.
`Button` has no `Command` property yet (see [Notes](#notes)); there you wire the click event
yourself, exactly as the demo does:

```csharp
button.Click += (_, _) => viewModel.Increment.Execute(null);
```

| `RelayCommand` member | Description |
|---|---|
| `RelayCommand(Action execute, Func<bool>? canExecute = null)` | Creates the command from an execute delegate and an optional guard. |
| `event EventHandler? CanExecuteChanged` | Raised by `RaiseCanExecuteChanged`. |
| `bool CanExecute(object? parameter)` | Invokes the guard; `true` when none was supplied. |
| `void Execute(object? parameter)` | Invokes the execute delegate. |
| `void RaiseCanExecuteChanged()` | Signals that `CanExecute` may have changed. |

| `RelayCommand<T>` member | Description |
|---|---|
| `RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)` | As above; delegates receive the command parameter. |
| `event EventHandler? CanExecuteChanged` | Raised by `RaiseCanExecuteChanged`. |
| `bool CanExecute(object? parameter)` | Casts the parameter to `T?` and invokes the guard; `true` when none was supplied. |
| `void Execute(object? parameter)` | Casts the parameter to `T?` and invokes the execute delegate. |
| `void RaiseCanExecuteChanged()` | Signals that `CanExecute` may have changed. |

## PropertyBinding&lt;T&gt;

The binding primitive: connects a source property on any `INotifyPropertyChanged` to an arbitrary
target, expressed entirely through delegates. The higher-level control data-binding layer is built
on it, and it serves MVVM, MVC and MVP alike.

```csharp
public PropertyBinding(
    INotifyPropertyChanged source,
    string sourcePropertyName,
    Func<T> getSource,
    Action<T> setTarget,
    BindingMode mode = BindingMode.OneWay,
    Action<T>? setSource = null,
    Func<T>? getTarget = null,
    Action<EventHandler>? subscribeTargetChanged = null,
    Action<EventHandler>? unsubscribeTargetChanged = null)
```

Semantics:

- The binding is **active immediately**: construction performs the initial synchronization —
  target→source for `OneWayToSource`, source→target for every other mode (for `OneTime` that single
  push is all that ever happens).
- `sourcePropertyName` (pass `nameof(...)`) only **filters** the source's `PropertyChanged` event;
  it never looks a member up. A notification with a null/empty property name counts as
  "everything changed" and refreshes the target.
- `TwoWay` and `OneWayToSource` **require** `setSource` and `getTarget` (an `ArgumentException`
  otherwise). Write-back is driven by the target's own change event, hooked through
  `subscribeTargetChanged`/`unsubscribeTargetChanged`; without those hooks the target is only read
  at construction.
- An internal guard suppresses re-entrancy, so a two-way binding cannot ping-pong.
- `Dispose()` detaches both endpoints and is idempotent.

**Lifetime:** the source object itself keeps the binding alive through its PropertyChanged subscription — keep the reference only if you want to dispose the binding early — it subscribes to the
source, not the other way round. Hold it in a field for as long as the view lives (as the demo's
`MainForm` does), and dispose it to disconnect early.

All four `BindingMode` values exist:

| Mode | Flow |
|---|---|
| `OneWay` | Source changes are pushed to the target; the target never writes back. |
| `TwoWay` | Changes flow both ways. |
| `OneWayToSource` | Target changes are written back to the source; the source never pushes. |
| `OneTime` | The source value is applied to the target once, at bind time, and never again. |

| Member | Description |
|---|---|
| `PropertyBinding(source, sourcePropertyName, getSource, setTarget, mode, setSource, getTarget, subscribeTargetChanged, unsubscribeTargetChanged)` | Creates and immediately activates the binding (full signature above). |
| `BindingMode Mode { get; }` | The flow direction chosen at construction. |
| `void Dispose()` | Detaches the binding from both endpoints. |

A two-way binding, mirroring the binding tests — any target with a value and a change event fits
the write-back shape:

```csharp
using Hawkynt.NativeForms.ComponentModel;

public sealed class PersonViewModel : ObservableObject
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set => this.SetProperty(ref _name, value);
    }
}

public sealed class NameEditor
{
    private string _value = string.Empty;

    public event EventHandler? ValueChanged;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;

            _value = value;
            this.ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

var vm = new PersonViewModel { Name = "Ada" };
var editor = new NameEditor();

var binding = new PropertyBinding<string>(
    vm,
    nameof(PersonViewModel.Name),
    () => vm.Name,
    v => editor.Value = v,
    BindingMode.TwoWay,
    setSource: v => vm.Name = v,
    getTarget: () => editor.Value,
    subscribeTargetChanged: h => editor.ValueChanged += h,
    unsubscribeTargetChanged: h => editor.ValueChanged -= h);

editor.Value = "Grace"; // written back: vm.Name is now "Grace"
vm.Name = "Linus";      // pushed out:   editor.Value is now "Linus"
```

## ObservableList&lt;T&gt;

The reflection-free replacement for `BindingList<T>`: an `IList<T>` that raises a granular
`ListChanged` after every structural change, so a bound control repaints only what changed instead
of rebuilding.

```csharp
var list = new ObservableList<string>(["a", "b"]);
list.ListChanged += (_, e) => Console.WriteLine($"{e.ChangeType} at {e.Index}");

list.Add("c");    // Added at 2
list[0] = "A";    // Replaced at 0
list.RemoveAt(1); // Removed at 1
list.Clear();     // Reset at -1
```

Appending with **no listener allocates nothing** beyond the underlying array's growth: the
null-conditional event raise short-circuits before the `ListChangedEventArgs` is constructed. The
allocation-budget tests guard this.

List controls consume it directly. `ListBox` and `DataGridView` each own an
`ObservableList<object?> Items`, subscribe to its `ListChanged`, and clamp selection/scroll and
repaint per change; their `DataSource` setter is a one-way convenience that clears `Items` and
refills it from any `IEnumerable`. Cell/row content is mapped by reflection-free selector delegates
(`ListBox.DisplaySelector`, `DataGridViewColumn.ValueSelector`), never by member-name strings:

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.ComponentModel;

public sealed record Person(string Name, int Age);

var listBox = new ListBox
{
    Bounds = new(0, 0, 160, 200),
    DisplaySelector = static item => ((Person)item!).Name,
};
listBox.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25)]);

var grid = new DataGridView { Bounds = new(0, 0, 320, 200) };
grid.Columns.Add(new DataGridViewColumn("Name", static row => ((Person)row!).Name) { Width = 160 });
grid.Columns.Add(new DataGridViewColumn("Age", static row => ((Person)row!).Age) { Width = 60 });

grid.Items.AddRange([new Person("Alice", 30), new Person("Bob", 25)]);
grid.Items.Add(new Person("Carol", 40));           // granular repaint: Added at index 2
grid.DataSource = new[] { new Person("Dan", 22) }; // replaces all rows
```

| `ObservableList<T>` member | Description |
|---|---|
| `ObservableList()` | Creates an empty list. |
| `ObservableList(IEnumerable<T> items)` | Creates a list pre-filled from a sequence. |
| `event EventHandler<ListChangedEventArgs>? ListChanged` | Raised after every structural change. |
| `int Count` / `bool IsReadOnly` | Element count; always writable. |
| `T this[int index]` | Setter raises `Replaced`. |
| `void Add(T item)` / `void Insert(int index, T item)` | Raise `Added` with the item's index. |
| `void AddRange(IEnumerable<T> items)` | Adds several items, one notification per item. |
| `bool Remove(T item)` / `void RemoveAt(int index)` | Raise `Removed` with the index. |
| `void Clear()` | Raises `Reset` with index −1. |
| `bool Contains(T item)` / `int IndexOf(T item)` / `void CopyTo(T[] array, int arrayIndex)` / `GetEnumerator()` | Plain list plumbing, no notifications. |

| `ListChangedEventArgs` member | Description |
|---|---|
| `ListChangedEventArgs(ListChangeType changeType, int index)` | Describes one change. |
| `ListChangeType ChangeType` | `Added`, `Removed`, `Replaced` or `Reset` (clear/bulk). |
| `int Index` | The affected index, or −1 for `Reset`. |

## Worked example

The demo's bound counter, self-contained: `CounterViewModel` from above plus the form that binds to
it. The button routes its click into the command; the one-way binding watches `Count` and pushes
the derived `Display` text onto the label.

```csharp
using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.ComponentModel;

public sealed class CounterForm : Form
{
    private readonly CounterViewModel _viewModel = new();
    private readonly Label _label;
    private readonly Button _button;

    // Held in a field so the binding is not garbage-collected while the window lives.
    private readonly PropertyBinding<string> _labelBinding;

    public CounterForm()
    {
        this.Text = "Counter";
        this.Bounds = new(Point.Empty, new Size(320, 160));

        _label = new()
        {
            Bounds = new(20, 20, 280, 24),
            Text = _viewModel.Display,
        };

        _button = new()
        {
            Bounds = new(20, 64, 140, 36),
            Text = "Click me",
        };
        _button.Click += (_, _) => _viewModel.Increment.Execute(null);

        this.Controls.AddRange(_label, _button);

        _labelBinding = new(
            _viewModel,
            nameof(CounterViewModel.Count),
            () => _viewModel.Display,
            text => _label.Text = text);
    }
}
```

Run it like any NativeForms app — register the backends the build ships, then start the loop:

```csharp
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.Windows;

BackendRegistry.Register(new Win32Backend());
BackendRegistry.Register(new GtkBackend());

Application.Run(new CounterForm());
```

## Notes

Planned, not yet implemented — tracked box-by-box in `docs/PRD.md` §3 and §6:

- **Lambda binding sugar on controls** over `PropertyBinding<T>`, e.g.
  `label.Bind(vm, nameof(vm.Count), v => v.Display, (c, text) => c.Text = text)` plus two-way
  overloads. The WinForms string API (`DataBindings.Add("Text", vm, "Name")`) is a non-goal — it
  needs reflection.
- **`ICommand` wiring on `Button`** with automatic enable/disable via `CanExecute`; until then, wire
  `Click` manually as shown above. Menu and tool-strip items already have it
  (`ToolStripItem.Command`).
- **Lambdas everywhere:** every binding/configuration surface (bindings, column value/image/style
  selectors, read-only predicates, display-text/tooltip providers) accepts plain `Func<>`/`Action<>`
  lambdas — never string member names, never `Expression<>` trees.
- **Source-generated property accessors** (`[Bindable]`) so `DataSource` + `DisplayMember` resolve
  member getters at compile time.
- **`IReadOnlyObservableList<T>`** and richer change granularity (move) for virtualized list
  controls.
- **Format/parse converters** (`IValueConverter`-style) for two-way text↔value.
- **Validation hooks** (`INotifyDataErrorInfo`-style) with error surfacing on controls.
- **Nested-path binding** (`a.b.c`) via chained typed selectors.
- **Remaining list/selection binding:** `ListView` value binding; `ComboBox` already closes the
  `ValueMember`/`SelectedValue` loop reflection-free via its `ValueSelector` delegate, and
  `ListBox`/`DataGridView` `DataSource` + selectors are done (one-way).
- **An MVP passive-view sample** (`IView` interfaces + presenter).
