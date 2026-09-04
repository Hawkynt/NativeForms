using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ExternalDropTests {
  [Test]
  public void Drop_accepted_by_realized_form_raises_enter_and_drop() {
    var files = new[] { "first.png", "second.png" };
    var form = new Form { AllowDrop = true, Bounds = new Rectangle(100, 200, 320, 240) };
    var backend = new DropBackend(files, new Point(135, 245), DragDropEffects.Copy);
    DragEventArgs? dropped = null;

    form.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    form.DragDrop += (_, e) => dropped = e;

    Application.Run(form, backend);

    Assert.Multiple(() => {
      Assert.That(backend.Result, Is.EqualTo(DragDropEffects.Copy));
      Assert.That(dropped, Is.Not.Null);
      Assert.That(dropped!.Data, Is.SameAs(files));
      Assert.That(dropped.AllowedEffect, Is.EqualTo(DragDropEffects.Copy));
      Assert.That(dropped.Effect, Is.EqualTo(DragDropEffects.Copy));
      Assert.That(dropped.X, Is.EqualTo(135));
      Assert.That(dropped.Y, Is.EqualTo(245));
    });
  }

  [Test]
  public void Drop_rejected_by_enter_raises_leave_without_drop() {
    var form = new Form { AllowDrop = true, Bounds = new Rectangle(10, 20, 200, 100) };
    var backend = new DropBackend(new[] { "rejected.png" }, new Point(20, 30), DragDropEffects.Copy);
    var leaveCount = 0;
    var dropCount = 0;

    form.DragLeave += (_, _) => ++leaveCount;
    form.DragDrop += (_, _) => ++dropCount;

    Application.Run(form, backend);

    Assert.Multiple(() => {
      Assert.That(backend.Result, Is.EqualTo(DragDropEffects.None));
      Assert.That(leaveCount, Is.EqualTo(1));
      Assert.That(dropCount, Is.Zero);
    });
  }

  private sealed class DropBackend(object data, Point point, DragDropEffects allowed) : IPlatformBackend {
    private readonly FakeWindowPeer _window = new();
    public DragDropEffects Result { get; private set; }
    public string Name => "Drop test";
    public bool IsSupported => true;
    public ITheme Theme => DefaultTheme.Instance;
    public event EventHandler? ThemeChanged { add { } remove { } }
    public double GetDpiScale() => 1d;
    public IWindowPeer CreateWindow() => _window;
    public IButtonPeer CreateButton() => throw new NotSupportedException();
    public ILabelPeer CreateLabel() => throw new NotSupportedException();
    public ITextBoxPeer CreateTextBox() => throw new NotSupportedException();
    public IRichTextBoxPeer CreateRichTextBox() => throw new NotSupportedException();
    public ICanvasPeer CreateCanvas() => throw new NotSupportedException();
    public IPopupPeer CreatePopup(IWindowPeer? owner) => throw new NotSupportedException();
    public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb) => throw new NotSupportedException();
    public Size GetScreenSize() => new(1920, 1080);
    public Size MeasureText(string text, Font font) => Size.Empty;
    public ITimerPeer CreateTimer() => throw new NotSupportedException();
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null) => throw new NotSupportedException();
    public string[]? ShowFileDialog(in FileDialogOptions options) => throw new NotSupportedException();
    public Color? ShowColorDialog(Color color) => throw new NotSupportedException();
    public Font? ShowFontDialog(Font font) => throw new NotSupportedException();
    public INotifyIconPeer CreateNotifyIcon() => throw new NotSupportedException();
    public void SetClipboardText(string text) => throw new NotSupportedException();
    public string? GetClipboardText() => throw new NotSupportedException();
    public void Post(Action action) => action();
    public void Run(IWindowPeer mainWindow) => this.Result = ExternalDropBridge.Route(mainWindow, data, allowed, point);
    public void Quit() { }
  }

  private sealed class FakeWindowPeer : IWindowPeer {
    private Rectangle _bounds;
    public event EventHandler? GotFocus { add { } remove { } }
    public event EventHandler? LostFocus { add { } remove { } }
    public event EventHandler<MouseEventArgs>? PointerMove { add { } remove { } }
    public event EventHandler? PointerLeave { add { } remove { } }
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested { add { } remove { } }
    public event EventHandler<System.ComponentModel.CancelEventArgs>? CloseRequested { add { } remove { } }
    public event EventHandler? Closed;
    public event EventHandler<Rectangle>? BoundsChangedByUser { add { } remove { } }
    public event EventHandler<FormWindowState>? WindowStateChanged { add { } remove { } }
    public void SetBounds(Rectangle bounds) => _bounds = bounds;
    public void SetText(string text) { }
    public void SetVisible(bool visible) { }
    public void SetEnabled(bool enabled) { }
    public void SetFont(Font font) { }
    public void SetColors(Color foreColor, Color backColor) { }
    public void SetCursor(Cursor cursor) { }
    public Point PointToScreen(Point clientPoint) => new(_bounds.X + clientPoint.X, _bounds.Y + clientPoint.Y);
    public void Focus() { }
    public void ShowToolTip(string? text) { }
    public void AddChild(IControlPeer child) { }
    public void RemoveChild(IControlPeer child) { }
    public void Show() { }
    public void RunModal(IWindowPeer? owner) { }
    public void Close() => this.Closed?.Invoke(this, EventArgs.Empty);
    public void SetBorderStyle(FormBorderStyle borderStyle) { }
    public void SetWindowState(FormWindowState state) { }
    public void SetMinimizeBox(bool visible) { }
    public void SetMaximizeBox(bool visible) { }
    public void SetSizeLimits(Size minimum, Size maximum) { }
    public void SetIcon(int width, int height, ReadOnlySpan<int> argb) { }
    public void SetTopMost(bool topMost) { }
    public void SetQuitsOnClose(bool quits) { }
    public void SetOpacity(double opacity) { }
    public void Dispose() { }
  }
}
