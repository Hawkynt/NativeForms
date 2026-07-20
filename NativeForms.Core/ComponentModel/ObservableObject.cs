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
    /// <summary>The current validation errors, keyed by property name. Null until the first error.</summary>
    private Dictionary<string, string>? _errors;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <summary>Raised when a property's validation error is set or cleared via <see cref="SetError"/>.</summary>
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>The current validation error for a property, or <see langword="null"/> while it is valid.</summary>
    public string? GetError(string propertyName) => _errors?.GetValueOrDefault(propertyName);

    /// <summary>
    /// Records or clears the validation error for a property — the minimal
    /// <c>INotifyDataErrorInfo</c>-style hook, kept to one error per property and zero storage while
    /// everything is valid. A null or empty <paramref name="error"/> clears; <see cref="ErrorsChanged"/>
    /// is raised only when the stored error actually changes.
    /// </summary>
    protected void SetError(string propertyName, string? error)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);

        if (string.IsNullOrEmpty(error))
        {
            if (_errors is null || !_errors.Remove(propertyName))
                return;
        }
        else
        {
            var errors = _errors ??= [];
            if (errors.TryGetValue(propertyName, out var existing) && existing == error)
                return;

            errors[propertyName] = error;
        }

        this.ErrorsChanged?.Invoke(this, new(propertyName));
    }

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
