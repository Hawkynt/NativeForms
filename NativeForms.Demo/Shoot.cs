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
    /// What the injected-input route says about itself, where the route had to be built by hand.
    /// </summary>
    /// <remarks>
    /// Only macOS answers. Its events are constructed argument by argument through a ten-argument
    /// Objective-C selector, and a wrong signature there does not fail — it reads the wrong registers
    /// and hands back something that looks like an event. So the constructors are asked to repeat
    /// their own arguments back before any check depends on them, and the answer goes in the log
    /// beside the counts it underwrites.
    /// </remarks>
    public static string? InputRoute => OperatingSystem.IsMacOS() ? ShootInputMac.Route() : null;

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
    /// What the window is holding of the chrome the form asked for, where the platform can be asked.
    /// </summary>
    /// <remarks>
    /// A round trip rather than wiring, and the only one on this list that is: the gallery sets a
    /// minimum size, so the number read back off the live window either is the one the form gave or
    /// the property did nothing. The caption buttons ride along because they are read the same way and
    /// are equally invisible in a capture of the content view.
    /// </remarks>
    public static string? WindowChrome => OperatingSystem.IsMacOS() ? ShootMacOS.WindowChrome() : null;

    /// <summary>
    /// Whether the plain native widgets report the user's work to the toolkit, where it can be asked.
    /// </summary>
    /// <remarks>
    /// Read at the end of the walkthrough for the same reason the class census is: a widget on a tab
    /// page does not exist until the page has been shown, so a count taken earlier describes one
    /// sixteenth of the gallery.
    /// </remarks>
    public static string? NativeInput => OperatingSystem.IsMacOS() ? ShootMacOS.NativeInput() : null;

    /// <summary>
    /// Which bezel the gallery's push buttons carry, where the platform can be asked.
    /// </summary>
    /// <remarks>
    /// Geometry rather than wiring, and read at the end of the walk for the reason every other line
    /// here is: a button on a tab page has no widget behind it until the page has been shown.
    /// </remarks>
    public static string? ButtonBezels => OperatingSystem.IsMacOS() ? ShootMacOS.ButtonBezels() : null;

    /// <summary>
    /// Whether the rich text box's link activation reaches the toolkit, where the platform can be asked.
    /// </summary>
    /// <remarks>Wiring, not delivery — for the reason <see cref="HoverWiring"/> gives.</remarks>
    public static string? LinkWiring => OperatingSystem.IsMacOS() ? ShootMacOS.LinkWiring() : null;

    /// <summary>
    /// Whether the two routes a pointer shape can take on macOS are in place, where they can be asked.
    /// </summary>
    /// <remarks>
    /// A cursor is the one property on this list nothing anywhere can photograph: a screen capture is
    /// of the window's own drawing and the pointer is the window server's, drawn over the top. So this
    /// is wiring by definition rather than by the limits of this job.
    /// </remarks>
    public static string? CursorWiring => OperatingSystem.IsMacOS() ? ShootMacOS.CursorWiring() : null;

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

    /// <summary>
    /// Whether the toolkit follows the desktop into the other appearance, posed by switching the
    /// application into dark aqua and back.
    /// </summary>
    /// <remarks>
    /// The only check that changes something about the desktop rather than reading it, so it runs after
    /// the last shot and undoes itself. Both other backends have followed a live theme change since
    /// they were written; this is the line that says whether this one does.
    /// </remarks>
    public static string? Appearance => OperatingSystem.IsMacOS() ? ShootMacOS.AppearanceSwitch() : null;

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
