using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class MessageBoxTests
{
    [Test]
    public void Show_forwards_text_caption_buttons_and_icon_to_the_backend()
    {
        var backend = new HeadlessBackend();

        MessageBox.Show(backend, "Save changes?", "Editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        var call = backend.MessageBoxes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Text, Is.EqualTo("Save changes?"));
            Assert.That(call.Caption, Is.EqualTo("Editor"));
            Assert.That(call.Buttons, Is.EqualTo(MessageBoxButtons.YesNoCancel));
            Assert.That(call.Icon, Is.EqualTo(MessageBoxIcon.Question));
            Assert.That(call.Owner, Is.Null, "the ownerless overload passes no owner");
        });
    }

    [Test]
    public void Show_returns_the_scripted_result()
    {
        var backend = new HeadlessBackend { MessageBoxResult = DialogResult.No };

        var result = MessageBox.Show(backend, "Sure?", "App", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        Assert.That(result, Is.EqualTo(DialogResult.No));
    }

    [TestCase(MessageBoxButtons.OK)]
    [TestCase(MessageBoxButtons.OKCancel)]
    [TestCase(MessageBoxButtons.AbortRetryIgnore)]
    [TestCase(MessageBoxButtons.YesNo)]
    [TestCase(MessageBoxButtons.YesNoCancel)]
    [TestCase(MessageBoxButtons.RetryCancel)]
    public void Show_forwards_every_button_set(MessageBoxButtons buttons)
    {
        var backend = new HeadlessBackend();

        MessageBox.Show(backend, "x", "y", buttons, MessageBoxIcon.None);

        Assert.That(backend.MessageBoxes.Single().Buttons, Is.EqualTo(buttons));
    }

    [Test]
    public void Show_without_a_running_backend_throws()
        => Assert.Throws<InvalidOperationException>(() => MessageBox.Show("boom"));

    [Test]
    public void Show_with_an_owner_forwards_the_owner_window_peer()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        Application.Run(form, backend);

        var result = MessageBox.Show(form, "Sure?", "App", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

        var call = backend.MessageBoxes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(backend.MessageBoxResult));
            Assert.That(call.Owner, Is.SameAs(form.WindowPeer), "the box is owned by the form's window");
            Assert.That(call.Buttons, Is.EqualTo(MessageBoxButtons.OKCancel));
        });
    }

    [Test]
    public void AbortRetryIgnore_carries_the_windows_forms_value()
        => Assert.That((int)MessageBoxButtons.AbortRetryIgnore, Is.EqualTo(2));
}
