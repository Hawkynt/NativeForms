namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ListView.SetDataSource{T}"/>: the reflection-free value-binding parity with
/// <see cref="ListBox.DataSource"/> — a snapshot fill whose rows are produced by a caller-supplied
/// item factory, with the source model riding along in <see cref="ListViewItem.Tag"/>.
/// </summary>
[TestFixture]
internal sealed class ListViewBindingTests
{
    private sealed record Person(string Name, int Age);

    [Test]
    public void SetDataSource_builds_one_item_per_model_via_the_factory()
    {
        var listView = new ListView();

        listView.SetDataSource(
            new Person[] { new("Alice", 30), new("Bob", 25) },
            static p => new(p.Name, p.Age.ToString()));

        Assert.That(listView.Items, Has.Count.EqualTo(2));
        Assert.That(listView.Items[0].Text, Is.EqualTo("Alice"));
        Assert.That(listView.Items[0].SubItems, Is.EqualTo(new[] { "30" }));
        Assert.That(listView.Items[1].Text, Is.EqualTo("Bob"));
    }

    [Test]
    public void SetDataSource_stores_the_model_in_tag_when_the_factory_leaves_it_null()
    {
        var listView = new ListView();
        var alice = new Person("Alice", 30);

        listView.SetDataSource(new[] { alice }, static p => new(p.Name));

        Assert.That(listView.Items[0].Tag, Is.SameAs(alice));
    }

    [Test]
    public void SetDataSource_keeps_a_tag_the_factory_assigned()
    {
        var listView = new ListView();

        listView.SetDataSource(
            new Person[] { new("Alice", 30) },
            static p => new(p.Name) { Tag = "custom" });

        Assert.That(listView.Items[0].Tag, Is.EqualTo("custom"));
    }

    [Test]
    public void SetDataSource_replaces_previous_items()
    {
        var listView = new ListView();
        listView.Items.Add(new("stale"));

        listView.SetDataSource(new Person[] { new("Alice", 30) }, static p => new(p.Name));

        Assert.That(listView.Items, Has.Count.EqualTo(1));
        Assert.That(listView.Items[0].Text, Is.EqualTo("Alice"));
    }

    [Test]
    public void SetDataSource_null_clears_the_items()
    {
        var listView = new ListView();
        listView.Items.Add(new("stale"));

        listView.SetDataSource<Person>(null, static p => new(p.Name));

        Assert.That(listView.Items, Is.Empty);
    }

    [Test]
    public void SetDataSource_selection_maps_back_to_the_model()
    {
        var listView = new ListView();
        var bob = new Person("Bob", 25);

        listView.SetDataSource(new[] { new Person("Alice", 30), bob }, static p => new(p.Name));
        listView.SelectedIndex = 1;

        Assert.That(listView.SelectedItem?.Tag, Is.SameAs(bob));
    }
}
