using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class CheckedListBoxTests
{
    private static HeadlessCanvasPeer Realize(OwnerDrawnControl control, HeadlessBackend? backend = null)
    {
        backend ??= new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static CheckedListBox CreateList(out HeadlessCanvasPeer canvas)
    {
        var list = new CheckedListBox { Bounds = new(0, 0, 120, 110) }; // 5 rows at 22px
        list.Items.AddRange(["a", "b", "c", "d", "e"]);
        canvas = Realize(list);
        return list;
    }

    [Test]
    public void Set_and_get_item_checked_roundtrip()
    {
        var list = CreateList(out _);

        list.SetItemChecked(1, true);

        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemChecked(1), Is.True);
            Assert.That(list.GetItemChecked(0), Is.False);
        });
    }

    [Test]
    public void Checked_indices_are_sorted_and_items_map()
    {
        var list = CreateList(out _);

        list.SetItemChecked(3, true);
        list.SetItemChecked(0, true);

        Assert.Multiple(() =>
        {
            Assert.That(list.CheckedIndices, Is.EqualTo(new[] { 0, 3 }));
            Assert.That(list.CheckedItems, Is.EqualTo(new[] { "a", "d" }));
        });
    }

    [Test]
    public void Item_check_is_raised_before_the_flip()
    {
        var list = CreateList(out _);
        var raised = 0;
        list.ItemCheck += (_, e) =>
        {
            ++raised;
            Assert.Multiple(() =>
            {
                Assert.That(e.Index, Is.EqualTo(2));
                Assert.That(e.CurrentValue, Is.False);
                Assert.That(e.NewValue, Is.True);
                Assert.That(list.GetItemChecked(2), Is.False, "state not yet flipped");
            });
        };

        list.SetItemChecked(2, true);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(list.GetItemChecked(2), Is.True);
        });
    }

    [Test]
    public void Item_check_can_veto_the_flip()
    {
        var list = CreateList(out _);
        list.ItemCheck += (_, e) => e.NewValue = e.CurrentValue;

        list.SetItemChecked(2, true);

        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemChecked(2), Is.False);
            Assert.That(list.CheckedIndices, Is.Empty);
        });
    }

    [Test]
    public void Without_check_on_click_the_first_click_selects_and_the_second_toggles()
    {
        var list = CreateList(out var canvas);

        canvas.RaiseMouseDown(10, 25); // row 1: select only
        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.EqualTo(1));
            Assert.That(list.GetItemChecked(1), Is.False);
        });

        canvas.RaiseMouseDown(10, 25); // already selected: toggle on
        Assert.That(list.GetItemChecked(1), Is.True);

        canvas.RaiseMouseDown(10, 25); // toggle off again
        Assert.That(list.GetItemChecked(1), Is.False);
    }

    [Test]
    public void With_check_on_click_every_click_toggles()
    {
        var list = CreateList(out var canvas);
        list.CheckOnClick = true;

        canvas.RaiseMouseDown(10, 47); // row 2

        Assert.Multiple(() =>
        {
            Assert.That(list.SelectedIndex, Is.EqualTo(2));
            Assert.That(list.GetItemChecked(2), Is.True);
        });
    }

    [Test]
    public void Space_toggles_every_selected_item()
    {
        var list = CreateList(out var canvas);
        list.SelectionMode = SelectionMode.MultiSimple;
        canvas.RaiseMouseDown(10, 5);  // row 0
        canvas.RaiseMouseDown(10, 47); // row 2

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.CheckedIndices, Is.EqualTo(new[] { 0, 2 }));

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(list.CheckedIndices, Is.Empty);
    }

    [Test]
    public void Removing_an_item_keeps_check_states_aligned()
    {
        var list = CreateList(out _);
        list.SetItemChecked(2, true);

        list.Items.RemoveAt(0);

        Assert.Multiple(() =>
        {
            Assert.That(list.GetItemChecked(1), Is.True, "\"c\" kept its check at its new index");
            Assert.That(list.CheckedIndices, Is.EqualTo(new[] { 1 }));
            Assert.That(list.CheckedItems, Is.EqualTo(new[] { "c" }));
        });
    }

    [Test]
    public void Paint_draws_a_check_glyph_and_indents_the_text()
    {
        var list = CreateList(out var canvas);
        list.SetItemChecked(0, true);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            // A 14px box, vertically centered in the 22px row, on every visible row.
            Assert.That(g.Operations.Exists(o => o.StartsWith("rect ") && o.EndsWith(" 2,4,14,14")), Is.True, "row 0 box");
            Assert.That(g.Operations.Exists(o => o.StartsWith("rect ") && o.EndsWith(" 2,26,14,14")), Is.True, "row 1 box");
            // The checked row gets the two checkmark strokes.
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ") && o.Contains("5,11-8,14")), Is.True, "first stroke");
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ") && o.Contains("8,14-13,7")), Is.True, "second stroke");
            // Text starts past the glyph.
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"a\"") && o.EndsWith("@22,0")), Is.True, "text indented");
        });
    }

    [Test]
    public void Paint_keeps_icons_after_the_check_glyph()
    {
        var backend = new HeadlessBackend();
        var icon = backend.CreateImage(8, 8, stackalloc int[64]);
        var list = new CheckedListBox { Bounds = new(0, 0, 120, 110), ImageSelector = _ => icon };
        list.Items.Add("a");
        var canvas = Realize(list, backend);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image ") && o.Contains("@22,2,")), Is.True, "icon past the glyph");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"a\"") && o.EndsWith("@44,0")), Is.True, "text past the icon");
        });
    }

    [Test]
    public void Paint_only_touches_visible_rows()
    {
        var list = new CheckedListBox { Bounds = new(0, 0, 120, 66) }; // 3 visible rows
        for (var i = 0; i < 10_000; ++i)
            list.Items.Add(i);
        var canvas = Realize(list);

        var g = canvas.RaisePaint();

        var texts = g.Operations.FindAll(o => o.StartsWith("text "));
        Assert.That(texts, Has.Count.LessThanOrEqualTo(4), "only the visible row range is painted");
    }
}
