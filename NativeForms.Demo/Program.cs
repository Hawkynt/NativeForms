using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.MacOS;
using Hawkynt.NativeForms.Backends.Windows;
using Hawkynt.NativeForms.Demo;

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

var form = new MainForm();
Autopilot.Attach(form);
Application.Run(form);
return Autopilot.ExitCode;
