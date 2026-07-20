using System.ComponentModel;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The lambda binding sugar (<see cref="BindingExtensions.Bind"/>): the fluent control-first
/// overloads over <see cref="PropertyBinding{T}"/>, plus the per-binding fallbacks and the
/// validation-error callback that ride along with them.
/// </summary>
[TestFixture]
internal sealed class BindingSugarTests
{
    private sealed class CounterViewModel : ObservableObject
    {
        public int Count
        {
            get => field;
            set
            {
                if (this.SetProperty(ref field, value))
                    this.OnPropertyChanged(nameof(this.Display));
            }
        }

        public string Display => $"Clicked {this.Count} time(s).";
    }

    private sealed class PersonViewModel : ObservableObject
    {
        public string Name
        {
            get => field;
            set
            {
                if (this.SetProperty(ref field, value))
                    this.SetError(nameof(this.Name), value.Length == 0 ? "Name is required." : null);
            }
        } = "Ada";

        public PersonViewModel? Partner
        {
            get => field;
            set => this.SetProperty(ref field, value);
        }
    }

    /// <summary>An <see cref="INotifyPropertyChanged"/> source that is not an <see cref="ObservableObject"/>.</summary>
    private sealed class BarePropertySource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public string Name => "bare";
    }

    [Test]
    public void Bind_target_first_pushes_source_changes_into_the_control()
    {
        var vm = new CounterViewModel();
        var label = new Label();

        label.Bind(vm, nameof(vm.Count), v => v.Display, (c, text) => c.Text = text);

        Assert.That(label.Text, Is.EqualTo("Clicked 0 time(s)."), "initial sync");

        vm.Count = 3;
        Assert.That(label.Text, Is.EqualTo("Clicked 3 time(s)."));
    }

    [Test]
    public void Bind_with_plain_setter_round_trips_source_changes()
    {
        var vm = new PersonViewModel();
        var label = new Label();

        var binding = label.Bind(vm, nameof(vm.Name), v => v.Name, text => label.Text = text);

        Assert.That(binding, Is.InstanceOf<PropertyBinding<string>>());
        Assert.That(label.Text, Is.EqualTo("Ada"));

        vm.Name = "Grace";
        Assert.That(label.Text, Is.EqualTo("Grace"));
    }

    [Test]
    public void Bind_discarded_result_keeps_updating_while_the_source_lives()
    {
        var vm = new CounterViewModel();
        var label = new Label();

        label.Bind(vm, nameof(vm.Count), v => v.Display, (c, text) => c.Text = text);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        vm.Count = 5;
        Assert.That(label.Text, Is.EqualTo("Clicked 5 time(s)."),
            "the source's PropertyChanged subscription roots the binding — discarding the result is safe");
    }

    [Test]
    public void Bind_one_time_pushes_only_the_initial_value()
    {
        var vm = new PersonViewModel();
        var label = new Label();

        label.Bind(vm, nameof(vm.Name), v => v.Name, (c, text) => c.Text = text, BindingMode.OneTime);

        vm.Name = "Grace";
        Assert.That(label.Text, Is.EqualTo("Ada"));
    }

    [Test]
    public void Bind_two_way_round_trips_through_a_text_box()
    {
        var vm = new PersonViewModel();
        var textBox = new TextBox();

        using var _ = textBox.Bind(
            vm,
            nameof(vm.Name),
            v => v.Name,
            (c, text) => c.Text = text,
            (v, text) => v.Name = text,
            c => c.Text,
            (c, h) => c.TextChanged += h,
            (c, h) => c.TextChanged -= h);

        Assert.That(textBox.Text, Is.EqualTo("Ada"), "initial sync");

        textBox.Text = "Grace";
        Assert.That(vm.Name, Is.EqualTo("Grace"), "target edit written back");

        vm.Name = "Linus";
        Assert.That(textBox.Text, Is.EqualTo("Linus"), "source change pushed out");
    }

    [Test]
    public void Bind_two_way_dispose_unhooks_the_target_change_event()
    {
        var vm = new PersonViewModel();
        var textBox = new TextBox();

        var binding = textBox.Bind(
            vm,
            nameof(vm.Name),
            v => v.Name,
            (c, text) => c.Text = text,
            (v, text) => v.Name = text,
            c => c.Text,
            (c, h) => c.TextChanged += h,
            (c, h) => c.TextChanged -= h);

        binding.Dispose();
        textBox.Text = "Grace";

        Assert.That(vm.Name, Is.EqualTo("Ada"));
    }

    [Test]
    public void Bind_default_value_replaces_a_throwing_read()
    {
        var vm = new PersonViewModel();
        var label = new Label();

        label.Bind(
            vm,
            nameof(vm.Partner),
            v => v.Partner!.Name,
            (c, text) => c.Text = text,
            defaultValue: "(nobody)");

        Assert.That(label.Text, Is.EqualTo("(nobody)"), "unset chain falls back");

        vm.Partner = new() { Name = "Grace" };
        Assert.That(label.Text, Is.EqualTo("Grace"), "a readable source wins over the fallback");
    }

    [Test]
    public void Bind_without_default_value_lets_the_read_exception_propagate()
    {
        var vm = new PersonViewModel();
        var label = new Label();

        Assert.Throws<NullReferenceException>(
            () => label.Bind(vm, nameof(vm.Partner), v => v.Partner!.Name, (c, text) => c.Text = text));
    }

    [Test]
    public void Bind_null_replacement_substitutes_a_null_source_value()
    {
        var vm = new PersonViewModel();
        var label = new Label();

        label.Bind(
            vm,
            nameof(vm.Partner),
            v => v.Partner?.Name,
            (c, text) => c.Text = text!,
            nullReplacement: "(unnamed)");

        Assert.That(label.Text, Is.EqualTo("(unnamed)"), "null reads are replaced");

        vm.Partner = new() { Name = "Grace" };
        Assert.That(label.Text, Is.EqualTo("Grace"), "non-null reads pass through");
    }

    [Test]
    public void Bind_default_value_wins_over_null_replacement_when_the_read_throws()
    {
        var vm = new PersonViewModel();
        var label = new Label();

        label.Bind(
            vm,
            nameof(vm.Partner),
            v => v.Partner!.Name,
            (c, text) => c.Text = text,
            defaultValue: "(default)",
            nullReplacement: "(null)");

        Assert.That(label.Text, Is.EqualTo("(default)"));
    }

    [Test]
    public void Bind_surfaces_errors_through_the_callback()
    {
        var vm = new PersonViewModel();
        var label = new Label();
        var errors = new List<string?>();

        label.Bind(vm, nameof(vm.Name), v => v.Name, (c, text) => c.Text = text, onError: errors.Add);

        Assert.That(errors, Is.EqualTo(new string?[] { null }), "initial state is surfaced at bind time");

        vm.Name = string.Empty;
        Assert.That(errors[^1], Is.EqualTo("Name is required."));

        vm.Name = "Grace";
        Assert.That(errors[^1], Is.Null);
    }

    [Test]
    public void Bind_error_callback_sees_a_pre_existing_error_immediately()
    {
        var vm = new PersonViewModel { Name = string.Empty };
        var label = new Label();
        string? seen = "unset";

        label.Bind(vm, nameof(vm.Name), v => v.Name, (c, text) => c.Text = text, onError: e => seen = e);

        Assert.That(seen, Is.EqualTo("Name is required."));
    }

    [Test]
    public void Bind_disposed_binding_stops_the_error_callback()
    {
        var vm = new PersonViewModel();
        var label = new Label();
        var calls = 0;

        var binding = label.Bind(vm, nameof(vm.Name), v => v.Name, (c, text) => c.Text = text, onError: _ => ++calls);
        var callsAtDispose = calls;
        binding.Dispose();

        vm.Name = string.Empty;
        Assert.That(calls, Is.EqualTo(callsAtDispose));
    }

    [Test]
    public void Bind_error_callback_requires_an_observable_object_source()
    {
        var source = new BarePropertySource();
        var label = new Label();

        Assert.Throws<ArgumentException>(
            () => label.Bind(source, nameof(source.Name), v => v.Name, (c, text) => c.Text = text, onError: _ => { }));
    }
}
