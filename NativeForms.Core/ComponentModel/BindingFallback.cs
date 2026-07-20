namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>
/// An optional per-binding fallback value — a tiny allocation-free "value or absent" wrapper that
/// lets a binding tell a deliberately supplied <c>default(T)</c> apart from "no fallback given".
/// Callers never construct it explicitly: passing a plain value to a <c>defaultValue</c>/
/// <c>nullReplacement</c>/<c>fallbackValue</c> parameter converts implicitly, and omitting the
/// parameter means <see cref="None"/>.
/// </summary>
/// <typeparam name="T">The bound value type.</typeparam>
public readonly struct BindingFallback<T>
{
    private readonly T _value;

    private BindingFallback(T value)
    {
        _value = value;
        this.IsSet = true;
    }

    /// <summary>Whether a fallback value was supplied.</summary>
    public bool IsSet { get; }

    /// <summary>The supplied fallback value, meaningful only while <see cref="IsSet"/>.</summary>
    internal T Value => _value;

    /// <summary>No fallback — the binding's default behavior applies.</summary>
    public static BindingFallback<T> None => default;

    /// <summary>Wraps a value so plain arguments read naturally at the call site.</summary>
    public static implicit operator BindingFallback<T>(T value) => new(value);
}
