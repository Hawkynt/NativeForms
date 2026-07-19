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

        Assert.That(backend.MessageBoxes, Is.EqualTo(new[]
        {
            ("Save changes?", "Editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question),
        }));
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
}
