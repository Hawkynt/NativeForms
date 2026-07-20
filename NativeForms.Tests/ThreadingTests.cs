using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// UI-thread affinity and marshalling: <see cref="Control.InvokeRequired"/> anchored to the loop
/// thread <see cref="Application.Run(Form)"/> records, <see cref="Control.Invoke"/>/
/// <see cref="Control.BeginInvoke"/> queueing through the backend, and the
/// <see cref="NativeFormsSynchronizationContext"/> the loop installs. The headless backend executes
/// posts inline and records them, so every path is observable without threads — except the
/// off-thread checks, which use a real joined thread.
/// </summary>
[TestFixture]
internal sealed class ThreadingTests
{
    [Test]
    public void InvokeRequired_is_false_without_a_running_loop()
        => Assert.That(new Button().InvokeRequired, Is.False);

    [Test]
    public void InvokeRequired_is_false_on_the_loop_thread()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        bool? observed = null;
        backend.RunAction = () => observed = form.InvokeRequired;

        Application.Run(form, backend);

        Assert.That(observed, Is.False);
    }

    [Test]
    public void InvokeRequired_is_true_off_the_loop_thread()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        bool? observed = null;
        backend.RunAction = () =>
        {
            var thread = new Thread(() => observed = form.InvokeRequired);
            thread.Start();
            thread.Join();
        };

        Application.Run(form, backend);

        Assert.That(observed, Is.True);
    }

    [Test]
    public void BeginInvoke_posts_through_the_backend_and_the_queue_drains()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var ran = 0;
        backend.RunAction = () =>
        {
            form.BeginInvoke(() => ++ran);
            form.BeginInvoke(() => ++ran);
        };

        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(2), "the headless loop executes posts inline");
            Assert.That(backend.PostedActions, Has.Count.EqualTo(2), "both actions went through the backend seam");
        });
    }

    [Test]
    public void BeginInvoke_without_loop_or_realization_throws()
        => Assert.Throws<InvalidOperationException>(() => new Button().BeginInvoke(() => { }));

    [Test]
    public void BeginInvoke_works_on_a_realized_control_without_a_loop()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var button = new Button();
        form.Controls.Add(button);
        form.RealizeWindow(backend);
        var ran = false;

        button.BeginInvoke(() => ran = true);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.True);
            Assert.That(backend.PostedActions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Invoke_runs_inline_on_the_loop_thread_without_posting()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var ran = false;
        backend.RunAction = () => form.Invoke(() => ran = true);

        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.True);
            Assert.That(backend.PostedActions, Is.Empty, "same-thread Invoke must not round-trip the queue");
        });
    }

    [Test]
    public void Invoke_from_another_thread_marshals_and_blocks_until_done()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var ran = false;
        backend.RunAction = () =>
        {
            var thread = new Thread(() => form.Invoke(() => ran = true));
            thread.Start();
            thread.Join();
        };

        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.True);
            Assert.That(backend.PostedActions, Has.Count.EqualTo(1), "cross-thread Invoke goes through the backend queue");
        });
    }

    [Test]
    public void Invoke_from_another_thread_propagates_the_exception_to_the_caller()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        Exception? caught = null;
        backend.RunAction = () =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    form.Invoke(() => throw new InvalidTimeZoneException("boom"));
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });
            thread.Start();
            thread.Join();
        };

        Application.Run(form, backend);

        Assert.That(caught, Is.InstanceOf<InvalidTimeZoneException>().With.Message.EqualTo("boom"));
    }

    [Test]
    public void SynchronizationContext_is_installed_during_run_and_removed_after()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        var before = SynchronizationContext.Current;
        SynchronizationContext? during = null;
        backend.RunAction = () => during = SynchronizationContext.Current;

        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(during, Is.InstanceOf<NativeFormsSynchronizationContext>());
            Assert.That(SynchronizationContext.Current, Is.SameAs(before), "the previous context is restored");
        });
    }

    [Test]
    public void SynchronizationContext_Post_queues_through_the_backend()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        object? observedState = null;
        backend.RunAction = () => SynchronizationContext.Current!.Post(state => observedState = state, "payload");

        Application.Run(form, backend);

        Assert.Multiple(() =>
        {
            Assert.That(observedState, Is.EqualTo("payload"));
            Assert.That(backend.PostedActions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SynchronizationContext_Send_propagates_exceptions_inline()
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        Exception? caught = null;
        backend.RunAction = () =>
        {
            try
            {
                SynchronizationContext.Current!.Send(_ => throw new InvalidTimeZoneException("boom"), null);
            }
            catch (Exception exception)
            {
                caught = exception;
            }
        };

        Application.Run(form, backend);

        Assert.That(caught, Is.InstanceOf<InvalidTimeZoneException>().With.Message.EqualTo("boom"));
    }
}
