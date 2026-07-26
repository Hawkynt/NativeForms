using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="TreeView"/> with <see cref="TreeView.LabelEdit"/> renames a node in place: F2 or
/// <see cref="TreeView.BeginEdit"/> opens a hosted editor over the label, Enter commits the typed text,
/// Escape discards it, and the Before/After events can veto either end.
/// </summary>
[TestFixture]
internal sealed class TreeViewLabelEditTests
{
    private static TreeView Create(out HeadlessBackend backend)
    {
        var tree = new TreeView { Bounds = new(0, 0, 300, 220), LabelEdit = true };
        tree.Nodes.Add("root0");
        tree.Nodes.Add("root1");
        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(tree);
        Application.Run(form, backend);
        return tree;
    }

    private static HeadlessTextBoxPeer Editor(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessTextBoxPeer>().Single();

    [Test]
    public void BeginEdit_opens_a_hosted_editor_prefilled_with_the_label()
    {
        var tree = Create(out var backend);

        tree.BeginEdit(tree.Nodes[0]);

        var editor = Editor(backend);
        Assert.Multiple(() =>
        {
            Assert.That(tree.IsEditing, Is.True);
            Assert.That(editor.Text, Is.EqualTo("root0"));
            Assert.That(editor.FocusRequested, Is.True);
        });
    }

    [Test]
    public void F2_starts_editing_the_selected_node()
    {
        var tree = Create(out var backend);
        tree.SelectedNode = tree.Nodes[1];
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        canvas.RaiseKeyDown(Keys.F2);

        Assert.Multiple(() =>
        {
            Assert.That(tree.IsEditing, Is.True);
            Assert.That(Editor(backend).Text, Is.EqualTo("root1"));
        });
    }

    [Test]
    public void Enter_commits_the_edited_label()
    {
        var tree = Create(out var backend);
        tree.BeginEdit(tree.Nodes[0]);
        var editor = Editor(backend);
        editor.SimulateUserInput("renamed");

        editor.SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(tree.IsEditing, Is.False);
            Assert.That(tree.Nodes[0].Text, Is.EqualTo("renamed"));
        });
    }

    [Test]
    public void Escape_discards_the_edited_label()
    {
        var tree = Create(out var backend);
        tree.BeginEdit(tree.Nodes[0]);
        var editor = Editor(backend);
        editor.SimulateUserInput("renamed");

        editor.SimulateKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(tree.IsEditing, Is.False);
            Assert.That(tree.Nodes[0].Text, Is.EqualTo("root0"), "Escape keeps the original label");
        });
    }

    [Test]
    public void BeforeLabelEdit_can_veto_the_edit()
    {
        var tree = Create(out var backend);
        tree.BeforeLabelEdit += (_, e) => e.CancelEdit = true;

        tree.BeginEdit(tree.Nodes[0]);

        Assert.That(tree.IsEditing, Is.False, "the veto keeps the editor closed");
    }

    [Test]
    public void AfterLabelEdit_can_veto_the_commit()
    {
        var tree = Create(out var backend);
        tree.AfterLabelEdit += (_, e) => e.CancelEdit = true;
        tree.BeginEdit(tree.Nodes[0]);
        var editor = Editor(backend);
        editor.SimulateUserInput("renamed");

        editor.SimulateKeyDown(Keys.Enter);

        Assert.That(tree.Nodes[0].Text, Is.EqualTo("root0"), "the after-veto discards the typed text");
    }

    [Test]
    public void The_editing_node_label_is_not_painted_under_the_editor()
    {
        var tree = Create(out var backend);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        tree.BeginEdit(tree.Nodes[0]);
        var g = canvas.RaisePaint();

        Assert.That(g.Operations.Exists(o => o.Contains("\"root0\"")), Is.False,
            "the hosted editor covers the label, so the row text is skipped");
    }

    [Test]
    public void BeginEdit_without_LabelEdit_throws()
    {
        var tree = Create(out _);
        tree.LabelEdit = false;

        Assert.Throws<InvalidOperationException>(() => tree.BeginEdit(tree.Nodes[0]));
    }
}
