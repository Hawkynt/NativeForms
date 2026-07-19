using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>
/// A minimal base for view-models and models that raises change notifications. Deliberately tiny and
/// reflection-free — property names arrive through <see cref="CallerMemberNameAttribute"/> at compile
/// time, so nothing here defeats trimming or NativeAOT, and an instance costs only its own fields
/// plus two (usually null) event slots.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <summary>Raises <see cref="PropertyChanging"/> for the calling property.</summary>
    protected void OnPropertyChanging([CallerMemberName] string? propertyName = null)
        => this.PropertyChanging?.Invoke(this, new(propertyName));

    /// <summary>Raises <see cref="PropertyChanged"/> for the calling property.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => this.PropertyChanged?.Invoke(this, new(propertyName));

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> if it differs, raising the
    /// changing/changed notifications around the write. Returns whether a change occurred.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        this.OnPropertyChanging(propertyName);
        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }
}
