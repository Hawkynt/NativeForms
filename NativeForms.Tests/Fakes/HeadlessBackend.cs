using System.Drawing;
using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Tests.Fakes;

/// <summary>
/// A display-free backend that records every peer interaction, so control-tree, realization and
/// event logic can be unit-tested without a windowing system. <see cref="Run"/> returns immediately
/// rather than blocking on a message loop.
/// </summary>
internal sealed class HeadlessBackend : IPlatformBackend
{
    public List<HeadlessPeer> Created { get; } = [];
    public bool DidRun { get; private set; }
    public bool DidQuit { get; private set; }

    public string Name => "Headless";
    public bool IsSupported => true;

    public IWindowPeer CreateWindow() => this.Track(new HeadlessWindowPeer());
    public IButtonPeer CreateButton() => this.Track(new HeadlessButtonPeer());
    public ILabelPeer CreateLabel() => this.Track(new HeadlessLabelPeer());

    public void Run(IWindowPeer mainWindow) => this.DidRun = true;
    public void Quit() => this.DidQuit = true;

    private T Track<T>(T peer) where T : HeadlessPeer
    {
        this.Created.Add(peer);
        return peer;
    }
}

/// <summary>Base recorder peer.</summary>
internal abstract class HeadlessPeer : IControlPeer
{
    public Rectangle Bounds { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool Visible { get; private set; }
    public bool Enabled { get; private set; }
    public bool Disposed { get; private set; }

    public void SetBounds(Rectangle bounds) => this.Bounds = bounds;
    public void SetText(string text) => this.Text = text;
    public void SetVisible(bool visible) => this.Visible = visible;
    public void SetEnabled(bool enabled) => this.Enabled = enabled;
    public void Dispose() => this.Disposed = true;
}

internal sealed class HeadlessWindowPeer : HeadlessPeer, IWindowPeer
{
    public List<IControlPeer> Children { get; } = [];
    public bool Shown { get; private set; }

    public event EventHandler? Closed;

    public void AddChild(IControlPeer child) => this.Children.Add(child);
    public void Show() => this.Shown = true;

    public void RaiseClosed() => this.Closed?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessButtonPeer : HeadlessPeer, IButtonPeer
{
    public event EventHandler? Clicked;

    public void RaiseClicked() => this.Clicked?.Invoke(this, EventArgs.Empty);
}

internal sealed class HeadlessLabelPeer : HeadlessPeer, ILabelPeer;
