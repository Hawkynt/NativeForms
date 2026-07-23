using System.Diagnostics;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.MacOS;
using Hawkynt.NativeForms.Backends.Windows;
using Hawkynt.NativeForms.Demo;

// Started before any work so --measure-startup reports the whole cold path: backend registration,
// the gallery's construction, its realization and the first window being shown.
var startup = Stopwatch.StartNew();
var minimalStartup = Array.IndexOf(args, "--measure-startup-minimal") >= 0;
var measureStartup = minimalStartup || Array.IndexOf(args, "--measure-startup") >= 0;

// --autopilot drives the whole gallery with synthesized input and reports what misbehaves; it must
// be switched on before the window is built, because that is when the gallery publishes the control
// references the walkthrough drives. Without the switch the demo behaves exactly as it always has.
if (Array.IndexOf(args, "--autopilot") >= 0)
    Autopilot.Enable();

// Register the backends this build ships. Referencing the concrete types keeps the linker from
// trimming them; only the backend whose IsSupported matches the current OS is ever realized.
// Ship just one to shrink a single-platform build.
BackendRegistry.Register(new Win32Backend());
BackendRegistry.Register(new GtkBackend());
BackendRegistry.Register(new CocoaBackend());
if (measureStartup)
    Console.WriteLine($"  phase backends-registered: {startup.Elapsed.TotalMilliseconds:F1} ms");

// --measure-startup-minimal isolates the toolkit's cold floor from the gallery's construction cost:
// a bare one-label window instead of the whole tabbed showcase.
Form form = minimalStartup
    ? new Form { Text = "NativeForms", Bounds = new(0, 0, 320, 160), Controls = { new Label { Bounds = new(20, 20, 260, 24), Text = "Hello, NativeForms." } } }
    : new MainForm();
if (!minimalStartup)
    Autopilot.Attach((MainForm)form);
if (measureStartup)
    Console.WriteLine($"  phase constructed: {startup.Elapsed.TotalMilliseconds:F1} ms");

// --measure-startup stops the clock when the form loads (its whole peer tree realized, the window
// up) and reports the cold time to first window, then closes on the first tick — once the message
// loop is actually running, so the shutdown is clean — without driving the gallery.
if (measureStartup)
    form.Load += (_, _) =>
    {
        startup.Stop();
        Console.WriteLine($"startup-to-loaded: {startup.Elapsed.TotalMilliseconds:F1} ms");
        var closer = new Hawkynt.NativeForms.Timer { Interval = 1 };
        closer.Tick += (_, _) =>
        {
            closer.Stop();
            form.Close();
        };
        closer.Start();
    };

Application.Run(form);
return Autopilot.ExitCode;
