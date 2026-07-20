using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The appearance foundation must behave like the Windows Forms ambient properties: an unset
/// <see cref="Control.Font"/>/<see cref="Control.ForeColor"/>/<see cref="Control.BackColor"/>
/// inherits from the parent chain and finally the theme, explicit values flush to the peer on
/// realization and forward live afterwards (cascading to inheriting children), owner-drawn controls
/// paint with the effective values, and <see cref="Control.Padding"/> insets the owner-drawn content
/// layout — all without growing the unrealized per-control footprint beyond one null slot.
/// </summary>
[TestFixture]
internal sealed class AppearanceTests
{
    private static readonly Font _testFont = new("Test Sans", 14f, FontStyle.Bold);

    /// <summary>Realizes the given form on a fresh headless backend.</summary>
    private static HeadlessBackend Realize(Form form)
    {
        var backend = new HeadlessBackend();
        Application.Run(form, backend);
        return backend;
    }

    [Test]
    public void Unset_font_resolves_to_the_theme_default()
    {
        var label = new Label();

        Assert.That(label.Font, Is.EqualTo(DefaultTheme.Instance.DefaultFont));
    }

    [Test]
    public void Unset_font_inherits_from_the_parent_chain()
    {
        var form = new Form { Font = _testFont };
        var panel = new Panel();
        var label = new Label();
        form.Controls.Add(panel);
        panel.Controls.Add(label);

        Assert.That(label.Font, Is.EqualTo(_testFont), "the grandchild inherits through the unset panel");
    }

    [Test]
    public void Own_font_wins_over_the_inherited_one()
    {
        var own = new Font("Other", 8f);
        var form = new Form { Font = _testFont };
        var label = new Label { Font = own };
        form.Controls.Add(label);

        Assert.That(label.Font, Is.EqualTo(own));
    }

    [Test]
    public void ResetFont_returns_to_the_ambient_value()
    {
        var form = new Form { Font = _testFont };
        var label = new Label { Font = new("Other", 8f) };
        form.Controls.Add(label);

        label.ResetFont();

        Assert.That(label.Font, Is.EqualTo(_testFont));
    }

    [Test]
    public void Unset_colors_resolve_to_the_theme()
    {
        var label = new Label();

        Assert.Multiple(() =>
        {
            Assert.That(label.ForeColor, Is.EqualTo(DefaultTheme.Instance.ControlText));
            Assert.That(label.BackColor, Is.EqualTo(DefaultTheme.Instance.ControlBackground));
        });
    }

    [Test]
    public void Unset_colors_inherit_from_the_parent_chain()
    {
        var form = new Form { ForeColor = Color.Red, BackColor = Color.Yellow };
        var label = new Label();
        form.Controls.Add(label);

        Assert.Multiple(() =>
        {
            Assert.That(label.ForeColor, Is.EqualTo(Color.Red));
            Assert.That(label.BackColor, Is.EqualTo(Color.Yellow));
        });
    }

    [Test]
    public void ListBox_default_back_color_is_the_field_background()
    {
        var list = new ListBox();

        Assert.That(list.BackColor, Is.EqualTo(DefaultTheme.Instance.FieldBackground));
    }

    [Test]
    public void Realization_flushes_explicit_font_and_colors_to_the_peer()
    {
        var label = new Label { Font = _testFont, ForeColor = Color.Red, BackColor = Color.Yellow };
        var form = new Form();
        form.Controls.Add(label);

        var backend = Realize(form);

        var peer = backend.Created.OfType<HeadlessLabelPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(peer.Font, Is.EqualTo(_testFont));
            Assert.That(peer.ForeColor, Is.EqualTo(Color.Red));
            Assert.That(peer.BackColor, Is.EqualTo(Color.Yellow));
        });
    }

    [Test]
    public void Realization_flushes_the_inherited_font_to_a_child_peer()
    {
        var label = new Label();
        var form = new Form { Font = _testFont };
        form.Controls.Add(label);

        var backend = Realize(form);

        Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Single().Font, Is.EqualTo(_testFont));
    }

    [Test]
    public void All_default_control_leaves_the_peer_untouched()
    {
        var label = new Label();
        var form = new Form();
        form.Controls.Add(label);

        var backend = Realize(form);

        var peer = backend.Created.OfType<HeadlessLabelPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(peer.Font, Is.Null, "no font pushed — the native default stays");
            Assert.That(peer.ForeColor, Is.EqualTo(Color.Empty));
            Assert.That(peer.BackColor, Is.EqualTo(Color.Empty));
        });
    }

    [Test]
    public void Live_font_change_forwards_to_the_peer()
    {
        var label = new Label();
        var form = new Form();
        form.Controls.Add(label);
        var backend = Realize(form);

        label.Font = _testFont;

        Assert.That(backend.Created.OfType<HeadlessLabelPeer>().Single().Font, Is.EqualTo(_testFont));
    }

    [Test]
    public void Live_parent_font_change_cascades_to_inheriting_children_only()
    {
        var inheriting = new Label();
        var pinned = new Label { Font = new("Pinned", 8f) };
        var form = new Form();
        form.Controls.Add(inheriting);
        form.Controls.Add(pinned);
        var backend = Realize(form);

        form.Font = _testFont;

        var peers = backend.Created.OfType<HeadlessLabelPeer>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(peers[0].Font, Is.EqualTo(_testFont), "the unset child follows the parent");
            Assert.That(peers[1].Font, Is.EqualTo(new Font("Pinned", 8f)), "an own font cuts off the cascade");
        });
    }

    [Test]
    public void Live_color_change_forwards_to_the_peer()
    {
        var label = new Label();
        var form = new Form();
        form.Controls.Add(label);
        var backend = Realize(form);

        label.ForeColor = Color.Blue;

        var peer = backend.Created.OfType<HeadlessLabelPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(peer.ForeColor, Is.EqualTo(Color.Blue));
            Assert.That(peer.BackColor, Is.EqualTo(Color.Empty), "the unset background stays default");
        });
    }

    [Test]
    public void CheckBox_paints_with_its_font_and_colors()
    {
        var checkBox = new CheckBox { Text = "Pick me", Bounds = new(0, 0, 150, 24), Font = _testFont, ForeColor = Color.Red, BackColor = Color.Yellow };
        var form = new Form();
        form.Controls.Add(checkBox);
        var backend = Realize(form);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FFFFFF00 0,0,150,24"), "background uses BackColor");
            Assert.That(g.DrewTextWithFont("Pick me", _testFont), "caption uses the set font");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Pick me\" #FFFF0000")), "caption uses ForeColor");
        });
    }

    [Test]
    public void CheckBox_inherits_paint_font_from_the_form()
    {
        var checkBox = new CheckBox { Text = "Ambient", Bounds = new(0, 0, 150, 24) };
        var form = new Form { Font = _testFont };
        form.Controls.Add(checkBox);
        var backend = Realize(form);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        var g = canvas.RaisePaint();

        Assert.That(g.DrewTextWithFont("Ambient", _testFont));
    }

    [Test]
    public void Padding_shifts_the_CheckBox_content_layout()
    {
        var plain = new CheckBox { Text = "Padded", Bounds = new(0, 0, 150, 40) };
        var padded = new CheckBox { Text = "Padded", Bounds = new(0, 0, 150, 40), Padding = new(10, 4, 0, 0) };
        var form = new Form();
        form.Controls.Add(plain);
        form.Controls.Add(padded);
        var backend = Realize(form);
        var canvases = backend.Created.OfType<HeadlessCanvasPeer>().ToArray();

        var plainText = canvases[0].RaisePaint().Operations.Find(o => o.StartsWith("text "));
        var paddedText = canvases[1].RaisePaint().Operations.Find(o => o.StartsWith("text "));

        Assert.Multiple(() =>
        {
            Assert.That(plainText, Does.EndWith("@20,0"), "unpadded: box (14px) + gap (6px)");
            Assert.That(paddedText, Does.EndWith("@30,4"), "padding shifts the content origin");
        });
    }

    [Test]
    public void DisplayRectangle_deflates_the_client_area_by_the_padding()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 80), Padding = new(5, 6, 7, 8) };

        Assert.That(panel.DisplayRectangle, Is.EqualTo(new Rectangle(5, 6, 88, 66)));
    }

    [Test]
    public void GroupBox_display_rectangle_insets_frame_caption_and_padding()
    {
        var group = new GroupBox { Text = "Caption", Bounds = new(0, 0, 200, 100), Padding = new(4) };
        var form = new Form();
        form.Controls.Add(group);
        Realize(form);

        // Caption height is 16 (deterministic headless measurement); frame line is 1px.
        Assert.That(group.DisplayRectangle, Is.EqualTo(new Rectangle(5, 20, 190, 75)));
    }

    [Test]
    public void Panel_auto_scroll_extent_includes_the_trailing_padding()
    {
        var panel = new Panel { Bounds = new(0, 0, 100, 100), AutoScroll = true, Padding = new(0, 0, 0, 40) };
        var child = new Panel { Bounds = new(0, 0, 50, 120) };
        panel.Controls.Add(child);
        var form = new Form();
        form.Controls.Add(panel);
        Realize(form);

        panel.AutoScrollPosition = new(0, 1000);

        // Content is 120 + 40 padding = 160 tall in a 100px viewport → max offset 60.
        Assert.That(panel.AutoScrollPosition, Is.EqualTo(new Point(0, -60)));
    }

    [Test]
    public void ListBox_rows_paint_with_the_controls_font_and_fore_color()
    {
        var list = new ListBox { Bounds = new(0, 0, 100, 60), Font = _testFont, ForeColor = Color.Red };
        list.Items.Add("Alpha");
        var form = new Form();
        form.Controls.Add(list);
        var backend = Realize(form);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.DrewTextWithFont("Alpha", _testFont));
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Alpha\" #FFFF0000")));
        });
    }

    [Test]
    public void Appearance_changes_repaint_owner_drawn_controls()
    {
        var checkBox = new CheckBox { Bounds = new(0, 0, 100, 24) };
        var form = new Form();
        form.Controls.Add(checkBox);
        var backend = Realize(form);
        var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
        var before = canvas.InvalidateCount;

        checkBox.ForeColor = Color.Red;
        checkBox.Padding = new(2);
        form.Font = _testFont;

        Assert.That(canvas.InvalidateCount, Is.EqualTo(before + 3), "own color, own padding and the inherited font each repaint");
    }
}
