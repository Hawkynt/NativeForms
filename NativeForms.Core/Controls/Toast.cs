using System.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Shows a transient in-app notification — a small <see cref="InfoBar"/> anchored to the bottom-right of a
/// form that auto-dismisses after a delay (or when its × is clicked). The in-window counterpart of the OS
/// tray balloon, for "Saved", "Update available" and the like.
/// </summary>
public static class Toast
{
    /// <summary>Pops a toast on <paramref name="form"/> with the given text and severity; it removes itself
    /// after <paramref name="durationMs"/> milliseconds or when dismissed.</summary>
    public static void Show(Form form, string title, string message, InfoBarSeverity severity = InfoBarSeverity.Info, int durationMs = 3000)
    {
        ArgumentNullException.ThrowIfNull(form);

        var width = Math.Min(360, Math.Max(160, form.ClientSize.Width - 24));
        var bar = new InfoBar
        {
            Title = title,
            Message = message,
            Severity = severity,
            Bounds = new Rectangle(form.ClientSize.Width - width - 12, form.ClientSize.Height - 48, width, 36),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };

        var timer = new Timer { Interval = Math.Max(1, durationMs) };

        void Dismiss()
        {
            timer.Stop();
            timer.Dispose();
            if (bar.Parent is { } parent)
                parent.Controls.Remove(bar);
        }

        timer.Tick += (_, _) => Dismiss();
        bar.Closed += (_, _) => Dismiss();

        form.Controls.Add(bar);
        timer.Start();
    }
}
