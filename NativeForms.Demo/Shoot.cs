using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Photographs a window with whichever in-process route the running backend offers — GTK/Cairo on
/// Linux, GDI on Win32 — so <c>--shoot</c> means the same thing everywhere.
/// </summary>
internal static partial class Shoot
{
    /// <summary>
    /// Why the last capture produced nothing, where the platform route can say. A shoot runs where
    /// nobody is watching, so a failure that names only itself costs a whole round trip to diagnose.
    /// </summary>
    public static string? Diagnosis => OperatingSystem.IsMacOS() ? ShootMacOS.Diagnosis : null;

    /// <summary>
    /// What the platform will say about its hover wiring, where it can say anything at all.
    /// </summary>
    /// <remarks>
    /// Only macOS answers, and only because it has to: hover there needs two separate things turned
    /// on and neither of them shows up in a screenshot, while the injected pointer this job would
    /// otherwise prove it with is dropped by the window server for want of an Accessibility grant.
    /// </remarks>
    public static string? HoverWiring => OperatingSystem.IsMacOS() ? ShootMacOS.HoverWiring() : null;

    /// <summary>Captures a form to a PNG, returning the size written or <see langword="null"/>.</summary>
    public static Size? Window(Form form, string path)
    {
        if (OperatingSystem.IsWindows())
            return ShootWindows.Window(form.Text, path);

        if (OperatingSystem.IsMacOS())
            return ShootMacOS.Window(path);

        // GTK finds the window by title, exactly as the autopilot does, so the capture works whether
        // or not the autopilot is driving.
        var handle = Injection.MainWindow(form.Text);
        return handle == 0 ? null : Capture.Toplevels(handle, path);
    }
}
