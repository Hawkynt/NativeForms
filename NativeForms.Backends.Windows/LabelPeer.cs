using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>The Win32 peer for a label — a native, left-aligned <c>STATIC</c> text window.</summary>
internal sealed class LabelPeer : Win32ChildPeer, ILabelPeer
{
    /// <inheritdoc/>
    protected override string WindowClass => "STATIC";

    /// <inheritdoc/>
    protected override uint ExtraStyle => NativeMethods.SS_LEFT;
}
