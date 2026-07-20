using System.ComponentModel;

namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>
/// The lambda binding sugar over <see cref="PropertyBinding{T}"/>: fluent, control-first overloads
/// so a binding reads as one line at the call site —
/// <c>label.Bind(vm, nameof(vm.Count), v =&gt; v.Display, (c, text) =&gt; c.Text = text)</c>.
/// Strings appear only as the <c>nameof(...)</c> change-notification filter; member access is always
/// a compiled delegate, so everything here survives trimming and NativeAOT.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime:</b> the returned binding may be discarded. The source's <c>PropertyChanged</c> event
/// holds the binding (and through its delegates the control) strongly, so it stays live exactly as
/// long as the source does. Keep the return value only when you want to <see cref="PropertyBinding{T}.Dispose"/>
/// early — for example when the control dies before the view-model.
/// </para>
/// <para>
/// <b>Fallbacks:</b> <c>defaultValue</c> replaces the pushed value whenever reading the source
/// throws (the "source unset" case, e.g. a null link inside the getter chain); without it the
/// exception propagates to whoever triggered the push. <c>nullReplacement</c> replaces a
/// successfully read <see langword="null"/>. They are independent: a throwing read yields
/// <c>defaultValue</c> as-is, never the null replacement. Both act on the source→target path only —
/// write-back passes target values through untouched.
/// </para>
/// <para>
/// <b>Validation:</b> <c>onError</c> receives the source property's current error (or
/// <see langword="null"/> while valid) once at bind time and again on every
/// <see cref="ObservableObject.ErrorsChanged"/> for that property, and is detached when the binding
/// is disposed. It requires the source to be an <see cref="ObservableObject"/>; how the error is
/// displayed is entirely the callback's choice, so any control can surface it.
/// </para>
/// </remarks>
public static class BindingExtensions
{
    /// <summary>
    /// Binds a source property to this control through a plain value setter (which typically closes
    /// over the control). Supports <see cref="BindingMode.OneWay"/> and <see cref="BindingMode.OneTime"/>;
    /// the write-back modes need the full two-way overload.
    /// </summary>
    /// <param name="target">The control the binding feeds.</param>
    /// <param name="source">The change-notifying source object.</param>
    /// <param name="sourcePropertyName">The source property whose change notifications refresh the target (<c>nameof</c>).</param>
    /// <param name="get">Reads the bound value from the source.</param>
    /// <param name="set">Applies a value to the target.</param>
    /// <param name="mode">The flow direction.</param>
    /// <param name="defaultValue">Replaces the value when reading the source throws.</param>
    /// <param name="nullReplacement">Replaces a source value that is <see langword="null"/>.</param>
    /// <param name="onError">Receives the property's validation error, or <see langword="null"/> when it clears.</param>
    /// <returns>The live binding; keep it only to dispose early.</returns>
    public static PropertyBinding<TValue> Bind<TSource, TValue>(
        this Control target,
        TSource source,
        string sourcePropertyName,
        Func<TSource, TValue> get,
        Action<TValue> set,
        BindingMode mode = BindingMode.OneWay,
        BindingFallback<TValue> defaultValue = default,
        BindingFallback<TValue> nullReplacement = default,
        Action<string?>? onError = null)
        where TSource : INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(set);
        var observable = RequireObservableForErrors(source, onError);

        var binding = new PropertyBinding<TValue>(
            source,
            sourcePropertyName,
            ComposeGet(source, get, defaultValue, nullReplacement),
            set,
            mode);
        AttachErrorCallback(binding, observable, sourcePropertyName, onError);
        return binding;
    }

    /// <summary>
    /// Binds a source property to this control, handing the control itself to the setter — the
    /// canonical shape: <c>label.Bind(vm, nameof(vm.Count), v =&gt; v.Display, (c, text) =&gt; c.Text = text)</c>.
    /// </summary>
    /// <param name="target">The control the binding feeds; passed to <paramref name="set"/>.</param>
    /// <param name="source">The change-notifying source object.</param>
    /// <param name="sourcePropertyName">The source property whose change notifications refresh the target (<c>nameof</c>).</param>
    /// <param name="get">Reads the bound value from the source.</param>
    /// <param name="set">Applies a value to the given control.</param>
    /// <param name="mode">The flow direction.</param>
    /// <param name="defaultValue">Replaces the value when reading the source throws.</param>
    /// <param name="nullReplacement">Replaces a source value that is <see langword="null"/>.</param>
    /// <param name="onError">Receives the property's validation error, or <see langword="null"/> when it clears.</param>
    /// <returns>The live binding; keep it only to dispose early.</returns>
    public static PropertyBinding<TValue> Bind<TControl, TSource, TValue>(
        this TControl target,
        TSource source,
        string sourcePropertyName,
        Func<TSource, TValue> get,
        Action<TControl, TValue> set,
        BindingMode mode = BindingMode.OneWay,
        BindingFallback<TValue> defaultValue = default,
        BindingFallback<TValue> nullReplacement = default,
        Action<string?>? onError = null)
        where TControl : Control
        where TSource : INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(set);
        var observable = RequireObservableForErrors(source, onError);

        var binding = new PropertyBinding<TValue>(
            source,
            sourcePropertyName,
            ComposeGet(source, get, defaultValue, nullReplacement),
            v => set(target, v),
            mode);
        AttachErrorCallback(binding, observable, sourcePropertyName, onError);
        return binding;
    }

    /// <summary>
    /// Binds a source property to this control with write-back: target edits, reported through the
    /// subscribed change event, flow into <paramref name="setSource"/>.
    /// </summary>
    /// <param name="target">The control the binding feeds; passed to every control-side delegate.</param>
    /// <param name="source">The change-notifying source object.</param>
    /// <param name="sourcePropertyName">The source property whose change notifications refresh the target (<c>nameof</c>).</param>
    /// <param name="getSource">Reads the bound value from the source.</param>
    /// <param name="setTarget">Applies a value to the given control.</param>
    /// <param name="setSource">Writes an edited value back to the source.</param>
    /// <param name="getTarget">Reads the current value from the control.</param>
    /// <param name="subscribeTargetChanged">Subscribes a handler to the control's change event.</param>
    /// <param name="unsubscribeTargetChanged">Removes that handler.</param>
    /// <param name="mode">The flow direction.</param>
    /// <param name="defaultValue">Replaces the value when reading the source throws.</param>
    /// <param name="nullReplacement">Replaces a source value that is <see langword="null"/>.</param>
    /// <param name="onError">Receives the property's validation error, or <see langword="null"/> when it clears.</param>
    /// <returns>The live binding; keep it only to dispose early.</returns>
    public static PropertyBinding<TValue> Bind<TControl, TSource, TValue>(
        this TControl target,
        TSource source,
        string sourcePropertyName,
        Func<TSource, TValue> getSource,
        Action<TControl, TValue> setTarget,
        Action<TSource, TValue> setSource,
        Func<TControl, TValue> getTarget,
        Action<TControl, EventHandler> subscribeTargetChanged,
        Action<TControl, EventHandler> unsubscribeTargetChanged,
        BindingMode mode = BindingMode.TwoWay,
        BindingFallback<TValue> defaultValue = default,
        BindingFallback<TValue> nullReplacement = default,
        Action<string?>? onError = null)
        where TControl : Control
        where TSource : INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(setTarget);
        ArgumentNullException.ThrowIfNull(setSource);
        ArgumentNullException.ThrowIfNull(getTarget);
        ArgumentNullException.ThrowIfNull(subscribeTargetChanged);
        ArgumentNullException.ThrowIfNull(unsubscribeTargetChanged);
        var observable = RequireObservableForErrors(source, onError);

        var binding = new PropertyBinding<TValue>(
            source,
            sourcePropertyName,
            ComposeGet(source, getSource, defaultValue, nullReplacement),
            v => setTarget(target, v),
            mode,
            setSource: v => setSource(source, v),
            getTarget: () => getTarget(target),
            subscribeTargetChanged: h => subscribeTargetChanged(target, h),
            unsubscribeTargetChanged: h => unsubscribeTargetChanged(target, h));
        AttachErrorCallback(binding, observable, sourcePropertyName, onError);
        return binding;
    }

    /// <summary>Bakes the source object and the fallback semantics into the parameterless getter the
    /// binding primitive expects. Without fallbacks the read passes through untouched.</summary>
    private static Func<TValue> ComposeGet<TSource, TValue>(
        TSource source,
        Func<TSource, TValue> get,
        BindingFallback<TValue> defaultValue,
        BindingFallback<TValue> nullReplacement)
    {
        ArgumentNullException.ThrowIfNull(get);

        if (!defaultValue.IsSet && !nullReplacement.IsSet)
            return () => get(source);

        return () =>
        {
            TValue value;
            if (defaultValue.IsSet)
                try
                {
                    value = get(source);
                }
                catch
                {
                    return defaultValue.Value;
                }
            else
                value = get(source);

            return value is null && nullReplacement.IsSet ? nullReplacement.Value : value;
        };
    }

    /// <summary>Validates the error-callback precondition before the binding is constructed: with a
    /// callback the source must be an <see cref="ObservableObject"/> (the error store lives there).</summary>
    private static ObservableObject? RequireObservableForErrors<TSource>(TSource source, Action<string?>? onError)
    {
        if (onError is null)
            return null;

        return source as ObservableObject
            ?? throw new ArgumentException(
                "Surfacing validation errors requires the source to be an ObservableObject.", nameof(onError));
    }

    /// <summary>Hooks the error callback to the source's <see cref="ObservableObject.ErrorsChanged"/>,
    /// pushes the current state once, and ties the un-hook to the binding's disposal.</summary>
    private static void AttachErrorCallback<TValue>(
        PropertyBinding<TValue> binding,
        ObservableObject? observable,
        string sourcePropertyName,
        Action<string?>? onError)
    {
        if (observable is null || onError is null)
            return;

        void Handler(object? sender, DataErrorsChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == sourcePropertyName)
                onError(observable.GetError(sourcePropertyName));
        }

        observable.ErrorsChanged += Handler;
        binding.RegisterDisposeCallback(() => observable.ErrorsChanged -= Handler);
        onError(observable.GetError(sourcePropertyName));
    }
}
