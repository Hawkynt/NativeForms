using System.Drawing;
using Hawkynt.NativeForms;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class ControlModelTests
{
    [Test]
    public void Geometry_helpers_project_onto_Bounds()
    {
        var button = new Button { Bounds = new(10, 20, 30, 40) };

        Assert.Multiple(() =>
        {
            Assert.That(button.Left, Is.EqualTo(10));
            Assert.That(button.Top, Is.EqualTo(20));
            Assert.That(button.Width, Is.EqualTo(30));
            Assert.That(button.Height, Is.EqualTo(40));
            Assert.That(button.Location, Is.EqualTo(new Point(10, 20)));
            Assert.That(button.Size, Is.EqualTo(new Size(30, 40)));
        });

        button.Width = 99;
        button.Top = 5;

        Assert.That(button.Bounds, Is.EqualTo(new Rectangle(10, 5, 99, 40)));
    }

    [Test]
    public void Text_setter_raises_TextChanged_only_on_change()
    {
        var label = new Label();
        var raised = 0;
        label.TextChanged += (_, _) => ++raised;

        label.Text = "a";
        label.Text = "a";
        label.Text = "b";

        Assert.That(raised, Is.EqualTo(2));
    }

    [Test]
    public void Null_text_is_normalized_to_empty()
    {
        var label = new Label { Text = "x" };
        label.Text = null!;
        Assert.That(label.Text, Is.EqualTo(string.Empty));
    }

    [Test]
    public void PerformClick_raises_Click()
    {
        var button = new Button();
        var clicks = 0;
        button.Click += (_, _) => ++clicks;

        button.PerformClick();

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Adding_control_sets_parent_and_membership()
    {
        var form = new Form();
        var child = new Button();

        form.Controls.Add(child);

        Assert.Multiple(() =>
        {
            Assert.That(child.Parent, Is.SameAs(form));
            Assert.That(form.Controls.Contains(child), Is.True);
            Assert.That(form.Controls, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Removing_control_clears_parent()
    {
        var form = new Form();
        var child = new Button();
        form.Controls.Add(child);

        var removed = form.Controls.Remove(child);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(child.Parent, Is.Null);
            Assert.That(form.Controls.Contains(child), Is.False);
        });
    }

    [Test]
    public void AddRange_adds_all_in_order()
    {
        var form = new Form();
        var a = new Button();
        var b = new Label();

        form.Controls.AddRange(a, b);

        Assert.That(form.Controls.ToArray(), Is.EqualTo(new Control[] { a, b }));
    }

    [Test]
    public void PerformClick_is_swallowed_while_disabled_or_hidden()
    {
        var button = new Button();
        var clicks = 0;
        button.Click += (_, _) => ++clicks;

        button.Enabled = false;
        button.PerformClick();
        Assert.That(clicks, Is.Zero, "disabled controls never click");

        button.Enabled = true;
        button.Visible = false;
        button.PerformClick();
        Assert.That(clicks, Is.Zero, "hidden controls never click");

        button.Visible = true;
        button.PerformClick();
        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void Tag_and_Name_default_and_round_trip()
    {
        var button = new Button();
        Assert.Multiple(() =>
        {
            Assert.That(button.Tag, Is.Null);
            Assert.That(button.Name, Is.EqualTo(string.Empty));
        });

        var payload = new object();
        button.Tag = payload;
        button.Name = "okButton";
        Assert.Multiple(() =>
        {
            Assert.That(button.Tag, Is.SameAs(payload));
            Assert.That(button.Name, Is.EqualTo("okButton"));
        });

        button.Name = null!;
        Assert.That(button.Name, Is.EqualTo(string.Empty), "null resets to the empty default");
    }

    [Test]
    public void Visible_walks_the_parent_chain_but_keeps_the_local_flag()
    {
        var form = new Form();
        var panel = new Panel();
        var button = new Button();
        panel.Controls.Add(button);
        form.Controls.Add(panel);

        panel.Visible = false;

        Assert.Multiple(() =>
        {
            Assert.That(button.Visible, Is.False, "a hidden ancestor hides the child effectively");
            Assert.That(panel.Visible, Is.False);
        });

        panel.Visible = true;
        Assert.That(button.Visible, Is.True, "the child's own flag survived the ancestor toggle");
    }

    [Test]
    public void Enabled_walks_the_parent_chain_but_keeps_the_local_flag()
    {
        var form = new Form();
        var panel = new Panel();
        var button = new Button();
        panel.Controls.Add(button);
        form.Controls.Add(panel);

        panel.Enabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(button.Enabled, Is.False, "a disabled ancestor disables the child effectively");
            Assert.That(form.Enabled, Is.True);
        });

        panel.Enabled = true;
        Assert.That(button.Enabled, Is.True, "the child's own flag survived the ancestor toggle");
    }

    [Test]
    public void Peer_receives_the_local_visibility_not_the_effective_one()
    {
        var backend = new Fakes.HeadlessBackend();
        var form = new Form();
        var panel = new Panel { Visible = false };
        var button = new Button();
        panel.Controls.Add(button);
        form.Controls.Add(panel);
        Application.Run(form, backend);

        var buttonPeer = backend.Created.OfType<Fakes.HeadlessButtonPeer>().Single();
        Assert.That(buttonPeer.Visible, Is.True, "the native nesting hides the child; its own peer flag stays set");
    }

    [Test]
    public void Invalidate_and_Refresh_are_safe_on_native_and_unrealized_controls()
    {
        var button = new Button();
        Assert.DoesNotThrow(() =>
        {
            button.Invalidate();
            button.Invalidate(new Rectangle(0, 0, 10, 10));
            button.Refresh();
        });
    }

    [Test]
    public void Invalidate_and_Refresh_reach_the_owner_drawn_canvas()
    {
        var backend = new Fakes.HeadlessBackend();
        var form = new Form();
        Control check = new CheckBox();
        form.Controls.Add(check);
        Application.Run(form, backend);
        var canvas = backend.Created.OfType<Fakes.HeadlessCanvasPeer>().Single();
        var baseline = canvas.InvalidateCount;

        check.Invalidate();
        check.Invalidate(new Rectangle(0, 0, 5, 5));
        check.Refresh();

        Assert.That(canvas.InvalidateCount, Is.EqualTo(baseline + 3), "all three route through the Control-level surface");
    }
}
