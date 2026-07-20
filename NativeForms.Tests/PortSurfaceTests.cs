using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The port-surface gaps a straight WinForms namespace swap trips over: the classic
/// <see cref="Keys"/> members with their Win32 virtual-key values, the <see cref="Cursors"/>
/// aliases and splitter/help shapes, and the static <see cref="Clipboard"/> facade over the
/// backend seams.
/// </summary>
[TestFixture]
internal sealed class PortSurfaceTests
{
    [Test]
    public void Keys_members_carry_the_Win32_virtual_key_values()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Keys.Return, Is.EqualTo(Keys.Enter), "Return aliases Enter");
            Assert.That((int)Keys.ShiftKey, Is.EqualTo(0x10));
            Assert.That((int)Keys.ControlKey, Is.EqualTo(0x11));
            Assert.That((int)Keys.Menu, Is.EqualTo(0x12));
            Assert.That((int)Keys.Apps, Is.EqualTo(0x5D));
            Assert.That((int)Keys.NumPad0, Is.EqualTo(0x60));
            Assert.That((int)Keys.NumPad9, Is.EqualTo(0x69));
            Assert.That((int)Keys.Decimal, Is.EqualTo(0x6E));
            Assert.That((int)Keys.Divide, Is.EqualTo(0x6F));
            Assert.That((int)Keys.Oemplus, Is.EqualTo(0xBB));
            Assert.That((int)Keys.Oemcomma, Is.EqualTo(0xBC));
            Assert.That((int)Keys.OemMinus, Is.EqualTo(0xBD));
            Assert.That((int)Keys.OemPeriod, Is.EqualTo(0xBE));
        });
    }

    [Test]
    public void Cursors_aliases_share_the_stock_instances_and_new_shapes_exist()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cursors.Default, Is.SameAs(Cursors.Arrow), "Default aliases the arrow");
            Assert.That(Cursors.WaitCursor, Is.SameAs(Cursors.Wait), "WaitCursor aliases the wait shape");
            Assert.That(Cursors.SizeAll.Kind, Is.EqualTo(CursorKind.SizeAll));
            Assert.That(Cursors.Help.Kind, Is.EqualTo(CursorKind.Help));
            Assert.That(Cursors.AppStarting.Kind, Is.EqualTo(CursorKind.AppStarting));
            Assert.That(Cursors.VSplit.Kind, Is.EqualTo(CursorKind.VSplit));
            Assert.That(Cursors.HSplit.Kind, Is.EqualTo(CursorKind.HSplit));
        });
    }

    [Test]
    public void Clipboard_routes_through_the_running_backend()
    {
        var backend = new HeadlessBackend { ClipboardText = null };
        var form = new Form();
        backend.RunAction = () =>
        {
            Assert.That(Clipboard.ContainsText(), Is.False);
            Assert.That(Clipboard.GetText(), Is.EqualTo(string.Empty), "no text reads as the empty string");

            Clipboard.SetText("hello");
            Assert.That(backend.ClipboardTexts, Is.EqualTo(new[] { "hello" }));

            backend.ClipboardText = "hello";
            Assert.That(Clipboard.ContainsText(), Is.True);
            Assert.That(Clipboard.GetText(), Is.EqualTo("hello"));
        };

        Application.Run(form, backend);
        Assert.That(backend.DidRun, Is.True);
    }

    [Test]
    public void Clipboard_without_a_loop_throws_and_SetText_rejects_empty()
    {
        Assert.Throws<InvalidOperationException>(static () => Clipboard.SetText("x"));
        Assert.Throws<InvalidOperationException>(static () => Clipboard.GetText());
        Assert.Throws<InvalidOperationException>(static () => Clipboard.ContainsText());

        var backend = new HeadlessBackend();
        backend.RunAction = static () => Assert.Throws<ArgumentException>(static () => Clipboard.SetText(string.Empty));
        Application.Run(new Form(), backend);
    }

    [Test]
    public void Theme_exposes_the_double_click_time()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Hawkynt.NativeForms.Drawing.DefaultTheme.Instance.DoubleClickTime, Is.EqualTo(500));
            Assert.That(new StubTheme { DoubleClickTime = 250 }.DoubleClickTime, Is.EqualTo(250));
        });
    }
}
