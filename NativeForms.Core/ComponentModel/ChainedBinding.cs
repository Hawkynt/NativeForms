using System.ComponentModel;

namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>
/// A one-way nested-path binding (<c>root.Mid.Value</c>) built from chained typed selectors — the
/// reflection-free answer to <c>"a.b.c"</c> path strings. It observes the root for replacements of
/// the middle object and the middle object for changes of the leaf value, re-subscribing whenever
/// the middle link swaps and unsubscribing from the one it left behind. Deeper paths compose by
/// nesting: let the change callback drive another chain.
/// </summary>
/// <remarks>
/// While the chain is broken (the middle selector yields <see langword="null"/>) the fallback value
/// is pushed when one was supplied; without a fallback the target simply keeps its last value.
/// Like every binding here, the root's event subscription keeps the chain alive as long as the root
/// lives — keep the instance only to <see cref="Dispose"/> early.
/// </remarks>
/// <typeparam name="TRoot">The observed root object.</typeparam>
/// <typeparam name="TMid">The middle object; must notify so leaf changes are seen.</typeparam>
/// <typeparam name="TValue">The leaf value type.</typeparam>
public sealed class ChainedBinding<TRoot, TMid, TValue> : IDisposable
    where TRoot : INotifyPropertyChanged
    where TMid : class, INotifyPropertyChanged
{
    private readonly TRoot _root;
    private readonly string _midPropertyName;
    private readonly Func<TRoot, TMid?> _getMid;
    private readonly string _valuePropertyName;
    private readonly Func<TMid, TValue> _getValue;
    private readonly Action<TValue> _setTarget;
    private readonly BindingFallback<TValue> _fallbackValue;
    private TMid? _mid;
    private bool _disposed;

    /// <summary>Creates and immediately activates the chain, pushing the initial value (or fallback).</summary>
    /// <param name="root">The root object to observe.</param>
    /// <param name="midPropertyName">The root property holding the middle object (<c>nameof</c>, filter only).</param>
    /// <param name="getMid">Selects the middle object off the root.</param>
    /// <param name="valuePropertyName">The middle object's property holding the value (<c>nameof</c>, filter only).</param>
    /// <param name="getValue">Selects the leaf value off the middle object.</param>
    /// <param name="setTarget">Receives every pushed value.</param>
    /// <param name="fallbackValue">Pushed while the chain is broken (middle object is <see langword="null"/>).</param>
    public ChainedBinding(
        TRoot root,
        string midPropertyName,
        Func<TRoot, TMid?> getMid,
        string valuePropertyName,
        Func<TMid, TValue> getValue,
        Action<TValue> setTarget,
        BindingFallback<TValue> fallbackValue = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(midPropertyName);
        ArgumentNullException.ThrowIfNull(getMid);
        ArgumentException.ThrowIfNullOrEmpty(valuePropertyName);
        ArgumentNullException.ThrowIfNull(getValue);
        ArgumentNullException.ThrowIfNull(setTarget);

        _root = root;
        _midPropertyName = midPropertyName;
        _getMid = getMid;
        _valuePropertyName = valuePropertyName;
        _getValue = getValue;
        _setTarget = setTarget;
        _fallbackValue = fallbackValue;

        root.PropertyChanged += this.OnRootPropertyChanged;
        this.AttachMid();
        this.Push();
    }

    private void OnRootPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != _midPropertyName)
            return;

        this.AttachMid();
        this.Push();
    }

    private void OnMidPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _valuePropertyName)
            this.Push();
    }

    /// <summary>Re-reads the middle object and moves the leaf subscription onto it.</summary>
    private void AttachMid()
    {
        var mid = _getMid(_root);
        if (ReferenceEquals(mid, _mid))
            return;

        if (_mid is not null)
            _mid.PropertyChanged -= this.OnMidPropertyChanged;

        _mid = mid;
        if (mid is not null)
            mid.PropertyChanged += this.OnMidPropertyChanged;
    }

    /// <summary>Pushes the current leaf value — or the fallback while the chain is broken.</summary>
    private void Push()
    {
        if (_mid is { } mid)
            _setTarget(_getValue(mid));
        else if (_fallbackValue.IsSet)
            _setTarget(_fallbackValue.Value);
    }

    /// <summary>Detaches from the root and from the currently observed middle object.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _root.PropertyChanged -= this.OnRootPropertyChanged;
        if (_mid is not null)
        {
            _mid.PropertyChanged -= this.OnMidPropertyChanged;
            _mid = null;
        }
    }
}

/// <summary>Factory for nested-path bindings, so the generic arguments flow from the selectors at
/// the call site instead of being spelled out.</summary>
public static class BindingPath
{
    /// <summary>
    /// Chains two typed selectors into a live <c>root.Mid.Value</c> observation:
    /// <c>BindingPath.Chain(vm, nameof(vm.Child), v =&gt; v.Child, nameof(Child.Name), c =&gt; c.Name, onChanged)</c>.
    /// </summary>
    /// <param name="root">The root object to observe.</param>
    /// <param name="midPropertyName">The root property holding the middle object (<c>nameof</c>, filter only).</param>
    /// <param name="getMid">Selects the middle object off the root.</param>
    /// <param name="valuePropertyName">The middle object's property holding the value (<c>nameof</c>, filter only).</param>
    /// <param name="getValue">Selects the leaf value off the middle object.</param>
    /// <param name="onChanged">Receives every pushed value.</param>
    /// <param name="fallbackValue">Pushed while the chain is broken (middle object is <see langword="null"/>).</param>
    /// <returns>The live chain; keep it only to dispose early.</returns>
    public static ChainedBinding<TRoot, TMid, TValue> Chain<TRoot, TMid, TValue>(
        TRoot root,
        string midPropertyName,
        Func<TRoot, TMid?> getMid,
        string valuePropertyName,
        Func<TMid, TValue> getValue,
        Action<TValue> onChanged,
        BindingFallback<TValue> fallbackValue = default)
        where TRoot : INotifyPropertyChanged
        where TMid : class, INotifyPropertyChanged
        => new(root, midPropertyName, getMid, valuePropertyName, getValue, onChanged, fallbackValue);
}
