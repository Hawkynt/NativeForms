namespace Hawkynt.NativeForms;

/// <summary>How a <see cref="DataGridViewColumn"/> computes its width.</summary>
public enum DataGridViewAutoSizeColumnMode
{
    /// <summary>The width is whatever <see cref="DataGridViewColumn.Width"/> says. The default.</summary>
    None,

    /// <summary>The width fits the widest cell text currently in the visible row window, remeasured on
    /// demand each paint. Deliberately window-scoped: measuring all rows would defeat virtualization,
    /// so the width may adapt as the grid scrolls.</summary>
    AllCells,
}
