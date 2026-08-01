using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.Windows;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The two side buttons a mouse puts under the thumb, which desktops map to back and forward.
/// </summary>
/// <remarks>
/// Each backend numbers them differently and none of them says so out loud, which is why a missing
/// arm here is silent: the button simply arrives as <see cref="MouseButtons.None"/> and every control
/// ignores it. That is exactly how they went unmapped — a file browser wired to navigate on them did
/// nothing at all, with no error anywhere to say why.
/// </remarks>
[TestFixture]
internal sealed class MouseSideButtonTests
{
    [Test]
    public void The_enum_names_both_side_buttons()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MouseButtons.XButton1, Is.Not.EqualTo(MouseButtons.None));
            Assert.That(MouseButtons.XButton2, Is.Not.EqualTo(MouseButtons.None));
            Assert.That(MouseButtons.XButton1, Is.Not.EqualTo(MouseButtons.XButton2));
        });
    }

    /// <summary>The flags have to stay distinct bits: a chord is reported as one combined value.</summary>
    [Test]
    public void The_buttons_are_separate_flags()
    {
        var chord = MouseButtons.Left | MouseButtons.XButton1;

        Assert.Multiple(() =>
        {
            Assert.That(chord.HasFlag(MouseButtons.XButton1), Is.True);
            Assert.That(chord.HasFlag(MouseButtons.XButton2), Is.False);
            Assert.That(chord.HasFlag(MouseButtons.Left), Is.True);
        });
    }

    // GDK numbers the side buttons 8 and 9, after the scroll axes that used to be buttons 4 to 7.
    [Test]
    [TestCase(1u, MouseButtons.Left)]
    [TestCase(2u, MouseButtons.Middle)]
    [TestCase(3u, MouseButtons.Right)]
    [TestCase(8u, MouseButtons.XButton1)]
    [TestCase(9u, MouseButtons.XButton2)]
    public void Gtk_maps_every_button_it_is_sent(uint button, MouseButtons expected)
        => Assert.That(GtkCanvasPeer.ToButton(button), Is.EqualTo(expected));

    /// <summary>Scroll arrives on its own signal, so those button numbers are not pointer presses.</summary>
    [Test]
    [TestCase(4u)]
    [TestCase(5u)]
    public void Gtk_ignores_the_scroll_axis_button_numbers(uint button)
        => Assert.That(GtkCanvasPeer.ToButton(button), Is.EqualTo(MouseButtons.None));

    // Windows sends one message for both and says which in wParam's high word.
    [Test]
    [TestCase(0x0001, MouseButtons.XButton1)]
    [TestCase(0x0002, MouseButtons.XButton2)]
    public void Win32_reads_the_button_out_of_the_high_word(int highWord, MouseButtons expected)
        => Assert.That(Win32CanvasPeer.XButton(highWord << 16), Is.EqualTo(expected));

    /// <summary>The low word carries the modifier keys, and must not change which button it is.</summary>
    [Test]
    public void Win32_ignores_the_modifier_flags_in_the_low_word()
        => Assert.That(Win32CanvasPeer.XButton((0x0002 << 16) | 0x0008), Is.EqualTo(MouseButtons.XButton2));

    [Test]
    public void A_side_button_press_reaches_the_control()
    {
        var box = new CheckBox { Bounds = new(0, 0, 80, 24) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        var canvas = (HeadlessCanvasPeer)box.Peer!;

        MouseEventArgs? down = null;
        box.MouseDown += (_, e) => down = e;
        canvas.RaiseMouseDown(10, 5, MouseButtons.XButton1);

        Assert.That(down?.Button, Is.EqualTo(MouseButtons.XButton1));
    }
}
