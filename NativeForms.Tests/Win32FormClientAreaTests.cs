using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends.Windows;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A form's size is its client area on Win32, as it already is on GTK: the window peer grows the native
/// window by the non-client frame — caption and borders, measured at the window's own DPI — so content
/// laid out to <see cref="Form.ClientSize"/> is not cut off at the bottom and the right.
/// </summary>
/// <remarks>
/// The arithmetic is plain and asserted headlessly; the measurement and the round trip through a real
/// window need <c>user32</c> and report themselves as ignored elsewhere.
/// </remarks>
[TestFixture]
public sealed partial class Win32FormClientAreaTests {
  /// <summary>Switches the calling thread's DPI awareness; returns the previous context.</summary>
  [LibraryImport("user32.dll")]
  private static partial nint SetThreadDpiAwarenessContext(nint context);

  /// <summary>A window created under this context is told 96 DPI whatever the display's scale.</summary>
  private const nint DpiUnaware = -1;

  [Test]
  public void Insets_from_an_adjusted_empty_rectangle_are_the_frame_per_edge() {
    var insets = Win32FrameInsets.FromAdjustedEmpty(new NativeMethods.RECT { left = -8, top = -31, right = 8, bottom = 8 });

    Assert.That(insets, Is.EqualTo(new Win32FrameInsets(8, 31, 8, 8)));
  }

  [Test]
  public void The_outer_size_adds_both_edges_of_the_frame() {
    var insets = new Win32FrameInsets(11, 45, 11, 11);

    Assert.That(insets.ToOuter(new Size(400, 300)), Is.EqualTo(new Size(422, 356)));
  }

  [Test]
  public void The_client_size_removes_them_again() {
    var insets = new Win32FrameInsets(11, 45, 11, 11);

    Assert.That(insets.ToClient(new Size(422, 356)), Is.EqualTo(new Size(400, 300)));
  }

  [Test]
  public void A_window_smaller_than_its_frame_has_an_empty_client_not_a_negative_one() {
    var insets = new Win32FrameInsets(8, 31, 8, 8);

    Assert.That(insets.ToClient(new Size(10, 20)), Is.EqualTo(Size.Empty));
  }

  [Test]
  public void A_frameless_window_is_its_own_client() {
    var insets = default(Win32FrameInsets);

    Assert.Multiple(() => {
      Assert.That(insets.ToOuter(new Size(400, 300)), Is.EqualTo(new Size(400, 300)));
      Assert.That(insets.ToClient(new Size(400, 300)), Is.EqualTo(new Size(400, 300)));
    });
  }

  [Test]
  public void A_zero_limit_stays_unset_rather_than_becoming_the_frame() {
    var insets = new Win32FrameInsets(8, 31, 8, 8);

    Assert.Multiple(() => {
      Assert.That(insets.ToOuterLimit(Size.Empty), Is.EqualTo(Size.Empty));
      Assert.That(insets.ToOuterLimit(new Size(0, 200)), Is.EqualTo(new Size(0, 239)));
      Assert.That(insets.ToOuterLimit(new Size(300, 0)), Is.EqualTo(new Size(316, 0)));
    });
  }

  [Test]
  public void The_frame_is_measured_at_the_dpi_it_is_asked_for() {
    RequireWindows();
    const uint style = NativeMethods.WS_OVERLAPPEDWINDOW;

    var at100 = Win32FrameInsets.Measure(style, 0, 96);
    var at150 = Win32FrameInsets.Measure(style, 0, 144);

    // The defect: a frame measured at 96 DPI on a 150% display undercounts the caption by about a third
    // of a title bar — which is the strip that went missing at the bottom of every form.
    Assert.Multiple(() => {
      Assert.That(at100.Top, Is.GreaterThan(0));
      Assert.That(at150.Top, Is.GreaterThan(at100.Top), "the caption scales with the DPI");
      Assert.That(at150.Bottom, Is.GreaterThanOrEqualTo(at100.Bottom), "so do the sizing borders");
    });
  }

  [Test]
  public void A_frameless_style_measures_no_frame() {
    RequireWindows();

    Assert.That(Win32FrameInsets.Measure(NativeMethods.WS_POPUP, 0, 144), Is.EqualTo(default(Win32FrameInsets)));
  }

  /// <summary>What the real window reported while the loop ran.</summary>
  private sealed record Observed(
      Size NativeClient,
      Size FormSize,
      Rectangle StripBounds,
      Size ClientAfterBorderChange,
      Size FormAfterNativeResize,
      Size NativeClientAfterNativeResize,
      uint Dpi);

  private static Observed RunRealForm(bool at96Dpi = false) {
    var form = new Form { Text = "client area", Size = new Size(400, 300) };
    var strip = new Panel { Dock = DockStyle.Bottom, Height = 22 };
    form.Controls.Add(strip);

    Observed? observed = null;
    string? failure = null;
    var timer = new Timer { Interval = 150 };
    timer.Tick += (_, _) => {
      timer.Stop();
      try {
        var handle = ((WindowPeer)form.Peer!).Handle;
        NativeMethods.GetClientRect(handle, out var client);
        var nativeClient = new Size(client.right - client.left, client.bottom - client.top);
        var formSize = form.Size;
        var stripBounds = strip.Bounds;
        var dpi = NativeMethods.GetDpiForWindow(handle);

        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        NativeMethods.GetClientRect(handle, out client);
        var afterBorder = new Size(client.right - client.left, client.bottom - client.top);

        // A resize the toolkit did not ask for — the user dragging an edge, Windows applying a DPI
        // change — has to come back as the new client size, not as the outer rectangle.
        NativeMethods.GetWindowRect(handle, out var outer);
        NativeMethods.SetWindowPos(
            handle, 0, outer.left, outer.top, outer.right - outer.left + 40, outer.bottom - outer.top + 30,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.GetClientRect(handle, out client);

        observed = new(
            nativeClient, formSize, stripBounds, afterBorder, form.Size,
            new Size(client.right - client.left, client.bottom - client.top), dpi);
      } catch (Exception ex) {
        failure = ex.ToString();
      } finally {
        form.Close();
      }
    };
    timer.Start();
    var previous = at96Dpi ? SetThreadDpiAwarenessContext(DpiUnaware) : 0;
    try {
      Application.Run(form, new Win32Backend());
    } finally {
      if (previous != 0)
        SetThreadDpiAwarenessContext(previous);
    }

    Assert.That(failure, Is.Null);
    return observed!;
  }

  [Test]
  public void A_form_gets_the_client_area_it_asked_for() {
    RequireWindows();

    var observed = RunRealForm();

    TestContext.Out.WriteLine($"window DPI {observed.Dpi}, native client {observed.NativeClient}");
    Assert.Multiple(() => {
      Assert.That(observed.NativeClient, Is.EqualTo(new Size(400, 300)), "the native client area");
      Assert.That(observed.FormSize, Is.EqualTo(new Size(400, 300)), "and the form agrees");
      Assert.That(observed.StripBounds.Bottom, Is.LessThanOrEqualTo(observed.NativeClient.Height), "a bottom-docked strip is fully visible");
    });
  }

  [Test]
  public void A_form_at_96_dpi_gets_it_too() {
    RequireWindows();

    var observed = RunRealForm(at96Dpi: true);

    Assert.Multiple(() => {
      Assert.That(observed.Dpi, Is.EqualTo(96u), "the window really was measured at 100%");
      Assert.That(observed.NativeClient, Is.EqualTo(new Size(400, 300)));
    });
  }

  [Test]
  public void Changing_the_frame_keeps_the_client_area() {
    RequireWindows();

    Assert.That(RunRealForm().ClientAfterBorderChange, Is.EqualTo(new Size(400, 300)));
  }

  [Test]
  public void A_native_resize_reports_the_client_size() {
    RequireWindows();

    var observed = RunRealForm();

    Assert.That(observed.FormAfterNativeResize, Is.EqualTo(observed.NativeClientAfterNativeResize));
  }

  private static void RequireWindows() {
    if (!OperatingSystem.IsWindows())
      Assert.Ignore("Measures a real Win32 frame.");
  }
}
