using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The press that light-dismisses a popup must not be swallowed: it has to reach the widget it was
/// aimed at, so one right-click on a second context-menu target closes the first menu and opens the
/// second in a single gesture. Before the replay fix the dismissing press ended at the popup, and the
/// second menu only appeared on a second click.
///
/// Like the lifetime fixture, this drives real GTK: the menus open through their public API, the
/// dismissing right-click is a genuine <c>GdkEventButton</c> aimed at the second control's own canvas
/// window and dispatched through <c>gtk_main_do_event</c>, and the assertions read the two menus'
/// open state afterwards. Without a display the fixture reports itself as ignored.
/// </summary>
[TestFixture]
public sealed partial class GtkPopupReplayTests {
  private const int _GdkButtonPress = 4;

  private static Observations? _observed;
  private static string? _skipReason;

  /// <summary>What the run on the GTK loop saw; the tests only assert against it.</summary>
  private sealed class Observations {
    public bool FirstMenuOpened;
    public bool FirstMenuClosedByPress;
    public bool SecondMenuOpenedBySamePress;
    public string? Failure;
  }

  [OneTimeSetUp]
  public void RunTheFormOnce() {
    if (!OperatingSystem.IsLinux()) {
      _skipReason = "GTK is only exercised on Linux.";
      return;
    }

    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) {
      _skipReason = "No DISPLAY: these assertions need a real GTK display.";
      return;
    }

    BackendRegistry.Register(new GtkBackend());
    var observed = new Observations();

    var form = new Form { Text = "popup replay", Width = 520, Height = 420 };
    // Owner-drawn so the right-click travels the canvas mouse pipeline into the context-menu
    // opener — the same path a real user's right-click takes.
    var first = new Button { Bounds = new Rectangle(20, 60, 160, 32), UseNativeWidget = false, Text = "first" };
    var second = new Button { Bounds = new Rectangle(20, 220, 160, 32), UseNativeWidget = false, Text = "second" };
    var firstMenu = new ContextMenuStrip();
    firstMenu.Items.Add(new ToolStripMenuItem { Text = "Alpha" });
    var secondMenu = new ContextMenuStrip();
    secondMenu.Items.Add(new ToolStripMenuItem { Text = "Beta" });
    first.ContextMenuStrip = firstMenu;
    second.ContextMenuStrip = secondMenu;
    form.Controls.Add(first);
    form.Controls.Add(second);

    var timer = new Timer { Interval = 400 };
    timer.Tick += (_, _) => {
      timer.Stop();
      try {
        Observe(observed, first, second, firstMenu, secondMenu);
      } catch (Exception exception) {
        observed.Failure = exception.ToString();
      } finally {
        Application.Exit();
      }
    };
    timer.Start();

    var watchdog = new Timer { Interval = 20_000 };
    watchdog.Tick += (_, _) => {
      watchdog.Stop();
      observed.Failure ??= "The GTK loop never reached the observation tick.";
      Application.Exit();
    };
    watchdog.Start();

    Application.Run(form);
    watchdog.Stop();
    firstMenu.Dispose();
    secondMenu.Dispose();
    _observed = observed;
  }

  /// <summary>Runs on the GTK loop with the form mapped: opens the first menu, then one press out.</summary>
  private static void Observe(
      Observations observed,
      Button first,
      Button second,
      ContextMenuStrip firstMenu,
      ContextMenuStrip secondMenu) {
    Pump();
    var top = FindToplevel("GtkWindow");
    Assert.That(top, Is.Not.Zero, "no GtkWindow toplevel");
    gtk_test_widget_wait_for_draw(top);
    Pump();

    firstMenu.Show(first, new Point(10, 10));
    Settle();
    observed.FirstMenuOpened = firstMenu.IsOpen;

    var wasOpen = firstMenu.IsOpen;
    RightClickOn(second);
    Settle();

    observed.FirstMenuClosedByPress = wasOpen && !firstMenu.IsOpen;
    observed.SecondMenuOpenedBySamePress = secondMenu.IsOpen;
    secondMenu.Close();
    Settle();
  }

  /// <summary>
  /// Dispatches a genuine right press aimed at the control's own canvas window — the window a real
  /// press would land on — with coordinates from the widget's allocation within that window.
  /// </summary>
  private static void RightClickOn(Control target) {
    var widget = ((GtkControlPeer)target.Peer!).WidgetHandle;
    var window = gtk_widget_get_window(widget);
    gtk_widget_get_allocation(widget, out var allocation);
    var x = allocation.X + allocation.Width / 2;
    var y = allocation.Y + allocation.Height / 2;

    var e = gdk_event_new(_GdkButtonPress);
    unsafe {
      ref var button = ref *(GdkButtonEvent*)e;
      button.Window = g_object_ref(window);
      button.SendEvent = 1;
      button.Time = 9000;
      button.X = x;
      button.Y = y;
      button.Button = 3;
      button.Device = gdk_seat_get_pointer(gdk_display_get_default_seat(gdk_display_get_default()));
      gdk_window_get_origin(window, out var rootX, out var rootY);
      button.XRoot = rootX + x;
      button.YRoot = rootY + y;
    }

    gtk_main_do_event(e);
    gdk_event_free(e);
  }

  private static Observations Result() {
    if (_skipReason is { } reason)
      Assert.Ignore(reason);

    Assert.That(_observed, Is.Not.Null, "the GTK loop produced no observations");
    Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
    return _observed;
  }

  // --- One press closes the old menu and opens the new one -------------------------------------

  [Test]
  public void First_context_menu_opens()
      => Assert.That(Result().FirstMenuOpened, Is.True);

  [Test]
  public void Right_click_elsewhere_closes_the_first_menu()
      => Assert.That(Result().FirstMenuClosedByPress, Is.True);

  [Test]
  public void The_same_right_click_opens_the_second_menu()
      => Assert.That(
          Result().SecondMenuOpenedBySamePress,
          Is.True,
          "the dismissing press was swallowed; the second menu needs a second click");

  // --- GTK plumbing the fixture needs -----------------------------------------------------------

  private static void Pump() {
    for (var i = 0; i < 400 && gtk_events_pending() != 0; ++i)
      gtk_main_iteration_do(0);
  }

  /// <summary>Turns the loop long enough for anything deferred to an idle or a timeout to run.</summary>
  private static void Settle() {
    for (var round = 0; round < 20; ++round) {
      Pump();
      g_usleep(5000);
    }

    Pump();
  }

  private static nint FindToplevel(string typeName) {
    var toplevels = gtk_window_list_toplevels();
    var count = g_list_length(toplevels);
    var found = (nint)0;
    for (var i = 0u; i < count; ++i) {
      var candidate = g_list_nth_data(toplevels, i);
      if (Marshal.PtrToStringUTF8(gtk_widget_get_name(candidate)) == typeName)
        found = candidate;
    }

    g_list_free(toplevels);
    return found;
  }

  /// <summary>The <c>GdkRectangle</c> a widget's allocation arrives in.</summary>
  [StructLayout(LayoutKind.Sequential)]
  private struct GdkRectangle {
    public int X;
    public int Y;
    public int Width;
    public int Height;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct GdkButtonEvent {
    public int Type;
    public nint Window;
    public sbyte SendEvent;
    public uint Time;
    public double X;
    public double Y;
    public nint Axes;
    public uint State;
    public uint Button;
    public nint Device;
    public double XRoot;
    public double YRoot;
  }

  private const string Gtk = "libgtk-3.so.0";
  private const string Gdk = "libgdk-3.so.0";
  private const string GLib = "libglib-2.0.so.0";
  private const string GObject = "libgobject-2.0.so.0";

  [LibraryImport(Gtk)] private static partial nint gtk_widget_get_window(nint widget);
  [LibraryImport(Gtk)] private static partial void gtk_widget_get_allocation(nint widget, out GdkRectangle allocation);
  [LibraryImport(Gtk)] private static partial nint gtk_widget_get_name(nint widget);
  [LibraryImport(Gtk)] private static partial nint gtk_window_list_toplevels();
  [LibraryImport(Gtk)] private static partial int gtk_test_widget_wait_for_draw(nint widget);
  [LibraryImport(Gtk)] private static partial int gtk_events_pending();
  [LibraryImport(Gtk)] private static partial int gtk_main_iteration_do(int blocking);
  [LibraryImport(Gtk)] private static partial void gtk_main_do_event(nint @event);

  [LibraryImport(Gdk)] private static partial nint gdk_event_new(int type);
  [LibraryImport(Gdk)] private static partial void gdk_event_free(nint @event);
  [LibraryImport(Gdk)] private static partial void gdk_window_get_origin(nint window, out int x, out int y);
  [LibraryImport(Gdk)] private static partial nint gdk_display_get_default();
  [LibraryImport(Gdk)] private static partial nint gdk_display_get_default_seat(nint display);
  [LibraryImport(Gdk)] private static partial nint gdk_seat_get_pointer(nint seat);

  [LibraryImport(GLib)] private static partial uint g_list_length(nint list);
  [LibraryImport(GLib)] private static partial nint g_list_nth_data(nint list, uint n);
  [LibraryImport(GLib)] private static partial void g_list_free(nint list);
  [LibraryImport(GLib)] private static partial void g_usleep(nuint microseconds);
  [LibraryImport(GObject)] private static partial nint g_object_ref(nint instance);
}
