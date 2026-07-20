using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// A context menu: the same item model and owner-drawn drop-down engine as a <see cref="MenuStrip"/>
/// drop-down, opened at an arbitrary screen point instead of below a bar. Assign one to
/// <see cref="Control.ContextMenuStrip"/> and a right-click on an owner-drawn control opens it at
/// the cursor; <see cref="Show(Control, Point)"/> opens it programmatically. It is a component, not
/// a control — it owns no peer until it opens and can serve any number of controls at once.
/// </summary>
/// <remarks>
/// Right-click opening is wired through the owner-drawn mouse pipeline; native-widget controls
/// (Button, TextBox …) need right-click events on their peers first, tracked in <c>docs/PRD.md</c>.
/// </remarks>
public class ContextMenuStrip : Component
{
    private MenuDropDown? _dropDown;
    private IPlatformBackend? _backend;

    /// <summary>Creates an empty context menu.</summary>
    public ContextMenuStrip() => this.Items = new();

    /// <summary>The menu items, sharing the <see cref="MenuStrip"/> item model.</summary>
    public ToolStripItemCollection Items { get; }

    /// <summary>Whether the menu is currently open.</summary>
    public bool IsOpen => _dropDown is { IsOpen: true };

    /// <summary>Raised after the menu (and its whole cascade) has closed.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Opens the menu at a position given in <paramref name="control"/>'s client space. The control
    /// must be realized — only a live widget knows its screen position.
    /// </summary>
    public void Show(Control control, Point clientLocation)
    {
        ArgumentNullException.ThrowIfNull(control);
        var backend = control.Backend;
        if (backend is null)
            return;

        this.ShowAt(backend, control.PointToScreen(clientLocation));
    }

    /// <summary>Opens the menu at an absolute screen position on the given backend.</summary>
    internal void ShowAt(IPlatformBackend backend, Point screenLocation)
    {
        var engine = _dropDown;
        if (engine is null || !ReferenceEquals(_backend, backend))
        {
            engine?.CloseAll();
            _backend = backend;
            _dropDown = engine = new(backend, backend.Theme);
            engine.Closed += (_, _) => this.Closed?.Invoke(this, EventArgs.Empty);
        }

        engine.Open(this.Items, screenLocation);
    }

    /// <summary>Closes the menu, if open.</summary>
    public void Close() => _dropDown?.CloseAll();

    /// <summary>Closes the menu and drops the drop-down engine (and with it the native popups).</summary>
    protected override void Dispose(bool disposing)
    {
        _dropDown?.CloseAll();
        _dropDown = null;
        _backend = null;
    }
}
