namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>
/// The base of the non-visual building blocks (<see cref="Timer"/>, <see cref="ToolTip"/>,
/// <see cref="NotifyIcon"/>, <see cref="ContextMenuStrip"/>) — the moral equivalent of
/// <c>System.ComponentModel.Component</c> minus the designer machinery: no sites, no reflection,
/// just deterministic disposal and optional ownership by an <see cref="IContainer"/>.
/// </summary>
public abstract class Component : IDisposable
{
    private bool _disposed;

    /// <summary>The container that owns (and will dispose) this component, or <see langword="null"/>.</summary>
    public IContainer? Container { get; internal set; }

    /// <summary>Raised once, after the component has been disposed.</summary>
    public event EventHandler? Disposed;

    /// <summary>Releases the component's resources; subclasses override to tear down their state.</summary>
    /// <param name="disposing">Always <see langword="true"/> — there are no finalizers in this model.</param>
    protected virtual void Dispose(bool disposing) { }

    /// <summary>
    /// Disposes the component: runs the subclass teardown, detaches it from its
    /// <see cref="Container"/> and raises <see cref="Disposed"/>. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        this.Dispose(true);
        this.Container?.Remove(this);
        this.Disposed?.Invoke(this, EventArgs.Empty);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Owns a set of <see cref="Component"/>s and disposes them with itself — the designer-free
/// counterpart of <c>System.ComponentModel.IContainer</c>.
/// </summary>
public interface IContainer : IDisposable
{
    /// <summary>The owned components, in the order they were added.</summary>
    IReadOnlyList<Component> Components { get; }

    /// <summary>Takes ownership of a component (removing it from any previous container).</summary>
    void Add(Component component);

    /// <summary>Releases a component from this container without disposing it.</summary>
    void Remove(Component component);
}

/// <summary>
/// The standard <see cref="IContainer"/>: a list of owned components, disposed in reverse order of
/// addition when the container itself is disposed — the pattern generated WinForms code relies on
/// (<c>components.Dispose()</c> in a form's <c>Dispose</c>).
/// </summary>
public sealed class Container : IContainer
{
    private readonly List<Component> _components = [];

    /// <inheritdoc/>
    public IReadOnlyList<Component> Components => _components;

    /// <inheritdoc/>
    public void Add(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (ReferenceEquals(component.Container, this))
            return;

        component.Container?.Remove(component);
        _components.Add(component);
        component.Container = this;
    }

    /// <inheritdoc/>
    public void Remove(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (_components.Remove(component))
            component.Container = null;
    }

    /// <summary>Disposes every owned component, last-added first, and empties the container.</summary>
    public void Dispose()
    {
        for (var i = _components.Count - 1; i >= 0; --i)
        {
            var component = _components[i];

            // Detach before disposing so the component's own Remove call is a no-op.
            component.Container = null;
            _components.RemoveAt(i);
            component.Dispose();
        }
    }
}
