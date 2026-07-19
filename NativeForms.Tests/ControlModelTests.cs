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
}
