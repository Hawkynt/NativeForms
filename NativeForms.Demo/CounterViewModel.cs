using System.Windows.Input;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// A tiny MVVM view-model exercising the binding stack: an observable <see cref="Count"/>, a derived
/// <see cref="Display"/> string, and an <see cref="Increment"/> command a button binds to.
/// </summary>
internal sealed class CounterViewModel : ObservableObject
{
    private int _count;

    public CounterViewModel() => this.Increment = new RelayCommand(() => ++this.Count);

    /// <summary>The number of clicks so far.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (!this.SetProperty(ref _count, value))
                return;

            // Display is derived from Count, so notify it too — bindings watching Count re-read it.
            this.OnPropertyChanged(nameof(this.Display));
        }
    }

    /// <summary>The human-readable label text.</summary>
    public string Display => _count == 0 ? "Click the button." : $"Clicked {_count} time(s).";

    /// <summary>Increments <see cref="Count"/>.</summary>
    public ICommand Increment { get; }
}
