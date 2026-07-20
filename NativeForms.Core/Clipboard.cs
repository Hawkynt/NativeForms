using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms;

/// <summary>
/// The system clipboard, reduced to the plain-text surface every platform offers — the moral
/// equivalent of <c>System.Windows.Forms.Clipboard</c>. Calls go through the running application's
/// backend (<c>OpenClipboard</c>/<c>SetClipboardData</c> on Win32, <c>GtkClipboard</c> on GTK), so a
/// message loop must be active; non-text formats are not part of the contract.
/// </summary>
public static class Clipboard
{
    /// <summary>Places <paramref name="text"/> on the clipboard, replacing its current content.</summary>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty — matching the
    /// Windows Forms contract, which refuses to clear the clipboard through <c>SetText</c>.</exception>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public static void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Backend.SetClipboardText(text);
    }

    /// <summary>The clipboard's plain-text content, or the empty string while it holds no text.</summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public static string GetText() => Backend.GetClipboardText() ?? string.Empty;

    /// <summary>Whether the clipboard currently holds text.</summary>
    /// <exception cref="InvalidOperationException">No application message loop is running.</exception>
    public static bool ContainsText() => Backend.GetClipboardText() is { Length: > 0 };

    /// <summary>The running application's backend — the clipboard has no life of its own.</summary>
    private static IPlatformBackend Backend
        => Application.Current ?? throw new InvalidOperationException(
            "The clipboard needs a running message loop — use it while Application.Run is active.");
}
