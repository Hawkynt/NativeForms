using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class DialogTests
{
    [Test]
    public void Filter_parses_into_name_pattern_pairs()
    {
        var backend = new HeadlessBackend { FileDialogResult = ["/tmp/a.txt"] };
        var dialog = new OpenFileDialog(backend) { Filter = "Text files|*.txt|All files|*.*" };

        dialog.ShowDialog();

        var filters = backend.LastFileDialog!.Value.Filters;
        Assert.Multiple(() =>
        {
            Assert.That(filters, Has.Length.EqualTo(2));
            Assert.That(filters[0].Name, Is.EqualTo("Text files"));
            Assert.That(filters[0].Patterns, Is.EqualTo("*.txt"));
            Assert.That(filters[1].Name, Is.EqualTo("All files"));
            Assert.That(filters[1].Patterns, Is.EqualTo("*.*"));
        });
    }

    [Test]
    public void Filter_with_an_odd_segment_count_throws()
        => Assert.Throws<ArgumentException>(() => new OpenFileDialog().Filter = "Text files|*.txt|Orphan");

    [Test]
    public void Open_dialog_forwards_its_options()
    {
        var backend = new HeadlessBackend { FileDialogResult = ["/tmp/a.txt"] };
        var dialog = new OpenFileDialog(backend)
        {
            Title = "Pick",
            FileName = "seed.txt",
            InitialDirectory = "/tmp",
            Filter = "Text files|*.txt|All files|*.*",
            FilterIndex = 2,
            Multiselect = true,
        };

        dialog.ShowDialog();

        var options = backend.LastFileDialog!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(options.Kind, Is.EqualTo(FileDialogKind.Open));
            Assert.That(options.Title, Is.EqualTo("Pick"));
            Assert.That(options.FileName, Is.EqualTo("seed.txt"));
            Assert.That(options.InitialDirectory, Is.EqualTo("/tmp"));
            Assert.That(options.FilterIndex, Is.EqualTo(2));
            Assert.That(options.Multiselect, Is.True);
        });
    }

    [Test]
    public void Open_dialog_applies_the_scripted_selection()
    {
        var backend = new HeadlessBackend { FileDialogResult = ["/tmp/a.txt", "/tmp/b.txt"] };
        var dialog = new OpenFileDialog(backend) { Multiselect = true };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.OK));
            Assert.That(dialog.FileName, Is.EqualTo("/tmp/a.txt"));
            Assert.That(dialog.FileNames, Is.EqualTo(new[] { "/tmp/a.txt", "/tmp/b.txt" }));
        });
    }

    [Test]
    public void Open_dialog_cancel_keeps_the_previous_file_name()
    {
        var backend = new HeadlessBackend();
        var dialog = new OpenFileDialog(backend) { FileName = "before.txt" };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(dialog.FileName, Is.EqualTo("before.txt"));
            Assert.That(dialog.FileNames, Is.Empty);
        });
    }

    [Test]
    public void Save_dialog_asks_for_the_save_kind_and_applies_the_path()
    {
        var backend = new HeadlessBackend { FileDialogResult = ["/tmp/out.txt"] };
        var dialog = new SaveFileDialog(backend) { FileName = "out.txt" };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.Kind, Is.EqualTo(FileDialogKind.Save));
            Assert.That(backend.LastFileDialog!.Value.Multiselect, Is.False);
            Assert.That(result, Is.EqualTo(DialogResult.OK));
            Assert.That(dialog.FileName, Is.EqualTo("/tmp/out.txt"));
        });
    }

    [Test]
    public void Folder_browser_round_trips_the_selected_path()
    {
        var backend = new HeadlessBackend { FileDialogResult = ["/home/user/music"] };
        var dialog = new FolderBrowserDialog(backend) { SelectedPath = "/home/user" };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFileDialog!.Value.Kind, Is.EqualTo(FileDialogKind.SelectFolder));
            Assert.That(backend.LastFileDialog!.Value.InitialDirectory, Is.EqualTo("/home/user"));
            Assert.That(result, Is.EqualTo(DialogResult.OK));
            Assert.That(dialog.SelectedPath, Is.EqualTo("/home/user/music"));
        });
    }

    [Test]
    public void Folder_browser_cancel_keeps_the_previous_path()
    {
        var backend = new HeadlessBackend();
        var dialog = new FolderBrowserDialog(backend) { SelectedPath = "/home/user" };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(dialog.SelectedPath, Is.EqualTo("/home/user"));
        });
    }

    [Test]
    public void Color_dialog_round_trips_the_color()
    {
        var backend = new HeadlessBackend { ColorDialogResult = Color.FromArgb(1, 2, 3) };
        var dialog = new ColorDialog(backend) { Color = Color.Red };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastColorDialogColor, Is.EqualTo(Color.Red));
            Assert.That(result, Is.EqualTo(DialogResult.OK));
            Assert.That(dialog.Color, Is.EqualTo(Color.FromArgb(1, 2, 3)));
        });
    }

    [Test]
    public void Color_dialog_cancel_keeps_the_previous_color()
    {
        var backend = new HeadlessBackend();
        var dialog = new ColorDialog(backend) { Color = Color.Red };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(dialog.Color, Is.EqualTo(Color.Red));
        });
    }

    [Test]
    public void Font_dialog_round_trips_the_font()
    {
        var chosen = new Font("Serif", 14f, FontStyle.Bold);
        var backend = new HeadlessBackend { FontDialogResult = chosen };
        var dialog = new FontDialog(backend) { Font = new("Sans", 10f) };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(backend.LastFontDialogFont, Is.EqualTo(new Font("Sans", 10f)));
            Assert.That(result, Is.EqualTo(DialogResult.OK));
            Assert.That(dialog.Font, Is.EqualTo(chosen));
        });
    }

    [Test]
    public void Font_dialog_cancel_keeps_the_previous_font()
    {
        var backend = new HeadlessBackend();
        var dialog = new FontDialog(backend) { Font = new("Sans", 10f) };

        var result = dialog.ShowDialog();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(dialog.Font, Is.EqualTo(new Font("Sans", 10f)));
        });
    }

    [Test]
    public void ShowDialog_without_a_running_backend_throws()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => new OpenFileDialog().ShowDialog());
            Assert.Throws<InvalidOperationException>(() => new SaveFileDialog().ShowDialog());
            Assert.Throws<InvalidOperationException>(() => new FolderBrowserDialog().ShowDialog());
            Assert.Throws<InvalidOperationException>(() => new ColorDialog().ShowDialog());
            Assert.Throws<InvalidOperationException>(() => new FontDialog().ShowDialog());
        });
    }
}
