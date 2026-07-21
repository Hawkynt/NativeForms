using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Containers that hide their content wholesale — a collapsed <see cref="Expander"/>, a collapsed
/// <see cref="SplitContainer"/> panel — veto their children's peer visibility per level. Restoring
/// them therefore has to re-ask the veto for the <em>whole</em> subtree: a grandchild's peer is
/// vetoed by its own parent, and nothing else recomputes it. The native backends nest widgets and so
/// hide a subtree along with its parent, which papers over a missed descendant on screen; the
/// headless peers deliberately do not, so the defect is visible here.
/// </summary>
[TestFixture]
internal sealed class ContainerVisibilityTests
{
    private static HeadlessBackend Realize(Control root)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(root);
        Application.Run(form, backend);
        return backend;
    }

    [Test]
    public void Expanding_restores_every_descendants_peer_visibility()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var directChild = new Button { Bounds = new(4, 30, 60, 20) };
        var nested = new Panel { Bounds = new(4, 60, 180, 60) };
        var grandchild = new Button { Bounds = new(4, 4, 60, 20) };
        var deeper = new Panel { Bounds = new(4, 30, 120, 26) };
        var greatGrandchild = new Label { Bounds = new(2, 2, 60, 20) };

        deeper.Controls.Add(greatGrandchild);
        nested.Controls.Add(grandchild);
        nested.Controls.Add(deeper);
        expander.Controls.Add(directChild);
        expander.Controls.Add(nested);

        var backend = Realize(expander);
        var directPeer = (HeadlessPeer)backend.Created.OfType<HeadlessButtonPeer>().First();
        var grandchildPeer = (HeadlessPeer)backend.Created.OfType<HeadlessButtonPeer>().Last();
        var greatGrandchildPeer = backend.Created.OfType<HeadlessLabelPeer>().Single();

        Assert.That(greatGrandchildPeer.EffectivelyVisible, Is.True, "everything starts visible");

        expander.Expanded = false;

        Assert.Multiple(() =>
        {
            Assert.That(directPeer.EffectivelyVisible, Is.False);
            Assert.That(grandchildPeer.EffectivelyVisible, Is.False);
            Assert.That(greatGrandchildPeer.EffectivelyVisible, Is.False);
            Assert.That(
                greatGrandchild.Visible,
                Is.False,
                "Visible is effective, and a collapsed expander's whole subtree is off screen");
        });

        expander.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(directPeer.EffectivelyVisible, Is.True, "the direct child comes back");
            Assert.That(grandchildPeer.EffectivelyVisible, Is.True, "so does the grandchild");
            Assert.That(
                greatGrandchildPeer.EffectivelyVisible,
                Is.True,
                "and so does every level below it — the whole subtree is re-pushed, not just the direct children");
        });
    }

    [Test]
    public void Expanding_re_pushes_visibility_to_nested_descendants()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var nested = new Panel { Bounds = new(4, 30, 180, 90) };
        var grandchild = new Button { Bounds = new(4, 4, 60, 20) };
        nested.Controls.Add(grandchild);
        expander.Controls.Add(nested);

        var backend = Realize(expander);
        var grandchildPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();
        var pushesAfterRealize = grandchildPeer.VisiblePushes.Count;

        expander.Expanded = false;
        expander.Expanded = true;

        Assert.That(
            grandchildPeer.VisiblePushes.Count,
            Is.EqualTo(pushesAfterRealize + 2),
            "collapsing and expanding each re-push the grandchild's peer rather than leaving it stale");
    }

    [Test]
    public void A_child_added_while_collapsed_appears_on_expand()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var backend = Realize(expander);

        expander.Expanded = false;
        var late = new Button { Bounds = new(4, 30, 60, 20) };
        expander.Controls.Add(late);
        var latePeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        Assert.That(latePeer.EffectivelyVisible, Is.False, "a child added while collapsed realizes hidden");

        expander.Expanded = true;

        Assert.That(latePeer.EffectivelyVisible, Is.True);
    }

    [Test]
    public void A_childs_own_visibility_survives_a_collapse_expand_cycle()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var shown = new Button { Bounds = new(4, 30, 60, 20) };
        var hidden = new Button { Bounds = new(4, 60, 60, 20), Visible = false };
        expander.Controls.Add(shown);
        expander.Controls.Add(hidden);

        var backend = Realize(expander);
        var shownPeer = backend.Created.OfType<HeadlessButtonPeer>().First();
        var hiddenPeer = backend.Created.OfType<HeadlessButtonPeer>().Last();

        expander.Expanded = false;
        expander.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(shownPeer.EffectivelyVisible, Is.True);
            Assert.That(hiddenPeer.EffectivelyVisible, Is.False, "a child hidden by the app stays hidden");
        });
    }

    [Test]
    public void Showing_a_hidden_expander_does_not_resurrect_a_collapsed_subtree()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var child = new Button { Bounds = new(4, 30, 60, 20) };
        expander.Controls.Add(child);

        var backend = Realize(expander);
        var childPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        expander.Expanded = false;
        expander.Visible = false;
        expander.Visible = true;

        Assert.That(
            childPeer.EffectivelyVisible,
            Is.False,
            "the expander is visible again but still collapsed, so its content stays hidden");
    }

    /// <summary>
    /// The reported "re-expanding has vanished client controls", in the shape the demo has it: the
    /// expander lives on a tab page that is not the selected one at startup, so the page's own
    /// <see cref="Control.Visible"/> is false while the tree realizes. A veto that folds in the
    /// <em>effective</em> <see cref="Control.Visible"/> — which walks up through that hidden page —
    /// latches the expander's children off at realization, and selecting the tab only re-pushes the
    /// page's own peer, so the expander comes up empty and stays empty.
    /// </summary>
    [Test]
    public void Content_of_an_expander_on_an_unselected_tab_page_appears_when_the_tab_is_selected()
    {
        var tabs = new TabControl { Bounds = new(0, 0, 400, 300) };
        var first = new TabPage("first");
        var second = new TabPage("second");
        var expander = new Expander { Bounds = new(0, 0, 240, 200) };
        var leaf = new Button { Bounds = new(4, 30, 60, 20) };
        expander.Controls.Add(leaf);
        second.Controls.Add(expander);
        tabs.TabPages.AddRange(first, second);

        var backend = Realize(tabs);
        var leafPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        tabs.SelectedIndex = 1;

        Assert.Multiple(() =>
        {
            Assert.That(leaf.Visible, Is.True, "logical visibility was never touched");
            Assert.That(
                leafPeer.Visible,
                Is.True,
                "the expander is open, so its veto must allow the leaf's peer once the page is shown");
            Assert.That(leafPeer.EffectivelyVisible, Is.True, "and the leaf is really on screen");
        });

        expander.Expanded = false;
        expander.Expanded = true;

        Assert.That(leafPeer.EffectivelyVisible, Is.True, "a collapse/expand cycle brings it back too");
    }

    [Test]
    public void Un_collapsing_a_split_panel_restores_every_descendants_peer_visibility()
    {
        var split = new SplitContainer { Bounds = new(0, 0, 300, 200) };
        var nested = new Panel { Bounds = new(2, 2, 100, 100) };
        var grandchild = new Button { Bounds = new(2, 2, 60, 20) };
        nested.Controls.Add(grandchild);
        split.Panel1.Controls.Add(nested);

        var backend = Realize(split);
        var grandchildPeer = backend.Created.OfType<HeadlessButtonPeer>().Single();

        split.Panel1Collapsed = true;

        Assert.That(grandchildPeer.EffectivelyVisible, Is.False);

        split.Panel1Collapsed = false;

        Assert.That(
            grandchildPeer.EffectivelyVisible,
            Is.True,
            "un-collapsing re-pushes the panel's whole subtree, not just the panel");
    }

    /// <summary>
    /// <see cref="Control.Visible"/> is the <em>effective</em> state, so a container's veto counts
    /// just like a hidden ancestor: while the expander is collapsed its whole subtree is off screen
    /// and must say so, and re-expanding must restore every reading — proving the veto was never
    /// written into the children's own flags.
    /// </summary>
    [Test]
    public void A_collapsed_expanders_subtree_reports_itself_invisible_and_recovers()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var child = new Button { Bounds = new(4, 30, 60, 20) };
        var nested = new Panel { Bounds = new(4, 60, 180, 60) };
        var grandchild = new Label { Bounds = new(2, 2, 60, 20) };
        nested.Controls.Add(grandchild);
        expander.Controls.Add(child);
        expander.Controls.Add(nested);
        Realize(expander);

        Assert.That(child.Visible, Is.True, "everything starts visible");

        expander.Expanded = false;

        Assert.Multiple(() =>
        {
            Assert.That(child.Visible, Is.False, "the direct child is off screen");
            Assert.That(grandchild.Visible, Is.False, "and so is a grandchild");
            Assert.That(expander.Visible, Is.True, "the expander itself is still on screen");
        });

        expander.Expanded = true;

        Assert.Multiple(() =>
        {
            Assert.That(child.Visible, Is.True, "the child's own flag was never clobbered");
            Assert.That(grandchild.Visible, Is.True, "nor the grandchild's");
        });
    }

    /// <summary>
    /// The veto combines with the child's <em>own</em> flag, never with the effective getter: a
    /// child hidden by hand stays hidden after the expander reopens, which is what distinguishes the
    /// two states and is exactly what folding the getter into the veto would destroy.
    /// </summary>
    [Test]
    public void A_child_hidden_by_hand_stays_hidden_when_the_expander_reopens()
    {
        var expander = new Expander { Bounds = new(0, 0, 200, 150) };
        var child = new Button { Bounds = new(4, 30, 60, 20) };
        expander.Controls.Add(child);
        Realize(expander);

        child.Visible = false;
        expander.Expanded = false;
        expander.Expanded = true;

        Assert.That(child.Visible, Is.False);
    }
}
