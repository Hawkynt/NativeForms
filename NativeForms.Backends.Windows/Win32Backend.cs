using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Windows (Win32/USER32) implementation of <see cref="IPlatformBackend"/>. It manufactures native
/// peers and pumps the classic <c>GetMessage</c>/<c>TranslateMessage</c>/<c>DispatchMessage</c> loop.
/// The type compiles on any OS; <see cref="IsSupported"/> gates it to Windows at run time.
/// </summary>
public sealed partial class Win32Backend : IPlatformBackend {
  /// <summary>The most recently constructed backend — the instance the static window procedures
  /// notify when a system-wide theme message arrives (an app runs exactly one backend).</summary>
  private static Win32Backend? _current;

  private Win32Theme? _theme;

  /// <summary>Registers this instance as the receiver of system theme-change notifications.</summary>
  public Win32Backend() => _current = this;

  /// <summary>
  /// Tells Windows this process scales its own windows, before any window exists.
  /// </summary>
  /// <remarks>
  /// Without this a process is DPI <em>unaware</em>, which is not a neutral default: Windows renders the
  /// window at 96 DPI and stretches the bitmap to the display's scale, so everything is soft, and
  /// <c>GetDpiForSystem</c> answers 96 whatever the display is actually set to — the toolkit would be
  /// told nothing is wrong. Per-monitor v2 additionally scales the non-client frame and delivers
  /// <c>WM_DPICHANGED</c> when a window moves between displays of different scale. Older Windows gets
  /// the system-wide opt-in, which is still far better than none.
  /// </remarks>
  /// <remarks>
  /// Called from the first window rather than from the constructor: a backend is constructed to be
  /// <em>registered</em>, and an application registers every backend it was built with before
  /// <see cref="IsSupported"/> picks one — so on Linux this type is instantiated with no <c>user32</c>
  /// to call into. Declaring awareness must still happen before any window exists, which the first
  /// <see cref="CreateWindow"/> guarantees.
  /// </remarks>
  private static void DeclareDpiAwareness() {
    if (_dpiAwarenessDeclared)
      return;

    _dpiAwarenessDeclared = true;
    try {
      if (NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        return;

      NativeMethods.SetProcessDPIAware();
    } catch (EntryPointNotFoundException) {
      // Before Windows 10 1703 the context API is absent, and older still lacks both. The process
      // stays unaware, which is the behaviour that shipped until now.
    } catch (DllNotFoundException) {
      // Not Windows at all — this backend was registered but will not be the one chosen.
    }
  }

  private static bool _dpiAwarenessDeclared;

  /// <inheritdoc/>
  public string Name => "Win32";

  /// <inheritdoc/>
  public bool IsSupported => OperatingSystem.IsWindows();

  /// <inheritdoc/>
  // Built lazily and cached: constructing it queries the OS, so we defer that until a control paints
  // (and never touch USER32/GDI just by instantiating the backend on a non-Windows host). The cache
  // is dropped when the desktop announces a theme change, so the next read snapshots fresh values.
  public ITheme Theme => _theme ??= new Win32Theme();

  /// <summary>
  /// The desktop's own UI font — what a stock control wears when the application named none.
  /// </summary>
  /// <remarks>
  /// Read through the same cached snapshot the painter reads, so a widget and the owner-drawn twin
  /// beside it agree on the face; a theme change drops that snapshot, so the next widget built picks
  /// the new one up. A peer created before any backend was constructed cannot happen — a peer comes
  /// out of one — but the fall-back keeps the property total rather than nullable.
  /// </remarks>
  internal static Font DefaultUiFont => (_current?.Theme ?? (_detachedTheme ??= new Win32Theme())).DefaultFont;

  /// <summary>Backs <see cref="DefaultUiFont"/> when no backend has been constructed.</summary>
  private static Win32Theme? _detachedTheme;

  /// <inheritdoc/>
  public event EventHandler? ThemeChanged;

  /// <summary>
  /// Called from the window procedures when the desktop announces a theme change
  /// (<c>WM_THEMECHANGED</c>, <c>WM_SYSCOLORCHANGE</c>, <c>WM_SETTINGCHANGE</c>): drops the cached
  /// theme snapshot, then raises <see cref="ThemeChanged"/> so realized owner-drawn controls
  /// re-read it and repaint.
  /// </summary>
  internal static void NotifySystemThemeChanged() {
    var backend = _current;
    if (backend is null)
      return;

    backend._theme = null;
    backend.ThemeChanged?.Invoke(backend, EventArgs.Empty);
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Per-monitor, so a window on a second display at a different scale is measured against its own —
  /// which is the whole point of the awareness declared above. <see cref="ActiveWindowForDpi"/> is the
  /// window currently being realized or painted; with none, the desktop's own scaling is the best
  /// answer available.
  /// </remarks>
  public double GetDpiScale() {
    var window = ActiveWindowForDpi;
    var dpi = window != 0 ? NativeMethods.GetDpiForWindow(window) : 0;
    if (dpi == 0)
      dpi = NativeMethods.GetDpiForSystem();

    return dpi > 0 ? dpi / 96.0 : 1.0;
  }

  /// <summary>
  /// The window whose display the scale is read from. Set by the window peer as it realizes and as it
  /// moves, so the answer follows the window rather than the desktop.
  /// </summary>
  internal static nint ActiveWindowForDpi { get; set; }

  /// <summary>
  /// Records the new scale, drops the cached theme and tells every realized control to re-measure.
  /// </summary>
  /// <remarks>
  /// Reported through <see cref="ThemeChanged"/> rather than an event of its own, because from the
  /// toolkit's side a scale change <em>is</em> a theme change: everything measured in pixels — the
  /// default font, the row height, the scroll-bar thickness, every owner-drawn metric of §5 — is read
  /// from <see cref="Theme"/> and derived from the DPI, so the snapshot is stale in exactly the same way
  /// and the listeners that already handle it are exactly the ones that need to react.
  /// </remarks>
  /// <param name="window">The window that reported the change; it becomes the one scale is read from.</param>
  internal static void NotifyDpiChanged(nint window) {
    if (_current is not { } backend)
      return;

    ActiveWindowForDpi = window;
    backend._theme = null;
    backend.ThemeChanged?.Invoke(backend, EventArgs.Empty);
  }

  /// <inheritdoc/>
  public IWindowPeer CreateWindow() {
    DeclareDpiAwareness();
    return new WindowPeer();
  }

  /// <inheritdoc/>
  public IButtonPeer CreateButton() => new ButtonPeer();

  /// <inheritdoc/>
  public ILabelPeer CreateLabel() => new LabelPeer();

  /// <inheritdoc/>
  public ITextBoxPeer CreateTextBox() => new TextBoxPeer();

  /// <inheritdoc/>
  public IRichTextBoxPeer CreateRichTextBox() => new RichTextBoxPeer();

  /// <inheritdoc/>
  public ICheckBoxPeer CreateCheckBox() => new CheckBoxPeer();

  /// <inheritdoc/>
  public IRadioButtonPeer CreateRadioButton() => new RadioButtonPeer();

  /// <inheritdoc/>
  public IScrollBarPeer CreateScrollBar(bool vertical) => new ScrollBarPeer(vertical);

  /// <inheritdoc/>
  public IComboBoxPeer CreateComboBox() => new ComboBoxPeer();

  /// <inheritdoc/>
  public IListBoxPeer CreateListBox() => new ListBoxPeer();

  /// <inheritdoc/>
  public IGroupBoxPeer CreateGroupBox() => new GroupBoxPeer();

  /// <inheritdoc/>
  /// <remarks>
  /// <c>SysLink</c> only exists in ComCtl32 version 6, which a process reaches through an application
  /// manifest — so unlike the stock classes it can genuinely be absent, and then the promotion has to be
  /// declined rather than attempted. Creating the window and discovering the failure later is too late:
  /// the core has committed to the peer by then, and the control would render nothing at all.
  /// </remarks>
  public ILinkLabelPeer? CreateLinkLabel() {
    EnsureCommonControls(NativeMethods.ICC_LINK_CLASS);
    return ClassExists(NativeMethods.WC_LINK) ? new LinkLabelPeer() : null;
  }

  /// <inheritdoc/>
  public IProgressBarPeer? CreateProgressBar() {
    EnsureCommonControls(NativeMethods.ICC_PROGRESS_CLASS);
    return ClassExists(NativeMethods.PROGRESS_CLASS) ? new ProgressBarPeer() : null;
  }

  /// <inheritdoc/>
  public ITrackBarPeer? CreateTrackBar(bool vertical) {
    EnsureCommonControls(NativeMethods.ICC_BAR_CLASSES);
    return ClassExists(NativeMethods.TRACKBAR_CLASS) ? new TrackBarPeer(vertical) : null;
  }

  /// <summary>
  /// Registers a block of common-control window classes once per process. Without this,
  /// <c>CreateWindowEx</c> on <c>msctls_progress32</c> or <c>msctls_trackbar32</c> fails with an
  /// unregistered class on hosts that carry no application manifest.
  /// </summary>
  /// <param name="classes">The <c>ICC_*</c> block to register.</param>
  private static void EnsureCommonControls(uint classes) {
    if ((_registeredCommonControls & classes) == classes)
      return;

    var request = new NativeMethods.INITCOMMONCONTROLSEX {
      dwSize = (uint)Marshal.SizeOf<NativeMethods.INITCOMMONCONTROLSEX>(),
      dwICC = classes,
    };

    if (NativeMethods.InitCommonControlsEx(ref request))
      _registeredCommonControls |= classes;
  }

  /// <summary>The <c>ICC_*</c> blocks already registered by <see cref="EnsureCommonControls"/>.</summary>
  private static uint _registeredCommonControls;

  /// <summary>
  /// Whether a window class can actually be instantiated in this process. The stock classes always can;
  /// the common controls depend on which ComCtl32 the process resolved, so a promotion that rests on one
  /// asks first. A peer that answers here and then fails to create its window would leave the control
  /// invisible — the core has already taken the native path by then, and the painter is no longer in play.
  /// </summary>
  /// <param name="className">The window class to look for.</param>
  private static bool ClassExists(string className) => NativeMethods.GetClassInfoExW(0, className, out _);

  /// <inheritdoc/>
  public ICanvasPeer CreateCanvas() => new Win32CanvasPeer();

  /// <inheritdoc/>
  public IPopupPeer CreatePopup(IWindowPeer? owner) => new Win32PopupPeer(owner is WindowPeer window ? window.Handle : 0);

  /// <inheritdoc/>
  public IImage CreateImage(int width, int height, ReadOnlySpan<int> argb)
      => new Win32Image(width, height, argb);

  /// <inheritdoc/>
  public Color SampleScreenPixel(Point screen) {
    var hdc = NativeMethods.GetDC(0);
    if (hdc == 0)
      return Color.Empty;

    try {
      var bgr = NativeMethods.GetPixel(hdc, screen.X, screen.Y);
      if (bgr == 0xFFFFFFFF) // CLR_INVALID — the point is off every monitor
        return Color.Empty;

      return Color.FromArgb(255, (int)(bgr & 0xFF), (int)((bgr >> 8) & 0xFF), (int)((bgr >> 16) & 0xFF));
    } finally {
      NativeMethods.ReleaseDC(0, hdc);
    }
  }

  /// <inheritdoc/>
  public ITimerPeer CreateTimer() => new Win32TimerPeer();

  /// <inheritdoc/>
  public INotifyIconPeer CreateNotifyIcon() => new Win32NotifyIconPeer();

  /// <inheritdoc/>
  public Size GetScreenSize()
      => new(
          NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
          NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));

  /// <inheritdoc/>
  public Size MeasureText(string text, Font font) {
    var hdc = NativeMethods.GetDC(0);
    if (hdc == 0)
      return Size.Empty;

    try {
      var dpi = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);
      return Win32Graphics.MeasureText(hdc, text, font, dpi > 0 ? dpi : 96);
    } finally {
      NativeMethods.ReleaseDC(0, hdc);
    }
  }

  /// <inheritdoc/>
  public void SetClipboardText(string text) {
    ArgumentNullException.ThrowIfNull(text);
    if (NativeMethods.OpenClipboard(0) == 0)
      return;

    try {
      NativeMethods.EmptyClipboard();

      // CF_UNICODETEXT is a zero-terminated UTF-16 string in a movable global block; on a
      // successful SetClipboardData the system takes ownership of the handle.
      var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)((text.Length + 1) * sizeof(char)));
      if (handle == 0)
        return;

      var target = NativeMethods.GlobalLock(handle);
      if (target == 0) {
        NativeMethods.GlobalFree(handle);
        return;
      }

      unsafe {
        var destination = new Span<char>((void*)target, text.Length + 1);
        text.AsSpan().CopyTo(destination);
        destination[text.Length] = '\0';
      }

      NativeMethods.GlobalUnlock(handle);
      if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, handle) == 0)
        NativeMethods.GlobalFree(handle);
    } finally {
      NativeMethods.CloseClipboard();
    }
  }

  /// <inheritdoc/>
  public string? GetClipboardText() {
    if (NativeMethods.OpenClipboard(0) == 0)
      return null;

    try {
      // CF_UNICODETEXT arrives as a zero-terminated UTF-16 string in a global block the
      // clipboard keeps owning — lock, copy into a managed string, unlock, never free.
      var handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
      if (handle == 0)
        return null;

      var source = NativeMethods.GlobalLock(handle);
      if (source == 0)
        return null;

      try {
        unsafe {
          return new string((char*)source);
        }
      } finally {
        NativeMethods.GlobalUnlock(handle);
      }
    } finally {
      NativeMethods.CloseClipboard();
    }
  }

  /// <inheritdoc/>
  public void Post(Action action) {
    ArgumentNullException.ThrowIfNull(action);
    Win32Dispatcher.Post(action);
  }

  /// <inheritdoc/>
  public void Run(IWindowPeer mainWindow) {
    ArgumentNullException.ThrowIfNull(mainWindow);

    // The dispatcher's message-only window must live on the loop thread; creating it here also
    // drains anything posted before the loop started.
    Win32Dispatcher.EnsureCreated();

    while (true) {
      var result = NativeMethods.GetMessageW(out var msg, 0, 0, 0);

      // 0 => WM_QUIT (normal exit); -1 => error. Either way, stop pumping.
      if (result is 0 or -1)
        break;

      NativeMethods.TranslateMessage(in msg);
      NativeMethods.DispatchMessageW(in msg);
    }
  }

  /// <inheritdoc/>
  public void Quit() => NativeMethods.PostQuitMessage(0);
}
