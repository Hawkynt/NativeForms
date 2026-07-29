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

var shooting = Array.IndexOf(args, "--shoot") >= 0;
var shootFailures = 0;
var shootShots = 0;

// Opened before the gallery is built, not after. A WinExe has no console to complain to and nobody
// watches one on a runner, so everything the shoot has to say goes into the artifact — and the run
// that most needs explaining is the one that dies during construction, which is exactly what a
// platform whose backend is still a placeholder does. Setting the log up afterwards means that run
// produces nothing at all, which is what the first macOS probe did.
var shootDirectory = string.Empty;
var shootLog = string.Empty;
void Note(string line)
{
    Console.WriteLine(line);
    if (shootLog.Length > 0)
        File.AppendAllText(shootLog, line + Environment.NewLine);
}

if (shooting)
{
    var index = Array.IndexOf(args, "--shoot");
    shootDirectory = index + 1 < args.Length && !args[index + 1].StartsWith('-')
        ? args[index + 1]
        : Path.Combine(Path.GetTempPath(), "nativeforms-shots");

    Directory.CreateDirectory(shootDirectory);
    shootLog = Path.Combine(shootDirectory, "shoot.log");
    File.WriteAllText(shootLog, $"shoot on {Environment.OSVersion} / {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}{Environment.NewLine}");
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        File.AppendAllText(shootLog, $"unhandled: {e.ExceptionObject}{Environment.NewLine}");

    // Names the backend that will actually serve, and what it says about the display. On a platform
    // under construction this is the difference between "it failed" and "it failed here, with this
    // much working" — and a DPI scale that comes back sane is the messaging layer reporting for duty.
    var resolved = BackendRegistry.Resolve();
    Note($"backend: {resolved.Name}, dpi scale {resolved.GetDpiScale():0.##}, screen {resolved.GetScreenSize().Width}x{resolved.GetScreenSize().Height}");
    Note("backends registered, building the gallery");
}
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
if (measureStartup && !shooting)
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

// --shoot photographs the gallery and exits. It exists so "what does this actually look like on
// Windows" has an answer that does not depend on someone having a Windows desktop in front of them:
// the same switch runs on a CI runner with no session, under wine, and on a real machine, and each
// writes the same PNGs. The capture is in-process on both backends for the reason Autopilot.Capture
// gives — there is no screenshot tool to point at a headless runner.
// Pairing it with --measure-startup-minimal is how the Win32 capture gets exercised without a
// Windows desktop: the bare one-label window has none of the controls that make the gallery
// interesting, but it has a real HWND, which is all the capture route selection needs.
if (shooting)
{
    var directory = shootDirectory;

    // Every page, not just the one that happens to be in front. Switching pages through the tab
    // control's own property rather than by synthesizing clicks is what makes this work on every
    // backend: the autopilot's input injection is GDK-only, so a walkthrough built on it can never
    // photograph Windows.
    var pages = (form as MainForm)?.Tabs;
    var page = 0;

    form.Load += (_, _) =>
    {
        // One tick of the real loop first, so the shot is of a window the platform has finished
        // mapping and painting rather than one caught mid-realization.
        Note("load fired, arming the shutter");
        var shutter = new Hawkynt.NativeForms.Timer { Interval = 400 };
        shutter.Tick += (_, _) =>
        {
            var name = pages is null
                ? "gallery"
                : $"{page:00}-{new string([.. pages.TabPages[page].Text.Where(char.IsLetterOrDigit)]).ToLowerInvariant()}";

            var path = Path.Combine(directory, name + ".png");
            try
            {
                var size = Shoot.Window(form, path);
                if (size is not null)
                    ++shootShots;

                Note(size is { } written
                    ? $"shot: {name} ({written.Width}x{written.Height})"
                    : $"shot failed: {name} - no capture route produced pixels");
            }
            catch (Exception e)
            {
                Note($"shot threw: {e}");
            }

            // A shot proves the page renders; these prove it responds. On Windows nothing else can:
            // the autopilot's input is gdk_test_simulate_*, so its whole walkthrough is GTK-only.
            if (pages is not null)
                try
                {
                    var failures = Shoot.Check(pages.TabPages[page], form.ClientSize, Note)
                        + Shoot.CheckInput(pages.TabPages[page], form.Text, Note);
                    shootFailures += failures;
                    if (failures > 0)
                        Note($"  {name}: {failures} check(s) failed");
                }
                catch (Exception e)
                {
                    Note($"  {name}: checks threw: {e.Message}");
                    ++shootFailures;
                }

            if (pages is not null && ++page < pages.TabPages.Count)
            {
                pages.SelectedIndex = page;
                return; // the next tick photographs the page this one just selected
            }

            shutter.Stop();
            // Say what was actually exercised, not just that nothing complained: a run that injected
            // nothing and reported a pass would be the same lie as a blank screenshot reporting success.
            var injected = OperatingSystem.IsWindows()
                ? $", {Shoot.Clicks} real click(s) and {Shoot.Keystrokes} real keystroke(s) delivered through the OS input queue"
                : string.Empty;

            Note(shootFailures == 0
                ? $"shoot: {page} page(s), every check passed{injected}"
                : $"shoot: {shootFailures} check(s) failed across {page} page(s){injected}");

            form.Close();
        };

        shutter.Start();
    };

    Note("gallery constructed, waiting for the window");
}

Application.Run(form);

// Says whether the loop ran at all. A backend under construction can return from Run before the
// shutter ever ticks, and the difference between "the shot failed" and "nothing ever asked for one"
// is invisible without this line.
if (shooting)
    Note($"loop returned, {shootShots} shot(s) taken");

return shootFailures > 0 ? 1 : Autopilot.ExitCode;
