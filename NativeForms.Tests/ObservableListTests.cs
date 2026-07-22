using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ObservableListTests
{
    [Test]
    public void Add_raises_added_with_index()
    {
        var list = new ObservableList<string>();
        ListChangedEventArgs? last = null;
        list.ListChanged += (_, e) => last = e;

        list.Add("x");

        Assert.Multiple(() =>
        {
            Assert.That(last!.ChangeType, Is.EqualTo(ListChangeType.Added));
            Assert.That(last!.Index, Is.EqualTo(0));
        });
    }

    [Test]
    public void RemoveAt_raises_removed()
    {
        var list = new ObservableList<string>(["a", "b"]);
        var events = new List<ListChangeType>();
        list.ListChanged += (_, e) => events.Add(e.ChangeType);

        list.RemoveAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(new[] { ListChangeType.Removed }));
            Assert.That(list, Is.EqualTo(new[] { "b" }));
        });
    }

    [Test]
    public void Clear_raises_reset_with_negative_index()
    {
        var list = new ObservableList<int>([1, 2, 3]);
        ListChangedEventArgs? last = null;
        list.ListChanged += (_, e) => last = e;

        list.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(last!.ChangeType, Is.EqualTo(ListChangeType.Reset));
            Assert.That(last!.Index, Is.EqualTo(-1));
            Assert.That(list, Is.Empty);
        });
    }

    [Test]
    public void Sort_reorders_stably_and_raises_single_reset()
    {
        var list = new ObservableList<string>(["bb", "c", "aa", "b"]);
        var events = new List<ListChangedEventArgs>();
        list.ListChanged += (_, e) => events.Add(e);

        list.Sort(static (a, b) => a.Length - b.Length); // equal lengths must keep their order

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "c", "b", "bb", "aa" }));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].ChangeType, Is.EqualTo(ListChangeType.Reset));
        });
    }

    [Test]
    public void Indexer_set_raises_replaced()
    {
        var list = new ObservableList<string>(["a"]);
        ListChangedEventArgs? last = null;
        list.ListChanged += (_, e) => last = e;

        list[0] = "b";

        Assert.Multiple(() =>
        {
            Assert.That(last!.ChangeType, Is.EqualTo(ListChangeType.Replaced));
            Assert.That(list[0], Is.EqualTo("b"));
        });
    }

    [Test]
    public void Move_reorders_the_item_and_reports_both_indices()
    {
        var list = new ObservableList<string>(["a", "b", "c", "d"]);
        ListChangedEventArgs? last = null;
        list.ListChanged += (_, e) => last = e;

        list.Move(0, 2); // move "a" to index 2

        Assert.Multiple(() =>
        {
            Assert.That(list, Is.EqualTo(new[] { "b", "c", "a", "d" }));
            Assert.That(last!.ChangeType, Is.EqualTo(ListChangeType.Moved));
            Assert.That(last.OldIndex, Is.EqualTo(0));
            Assert.That(last.Index, Is.EqualTo(2));
        });
    }

    [Test]
    public void Move_to_the_same_index_is_a_silent_no_op()
    {
        var list = new ObservableList<int>([1, 2, 3]);
        var raised = 0;
        list.ListChanged += (_, _) => ++raised;

        list.Move(1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.Zero);
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void Move_rejects_an_out_of_range_index()
    {
        var list = new ObservableList<int>([1, 2]);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Move(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Move(0, 2));
        });
    }

    [Test]
    public void The_list_is_usable_through_the_read_only_observable_view()
    {
        var list = new ObservableList<string>(["x"]);
        IReadOnlyObservableList<string> view = list;
        ListChangeType? seen = null;
        view.ListChanged += (_, e) => seen = e.ChangeType;

        list.Add("y");

        Assert.Multiple(() =>
        {
            Assert.That(view.Count, Is.EqualTo(2));
            Assert.That(view[1], Is.EqualTo("y"));
            Assert.That(seen, Is.EqualTo(ListChangeType.Added));
        });
    }
}
