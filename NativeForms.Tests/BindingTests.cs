using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class BindingTests
{
    private sealed class Person : ObservableObject
    {
        public string Name
        {
            get => field;
            set => this.SetProperty(ref field, value);
        } = string.Empty;
    }

    private sealed class Counter : ObservableObject
    {
        public int Count
        {
            get => field;
            set => this.SetProperty(ref field, value);
        }
    }

    private sealed class ValidatingPerson : ObservableObject
    {
        public string Name
        {
            get => field;
            set
            {
                if (this.SetProperty(ref field, value))
                    this.SetError(nameof(this.Name), value.Length == 0 ? "Name is required." : null);
            }
        } = "seed";

        public void ReportNameError(string? error) => this.SetError(nameof(this.Name), error);
    }

    /// <summary>A stand-in for a control property with a change notification, for two-way tests.</summary>
    private sealed class FakeTarget
    {
        public event EventHandler? ValueChanged;

        public string Value
        {
            get => field;
            set
            {
                if (field == value)
                    return;

                field = value;
                this.ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        } = string.Empty;
    }

    [Test]
    public void SetProperty_raises_changing_then_changed_and_reports_change()
    {
        var person = new Person();
        var order = new List<string>();
        person.PropertyChanging += (_, e) => order.Add($"changing:{e.PropertyName}");
        person.PropertyChanged += (_, e) => order.Add($"changed:{e.PropertyName}");

        person.Name = "Ada";

        Assert.That(order, Is.EqualTo(new[] { "changing:Name", "changed:Name" }));
    }

    [Test]
    public void SetProperty_is_noop_when_value_unchanged()
    {
        var person = new Person { Name = "Ada" };
        var raised = 0;
        person.PropertyChanged += (_, _) => ++raised;

        person.Name = "Ada";

        Assert.That(raised, Is.EqualTo(0));
    }

    [Test]
    public void RelayCommand_executes_and_honors_can_execute()
    {
        var runs = 0;
        var allowed = false;
        var command = new RelayCommand(() => ++runs, () => allowed);

        Assert.That(command.CanExecute(null), Is.False);
        allowed = true;
        Assert.That(command.CanExecute(null), Is.True);
        command.Execute(null);
        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public void OneWay_binding_pushes_source_to_target_on_change()
    {
        var person = new Person { Name = "initial" };
        var target = new FakeTarget();

        using var _ = new PropertyBinding<string>(
            person,
            nameof(Person.Name),
            () => person.Name,
            v => target.Value = v);

        Assert.That(target.Value, Is.EqualTo("initial"), "initial sync");

        person.Name = "updated";
        Assert.That(target.Value, Is.EqualTo("updated"));
    }

    [Test]
    public void TwoWay_binding_writes_target_changes_back_to_source()
    {
        var person = new Person { Name = "start" };
        var target = new FakeTarget();

        using var _ = new PropertyBinding<string>(
            person,
            nameof(Person.Name),
            () => person.Name,
            v => target.Value = v,
            BindingMode.TwoWay,
            setSource: v => person.Name = v,
            getTarget: () => target.Value,
            subscribeTargetChanged: h => target.ValueChanged += h,
            unsubscribeTargetChanged: h => target.ValueChanged -= h);

        Assert.That(target.Value, Is.EqualTo("start"), "initial sync");

        target.Value = "edited";
        Assert.That(person.Name, Is.EqualTo("edited"));

        person.Name = "again";
        Assert.That(target.Value, Is.EqualTo("again"));
    }

    [Test]
    public void Disposed_binding_stops_propagating()
    {
        var person = new Person { Name = "a" };
        var target = new FakeTarget();
        var binding = new PropertyBinding<string>(
            person, nameof(Person.Name), () => person.Name, v => target.Value = v);

        binding.Dispose();
        person.Name = "b";

        Assert.That(target.Value, Is.EqualTo("a"));
    }

    [Test]
    public void Converting_binding_pushes_converted_source_values()
    {
        var counter = new Counter { Count = 3 };
        var target = new FakeTarget();

        using var _ = new PropertyBinding<int, string>(
            counter,
            nameof(Counter.Count),
            () => counter.Count,
            static count => $"#{count}",
            v => target.Value = v);

        Assert.That(target.Value, Is.EqualTo("#3"), "initial sync");

        counter.Count = 7;
        Assert.That(target.Value, Is.EqualTo("#7"));
    }

    [Test]
    public void Converting_binding_converts_target_writes_back()
    {
        var counter = new Counter { Count = 1 };
        var target = new FakeTarget();

        using var _ = new PropertyBinding<int, string>(
            counter,
            nameof(Counter.Count),
            () => counter.Count,
            static count => count.ToString(),
            v => target.Value = v,
            BindingMode.TwoWay,
            convertBack: static text => int.Parse(text),
            setSource: v => counter.Count = v,
            getTarget: () => target.Value,
            subscribeTargetChanged: h => target.ValueChanged += h,
            unsubscribeTargetChanged: h => target.ValueChanged -= h);

        target.Value = "42";
        Assert.That(counter.Count, Is.EqualTo(42));

        counter.Count = 5;
        Assert.That(target.Value, Is.EqualTo("5"));
    }

    [Test]
    public void Converting_binding_requires_convert_back_for_two_way()
    {
        var counter = new Counter();
        var target = new FakeTarget();

        Assert.Throws<ArgumentException>(() => new PropertyBinding<int, string>(
            counter,
            nameof(Counter.Count),
            () => counter.Count,
            static count => count.ToString(),
            v => target.Value = v,
            BindingMode.TwoWay,
            setSource: v => counter.Count = v,
            getTarget: () => target.Value,
            subscribeTargetChanged: h => target.ValueChanged += h,
            unsubscribeTargetChanged: h => target.ValueChanged -= h));
    }

    [Test]
    public void SetError_raises_ErrorsChanged_and_GetError_reports_it()
    {
        var person = new ValidatingPerson();
        var notified = new List<string?>();
        person.ErrorsChanged += (_, e) => notified.Add(e.PropertyName);

        person.Name = string.Empty;

        Assert.That(person.GetError(nameof(ValidatingPerson.Name)), Is.EqualTo("Name is required."));
        Assert.That(notified, Is.EqualTo(new[] { nameof(ValidatingPerson.Name) }));
    }

    [Test]
    public void SetError_null_clears_the_error_and_notifies()
    {
        var person = new ValidatingPerson();
        person.Name = string.Empty;

        var notified = 0;
        person.ErrorsChanged += (_, _) => ++notified;

        person.Name = "Ada";

        Assert.That(person.GetError(nameof(ValidatingPerson.Name)), Is.Null);
        Assert.That(notified, Is.EqualTo(1));
    }

    [Test]
    public void SetError_is_silent_while_the_error_is_unchanged()
    {
        var person = new ValidatingPerson();
        var notified = 0;
        person.ErrorsChanged += (_, _) => ++notified;

        person.ReportNameError("bad");
        person.ReportNameError("bad");
        person.ReportNameError(null);
        person.ReportNameError(null);

        Assert.That(notified, Is.EqualTo(2));
    }
}
