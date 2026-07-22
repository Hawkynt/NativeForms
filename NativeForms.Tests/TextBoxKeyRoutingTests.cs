using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A native <see cref="TextBox"/> previews its keys through the form's dialog-key chain (the
/// <c>ITextBoxPeer.KeyDown</c> seam): Enter reaches the <see cref="Form.AcceptButton"/>, Tab
/// navigates, unless <see cref="TextBox.AcceptsReturn"/>/<see cref="TextBox.AcceptsTab"/> keep the
/// key for the multiline editor.
/// </summary>
[TestFixture]
internal sealed class TextBoxKeyRoutingTests
{
    private static HeadlessTextBoxPeer EditorOf(TextBox box) => (HeadlessTextBoxPeer)box.Peer!;

    [Test]
    public void Enter_in_a_single_line_box_clicks_the_accept_button()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var ok = new Button();
        var clicks = 0;
        ok.Click += (_, _) => ++clicks;
        var box = new TextBox();
        form.Controls.Add(ok);
        form.Controls.Add(box);
        form.AcceptButton = ok;
        Application.Run(form, backend);

        var handled = EditorOf(box).SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(handled, Is.True, "the consumed Enter never reaches the native editor");
        });
    }

    [Test]
    public void A_multiline_box_that_accepts_return_keeps_Enter()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var ok = new Button();
        var clicks = 0;
        ok.Click += (_, _) => ++clicks;
        var box = new TextBox { Multiline = true, AcceptsReturn = true };
        form.Controls.Add(ok);
        form.Controls.Add(box);
        form.AcceptButton = ok;
        Application.Run(form, backend);

        var handled = EditorOf(box).SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero, "the default button is not activated");
            Assert.That(handled, Is.False, "Enter is left to the editor as a newline");
        });
    }

    [Test]
    public void Tab_navigates_to_the_next_control()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var first = new TextBox();
        var second = new TextBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        Application.Run(form, backend);
        first.Focus();

        var handled = EditorOf(first).SimulateKeyDown(Keys.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(form.ActiveControl, Is.SameAs(second));
        });
    }

    [Test]
    public void A_box_that_accepts_tab_keeps_it()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var first = new TextBox { Multiline = true, AcceptsTab = true };
        var second = new TextBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        Application.Run(form, backend);
        first.Focus();

        var handled = EditorOf(first).SimulateKeyDown(Keys.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False, "Tab is left to the editor");
            Assert.That(form.ActiveControl, Is.SameAs(first), "focus does not move");
        });
    }
}
