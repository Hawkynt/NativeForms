namespace Hawkynt.NativeForms;

/// <summary>Identifies the page a <see cref="TabControl"/> event is about.</summary>
public sealed class TabPageEventArgs(TabPage page, int index) : EventArgs
{
    /// <summary>The page the event concerns.</summary>
    public TabPage Page { get; } = page;

    /// <summary>The page's index within <see cref="TabControl.TabPages"/> at the time of the event.</summary>
    public int Index { get; } = index;
}

/// <summary>
/// Identifies the page whose close button was pressed, and lets a handler veto the close by setting
/// <see cref="System.ComponentModel.CancelEventArgs.Cancel"/> — the page then stays in the control.
/// </summary>
public sealed class TabPageCancelEventArgs(TabPage page, int index)
    : System.ComponentModel.CancelEventArgs
{
    /// <summary>The page about to close.</summary>
    public TabPage Page { get; } = page;

    /// <summary>The page's index within <see cref="TabControl.TabPages"/>.</summary>
    public int Index { get; } = index;
}
