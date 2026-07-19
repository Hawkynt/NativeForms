using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ModalTests
{
    [Test]
    public void ShowDialog_realizes_shows_and_runs_the_peer_modally()
    {
        var backend = new HeadlessBackend();
        var form = new Form { Text = "Dialog" };

        form.ShowDialog(null, backend);

        var window = backend.Created.OfType<HeadlessWindowPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(window.WasModal, Is.True);
            Assert.That(window.Shown, Is.True);
            Assert.That(window.ModalOwner, Is.Null);
            Assert.That(window.Text, Is.EqualTo("Dialog"));
        });
    }

    [Test]
    public void ShowDialog_passes_the_owners_window_peer()
    {
        var backend = new HeadlessBackend();
        var owner = new Form();
        Application.Run(owner, backend);
        var ownerPeer = backend.Created.OfType<HeadlessWindowPeer>().Single();

        var dialog = new Form();
        dialog.ShowDialog(owner, backend);

        var dialogPeer = backend.Created.OfType<HeadlessWindowPeer>().Last();
        Assert.Multiple(() =>
        {
            Assert.That(dialogPeer, Is.Not.SameAs(ownerPeer));
            Assert.That(dialogPeer.ModalOwner, Is.SameAs(ownerPeer));
        });
    }

    [Test]
    public void ShowDialog_defaults_to_Cancel_when_closed_without_a_result()
    {
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var form = new Form();

        var result = form.ShowDialog(null, backend);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.Cancel));
            Assert.That(form.DialogResult, Is.EqualTo(DialogResult.Cancel));
        });
    }

    [Test]
    public void ShowDialog_raises_FormClosed_and_unrealizes_the_form()
    {
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var form = new Form();
        var closed = 0;
        form.FormClosed += (_, _) => ++closed;

        form.ShowDialog(null, backend);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(form.Peer, Is.Null);
            Assert.That(backend.Created.OfType<HeadlessWindowPeer>().Single().Disposed, Is.True);
        });
    }

    [Test]
    public void Setting_DialogResult_while_modal_closes_with_that_result()
    {
        var form = new Form();
        var backend = new HeadlessBackend { ModalAction = _ => form.DialogResult = DialogResult.Yes };

        var result = form.ShowDialog(null, backend);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.Yes));
            Assert.That(backend.Created.OfType<HeadlessWindowPeer>().Single().CloseCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Button_click_with_DialogResult_closes_the_dialog_with_that_result()
    {
        var backend = new HeadlessBackend();
        backend.ModalAction = _ => backend.Created.OfType<HeadlessButtonPeer>().Single().RaiseClicked();
        var form = new Form();
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        form.Controls.Add(ok);

        var result = form.ShowDialog(null, backend);

        Assert.That(result, Is.EqualTo(DialogResult.OK));
    }

    [Test]
    public void Button_click_deep_in_the_tree_finds_the_owning_form()
    {
        var backend = new HeadlessBackend();
        backend.ModalAction = _ => backend.Created.OfType<HeadlessButtonPeer>().Single().RaiseClicked();
        var form = new Form();
        var panel = new Panel { Bounds = new(0, 0, 100, 100) };
        var retry = new Button { DialogResult = DialogResult.Retry };
        panel.Controls.Add(retry);
        form.Controls.Add(panel);

        var result = form.ShowDialog(null, backend);

        Assert.That(result, Is.EqualTo(DialogResult.Retry));
    }

    [Test]
    public void Button_click_without_DialogResult_leaves_the_dialog_open()
    {
        var backend = new HeadlessBackend();
        backend.ModalAction = _ => backend.Created.OfType<HeadlessButtonPeer>().Single().RaiseClicked();
        var form = new Form();
        form.Controls.Add(new Button());

        form.ShowDialog(null, backend);

        Assert.That(backend.Created.OfType<HeadlessWindowPeer>().Single().CloseCount, Is.Zero);
    }

    [Test]
    public void Button_click_outside_a_modal_loop_sets_the_result_but_does_not_close()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var ok = new Button { DialogResult = DialogResult.OK };
        form.Controls.Add(ok);
        Application.Run(form, backend);

        backend.Created.OfType<HeadlessButtonPeer>().Single().RaiseClicked();

        Assert.Multiple(() =>
        {
            Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
            Assert.That(backend.Created.OfType<HeadlessWindowPeer>().Single().CloseCount, Is.Zero);
        });
    }

    [Test]
    public void ShowDialog_can_run_the_same_form_again()
    {
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var form = new Form { Text = "Again" };

        var first = form.ShowDialog(null, backend);
        var second = form.ShowDialog(null, backend);

        var windows = backend.Created.OfType<HeadlessWindowPeer>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(DialogResult.Cancel));
            Assert.That(second, Is.EqualTo(DialogResult.Cancel));
            Assert.That(windows, Has.Length.EqualTo(2));
            Assert.That(windows[1].Text, Is.EqualTo("Again"));
        });
    }

    [Test]
    public void CancelButton_assignment_defaults_the_buttons_result_to_Cancel()
    {
        var form = new Form();
        var cancel = new Button();
        var custom = new Button { DialogResult = DialogResult.No };

        form.CancelButton = cancel;
        Assert.That(cancel.DialogResult, Is.EqualTo(DialogResult.Cancel));

        form.CancelButton = custom;
        Assert.That(custom.DialogResult, Is.EqualTo(DialogResult.No));
    }

    [Test]
    public void AcceptButton_is_stored_without_touching_the_buttons_result()
    {
        var form = new Form();
        var ok = new Button();

        form.AcceptButton = ok;

        Assert.Multiple(() =>
        {
            Assert.That(form.AcceptButton, Is.SameAs(ok));
            Assert.That(ok.DialogResult, Is.EqualTo(DialogResult.None));
        });
    }

    [Test]
    public void ShowDialog_while_already_modal_throws()
    {
        var form = new Form();
        var backend = new HeadlessBackend();
        backend.ModalAction = _ =>
        {
            Assert.Throws<InvalidOperationException>(() => form.ShowDialog(null, backend));
            form.Close();
        };

        form.ShowDialog(null, backend);

        Assert.That(backend.Created.OfType<HeadlessWindowPeer>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ShowDialog_without_a_running_backend_throws()
        => Assert.Throws<InvalidOperationException>(() => new Form().ShowDialog());
}
