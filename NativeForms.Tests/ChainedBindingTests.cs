using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Nested-path binding (<see cref="BindingPath.Chain"/> / <see cref="ChainedBinding{TRoot,TMid,TValue}"/>):
/// a two-level <c>root.Mid.Value</c> observation that re-subscribes when the middle object is
/// replaced and falls back while the chain is broken.
/// </summary>
[TestFixture]
internal sealed class ChainedBindingTests
{
    private sealed class Address : ObservableObject
    {
        public string City
        {
            get => field;
            set => this.SetProperty(ref field, value);
        } = string.Empty;
    }

    private sealed class Customer : ObservableObject
    {
        public Address? Address
        {
            get => field;
            set => this.SetProperty(ref field, value);
        }
    }

    [Test]
    public void Chain_pushes_the_initial_value()
    {
        var customer = new Customer { Address = new() { City = "Berlin" } };
        var seen = string.Empty;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city);

        Assert.That(seen, Is.EqualTo("Berlin"));
    }

    [Test]
    public void Chain_follows_a_change_on_the_middle_object()
    {
        var customer = new Customer { Address = new() { City = "Berlin" } };
        var seen = string.Empty;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city);

        customer.Address!.City = "Paris";
        Assert.That(seen, Is.EqualTo("Paris"));
    }

    [Test]
    public void Chain_follows_a_middle_object_replacement()
    {
        var customer = new Customer { Address = new() { City = "Berlin" } };
        var seen = string.Empty;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city);

        customer.Address = new() { City = "London" };
        Assert.That(seen, Is.EqualTo("London"), "the replacement's value is pushed");

        customer.Address!.City = "Madrid";
        Assert.That(seen, Is.EqualTo("Madrid"), "the replacement is observed");
    }

    [Test]
    public void Chain_unsubscribes_from_the_replaced_middle_object()
    {
        var oldAddress = new Address { City = "Berlin" };
        var customer = new Customer { Address = oldAddress };
        var pushes = 0;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            _ => ++pushes);

        customer.Address = new() { City = "London" };
        var pushesAfterSwap = pushes;

        oldAddress.City = "Ghost";
        Assert.That(pushes, Is.EqualTo(pushesAfterSwap), "the detached middle object no longer pushes");
    }

    [Test]
    public void Chain_broken_pushes_the_fallback_value()
    {
        var customer = new Customer { Address = new() { City = "Berlin" } };
        var seen = string.Empty;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city,
            fallbackValue: "(no address)");

        customer.Address = null;
        Assert.That(seen, Is.EqualTo("(no address)"));
    }

    [Test]
    public void Chain_broken_without_fallback_leaves_the_target_untouched()
    {
        var customer = new Customer { Address = new() { City = "Berlin" } };
        var seen = string.Empty;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city);

        customer.Address = null;
        Assert.That(seen, Is.EqualTo("Berlin"));
    }

    [Test]
    public void Chain_starting_broken_uses_the_fallback()
    {
        var customer = new Customer();
        var seen = string.Empty;

        using var _ = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city,
            fallbackValue: "(no address)");

        Assert.That(seen, Is.EqualTo("(no address)"));

        customer.Address = new() { City = "Berlin" };
        Assert.That(seen, Is.EqualTo("Berlin"), "an attached chain resumes pushing");
    }

    [Test]
    public void Chain_disposed_stops_observing_both_levels()
    {
        var customer = new Customer { Address = new() { City = "Berlin" } };
        var seen = string.Empty;

        var chain = BindingPath.Chain(
            customer, nameof(Customer.Address), c => c.Address,
            nameof(Address.City), a => a.City,
            city => seen = city);

        chain.Dispose();

        customer.Address!.City = "Paris";
        Assert.That(seen, Is.EqualTo("Berlin"), "value changes are ignored after dispose");

        customer.Address = new() { City = "London" };
        Assert.That(seen, Is.EqualTo("Berlin"), "replacements are ignored after dispose");
    }
}
