namespace Hawkynt.NativeForms;

/// <summary>
/// A <see cref="TextBox"/> whose native editor draws no border of its own, for a composite that frames
/// the editor itself — a <see cref="SearchBox"/> or a spinner — so a second border does not nest inside
/// the drawn shell. It adds no instance state over <see cref="TextBox"/>, only the frame override, so a
/// hosted editor costs no extra footprint.
/// </summary>
internal sealed class FramelessTextBox : TextBox {
  private protected override bool HasFrame => false;
}
