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

    /// <summary>
    /// Which native classes the window is really made of, where the platform can be asked.
    /// </summary>
    /// <remarks>
    /// Read at the end of the walkthrough rather than at the start: a tab page realizes its children
    /// only once it has been shown, so a count taken off the first page describes one sixteenth of the
    /// gallery and would drift with whatever else happened to have realized by then.
    /// </remarks>
    public static string? NativeWidgets => OperatingSystem.IsMacOS() ? ShootMacOS.NativeWidgets() : null;

    /// <summary>
    /// Whether the rich text box's link activation reaches the toolkit, where the platform can be asked.
    /// </summary>
    /// <remarks>Wiring, not delivery — for the reason <see cref="HoverWiring"/> gives.</remarks>
    public static string? LinkWiring => OperatingSystem.IsMacOS() ? ShootMacOS.LinkWiring() : null;

    /// <summary>
    /// What the tray icon reports about itself, where the platform keeps one somewhere askable.
    /// </summary>
    /// <remarks>
    /// It is nowhere in the gallery's window — the item lives in the menu bar, in a window of its own —
    /// so nothing about it shows in a screenshot and only the platform can be asked.
    /// </remarks>
    public static string? StatusItem => OperatingSystem.IsMacOS() ? ShootMacOS.StatusItem() : null;

    /// <summary>
    /// Whether the objects the colour and font choosers are built on resolve, where they are shared
    /// panels rather than dialogs.
    /// </summary>
    /// <remarks>
    /// Deliberately short of running them: a modeless panel ends when someone closes it, and a
    /// walkthrough has nobody to do that. What this rules out is the silent failure — a chooser whose
    /// class never resolved answers null forever, which reads exactly like a user always cancelling.
    /// </remarks>
    public static string? Choosers => OperatingSystem.IsMacOS() ? ShootMacOS.Choosers() : null;

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
