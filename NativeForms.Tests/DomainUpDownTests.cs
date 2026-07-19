using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class DomainUpDownTests
{
    private static DomainUpDown CreateAbc() =>
        new() { Bounds = new(0, 0, 120, 24), Items = { "Alpha", "Beta", "Gamma" } };

    private static HeadlessBackend Realize(DomainUpDown upDown)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(upDown);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessCanvasPeer>().Single();

    private static HeadlessTextBoxPeer EditorOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessTextBoxPeer>().Single();

    [Test]
    public void Starts_without_a_selection()
    {
        var upDown = CreateAbc();

        Assert.Multiple(() =>
        {
            Assert.That(upDown.SelectedIndex, Is.EqualTo(-1));
            Assert.That(upDown.SelectedItem, Is.Null);
        });
    }

    [Test]
    public void Selecting_an_index_mirrors_the_item_into_the_editor()
    {
        var upDown = CreateAbc();
        var backend = Realize(upDown);

        upDown.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(upDown.SelectedItem, Is.EqualTo("Beta"));
            Assert.That(EditorOf(backend).Text, Is.EqualTo("Beta"));
        });
    }

    [Test]
    public void SelectedItem_assignment_selects_by_value()
    {
        var upDown = CreateAbc();

        upDown.SelectedItem = "Gamma";

        Assert.That(upDown.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void Selection_change_raises_SelectedItemChanged_once()
    {
        var upDown = CreateAbc();
        var changes = 0;
        upDown.SelectedItemChanged += (_, _) => ++changes;

        upDown.SelectedIndex = 1;
        upDown.SelectedIndex = 1;

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Down_walks_forward_and_clamps_without_Wrap()
    {
        var upDown = CreateAbc();

        upDown.DownButton(); // -1 -> 0
        upDown.DownButton(); // 0 -> 1
        upDown.DownButton(); // 1 -> 2
        upDown.DownButton(); // clamps at the last item

        Assert.That(upDown.SelectedIndex, Is.EqualTo(2));
    }

    [Test]
    public void Up_walks_backward_and_clamps_without_Wrap()
    {
        var upDown = CreateAbc();
        upDown.SelectedIndex = 1;

        upDown.UpButton(); // 1 -> 0
        upDown.UpButton(); // clamps at the first item

        Assert.That(upDown.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Wrap_cycles_past_both_ends()
    {
        var upDown = CreateAbc();
        upDown.Wrap = true;
        upDown.SelectedIndex = 0;

        upDown.UpButton();
        Assert.That(upDown.SelectedIndex, Is.EqualTo(2), "wraps from the first to the last item");

        upDown.DownButton();
        Assert.That(upDown.SelectedIndex, Is.Zero, "wraps from the last back to the first");
    }

    [Test]
    public void Spinner_clicks_and_arrow_keys_move_the_selection()
    {
        var upDown = CreateAbc();
        var backend = Realize(upDown);
        var canvas = CanvasOf(backend);

        canvas.RaiseMouseDown(110, 20); // down button
        canvas.RaiseMouseUp(110, 20);
        Assert.That(upDown.SelectedIndex, Is.Zero);

        canvas.RaiseKeyDown(Keys.Down);
        Assert.That(upDown.SelectedIndex, Is.EqualTo(1));

        canvas.RaiseKeyDown(Keys.Up);
        Assert.That(upDown.SelectedIndex, Is.Zero);
    }

    [Test]
    public void Typing_a_matching_item_selects_it_on_commit()
    {
        var upDown = CreateAbc();
        var backend = Realize(upDown);
        var editor = EditorOf(backend);

        editor.SimulateUserInput("beta"); // case-insensitive match
        CanvasOf(backend).RaiseLostFocus();

        Assert.Multiple(() =>
        {
            Assert.That(upDown.SelectedIndex, Is.EqualTo(1));
            Assert.That(editor.Text, Is.EqualTo("Beta"), "commit normalizes the casing");
        });
    }

    [Test]
    public void Typing_garbage_reverts_on_commit()
    {
        var upDown = CreateAbc();
        upDown.SelectedIndex = 2;
        var backend = Realize(upDown);
        var editor = EditorOf(backend);

        editor.SimulateUserInput("nope");
        CanvasOf(backend).RaiseLostFocus();

        Assert.Multiple(() =>
        {
            Assert.That(upDown.SelectedIndex, Is.EqualTo(2));
            Assert.That(editor.Text, Is.EqualTo("Gamma"));
        });
    }

    [Test]
    public void Removing_the_selected_item_clears_the_selection()
    {
        var upDown = CreateAbc();
        upDown.SelectedIndex = 1;
        var changes = 0;
        upDown.SelectedItemChanged += (_, _) => ++changes;

        upDown.Items.RemoveAt(1);

        Assert.Multiple(() =>
        {
            Assert.That(upDown.SelectedIndex, Is.EqualTo(-1));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Inserting_before_the_selection_keeps_the_same_item_selected()
    {
        var upDown = CreateAbc();
        upDown.SelectedIndex = 1;

        upDown.Items.Insert(0, "Zero");

        Assert.Multiple(() =>
        {
            Assert.That(upDown.SelectedIndex, Is.EqualTo(2));
            Assert.That(upDown.SelectedItem, Is.EqualTo("Beta"));
        });
    }
}
