using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="SearchBox"/> must host a native editor with the "Search" placeholder between a
/// painted magnifier glyph and a clear (×) zone that appears only while text is present, clear on a
/// × click raising <see cref="SearchBox.SearchCleared"/>, and raise
/// <see cref="SearchBox.SearchCommitted"/> on Enter.
/// </summary>
[TestFixture]
internal sealed class SearchBoxTests
{
    /// <summary>Realizes a 150×24 search box on a fresh form and returns all the actors.</summary>
    private static SearchBox CreateBox(out HeadlessCanvasPeer canvas, out HeadlessTextBoxPeer editor)
    {
        var box = new SearchBox { Bounds = new(0, 0, 150, 24) };
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
        return box;
    }

    [Test]
    public void Hosts_an_editor_with_the_search_placeholder_between_the_zones()
    {
        CreateBox(out _, out var editor);

        Assert.Multiple(() =>
        {
            Assert.That(editor.Placeholder, Is.EqualTo("Search"));
            Assert.That(editor.Bounds, Is.EqualTo(new Rectangle(20, 0, 110, 24)), "the editor fills the field between the glyph and clear zones");
        });
    }

    [Test]
    public void Placeholder_is_forwardable()
    {
        var box = CreateBox(out _, out var editor);

        box.PlaceholderText = "Filter";

        Assert.Multiple(() =>
        {
            Assert.That(box.PlaceholderText, Is.EqualTo("Filter"));
            Assert.That(editor.Placeholder, Is.EqualTo("Filter"));
        });
    }

    [Test]
    public void Paints_the_magnifier_glyph_and_the_field()
    {
        CreateBox(out var canvas, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFFFF 0,0,150,24"), "field background");
            Assert.That(g.Operations, Does.Contain("ellipse #FF1A1A1A 5,6,8,8"), "magnifier lens");
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 12,13-15,16"), "magnifier handle");
            Assert.That(g.Operations, Does.Contain("rect #FFC8C8C8 0,0,149,23"), "field border");
        });
    }

    [Test]
    public void Clear_glyph_appears_only_while_text_is_present()
    {
        var box = CreateBox(out var canvas, out _);

        Assert.That(canvas.RaisePaint().Operations, Does.Not.Contain("line #FF1A1A1A 137,9-143,15"), "no × while empty");

        box.Text = "abc";
        var g = canvas.RaisePaint();
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 137,9-143,15"), "first × stroke");
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 137,15-143,9"), "second × stroke");
        });
    }

    [Test]
    public void Clicking_the_clear_zone_clears_and_raises_SearchCleared()
    {
        var box = CreateBox(out var canvas, out var editor);
        box.Text = "abc";
        var cleared = 0;
        var textChanges = 0;
        box.SearchCleared += (_, _) => ++cleared;
        box.TextChanged += (_, _) => ++textChanges;

        canvas.RaiseMouseDown(140, 12);

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.Empty);
            Assert.That(editor.Text, Is.Empty, "the native editor is cleared too");
            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(textChanges, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clicking_the_clear_zone_while_empty_does_nothing()
    {
        var box = CreateBox(out var canvas, out _);
        var cleared = 0;
        box.SearchCleared += (_, _) => ++cleared;

        canvas.RaiseMouseDown(140, 12);

        Assert.That(cleared, Is.Zero);
    }

    [Test]
    public void Enter_raises_SearchCommitted()
    {
        var box = CreateBox(out var canvas, out _);
        box.Text = "abc";
        var committed = 0;
        box.SearchCommitted += (_, _) => ++committed;

        canvas.RaiseKeyDown(Keys.Enter);

        Assert.That(committed, Is.EqualTo(1));
    }

    [Test]
    public void User_edits_flow_back_into_Text()
    {
        var box = CreateBox(out _, out var editor);
        var textChanges = 0;
        box.TextChanged += (_, _) => ++textChanges;

        editor.SimulateUserInput("query");

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("query"));
            Assert.That(textChanges, Is.EqualTo(1));
        });
    }

    [Test]
    public void Focus_lands_on_the_hosted_editor_not_on_the_painted_surface()
    {
        // The editor is the widget that takes text; focusing the shell around it would leave the
        // keyboard nowhere and every typed character would be lost.
        var box = CreateBox(out var canvas, out var editor);

        box.Focus();

        Assert.Multiple(() =>
        {
            Assert.That(editor.FocusRequested, Is.True);
            Assert.That(canvas.FocusRequested, Is.False);
        });
    }

    [Test]
    public void Enter_typed_inside_the_hosted_editor_commits_the_search()
    {
        var box = CreateBox(out _, out var editor);
        var committed = 0;
        box.SearchCommitted += (_, _) => ++committed;
        editor.SimulateUserInput("grid");

        var handled = editor.SimulateKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.EqualTo(1));
            Assert.That(handled, Is.True, "the key is claimed, so the editor never sees it");
        });
    }

    [Test]
    public void Keys_the_search_box_does_not_claim_stay_with_the_editor()
    {
        var box = CreateBox(out _, out var editor);
        var committed = 0;
        box.SearchCommitted += (_, _) => ++committed;

        var handled = editor.SimulateKeyDown(Keys.A);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.Zero);
            Assert.That(handled, Is.False);
        });
    }

    [Test]
    public void Disabled_box_ignores_the_clear_zone()
    {
        var box = CreateBox(out var canvas, out _);
        box.Text = "abc";
        box.Enabled = false;
        var cleared = 0;
        box.SearchCleared += (_, _) => ++cleared;

        canvas.RaiseMouseDown(140, 12);

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("abc"));
            Assert.That(cleared, Is.Zero);
        });
    }
}
