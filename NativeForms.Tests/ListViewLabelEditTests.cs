using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Label editing on <see cref="ListView"/>: the hosted native <see cref="TextBox"/> overlay is
/// positioned over the item's label, commits on Enter or a click elsewhere, cancels on Escape, and
/// every outcome runs through the vetoable <see cref="ListView.AfterLabelEdit"/>.
/// </summary>
[TestFixture]
internal sealed class ListViewLabelEditTests
{
    private static HeadlessBackend Realize(ListView control, out HeadlessCanvasPeer canvas)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        return backend;
    }

    private static ListView MakeEditable()
    {
        var list = new ListView { Bounds = new(0, 0, 300, 220), LabelEdit = true };
        list.Columns.Add(new ColumnHeader("Name", 140));
        list.Items.AddRange([new ListViewItem("File1"), new ListViewItem("File2"), new ListViewItem("File3")]);
        return list;
    }

    [Test]
    public void Begin_edit_requires_label_edit_enabled()
    {
        var list = MakeEditable();
        list.LabelEdit = false;

        Assert.Throws<InvalidOperationException>(() => list.BeginEdit(0));
    }

    [Test]
    public void Begin_edit_hosts_the_editor_over_the_label_with_the_item_text()
    {
        var list = MakeEditable();
        var backend = Realize(list, out _);

        list.BeginEdit(1);

        var editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(list.IsEditing, Is.True);
            Assert.That(editor.Visible, Is.True);
            Assert.That(editor.Text, Is.EqualTo("File2"));
            Assert.That(editor.Bounds, Is.EqualTo(new Rectangle(2, 44, 136, 22)), "over row 1's label, inside the first column");
            Assert.That(editor.SelectionLength, Is.EqualTo(5), "pre-selected for overtyping");
        });
    }

    [Test]
    public void Enter_commits_the_typed_text_and_raises_after_label_edit()
    {
        var list = MakeEditable();
        var backend = Realize(list, out var canvas);
        LabelEditEventArgs? edit = null;
        list.AfterLabelEdit += (_, e) => edit = e;

        list.BeginEdit(1);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Renamed");
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[1].Text, Is.EqualTo("Renamed"));
            Assert.That(edit!.Item, Is.EqualTo(1));
            Assert.That(edit!.Label, Is.EqualTo("Renamed"));
            Assert.That(list.IsEditing, Is.False);
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Single().Visible, Is.False);
        });
    }

    [Test]
    public void Escape_cancels_and_keeps_the_text()
    {
        var list = MakeEditable();
        var backend = Realize(list, out var canvas);
        LabelEditEventArgs? edit = null;
        list.AfterLabelEdit += (_, e) => edit = e;

        list.BeginEdit(0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Scrapped");
        canvas.RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Text, Is.EqualTo("File1"));
            Assert.That(edit!.Label, Is.Null, "cancelled edits report a null label");
            Assert.That(list.IsEditing, Is.False);
        });
    }

    [Test]
    public void After_label_edit_handler_can_veto_the_commit()
    {
        var list = MakeEditable();
        var backend = Realize(list, out var canvas);
        list.AfterLabelEdit += (_, e) => e.CancelEdit = true;

        list.BeginEdit(0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Nope");
        canvas.RaiseKeyDown(Keys.Enter);

        Assert.That(list.Items[0].Text, Is.EqualTo("File1"));
    }

    [Test]
    public void Clicking_elsewhere_commits_the_pending_edit()
    {
        var list = MakeEditable();
        var backend = Realize(list, out var canvas);

        list.BeginEdit(1);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Away");
        canvas.RaiseMouseDown(10, 30); // row 0 — commits, then selects

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[1].Text, Is.EqualTo("Away"));
            Assert.That(list.SelectedIndex, Is.Zero);
            Assert.That(list.IsEditing, Is.False);
        });
    }

    [Test]
    public void F2_starts_editing_the_caret_item()
    {
        var list = MakeEditable();
        var backend = Realize(list, out var canvas);

        canvas.RaiseMouseDown(10, 30); // select + focus row 0
        canvas.RaiseKeyDown(Keys.F2);

        Assert.Multiple(() =>
        {
            Assert.That(list.IsEditing, Is.True);
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Single().Text, Is.EqualTo("File1"));
        });
    }

    [Test]
    public void Item_begin_edit_routes_through_the_owner()
    {
        var list = MakeEditable();
        var backend = Realize(list, out _);

        list.Items[2].BeginEdit();

        Assert.Multiple(() =>
        {
            Assert.That(list.IsEditing, Is.True);
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Single().Text, Is.EqualTo("File3"));
        });
    }

    [Test]
    public void Detached_item_begin_edit_throws()
        => Assert.Throws<InvalidOperationException>(static () => new ListViewItem("free").BeginEdit());

    [Test]
    public void Removing_the_edited_item_abandons_the_edit()
    {
        var list = MakeEditable();
        var backend = Realize(list, out _);
        var edits = 0;
        list.AfterLabelEdit += (_, _) => ++edits;

        list.BeginEdit(1);
        list.Items.RemoveAt(1);

        Assert.Multiple(() =>
        {
            Assert.That(list.IsEditing, Is.False);
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Single().Visible, Is.False, "the editor is hidden, not orphaned");
            Assert.That(edits, Is.Zero, "nothing left to commit to");
        });
    }

    [Test]
    public void Starting_another_edit_commits_the_first()
    {
        var list = MakeEditable();
        var backend = Realize(list, out _);

        list.BeginEdit(0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("First");
        list.BeginEdit(2);

        Assert.Multiple(() =>
        {
            Assert.That(list.Items[0].Text, Is.EqualTo("First"));
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Single().Text, Is.EqualTo("File3"));
        });
    }

    [Test]
    public void BeforeLabelEdit_cancel_keeps_the_editor_closed()
    {
        var list = MakeEditable();
        var backend = Realize(list, out _);
        LabelEditEventArgs? before = null;
        list.BeforeLabelEdit += (_, e) =>
        {
            before = e;
            e.CancelEdit = true;
        };

        list.BeginEdit(1);

        Assert.Multiple(() =>
        {
            Assert.That(list.IsEditing, Is.False);
            Assert.That(before!.Item, Is.EqualTo(1));
            Assert.That(before.Label, Is.EqualTo("File2"), "carries the current text");
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>(), Is.Empty, "no editor was hosted");
        });
    }
}
