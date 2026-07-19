using System;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class BackendRegistryTests
{
    [SetUp]
    public void Reset() => BackendRegistry.Clear();

    [TearDown]
    public void Cleanup() => BackendRegistry.Clear();

    [Test]
    public void Resolve_returns_first_supported_backend()
    {
        // Distinct types: registration is idempotent per concrete type, not per instance.
        var unsupported = new UnsupportedStub();
        var supported = new SupportedStub();
        BackendRegistry.Register(unsupported);
        BackendRegistry.Register(supported);

        Assert.That(BackendRegistry.Resolve(), Is.SameAs(supported));
    }

    [Test]
    public void Resolve_throws_when_nothing_registered()
        => Assert.Throws<PlatformNotSupportedException>(() => BackendRegistry.Resolve());

    [Test]
    public void Resolve_throws_when_none_supported()
    {
        BackendRegistry.Register(new UnsupportedStub());
        Assert.Throws<PlatformNotSupportedException>(() => BackendRegistry.Resolve());
    }

    [Test]
    public void Register_is_idempotent_per_type()
    {
        BackendRegistry.Register(new SupportedStub());
        BackendRegistry.Register(new SupportedStub());

        Assert.That(BackendRegistry.Registered, Has.Count.EqualTo(1));
    }

    private abstract class StubBackend : IPlatformBackend
    {
        public abstract string Name { get; }
        public abstract bool IsSupported { get; }
        public ITheme Theme => DefaultTheme.Instance;
        public IWindowPeer CreateWindow() => throw new NotSupportedException();
        public IButtonPeer CreateButton() => throw new NotSupportedException();
        public ILabelPeer CreateLabel() => throw new NotSupportedException();
        public ICanvasPeer CreateCanvas() => throw new NotSupportedException();
        public IPopupPeer CreatePopup() => throw new NotSupportedException();
        public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => throw new NotSupportedException();
        public System.Drawing.Size MeasureText(string text, Font font) => throw new NotSupportedException();
        public ITimerPeer CreateTimer() => throw new NotSupportedException();
        public void Run(IWindowPeer mainWindow) { }
        public void Quit() { }
    }

    private sealed class SupportedStub : StubBackend
    {
        public override string Name => "Supported";
        public override bool IsSupported => true;
    }

    private sealed class UnsupportedStub : StubBackend
    {
        public override string Name => "Unsupported";
        public override bool IsSupported => false;
    }
}
