using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>A non-interactive line of static text, backed by the platform's native label widget.</summary>
public class Label : Control
{
    private protected override IControlPeer CreatePeer(IPlatformBackend backend) => backend.CreateLabel();
}
