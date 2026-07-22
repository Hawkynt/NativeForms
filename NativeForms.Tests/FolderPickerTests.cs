using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="FolderPicker"/> must browse with the platform's select-folder dialog, seed it from
/// the committed path, commit the chosen directory, and answer
/// <see cref="PathPickerBase.PathExists"/> about directories rather than files.
/// </summary>
[TestFixture]
internal sealed class FolderPickerTests
{
    /// <summary>Realizes a 200×26 picker on a fresh form and returns all the actors.</summary>
    private static FolderPicker CreatePicker(out HeadlessBackend backend, out HeadlessCanvasPeer canvas, out HeadlessTextBoxPeer editor)
    {
        var picker = new FolderPicker { Bounds = new(0, 0, 200, 26) };
        var created = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, created);
        backend = created;
        canvas = created.Created.OfType<HeadlessCanvasPeer>().Single();
        editor = created.Created.OfType<HeadlessTextBoxPeer>().Single();
        return picker;
    }

    private static void ClickBrowse(HeadlessCanvasPeer canvas) => canvas.RaiseMouseDown(200 - 14, 13);

    [Test]
    public void Hosts_an_editor_beside_the_browse_zone()
    {
        CreatePicker(out _, out _, out var editor);

        Assert.That(editor.Bounds, Is.EqualTo(new Rectangle(0, 0, 172, 26)));
    }

    [Test]
    public void Browsing_opens_the_select_folder_dialog_and_commits_the_choice()
    {
        var picker = CreatePicker(out var backend, out var canvas, out var editor);
        backend.FileDialogResult = ["/home/user/documents"];
        var changes = 0;
        picker.PathChanged += (_, _) => ++changes;

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.Kind, Is.EqualTo(FileDialogKind.SelectFolder));
            Assert.That(picker.SelectedPath, Is.EqualTo("/home/user/documents"));
            Assert.That(editor.Text, Is.EqualTo("/home/user/documents"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void The_committed_path_seeds_the_dialogs_start_location()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.SelectedPath = "/home/user";
        picker.Title = "Where to?";
        backend.FileDialogResult = ["/home/user/pictures"];

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.InitialDirectory, Is.EqualTo("/home/user"));
            Assert.That(backend.LastFileDialog!.Value.Title, Is.EqualTo("Where to?"));
        });
    }

    [Test]
    public void A_cancelled_browse_changes_nothing()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.SelectedPath = "/home/user";
        backend.FileDialogResult = null;
        var changes = 0;
        picker.PathChanged += (_, _) => ++changes;

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.EqualTo("/home/user"));
            Assert.That(changes, Is.Zero);
        });
    }

    [Test]
    public void Enter_inside_the_hosted_editor_commits_the_typed_path()
    {
        var picker = CreatePicker(out _, out _, out var editor);
        editor.SimulateUserInput("/etc");

        var handled = editor.SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.EqualTo("/etc"));
            Assert.That(handled, Is.True);
        });
    }

    [Test]
    public void PathExists_asks_about_directories_not_files()
    {
        var file = Path.GetTempFileName();
        try
        {
            var picker = CreatePicker(out _, out _, out _);

            picker.SelectedPath = Path.GetTempPath();
            Assert.That(picker.PathExists, Is.True, "an existing directory");

            picker.SelectedPath = file;
            Assert.That(picker.PathExists, Is.False, "a file is not a directory");

            picker.SelectedPath = Path.Combine(Path.GetTempPath(), "definitely-not-here");
            Assert.That(picker.PathExists, Is.False, "a directory that names nothing");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void A_real_directory_commits_as_the_selected_path()
    {
        // The mirror of FilePicker's Open-mode veto: a folder is exactly what this picker stands
        // behind, so an existing directory is accepted, not refused.
        var picker = CreatePicker(out _, out _, out var editor);

        editor.SimulateUserInput(Path.GetTempPath());
        editor.SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.EqualTo(Path.GetTempPath()), "a directory is a valid folder selection");
            Assert.That(picker.PathExists, Is.True);
        });
    }

    [Test]
    public void ReadOnlyText_makes_the_field_browse_only()
    {
        var picker = CreatePicker(out var backend, out var canvas, out var editor);
        picker.ReadOnlyText = true;
        backend.FileDialogResult = ["/srv"];

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(editor.ReadOnly, Is.True, "typing is refused by the native editor");
            Assert.That(picker.SelectedPath, Is.EqualTo("/srv"), "but the browse button still commits");
        });
    }

    [Test]
    public void A_disabled_picker_ignores_the_browse_zone()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.Enabled = false;
        backend.FileDialogResult = ["/srv"];

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog, Is.Null);
            Assert.That(picker.SelectedPath, Is.Empty);
        });
    }

    [Test]
    public void A_missing_directory_is_framed_in_the_warning_colour()
    {
        var picker = CreatePicker(out _, out var canvas, out _);

        picker.SelectedPath = "/definitely/not/here";

        Assert.That(canvas.RaisePaint().Operations, Does.Contain("rect #FFE81123 0,0,199,25"));
    }

    [Test]
    public void Painting_stays_inside_the_client_rectangle()
    {
        var picker = CreatePicker(out _, out var canvas, out _);
        picker.SelectedPath = "/a/very/long/directory/path/that/could/overflow/the/field";

        Assert.That(canvas.RaisePaint().OutOfBoundsOperations, Is.Empty);
    }
}
