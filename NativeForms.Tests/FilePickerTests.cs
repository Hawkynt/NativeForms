using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="FilePicker"/> must host a native editor beside a browse button, hand the WinForms
/// filter options straight to the platform dialog, commit the dialog's choice (and a typed path on
/// Enter) into <see cref="PathPickerBase.SelectedPath"/>, and flag a path that names nothing.
/// </summary>
[TestFixture]
internal sealed class FilePickerTests
{
    /// <summary>Realizes a 200×26 picker on a fresh form and returns all the actors.</summary>
    private static FilePicker CreatePicker(out HeadlessBackend backend, out HeadlessCanvasPeer canvas, out HeadlessTextBoxPeer editor)
    {
        var picker = new FilePicker { Bounds = new(0, 0, 200, 26) };
        var created = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(picker);
        Application.Run(form, created);
        backend = created;
        canvas = created.Created.OfType<HeadlessCanvasPeer>().Single();
        editor = created.Created.OfType<HeadlessTextBoxPeer>().Single();
        return picker;
    }

    /// <summary>Clicks the middle of the browse zone at the picker's right edge.</summary>
    private static void ClickBrowse(HeadlessCanvasPeer canvas) => canvas.RaiseMouseDown(200 - 14, 13);

    [Test]
    public void Hosts_an_editor_beside_the_browse_zone()
    {
        CreatePicker(out _, out _, out var editor);

        Assert.That(editor.Bounds, Is.EqualTo(new Rectangle(0, 0, 172, 26)), "the editor fills the field left of the browse button");
    }

    [Test]
    public void Browsing_opens_the_open_dialog_and_commits_the_choice()
    {
        var picker = CreatePicker(out var backend, out var canvas, out var editor);
        backend.FileDialogResult = ["/tmp/report.txt"];
        var changes = 0;
        picker.PathChanged += (_, _) => ++changes;

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.Kind, Is.EqualTo(FileDialogKind.Open));
            Assert.That(picker.SelectedPath, Is.EqualTo("/tmp/report.txt"));
            Assert.That(editor.Text, Is.EqualTo("/tmp/report.txt"), "the choice is pushed into the native editor");
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Save_mode_browses_with_the_save_dialog()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.Mode = FilePickerMode.Save;
        backend.FileDialogResult = ["/tmp/out.csv"];

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.Kind, Is.EqualTo(FileDialogKind.Save));
            Assert.That(picker.SelectedPath, Is.EqualTo("/tmp/out.csv"));
        });
    }

    [Test]
    public void Filter_options_reach_the_dialog_in_WinForms_syntax()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.Filter = "Text files|*.txt|All files|*.*";
        picker.FilterIndex = 2;
        picker.Title = "Pick one";
        picker.InitialDirectory = "/var";
        backend.FileDialogResult = ["/var/a.txt"];

        ClickBrowse(canvas);

        var options = backend.LastFileDialog!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(options.Filters, Has.Length.EqualTo(2));
            Assert.That(options.Filters[0].Patterns, Is.EqualTo("*.txt"));
            Assert.That(options.Filters[1].Name, Is.EqualTo("All files"));
            Assert.That(options.FilterIndex, Is.EqualTo(2));
            Assert.That(options.Title, Is.EqualTo("Pick one"));
            Assert.That(options.InitialDirectory, Is.EqualTo("/var"));
        });
    }

    [Test]
    public void An_odd_filter_is_rejected_exactly_as_the_dialog_rejects_it()
    {
        var picker = new FilePicker();

        Assert.That(() => picker.Filter = "Text files|*.txt|orphan", Throws.ArgumentException);
    }

    [Test]
    public void Multiselect_publishes_every_chosen_path()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.Multiselect = true;
        backend.FileDialogResult = ["/tmp/a.txt", "/tmp/b.txt", "/tmp/c.txt"];

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.Multiselect, Is.True);
            Assert.That(picker.SelectedPath, Is.EqualTo("/tmp/a.txt"), "the first pick is the committed one");
            Assert.That(picker.SelectedPaths, Is.EqualTo(new[] { "/tmp/a.txt", "/tmp/b.txt", "/tmp/c.txt" }));
        });
    }

    [Test]
    public void A_typed_path_narrows_the_selection_back_to_itself()
    {
        // A multi-pick followed by a typed edit must not keep reporting the stale set.
        var picker = CreatePicker(out var backend, out var canvas, out var editor);
        picker.Multiselect = true;
        backend.FileDialogResult = ["/tmp/a.txt", "/tmp/b.txt"];
        ClickBrowse(canvas);

        editor.SimulateUserInput("/tmp/typed.txt");
        editor.SimulateKeyDown(Keys.Enter);

        Assert.That(picker.SelectedPaths, Is.EqualTo(new[] { "/tmp/typed.txt" }));
    }

    [Test]
    public void Re_picking_the_committed_path_still_widens_the_selection()
    {
        // The commit is a no-op — same path — yet the set behind it grew, so AfterBrowse must run
        // regardless of whether PathChanged fired.
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.Multiselect = true;
        backend.FileDialogResult = ["/tmp/a.txt"];
        ClickBrowse(canvas);

        backend.FileDialogResult = ["/tmp/a.txt", "/tmp/b.txt"];
        ClickBrowse(canvas);

        Assert.That(picker.SelectedPaths, Is.EqualTo(new[] { "/tmp/a.txt", "/tmp/b.txt" }));
    }

    [Test]
    public void A_cancelled_browse_changes_nothing()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        picker.SelectedPath = "/tmp/keep.txt";
        backend.FileDialogResult = null;
        var changes = 0;
        picker.PathChanged += (_, _) => ++changes;

        ClickBrowse(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.EqualTo("/tmp/keep.txt"));
            Assert.That(changes, Is.Zero);
        });
    }

    [Test]
    public void A_click_left_of_the_browse_zone_opens_nothing()
    {
        var picker = CreatePicker(out var backend, out var canvas, out _);
        backend.FileDialogResult = ["/tmp/nope.txt"];

        canvas.RaiseMouseDown(10, 13);

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog, Is.Null);
            Assert.That(picker.SelectedPath, Is.Empty);
        });
    }

    [Test]
    public void Enter_inside_the_hosted_editor_commits_the_typed_path()
    {
        var picker = CreatePicker(out _, out _, out var editor);
        var changes = 0;
        picker.PathChanged += (_, _) => ++changes;
        editor.SimulateUserInput("/tmp/typed.txt");

        var handled = editor.SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.EqualTo("/tmp/typed.txt"));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(handled, Is.True, "the key is claimed, so the editor never sees it");
        });
    }

    [Test]
    public void Focus_leaving_the_hosted_editor_commits_the_typed_path()
    {
        // The editor holds the focus, not the shell, so the shell's own LostFocus never fires — the
        // commit has to hang off the editor's. Without that this is silently dead and a typed path
        // is lost whenever the user tabs away instead of pressing Enter.
        var picker = CreatePicker(out _, out _, out var editor);
        var changes = 0;
        picker.PathChanged += (_, _) => ++changes;
        editor.SimulateUserInput("/tmp/tabbed-away.txt");

        editor.RaiseLostFocus();

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.EqualTo("/tmp/tabbed-away.txt"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void A_typed_path_stays_uncommitted_until_Enter()
    {
        var picker = CreatePicker(out _, out _, out var editor);

        editor.SimulateUserInput("/tmp/half-typed");

        Assert.Multiple(() =>
        {
            Assert.That(picker.SelectedPath, Is.Empty, "the committed path has not moved yet");
            Assert.That(picker.Text, Is.EqualTo("/tmp/half-typed"), "but the live editor text has");
        });
    }

    [Test]
    public void PathExists_tracks_the_committed_path_only()
    {
        var real = Path.GetTempFileName();
        try
        {
            var picker = CreatePicker(out _, out _, out _);

            picker.SelectedPath = real;
            Assert.That(picker.PathExists, Is.True, "an existing file");

            picker.SelectedPath = real + ".missing";
            Assert.That(picker.PathExists, Is.False, "a path that names nothing");

            picker.SelectedPath = string.Empty;
            Assert.That(picker.PathExists, Is.False, "an empty field is not an existing path");
        }
        finally
        {
            File.Delete(real);
        }
    }

    [Test]
    public void Save_mode_accepts_a_file_that_does_not_exist_yet_in_a_folder_that_does()
    {
        // Naming a file that is not there is what saving is for, so the frame must not flag it; only
        // a folder that does not exist is a real problem.
        var picker = CreatePicker(out _, out _, out _);
        picker.Mode = FilePickerMode.Save;

        picker.SelectedPath = Path.Combine(Path.GetTempPath(), "not-written-yet.csv");
        Assert.That(picker.PathExists, Is.True, "an unwritten file in a real folder is a fine save target");

        picker.SelectedPath = "/no/such/folder/out.csv";
        Assert.That(picker.PathExists, Is.False, "a folder that does not exist is not");

        picker.SelectedPath = "bare-name.csv";
        Assert.That(picker.PathExists, Is.True, "a bare name resolves against the working directory");
    }

    [Test]
    public void Open_mode_still_demands_the_file_itself()
    {
        var picker = CreatePicker(out _, out _, out _);

        picker.SelectedPath = Path.Combine(Path.GetTempPath(), "not-written-yet.csv");

        Assert.That(picker.PathExists, Is.False);
    }

    [Test]
    public void Open_mode_refuses_a_typed_directory_and_keeps_the_previous_file()
    {
        // A directory can never be a file selection; in Open mode it is vetoed outright, so the
        // committed path does not move to it and the editor snaps back — WinForms navigates into a
        // typed folder rather than returning it.
        var picker = CreatePicker(out _, out _, out var editor);
        var real = Path.GetTempFileName();
        try
        {
            picker.SelectedPath = real;
            var changes = 0;
            picker.PathChanged += (_, _) => ++changes;

            editor.SimulateUserInput(Path.GetTempPath());
            editor.SimulateKeyDown(Keys.Enter);

            Assert.Multiple(() =>
            {
                Assert.That(picker.SelectedPath, Is.EqualTo(real), "the directory was refused, the file stands");
                Assert.That(editor.Text, Is.EqualTo(real), "the editor snapped back to the committed file");
                Assert.That(changes, Is.Zero, "a refused commit raises nothing");
            });
        }
        finally
        {
            File.Delete(real);
        }
    }

    [Test]
    public void Open_mode_accepts_a_real_file()
    {
        var real = Path.GetTempFileName();
        try
        {
            var picker = CreatePicker(out _, out _, out _);

            picker.SelectedPath = real;

            Assert.Multiple(() =>
            {
                Assert.That(picker.SelectedPath, Is.EqualTo(real), "a real file is a valid Open selection");
                Assert.That(picker.PathExists, Is.True);
            });
        }
        finally
        {
            File.Delete(real);
        }
    }

    [Test]
    public void Save_mode_accepts_a_new_name_in_an_existing_folder_even_when_a_directory_shares_it()
    {
        // Save vetoes nothing: naming a not-yet-written file is the point, and even a directory path
        // is a legal target name to save under in the WinForms sense — only Open refuses folders.
        var picker = CreatePicker(out _, out _, out _);
        picker.Mode = FilePickerMode.Save;

        var newName = Path.Combine(Path.GetTempPath(), "save-target-" + Guid.NewGuid().ToString("N") + ".csv");
        picker.SelectedPath = newName;
        Assert.That(picker.SelectedPath, Is.EqualTo(newName), "a fresh name in a real folder commits");

        picker.SelectedPath = Path.GetTempPath();
        Assert.That(picker.SelectedPath, Is.EqualTo(Path.GetTempPath()), "Save does not veto a directory path");
    }

    [Test]
    public void ReadOnlyText_forwards_to_the_hosted_editor()
    {
        var picker = CreatePicker(out _, out _, out var editor);

        picker.ReadOnlyText = true;

        Assert.Multiple(() =>
        {
            Assert.That(picker.ReadOnlyText, Is.True);
            Assert.That(editor.ReadOnly, Is.True);
        });
    }

    [Test]
    public void Paints_the_field_the_browse_button_and_a_plain_frame()
    {
        CreatePicker(out _, out var canvas, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 0,0,200,26"), "field background");
            Assert.That(g.Operations, Does.Contain("fill #FFFDFDFD 172,0,28,26"), "browse button face");
            Assert.That(g.DrewText("…"), Is.True, "the browse ellipsis");
        });
    }

    [Test]
    public void A_missing_path_is_framed_in_the_warning_colour()
    {
        var picker = CreatePicker(out _, out var canvas, out _);

        // Empty is not an error — the plain themed border.
        Assert.That(canvas.RaisePaint().Operations, Does.Contain("rect #FFC8C8C8 0,0,199,25"));

        picker.SelectedPath = "/definitely/not/here.txt";

        Assert.That(canvas.RaisePaint().Operations, Does.Contain("rect #FFE81123 0,0,199,25"), "the warning frame");
    }

    [Test]
    public void Focus_lands_on_the_hosted_editor_not_on_the_painted_surface()
    {
        var picker = CreatePicker(out _, out var canvas, out var editor);

        picker.Focus();

        Assert.Multiple(() =>
        {
            Assert.That(editor.FocusRequested, Is.True);
            Assert.That(canvas.FocusRequested, Is.False);
        });
    }

    [Test]
    public void Painting_stays_inside_the_client_rectangle()
    {
        var picker = CreatePicker(out _, out var canvas, out _);
        picker.SelectedPath = "/some/rather/long/path/that/could/overflow/the/field.txt";

        var g = canvas.RaisePaint();

        Assert.That(g.OutOfBoundsOperations, Is.Empty);
    }
}
