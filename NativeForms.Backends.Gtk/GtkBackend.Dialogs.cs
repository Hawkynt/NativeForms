using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends.Gtk;

/// <summary>
/// The native common dialogs: <c>GtkMessageDialog</c>, <c>GtkFileChooserDialog</c> and the
/// color/font choosers. Each is driven by <c>gtk_dialog_run</c>, which nests its own modal main
/// loop and blocks until the user responds. Buttons are added individually with the matching
/// <see cref="DialogResult"/> value as the response id, so the answer maps back by cast.
/// </summary>
public sealed partial class GtkBackend
{
    /// <inheritdoc/>
    public DialogResult ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, IWindowPeer? owner = null)
    {
        EnsureInitialized();

        var type = icon switch
        {
            MessageBoxIcon.Information => NativeMethods.GTK_MESSAGE_INFO,
            MessageBoxIcon.Warning => NativeMethods.GTK_MESSAGE_WARNING,
            MessageBoxIcon.Question => NativeMethods.GTK_MESSAGE_QUESTION,
            MessageBoxIcon.Error => NativeMethods.GTK_MESSAGE_ERROR,
            _ => NativeMethods.GTK_MESSAGE_OTHER,
        };

        var dialog = NativeMethods.gtk_message_dialog_new(
            0, NativeMethods.GTK_DIALOG_MODAL, type, NativeMethods.GTK_BUTTONS_NONE, 0);
        try
        {
            if (owner is GtkControlPeer ownerPeer && ownerPeer.WidgetHandle != 0)
                NativeMethods.gtk_window_set_transient_for(dialog, ownerPeer.WidgetHandle);

            NativeMethods.g_object_set_string(dialog, "text", text, 0);
            NativeMethods.gtk_window_set_title(dialog, caption);

            // Affirmative button last, per the GNOME HIG; each response id is the DialogResult value.
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel:
                    AddButton(dialog, "_Cancel", DialogResult.Cancel);
                    AddButton(dialog, "_OK", DialogResult.OK);
                    break;
                case MessageBoxButtons.AbortRetryIgnore:
                    AddButton(dialog, "_Ignore", DialogResult.Ignore);
                    AddButton(dialog, "_Retry", DialogResult.Retry);
                    AddButton(dialog, "_Abort", DialogResult.Abort);
                    break;
                case MessageBoxButtons.YesNoCancel:
                    AddButton(dialog, "_Cancel", DialogResult.Cancel);
                    AddButton(dialog, "_No", DialogResult.No);
                    AddButton(dialog, "_Yes", DialogResult.Yes);
                    break;
                case MessageBoxButtons.YesNo:
                    AddButton(dialog, "_No", DialogResult.No);
                    AddButton(dialog, "_Yes", DialogResult.Yes);
                    break;
                case MessageBoxButtons.RetryCancel:
                    AddButton(dialog, "_Cancel", DialogResult.Cancel);
                    AddButton(dialog, "_Retry", DialogResult.Retry);
                    break;
                default:
                    AddButton(dialog, "_OK", DialogResult.OK);
                    break;
            }

            var response = NativeMethods.gtk_dialog_run(dialog);
            if (response > 0)
                return (DialogResult)response;

            // Closed via the window manager: OK-only reports OK (as Win32 does), Yes/No has no
            // escape hatch and reports No, Abort/Retry/Ignore reports the mild Ignore (Win32
            // disables the close box outright there), everything else means Cancel.
            return buttons switch
            {
                MessageBoxButtons.OK => DialogResult.OK,
                MessageBoxButtons.YesNo => DialogResult.No,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
                _ => DialogResult.Cancel,
            };
        }
        finally
        {
            NativeMethods.gtk_widget_destroy(dialog);
        }
    }

    /// <inheritdoc/>
    public string[]? ShowFileDialog(in FileDialogOptions options)
    {
        EnsureInitialized();

        var (action, acceptLabel) = options.Kind switch
        {
            FileDialogKind.Save => (NativeMethods.GTK_FILE_CHOOSER_ACTION_SAVE, "_Save"),
            FileDialogKind.SelectFolder => (NativeMethods.GTK_FILE_CHOOSER_ACTION_SELECT_FOLDER, "_Select"),
            _ => (NativeMethods.GTK_FILE_CHOOSER_ACTION_OPEN, "_Open"),
        };

        var dialog = NativeMethods.gtk_file_chooser_dialog_new(options.Title, 0, action, 0);
        try
        {
            NativeMethods.gtk_dialog_add_button(dialog, "_Cancel", NativeMethods.GTK_RESPONSE_CANCEL);
            NativeMethods.gtk_dialog_add_button(dialog, acceptLabel, NativeMethods.GTK_RESPONSE_ACCEPT);
            NativeMethods.gtk_dialog_set_default_response(dialog, NativeMethods.GTK_RESPONSE_ACCEPT);

            if (!string.IsNullOrEmpty(options.InitialDirectory))
                NativeMethods.gtk_file_chooser_set_current_folder(dialog, options.InitialDirectory);

            switch (options.Kind)
            {
                case FileDialogKind.Save:
                    NativeMethods.gtk_file_chooser_set_do_overwrite_confirmation(dialog, 1);
                    if (!string.IsNullOrEmpty(options.FileName))
                        NativeMethods.gtk_file_chooser_set_current_name(dialog, Path.GetFileName(options.FileName));
                    break;
                case FileDialogKind.Open when options.Multiselect:
                    NativeMethods.gtk_file_chooser_set_select_multiple(dialog, 1);
                    break;
            }

            AddFilters(dialog, options.Filters, options.FilterIndex);

            if (NativeMethods.gtk_dialog_run(dialog) != NativeMethods.GTK_RESPONSE_ACCEPT)
                return null;

            return options.Multiselect ? ReadFilenameList(dialog) : ReadSingleFilename(dialog);
        }
        finally
        {
            NativeMethods.gtk_widget_destroy(dialog);
        }
    }

    /// <inheritdoc/>
    public Color? ShowColorDialog(Color color)
    {
        EnsureInitialized();

        var dialog = NativeMethods.gtk_color_chooser_dialog_new("Select color", 0);
        try
        {
            var initial = new GdkRGBA
            {
                Red = color.R / 255.0,
                Green = color.G / 255.0,
                Blue = color.B / 255.0,
                Alpha = 1.0,
            };
            NativeMethods.gtk_color_chooser_set_rgba(dialog, in initial);

            if (NativeMethods.gtk_dialog_run(dialog) != NativeMethods.GTK_RESPONSE_OK)
                return null;

            NativeMethods.gtk_color_chooser_get_rgba(dialog, out var chosen);
            return Color.FromArgb(
                (int)Math.Round(chosen.Red * 255.0),
                (int)Math.Round(chosen.Green * 255.0),
                (int)Math.Round(chosen.Blue * 255.0));
        }
        finally
        {
            NativeMethods.gtk_widget_destroy(dialog);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Pango font descriptions carry no underline attribute, so the underline flag cannot round-trip
    /// through the GTK chooser and is dropped from the result.
    /// </remarks>
    public Font? ShowFontDialog(Font font)
    {
        EnsureInitialized();

        var dialog = NativeMethods.gtk_font_chooser_dialog_new("Select font", 0);
        try
        {
            NativeMethods.gtk_font_chooser_set_font(dialog, BuildPangoDescription(font));

            if (NativeMethods.gtk_dialog_run(dialog) != NativeMethods.GTK_RESPONSE_OK)
                return null;

            var raw = NativeMethods.gtk_font_chooser_get_font(dialog);
            if (raw == 0)
                return null;

            try
            {
                var description = Marshal.PtrToStringUTF8(raw);
                return string.IsNullOrEmpty(description) ? null : ParsePangoDescription(description, font);
            }
            finally
            {
                NativeMethods.g_free(raw);
            }
        }
        finally
        {
            NativeMethods.gtk_widget_destroy(dialog);
        }
    }

    /// <summary>Adds a dialog button whose response id is the <see cref="DialogResult"/> it stands for.</summary>
    private static void AddButton(nint dialog, string label, DialogResult result)
        => NativeMethods.gtk_dialog_add_button(dialog, label, (int)result);

    /// <summary>Populates the chooser's type drop-down and pre-activates the 1-based
    /// <paramref name="filterIndex"/>; each WinForms pattern list becomes one glob per <c>';'</c> entry.</summary>
    private static void AddFilters(nint dialog, FileDialogFilter[] filters, int filterIndex)
    {
        if (filters is not { Length: > 0 })
            return;

        for (var i = 0; i < filters.Length; ++i)
        {
            var filter = NativeMethods.gtk_file_filter_new();
            NativeMethods.gtk_file_filter_set_name(filter, filters[i].Name);
            foreach (var pattern in filters[i].Patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                NativeMethods.gtk_file_filter_add_pattern(filter, pattern);

            NativeMethods.gtk_file_chooser_add_filter(dialog, filter);
            if (i == filterIndex - 1)
                NativeMethods.gtk_file_chooser_set_filter(dialog, filter);
        }
    }

    /// <summary>Reads the chooser's single selection, or <see langword="null"/> when nothing is selected.</summary>
    private static string[]? ReadSingleFilename(nint dialog)
    {
        var raw = NativeMethods.gtk_file_chooser_get_filename(dialog);
        if (raw == 0)
            return null;

        try
        {
            var path = Marshal.PtrToStringUTF8(raw);
            return string.IsNullOrEmpty(path) ? null : [path];
        }
        finally
        {
            NativeMethods.g_free(raw);
        }
    }

    /// <summary>Walks the chooser's <c>GSList</c> of selections, freeing every cell and payload.</summary>
    private static unsafe string[]? ReadFilenameList(nint dialog)
    {
        var list = NativeMethods.gtk_file_chooser_get_filenames(dialog);
        if (list == 0)
            return null;

        var paths = new List<string>();
        try
        {
            for (var cell = list; cell != 0; cell = ((NativeMethods.GSList*)cell)->Next)
            {
                var data = ((NativeMethods.GSList*)cell)->Data;
                var path = Marshal.PtrToStringUTF8(data);
                NativeMethods.g_free(data);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
        }
        finally
        {
            NativeMethods.g_slist_free(list);
        }

        return paths.Count > 0 ? [.. paths] : null;
    }

    /// <summary>Formats a font as a Pango description string, for example <c>"Sans Bold Italic 12"</c>.</summary>
    private static string BuildPangoDescription(Font font)
    {
        var bold = (font.Style & FontStyle.Bold) != 0 ? " Bold" : string.Empty;
        var italic = (font.Style & FontStyle.Italic) != 0 ? " Italic" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture, $"{font.Family}{bold}{italic} {font.SizeInPoints:0.##}");
    }

    /// <summary>
    /// Parses a Pango description back into a font: the trailing token is the point size, the common
    /// weight/slant keywords before it fold into the style flags, and the remainder is the family.
    /// Unrecognized tokens stay part of the family name.
    /// </summary>
    private static Font ParsePangoDescription(string description, Font fallback)
    {
        var tokens = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = tokens.Length;

        var size = fallback.SizeInPoints;
        if (count > 1 && float.TryParse(tokens[count - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            size = parsed;
            --count;
        }

        var style = FontStyle.Regular;
        while (count > 1)
        {
            var token = tokens[count - 1];
            if (token.Equals("Bold", StringComparison.OrdinalIgnoreCase))
                style |= FontStyle.Bold;
            else if (token.Equals("Italic", StringComparison.OrdinalIgnoreCase) || token.Equals("Oblique", StringComparison.OrdinalIgnoreCase))
                style |= FontStyle.Italic;
            else if (!token.Equals("Regular", StringComparison.OrdinalIgnoreCase))
                break;

            --count;
        }

        var family = string.Join(' ', tokens, 0, count);
        return new(family.Length == 0 ? fallback.Family : family, size, style);
    }
}
