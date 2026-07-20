using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Button.Command"/>/<see cref="Button.CommandParameter"/>: the MVVM wiring a bound
/// button gets — <see cref="Control.Enabled"/> follows the command's guard, a click executes it,
/// and detaching or replacing the command drops the guard subscription.
/// </summary>
[TestFixture]
internal sealed class ButtonCommandTests
{
    [Test]
    public void Attaching_a_command_applies_its_guard_to_enabled()
    {
        var button = new Button();
        button.Command = new RelayCommand(static () => { }, static () => false);

        Assert.That(button.Enabled, Is.False);
    }

    [Test]
    public void Can_execute_changed_re_queries_the_guard()
    {
        var allowed = false;
        var command = new RelayCommand(static () => { }, () => allowed);
        var button = new Button { Command = command };

        Assert.That(button.Enabled, Is.False);

        allowed = true;
        command.RaiseCanExecuteChanged();
        Assert.That(button.Enabled, Is.True);

        allowed = false;
        command.RaiseCanExecuteChanged();
        Assert.That(button.Enabled, Is.False);
    }

    [Test]
    public void Click_executes_the_command_with_the_parameter()
    {
        object? received = "unset";
        var button = new Button
        {
            Command = new RelayCommand<string>(p => received = p),
            CommandParameter = "payload",
        };

        button.PerformClick();

        Assert.That(received, Is.EqualTo("payload"));
    }

    [Test]
    public void A_declining_command_disables_the_button_and_swallows_PerformClick()
    {
        var executed = 0;
        var clicked = 0;
        var button = new Button { Command = new RelayCommand(() => ++executed, static () => false) };
        button.Click += (_, _) => ++clicked;

        button.PerformClick();

        // The guard drove Enabled to false, and PerformClick on a disabled control is a no-op —
        // the WinForms contract: neither the event nor the command fires.
        Assert.That(button.Enabled, Is.False);
        Assert.That(clicked, Is.Zero);
        Assert.That(executed, Is.Zero);
    }

    [Test]
    public void Changing_the_parameter_re_queries_the_guard()
    {
        var button = new Button
        {
            Command = new RelayCommand<string>(static _ => { }, static p => p == "yes"),
        };

        Assert.That(button.Enabled, Is.False, "null parameter declines");

        button.CommandParameter = "yes";
        Assert.That(button.Enabled, Is.True);

        button.CommandParameter = "no";
        Assert.That(button.Enabled, Is.False);
    }

    [Test]
    public void Replacing_the_command_unsubscribes_the_previous_guard()
    {
        var oldCommand = new RelayCommand(static () => { }, static () => false);
        var button = new Button { Command = oldCommand };

        button.Command = new RelayCommand(static () => { });
        Assert.That(button.Enabled, Is.True, "the new command's guard applies");

        button.Enabled = false;
        oldCommand.RaiseCanExecuteChanged();
        Assert.That(button.Enabled, Is.False, "the old command no longer drives Enabled");
    }

    [Test]
    public void Detaching_the_command_stops_execution_and_guard_updates()
    {
        var executed = 0;
        var command = new RelayCommand(() => ++executed, static () => false);
        var button = new Button { Command = command };

        button.Command = null;
        button.Enabled = true;

        command.RaiseCanExecuteChanged();
        Assert.That(button.Enabled, Is.True, "the detached command no longer drives Enabled");

        button.PerformClick();
        Assert.That(executed, Is.Zero);
    }
}
