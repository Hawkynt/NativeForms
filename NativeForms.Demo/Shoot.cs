using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Photographs a window with whichever in-process route the running backend offers — GTK/Cairo on
/// Linux, GDI on Win32 — so <c>--shoot</c> means the same thing everywhere.
/// </summary>
internal static partial class Shoot
{
    /// <summary>Captures a form to a PNG, returning the size written or <see langword="null"/>.</summary>
    public static Size? Window(Form form, string path)
    {
        if (OperatingSystem.IsWindows())
            return ShootWindows.Window(form.Text, path);

        // No Cocoa capture yet, because there is no Cocoa backend yet to capture (PRD §2). Saying so
        // is the point: the macOS job runs the same walkthrough and its log records exactly how far the
        // toolkit gets on that platform, which is the baseline the backend will be built against.
        if (OperatingSystem.IsMacOS())
            return null;

        // GTK finds the window by title, exactly as the autopilot does, so the capture works whether
        // or not the autopilot is driving.
        var handle = Injection.MainWindow(form.Text);
        return handle == 0 ? null : Capture.Toplevels(handle, path);
    }
}
