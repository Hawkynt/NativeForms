using System.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// A titled, dockable pane hosted by a <see cref="DockPanel"/> (§7.2). It is an ordinary container —
/// its <see cref="Control.Controls"/> hold real nested children that realize as native peers — with a
/// caption bar (title, optional <see cref="ImageIndex"/> icon and close/float/auto-hide buttons) that
/// the owning <see cref="DockPanel"/> paints just above the content, plus a <see cref="DockState"/>
/// that says whether it is docked to an edge, in the central document well, floating in its own
/// window, or collapsed to an auto-hide strip.
/// </summary>
/// <remarks>
/// The pane itself never paints its own chrome: the manager owns every caption, tab strip and
/// splitter so the whole arrangement can be drawn — and hit-tested for docking — in one place, and so
/// a hidden or collapsed pane's peer is simply vetoed and costs nothing. Assigning
/// <see cref="DockState"/> or calling <see cref="Close"/>/<see cref="Float"/>/<see cref="ToggleAutoHide"/>
/// routes through the owning <see cref="DockPanel"/>, so the layout tree, z-order and persistence stay
/// consistent; before a manager adopts the pane those setters only record the intended state.
/// </remarks>
public class DockContent : Panel
{
    // Packed pane options — one byte instead of three bool fields, so an unshown pane stays cheap.
    [Flags]
    private enum Options : byte
    {
        None = 0,
        AllowClose = 1,
        AllowFloat = 2,
        AllowAutoHide = 4,
        Default = AllowClose | AllowFloat | AllowAutoHide,
    }

    private Options _options = Options.Default;
    private int _imageIndex = -1;
    private DockState _dockState = DockState.Hidden;
    private DockEdge _dockEdge = DockEdge.Left;

    /// <summary>Creates an empty pane.</summary>
    public DockContent() { }

    /// <summary>Creates a pane with the given caption.</summary>
    public DockContent(string title) => this.Text = title;

    /// <summary>The caption shown in the pane's title bar or tab — an alias for <see cref="Control.Text"/>.</summary>
    public string Title
    {
        get => this.Text;
        set => this.Text = value;
    }

    /// <summary>The index into the owning <see cref="DockPanel.ImageList"/> of the caption/tab icon,
    /// or -1 for none.</summary>
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value)
                return;

            _imageIndex = value;
            this.DockPanel?.InvalidateChrome();
        }
    }

    /// <summary>The key of this content's icon in the owning <see cref="DockPanel.ImageList"/>, used when
    /// <see cref="ImageIndex"/> is unset (&lt; 0). The index takes precedence when both are set.</summary>
    public string? ImageKey
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.DockPanel?.InvalidateChrome();
        }
    }

    /// <summary>Where this pane currently lives. Read freely; assign to move it (the owning
    /// <see cref="DockPanel"/> performs the move so the layout stays consistent). Before a manager
    /// owns the pane the setter only records the wish.</summary>
    public DockState DockState
    {
        get => _dockState;
        set
        {
            if (_dockState == value)
                return;

            if (this.DockPanel is { } panel)
                panel.SetContentState(this, value);
            else
                _dockState = value;
        }
    }

    /// <summary>The edge a docked or auto-hidden pane clings to. Changing it re-docks the pane when it
    /// is currently on an edge.</summary>
    public DockEdge DockEdge
    {
        get => _dockEdge;
        set
        {
            if (_dockEdge == value)
                return;

            _dockEdge = value;
            if (this.DockPanel is { } panel && _dockState is DockState.Docked or DockState.AutoHide)
                panel.ReDockToEdge(this, value);
        }
    }

    /// <summary>A stable key used by <see cref="DockPanel.SaveLayout"/>/<see cref="DockPanel.LoadLayout"/>
    /// to identify this pane across a save/restore. Defaults to <see cref="Control.Name"/> when unset.</summary>
    public string? PersistId { get; set; }

    /// <summary>Whether the caption shows a close button.</summary>
    public bool AllowClose
    {
        get => (_options & Options.AllowClose) != 0;
        set => this.SetOption(Options.AllowClose, value);
    }

    /// <summary>Whether the caption shows a float button and the pane may be torn off into a window.</summary>
    public bool AllowFloat
    {
        get => (_options & Options.AllowFloat) != 0;
        set => this.SetOption(Options.AllowFloat, value);
    }

    /// <summary>Whether the caption shows an auto-hide (pin) button.</summary>
    public bool AllowAutoHide
    {
        get => (_options & Options.AllowAutoHide) != 0;
        set => this.SetOption(Options.AllowAutoHide, value);
    }

    /// <summary>The manager that owns this pane, or <see langword="null"/> while it is unowned.</summary>
    public DockPanel? DockPanel { get; internal set; }

    /// <summary>Raised after <see cref="DockState"/> changes.</summary>
    public event EventHandler? DockStateChanged;

    /// <summary>Raised before the pane closes; cancelling keeps it where it is.</summary>
    public event EventHandler<CancelEventArgs>? CloseRequested;

    /// <summary>The effective persistence key: <see cref="PersistId"/> when set, else <see cref="Control.Name"/>.</summary>
    internal string Key => string.IsNullOrEmpty(this.PersistId) ? this.Name : this.PersistId!;

    /// <summary>Removes the pane from the panel (raising <see cref="CloseRequested"/> first, which may
    /// veto it). Without an owner this is a no-op.</summary>
    public void Close() => this.DockPanel?.CloseContent(this);

    /// <summary>Tears the pane off into its own floating window.</summary>
    public void Float() => this.DockState = DockState.Floating;

    /// <summary>Collapses a docked pane to its auto-hide strip, or pins a collapsed one back.</summary>
    public void ToggleAutoHide()
        => this.DockState = _dockState == DockState.AutoHide ? DockState.Docked : DockState.AutoHide;

    /// <summary>Brings the pane to the front of its group and gives it the active caption.</summary>
    public void Activate() => this.DockPanel?.ActivateContent(this);

    /// <summary>Sets the backing state without routing through the manager (the manager itself is the
    /// caller during a move).</summary>
    internal void SetStateInternal(DockState state)
    {
        if (_dockState == state)
            return;

        _dockState = state;
        this.OnDockStateChanged(EventArgs.Empty);
    }

    /// <summary>Sets the backing edge without re-docking (the manager is mid-move).</summary>
    internal void SetEdgeInternal(DockEdge edge) => _dockEdge = edge;

    /// <summary>Runs the vetoable close pipeline; returns whether the close may proceed.</summary>
    internal bool RequestClose()
    {
        if (this.CloseRequested is not { } handler)
            return true;

        var args = new CancelEventArgs();
        handler(this, args);
        return !args.Cancel;
    }

    /// <summary>Raises <see cref="DockStateChanged"/>.</summary>
    protected virtual void OnDockStateChanged(EventArgs e) => this.DockStateChanged?.Invoke(this, e);

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        this.DockPanel?.InvalidateChrome();
    }

    private void SetOption(Options option, bool on)
    {
        var updated = on ? _options | option : _options & ~option;
        if (updated == _options)
            return;

        _options = updated;
        this.DockPanel?.InvalidateChrome();
    }
}
