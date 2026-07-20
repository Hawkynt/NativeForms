using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The Windows Forms window lifecycle: <see cref="Form.Load"/> fires after realization and before
/// the first show, <see cref="Form.FormClosing"/> previews every close path (user close button,
/// <see cref="Form.Close"/>, modal teardown) with the right <see cref="CloseReason"/> and can veto
/// it, modeless <see cref="Form.Show"/> needs a running loop, and <see cref="Form.ClientSize"/>
/// rounds out the port surface.
/// </summary>
[TestFixture]
internal sealed class FormLifecycleTests
{
    [Test]
    public void Load_fires_after_realization_and_before_the_first_show()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var loads = 0;
        var realizedAtLoad = false;
        var shownAtLoad = true;
        form.Load += (_, _) =>
        {
            ++loads;
            realizedAtLoad = form.WindowPeer is not null;
            shownAtLoad = ((HeadlessWindowPeer)form.WindowPeer!).Shown;
        };

        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(loads, Is.EqualTo(1));
            Assert.That(realizedAtLoad, Is.True, "Load must see live peers");
            Assert.That(shownAtLoad, Is.False, "Load precedes the first show");
        });
    }

    [Test]
    public void Load_fires_on_the_ShowDialog_path_too()
    {
        var backend = new HeadlessBackend { ModalAction = static window => window.Close() };
        var dialog = new Form();
        var loads = 0;
        dialog.Load += (_, _) => ++loads;

        dialog.ShowDialog(null, backend);

        Assert.That(loads, Is.EqualTo(1));
    }

    [Test]
    public void A_user_close_raises_FormClosing_with_UserClosing_then_FormClosed()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var sequence = new List<string>();
        form.FormClosing += (_, e) => sequence.Add($"closing:{e.CloseReason}");
        form.FormClosed += (_, _) => sequence.Add("closed");
        Application.Run(form, backend);
        var peer = backend.Created.OfType<HeadlessWindowPeer>().Single();

        peer.Close();

        Assert.That(sequence, Is.EqualTo(new[] { "closing:UserClosing", "closed" }));
    }

    [Test]
    public void Form_Close_reports_ProgrammaticClosing()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        CloseReason? reason = null;
        form.FormClosing += (_, e) => reason = e.CloseReason;
        Application.Run(form, backend);

        form.Close();

        Assert.That(reason, Is.EqualTo(CloseReason.ProgrammaticClosing));
    }

    [Test]
    public void Cancelling_FormClosing_keeps_the_window_open()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var veto = true;
        var closed = 0;
        form.FormClosing += (_, e) => e.Cancel = veto;
        form.FormClosed += (_, _) => ++closed;
        Application.Run(form, backend);
        var peer = backend.Created.OfType<HeadlessWindowPeer>().Single();

        peer.Close();
        Assert.Multiple(() =>
        {
            Assert.That(peer.Shown, Is.True, "the vetoed close leaves the window open");
            Assert.That(closed, Is.Zero);
        });

        veto = false;
        form.Close();
        Assert.Multiple(() =>
        {
            Assert.That(peer.Shown, Is.False);
            Assert.That(closed, Is.EqualTo(1));
        });
    }

    [Test]
    public void Modal_teardown_by_DialogResult_runs_the_FormClosing_veto()
    {
        var dialog = new Form();
        var reasons = new List<CloseReason>();
        dialog.FormClosing += (_, e) => reasons.Add(e.CloseReason);
        var backend = new HeadlessBackend { ModalAction = _ => dialog.DialogResult = DialogResult.OK };

        var result = dialog.ShowDialog(null, backend);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(DialogResult.OK));
            Assert.That(reasons, Is.EqualTo(new[] { CloseReason.ProgrammaticClosing }), "DialogResult closes through Form.Close");
        });
    }

    [Test]
    public void Show_throws_without_a_running_message_loop()
    {
        var form = new Form();
        Assert.Throws<InvalidOperationException>(form.Show);
    }

    [Test]
    public void Show_realizes_and_shows_a_modeless_form_inside_the_loop()
    {
        var backend = new HeadlessBackend();
        var main = new Form();
        var extra = new Form { Bounds = new(0, 0, 100, 50) };
        var closed = 0;
        extra.FormClosed += (_, _) => ++closed;
        backend.RunAction = () =>
        {
            extra.Show();

            var peer = (HeadlessWindowPeer)extra.WindowPeer!;
            Assert.That(peer.Shown, Is.True, "Show realizes against the running backend and shows");

            peer.Close();
            Assert.That(closed, Is.EqualTo(1), "the modeless window participates in Closed as usual");
        };

        Application.Run(main, backend);

        Assert.That(backend.Created.OfType<HeadlessWindowPeer>().Count(), Is.EqualTo(2));
    }

    [Test]
    public void ClientSize_mirrors_Size_until_peers_report_nonclient_metrics()
    {
        var form = new Form { Bounds = new(0, 0, 300, 200) };
        Assert.That(form.ClientSize, Is.EqualTo(new Size(300, 200)));

        form.ClientSize = new(400, 250);
        Assert.That(form.Size, Is.EqualTo(new Size(400, 250)));
    }
}
