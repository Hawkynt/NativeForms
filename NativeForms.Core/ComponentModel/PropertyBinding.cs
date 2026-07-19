using System.ComponentModel;

namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>How values flow across a <see cref="PropertyBinding{T}"/>.</summary>
public enum BindingMode
{
    /// <summary>Source changes are pushed to the target; the target never writes back.</summary>
    OneWay,

    /// <summary>Changes flow both ways.</summary>
    TwoWay,

    /// <summary>Target changes are written back to the source; the source never pushes.</summary>
    OneWayToSource,

    /// <summary>The source value is applied to the target once, at bind time, and never again.</summary>
    OneTime,
}

/// <summary>
/// A reflection-free binding between a source property (on an <see cref="INotifyPropertyChanged"/>)
/// and an arbitrary target, expressed entirely through delegates. Because the accessors are compiled
/// delegates rather than <c>PropertyDescriptor</c>/<c>TypeDescriptor</c> lookups, binding survives
/// trimming and NativeAOT and costs nothing at steady state beyond the event subscription. This is
/// the primitive the higher-level control data-binding layer is built on; it equally serves MVVM,
/// MVC and MVP.
/// </summary>
/// <typeparam name="T">The bound value type.</typeparam>
public sealed class PropertyBinding<T> : IDisposable
{
    private readonly INotifyPropertyChanged _source;
    private readonly string _sourcePropertyName;
    private readonly Func<T> _getSource;
    private readonly Action<T>? _setSource;
    private readonly Action<T> _setTarget;
    private readonly Func<T>? _getTarget;
    private readonly Action<EventHandler>? _unsubscribeTargetChanged;
    private readonly EventHandler? _targetChangedHandler;
    private bool _syncing;
    private bool _disposed;

    /// <summary>
    /// Creates and immediately activates a binding. For <see cref="BindingMode.TwoWay"/> or
    /// <see cref="BindingMode.OneWayToSource"/> supply <paramref name="setSource"/>,
    /// <paramref name="getTarget"/> and the target change subscription hooks.
    /// </summary>
    /// <param name="source">The change-notifying source object.</param>
    /// <param name="sourcePropertyName">The source property to observe.</param>
    /// <param name="getSource">Reads the current source value.</param>
    /// <param name="setTarget">Applies a value to the target.</param>
    /// <param name="mode">The flow direction.</param>
    /// <param name="setSource">Writes a value back to the source (two-way / to-source).</param>
    /// <param name="getTarget">Reads the current target value (two-way / to-source).</param>
    /// <param name="subscribeTargetChanged">Subscribes a handler to the target's change event.</param>
    /// <param name="unsubscribeTargetChanged">Removes that handler.</param>
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
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(sourcePropertyName);
        ArgumentNullException.ThrowIfNull(getSource);
        ArgumentNullException.ThrowIfNull(setTarget);

        _source = source;
        _sourcePropertyName = sourcePropertyName;
        _getSource = getSource;
        _setTarget = setTarget;
        _setSource = setSource;
        _getTarget = getTarget;
        this.Mode = mode;

        if (mode is BindingMode.OneWay or BindingMode.TwoWay)
            source.PropertyChanged += this.OnSourcePropertyChanged;

        if (mode is BindingMode.TwoWay or BindingMode.OneWayToSource)
        {
            if (setSource is null || getTarget is null)
                throw new ArgumentException(
                    "Two-way and to-source bindings require setSource and getTarget.", nameof(setSource));

            if (subscribeTargetChanged is not null)
            {
                _targetChangedHandler = this.OnTargetChanged;
                _unsubscribeTargetChanged = unsubscribeTargetChanged;
                subscribeTargetChanged(_targetChangedHandler);
            }
        }

        // Initial synchronization.
        if (mode is BindingMode.OneWayToSource)
            this.PushTargetToSource();
        else
            this.PushSourceToTarget();
    }

    /// <summary>The flow direction chosen at construction.</summary>
    public BindingMode Mode { get; }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _sourcePropertyName)
            this.PushSourceToTarget();
    }

    private void OnTargetChanged(object? sender, EventArgs e) => this.PushTargetToSource();

    private void PushSourceToTarget()
    {
        if (_syncing)
            return;

        _syncing = true;
        try
        {
            _setTarget(_getSource());
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PushTargetToSource()
    {
        if (_syncing || _setSource is null || _getTarget is null)
            return;

        _syncing = true;
        try
        {
            _setSource(_getTarget());
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Detaches the binding from both endpoints.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.PropertyChanged -= this.OnSourcePropertyChanged;
        if (_targetChangedHandler is not null)
            _unsubscribeTargetChanged?.Invoke(_targetChangedHandler);
    }
}
