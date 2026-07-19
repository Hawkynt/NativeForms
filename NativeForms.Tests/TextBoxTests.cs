using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class TextBoxTests
{
    private static HeadlessBackend Realize(TextBox box)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessTextBoxPeer PeerOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessTextBoxPeer>().Single();

    [Test]
    public void Buffered_state_flushes_to_the_peer_on_realization()
    {
        var box = new TextBox
        {
            Text = "hello",
            Bounds = new(10, 10, 120, 24),
            Multiline = true,
            PlaceholderText = "hint",
            PasswordChar = '*',
            ReadOnly = true,
            MaxLength = 40,
            SelectionStart = 1,
            SelectionLength = 3,
        };

        var backend = Realize(box);

        var peer = PeerOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(peer.Text, Is.EqualTo("hello"));
            Assert.That(peer.Bounds, Is.EqualTo(new Rectangle(10, 10, 120, 24)));
            Assert.That(peer.Multiline, Is.True);
            Assert.That(peer.Placeholder, Is.EqualTo("hint"));
            Assert.That(peer.PasswordChar, Is.EqualTo('*'));
            Assert.That(peer.ReadOnly, Is.True);
            Assert.That(peer.MaxLength, Is.EqualTo(40));
            Assert.That(peer.SelectionStart, Is.EqualTo(1));
            Assert.That(peer.SelectionLength, Is.EqualTo(3));
        });
    }

    [Test]
    public void Programmatic_text_set_reaches_the_peer_and_raises_TextChanged_once()
    {
        var box = new TextBox();
        var backend = Realize(box);
        var changes = 0;
        box.TextChanged += (_, _) => ++changes;

        box.Text = "abc";

        Assert.Multiple(() =>
        {
            Assert.That(PeerOf(backend).Text, Is.EqualTo("abc"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void Peer_echo_of_a_programmatic_set_raises_no_second_TextChanged()
    {
        var box = new TextBox();
        var backend = Realize(box);
        var changes = 0;
        box.TextChanged += (_, _) => ++changes;

        box.Text = "abc";
        PeerOf(backend).SimulateUserInput("abc");

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(box.Text, Is.EqualTo("abc"));
        });
    }

    [Test]
    public void User_input_raises_TextChanged_once_and_updates_Text()
    {
        var box = new TextBox();
        var backend = Realize(box);
        var changes = 0;
        box.TextChanged += (_, _) => ++changes;

        PeerOf(backend).SimulateUserInput("typed");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("typed"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void CharacterCasing_applies_to_programmatic_text()
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper };
        var backend = Realize(box);

        box.Text = "abc";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("ABC"));
            Assert.That(PeerOf(backend).Text, Is.EqualTo("ABC"));
        });
    }

    [Test]
    public void CharacterCasing_applies_to_user_input_and_corrects_the_widget()
    {
        var box = new TextBox { CharacterCasing = CharacterCasing.Upper };
        var backend = Realize(box);
        var changes = 0;
        box.TextChanged += (_, _) => ++changes;

        PeerOf(backend).SimulateUserInput("abc");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("ABC"));
            Assert.That(PeerOf(backend).Text, Is.EqualTo("ABC"));
            Assert.That(changes, Is.EqualTo(1));
        });
    }

    [Test]
    public void CharacterCasing_change_recases_the_existing_text()
    {
        var box = new TextBox { Text = "MiXeD" };

        box.CharacterCasing = CharacterCasing.Lower;

        Assert.That(box.Text, Is.EqualTo("mixed"));
    }

    [Test]
    public void Selection_is_buffered_before_realization_and_live_after()
    {
        var box = new TextBox { Text = "hello world", SelectionStart = 2, SelectionLength = 3 };
        Assert.Multiple(() =>
        {
            Assert.That(box.SelectionStart, Is.EqualTo(2));
            Assert.That(box.SelectionLength, Is.EqualTo(3));
        });

        var backend = Realize(box);
        var peer = PeerOf(backend);
        Assert.Multiple(() =>
        {
            Assert.That(peer.SelectionStart, Is.EqualTo(2));
            Assert.That(peer.SelectionLength, Is.EqualTo(3));
        });

        // The widget moves the caret while the user types; the control reads it back live.
        peer.SimulateUserInput("typed");
        Assert.Multiple(() =>
        {
            Assert.That(box.SelectionStart, Is.EqualTo(5));
            Assert.That(box.SelectionLength, Is.EqualTo(0));
        });

        box.SelectionStart = 1;
        box.SelectionLength = 2;
        Assert.Multiple(() =>
        {
            Assert.That(peer.SelectionStart, Is.EqualTo(1));
            Assert.That(peer.SelectionLength, Is.EqualTo(2));
        });
    }

    [Test]
    public void Settings_changed_after_realization_forward_to_the_peer()
    {
        var box = new TextBox();
        var backend = Realize(box);
        var peer = PeerOf(backend);

        box.PlaceholderText = "search…";
        box.PasswordChar = '#';
        box.ReadOnly = true;
        box.MaxLength = 8;

        Assert.Multiple(() =>
        {
            Assert.That(peer.Placeholder, Is.EqualTo("search…"));
            Assert.That(peer.PasswordChar, Is.EqualTo('#'));
            Assert.That(peer.ReadOnly, Is.True);
            Assert.That(peer.MaxLength, Is.EqualTo(8));
        });
    }

    [Test]
    public void UseSystemPasswordChar_overrides_PasswordChar()
    {
        var box = new TextBox { PasswordChar = '*', UseSystemPasswordChar = true };
        var backend = Realize(box);
        var peer = PeerOf(backend);

        Assert.That(peer.PasswordChar, Is.EqualTo('●'));

        box.UseSystemPasswordChar = false;

        Assert.That(peer.PasswordChar, Is.EqualTo('*'));
    }

    [Test]
    public void Multiline_flip_after_realization_reaches_the_peer_and_keeps_the_text()
    {
        var box = new TextBox { Text = "keep" };
        var backend = Realize(box);
        var peer = PeerOf(backend);

        box.Multiline = true;

        Assert.Multiple(() =>
        {
            Assert.That(peer.Multiline, Is.True);
            Assert.That(peer.Calls, Does.Contain("multiline=True"));
            Assert.That(peer.Text, Is.EqualTo("keep"));
        });
    }

    [Test]
    public void SelectedText_reflects_and_replaces_the_selection()
    {
        var box = new TextBox { Text = "hello world", SelectionStart = 6, SelectionLength = 5 };

        Assert.That(box.SelectedText, Is.EqualTo("world"));

        box.SelectedText = "there";

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("hello there"));
            Assert.That(box.SelectionStart, Is.EqualTo(11));
            Assert.That(box.SelectionLength, Is.EqualTo(0));
        });
    }

    [Test]
    public void AcceptsReturn_and_AcceptsTab_default_off_and_are_settable()
    {
        var box = new TextBox();
        Assert.Multiple(() =>
        {
            Assert.That(box.AcceptsReturn, Is.False);
            Assert.That(box.AcceptsTab, Is.False);
        });

        box.AcceptsReturn = true;
        box.AcceptsTab = true;

        Assert.Multiple(() =>
        {
            Assert.That(box.AcceptsReturn, Is.True);
            Assert.That(box.AcceptsTab, Is.True);
        });
    }
}
