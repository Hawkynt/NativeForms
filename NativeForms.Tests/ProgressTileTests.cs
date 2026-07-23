using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="ProgressTile"/> must render the Explorer drive-tile shape — icon, caption, usage bar
/// and secondary caption — clamp its value, switch the bar to the warning colour past
/// <see cref="ProgressTile.WarningThreshold"/>, and behave as a button only while
/// <see cref="ProgressTile.Clickable"/>.
/// </summary>
[TestFixture]
internal sealed class ProgressTileTests
{
    /// <summary>Realizes a tile on a fresh form and returns its canvas.</summary>
    private static HeadlessCanvasPeer Realize(ProgressTile tile, out HeadlessBackend backend)
    {
        backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 200) };
        form.Controls.Add(tile);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().Single();
    }

    private static ProgressTile Drive() => new()
    {
        Text = "Windows (C:)",
        SecondaryText = "45.2 GB free of 128 GB",
        Image = new HeadlessImage(24, 24),
        Bounds = new(0, 0, 220, 64),
        Maximum = 128,
        Value = 83,
    };

    [Test]
    public void Renders_the_icon_both_captions_and_the_usage_bar()
    {
        var canvas = Realize(Drive(), out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 24x24")), Is.True, "the drive icon");
            Assert.That(g.DrewText("Windows (C:)"), Is.True, "the primary caption");
            Assert.That(g.DrewText("45.2 GB free of 128 GB"), Is.True, "the secondary caption");
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4")), Is.True, "the accent usage fill");
        });
    }

    [Test]
    public void The_bar_fills_in_proportion_to_the_value()
    {
        var tile = new ProgressTile { Text = "Half", Bounds = new(0, 0, 220, 64), Maximum = 100, Value = 50 };
        var canvas = Realize(tile, out _);

        var g = canvas.RaisePaint();

        // Content spans 8..212 (204 wide); the bar's track is 202 px, so half is 101 px.
        Assert.That(g.Operations.Exists(o => o.StartsWith("fill #FF0078D4 9,") && o.EndsWith(",101,6")), Is.True);
    }

    [Test]
    public void An_empty_secondary_caption_draws_no_second_line()
    {
        var tile = new ProgressTile { Text = "Only one line", Bounds = new(0, 0, 220, 64) };
        var canvas = Realize(tile, out _);

        var g = canvas.RaisePaint();

        Assert.That(g.Operations.FindAll(o => o.StartsWith("text ")), Has.Count.EqualTo(1));
    }

    [Test]
    public void Value_is_clamped_into_the_range()
    {
        var tile = new ProgressTile { Maximum = 100 };

        tile.Value = 500;
        Assert.That(tile.Value, Is.EqualTo(100), "clamped up");

        tile.Value = -20;
        Assert.That(tile.Value, Is.EqualTo(0), "clamped down");
    }

    [Test]
    public void Lowering_Maximum_pulls_the_value_down_with_it()
    {
        var tile = new ProgressTile { Maximum = 100, Value = 90 };

        tile.Maximum = 50;

        Assert.That(tile.Value, Is.EqualTo(50));
    }

    [Test]
    public void ValueChanged_reports_a_real_move_only()
    {
        var tile = new ProgressTile { Maximum = 100 };
        var changes = 0;
        tile.ValueChanged += (_, _) => ++changes;

        tile.Value = 40;
        tile.Value = 40;

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void The_bar_turns_the_warning_colour_past_the_threshold()
    {
        var tile = new ProgressTile { Bounds = new(0, 0, 220, 64), Maximum = 100, Value = 89, WarningThreshold = 90 };
        var canvas = Realize(tile, out _);

        Assert.That(tile.IsWarning, Is.False, "below the threshold");
        Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith("fill #FF0078D4")), Is.True, "still accent");

        tile.Value = 95;

        Assert.That(tile.IsWarning, Is.True, "past the threshold");
        Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith("fill #FFE81123")), Is.True, "the alert red");
    }

    [Test]
    public void A_zero_threshold_leaves_the_warning_off_entirely()
    {
        var tile = new ProgressTile { Bounds = new(0, 0, 220, 64), Maximum = 100, Value = 100 };
        var canvas = Realize(tile, out _);

        Assert.Multiple(() =>
        {
            Assert.That(tile.IsWarning, Is.False);
            Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith("fill #FFE81123")), Is.False);
        });
    }

    [Test]
    public void The_warning_colour_is_replaceable()
    {
        var tile = new ProgressTile
        {
            Bounds = new(0, 0, 220, 64),
            Maximum = 100,
            Value = 95,
            WarningThreshold = 90,
            WarningColor = Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00),
        };
        var canvas = Realize(tile, out _);

        Assert.That(canvas.RaisePaint().Operations.Exists(o => o.StartsWith("fill #FFFFA500")), Is.True);
    }

    [Test]
    public void An_inert_tile_takes_no_focus_and_raises_no_click()
    {
        var tile = Drive();
        var canvas = Realize(tile, out _);
        var clicks = 0;
        tile.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(50, 28);
        canvas.RaiseMouseUp(50, 28);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Focusable, Is.False);
            Assert.That(clicks, Is.Zero);
        });
    }

    [Test]
    public void A_clickable_tile_takes_focus_and_raises_Click()
    {
        var tile = Drive();
        tile.Clickable = true;
        var canvas = Realize(tile, out _);
        var clicks = 0;
        tile.Click += (_, _) => ++clicks;

        canvas.RaiseMouseDown(50, 28);
        canvas.RaiseMouseUp(50, 28);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Focusable, Is.True);
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    [Test]
    public void Space_activates_a_clickable_tile_on_release()
    {
        var tile = Drive();
        tile.Clickable = true;
        var canvas = Realize(tile, out _);
        var clicks = 0;
        tile.Click += (_, _) => ++clicks;

        canvas.RaiseKeyDown(Keys.Space);
        Assert.That(clicks, Is.Zero, "not on the press");

        canvas.RaiseKeyUp(Keys.Space);
        Assert.That(clicks, Is.EqualTo(1), "on the release");
    }

    [Test]
    public void Hovering_a_clickable_tile_highlights_it()
    {
        var tile = Drive();
        tile.Clickable = true;
        var canvas = Realize(tile, out _);

        Assert.That(canvas.RaisePaint().Operations, Does.Contain("fill #FFFDFDFD 0,0,220,64"), "the resting face");

        canvas.RaiseMouseMove(50, 28);

        Assert.That(canvas.RaisePaint().Operations, Does.Contain("fill #FFECECEC 0,0,220,64"), "the hot face");

        canvas.RaiseMouseLeave();

        Assert.That(canvas.RaisePaint().Operations, Does.Contain("fill #FFFDFDFD 0,0,220,64"), "back to resting");
    }

    [Test]
    public void An_inert_tile_does_not_highlight_on_hover()
    {
        var tile = Drive();
        var canvas = Realize(tile, out _);

        canvas.RaiseMouseMove(50, 28);

        Assert.That(canvas.RaisePaint().Operations, Does.Contain("fill #FFFDFDFD 0,0,220,64"));
    }

    [Test]
    public void A_selected_tile_paints_the_selection_face()
    {
        var tile = Drive();
        tile.Selected = true;
        var canvas = Realize(tile, out _);

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations, Does.Contain("fill #FF0078D4 0,0,220,64"), "the selection background");
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Windows (C:)\" #FFFFFFFF")), Is.True, "selection text colour");
        });
    }

    [Test]
    public void Painting_stays_inside_the_client_rectangle()
    {
        var tile = new ProgressTile
        {
            Text = "A caption far too long for this tile to hold",
            SecondaryText = "and a secondary line that is also much too long to fit",
            Image = new HeadlessImage(24, 24),
            Bounds = new(0, 0, 120, 40),
            Maximum = 100,
            Value = 60,
        };
        var canvas = Realize(tile, out _);

        Assert.That(canvas.RaisePaint().OutOfBoundsOperations, Is.Empty);
    }

    // --- Compact layout ----------------------------------------------------------------------------

    private static ProgressTile CompactDrive() => new()
    {
        Text = "Windows (C:)",
        SecondaryText = "45.2 GB free of 128 GB",
        Image = new HeadlessImage(32, 32),
        Bounds = new(0, 0, 240, 48), // content 8..40 tall → the 32px icon fills it
        Maximum = 128,
        Value = 83,
        Compact = true,
    };

    private static Point TextAt(RecordingGraphics g, string text)
    {
        var op = g.Operations.First(o => o.StartsWith("text") && o.Contains($"\"{text}\""));
        var at = op[(op.LastIndexOf('@') + 1)..].Split(',');
        return new(int.Parse(at[0]), int.Parse(at[1]));
    }

    private static int AccentFillY(RecordingGraphics g)
        => int.Parse(g.Operations.First(o => o.StartsWith("fill #FF0078D4")).Split(' ')[2].Split(',')[1]);

    [Test]
    public void Compact_puts_the_caption_over_the_bar_beside_the_icon_and_drops_the_secondary_line()
    {
        var g = Realize(CompactDrive(), out _).RaisePaint();
        var caption = TextAt(g, "Windows (C:)");

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("image 32x32")), Is.True, "the icon still paints");
            Assert.That(g.DrewText("45.2 GB free of 128 GB"), Is.False, "the secondary line is dropped in compact mode");
            Assert.That(caption.X, Is.GreaterThan(32), "the caption sits to the right of the icon");
            Assert.That(caption.Y, Is.EqualTo(8), "the caption is top-aligned to the icon top");
            Assert.That(AccentFillY(g), Is.GreaterThan(caption.Y), "the usage bar sits below the caption");
            Assert.That(AccentFillY(g), Is.LessThan(40), "and within the icon's vertical band");
        });
    }

    [Test]
    public void The_full_tile_still_shows_the_secondary_line()
    {
        var g = Realize(Drive(), out _).RaisePaint();
        Assert.That(g.DrewText("45.2 GB free of 128 GB"), Is.True, "the non-compact tile keeps its secondary caption");
    }

    // A small icon leaves the caption taller than the icon band: the bar, sitting below the caption,
    // must lay out against the content — not the icon — so an icon-height clip never swallows it.
    private static ProgressTile CompactSmallIcon() => new()
    {
        Text = "Downloads",
        Image = new HeadlessImage(16, 16),
        Bounds = new(0, 0, 240, 52), // content 8..44: a 16px icon shorter than caption + bar
        Maximum = 100,
        Value = 62,
        Compact = true,
    };

    [Test]
    public void Compact_draws_the_bar_below_a_caption_taller_than_a_small_icon_without_clipping_it()
    {
        var g = Realize(CompactSmallIcon(), out _).RaisePaint();
        var caption = TextAt(g, "Downloads");

        Assert.Multiple(() =>
        {
            Assert.That(AccentFillY(g), Is.GreaterThan(caption.Y), "the usage bar sits below the caption");
            Assert.That(g.StayedInBounds, Is.True, "an icon-height clip no longer swallows the bar");
        });
    }
}
