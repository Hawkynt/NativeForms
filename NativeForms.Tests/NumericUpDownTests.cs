using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class NumericUpDownTests
{
    private static HeadlessBackend Realize(NumericUpDown upDown)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(upDown);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessCanvasPeer>().Single();

    private static HeadlessTextBoxPeer EditorOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessTextBoxPeer>().Single();

    [Test]
    public void Defaults_match_the_classic_control()
    {
        var upDown = new NumericUpDown();

        Assert.Multiple(() =>
        {
            Assert.That(upDown.Minimum, Is.EqualTo(0m));
            Assert.That(upDown.Maximum, Is.EqualTo(100m));
            Assert.That(upDown.Value, Is.EqualTo(0m));
            Assert.That(upDown.Increment, Is.EqualTo(1m));
            Assert.That(upDown.DecimalPlaces, Is.Zero);
        });
    }

    [Test]
    public void Value_clamps_to_the_range()
    {
        var upDown = new NumericUpDown { Minimum = 10, Maximum = 20 };

        upDown.Value = 50;
        Assert.That(upDown.Value, Is.EqualTo(20m));

        upDown.Value = -5;
        Assert.That(upDown.Value, Is.EqualTo(10m));
    }

    [Test]
    public void Raising_Minimum_above_Maximum_drags_Maximum_along()
    {
        var upDown = new NumericUpDown { Minimum = 0, Maximum = 10 };

        upDown.Minimum = 40;

        Assert.Multiple(() =>
        {
            Assert.That(upDown.Maximum, Is.EqualTo(40m));
            Assert.That(upDown.Value, Is.EqualTo(40m));
        });
    }

    [Test]
    public void Stepping_honors_Increment_and_clamps_at_the_ends()
    {
        var upDown = new NumericUpDown { Maximum = 5, Increment = 3 };

        upDown.UpButton();
        Assert.That(upDown.Value, Is.EqualTo(3m));

        upDown.UpButton();
        Assert.That(upDown.Value, Is.EqualTo(5m), "clamps at Maximum");

        upDown.DownButton();
        upDown.DownButton();
        Assert.That(upDown.Value, Is.EqualTo(0m), "clamps at Minimum");
    }

    [Test]
    public void Value_change_raises_ValueChanged_once()
    {
        var upDown = new NumericUpDown();
        var changes = 0;
        upDown.ValueChanged += (_, _) => ++changes;

        upDown.Value = 42;
        upDown.Value = 42;

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Editor_shows_the_value_formatted_to_DecimalPlaces()
    {
        var upDown = new NumericUpDown { DecimalPlaces = 2, Increment = 0.25m };
        var backend = Realize(upDown);

        upDown.UpButton();

        Assert.That(EditorOf(backend).Text, Is.EqualTo(0.25m.ToString("F2")));
    }

    [Test]
    public void Editor_leaves_the_button_column_free()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var backend = Realize(upDown);

        // The button column is ScrollBarSize + 1 = 17px wide; the editor fills the rest.
        Assert.That(EditorOf(backend).Bounds, Is.EqualTo(new Rectangle(0, 0, 103, 24)));
    }

    [Test]
    public void Clicking_the_spinner_buttons_steps_the_value()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var backend = Realize(upDown);
        var canvas = CanvasOf(backend);

        canvas.RaiseMouseDown(110, 5); // upper half of the button column
        canvas.RaiseMouseUp(110, 5);
        Assert.That(upDown.Value, Is.EqualTo(1m));

        canvas.RaiseMouseDown(110, 20); // lower half
        canvas.RaiseMouseUp(110, 20);
        Assert.That(upDown.Value, Is.EqualTo(0m));
    }

    [Test]
    public void Holding_a_spinner_button_autorepeats_via_the_timer()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var backend = Realize(upDown);
        var canvas = CanvasOf(backend);

        canvas.RaiseMouseDown(110, 5); // steps once and arms the initial 500ms delay
        Assert.That(upDown.Value, Is.EqualTo(1m));
        var timer = backend.Timers.Single();
        Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500 }));

        timer.FireTick(); // first repeat switches to the 50ms cadence
        Assert.Multiple(() =>
        {
            Assert.That(upDown.Value, Is.EqualTo(2m));
            Assert.That(timer.StartedIntervals, Is.EqualTo(new[] { 500, 50 }));
        });

        timer.FireTick();
        timer.FireTick();
        Assert.That(upDown.Value, Is.EqualTo(4m));

        canvas.RaiseMouseUp(110, 5);
        Assert.That(timer.IsRunning, Is.False, "release stops the autorepeat");
    }

    [Test]
    public void Up_and_Down_keys_step_the_value()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var canvas = CanvasOf(Realize(upDown));

        canvas.RaiseKeyDown(Keys.Up);
        canvas.RaiseKeyDown(Keys.Up);
        canvas.RaiseKeyDown(Keys.Down);

        Assert.That(upDown.Value, Is.EqualTo(1m));
    }

    [Test]
    public void Typed_text_commits_on_Value_read_with_clamping()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var backend = Realize(upDown);
        var editor = EditorOf(backend);

        editor.SimulateUserInput("42");
        Assert.That(upDown.Value, Is.EqualTo(42m));

        editor.SimulateUserInput("250");
        Assert.Multiple(() =>
        {
            Assert.That(upDown.Value, Is.EqualTo(100m), "out-of-range input clamps");
            Assert.That(editor.Text, Is.EqualTo(100m.ToString("F0")), "editor shows the clamped value");
        });
    }

    [Test]
    public void Invalid_text_reverts_on_commit()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24), Value = 10 };
        var backend = Realize(upDown);
        var editor = EditorOf(backend);

        editor.SimulateUserInput("abc");

        Assert.Multiple(() =>
        {
            Assert.That(upDown.Value, Is.EqualTo(10m));
            Assert.That(editor.Text, Is.EqualTo(10m.ToString("F0")), "commit rewrites the garbage");
        });
    }

    [Test]
    public void Typed_text_commits_when_the_surface_loses_focus()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var backend = Realize(upDown);
        var changes = 0;
        upDown.ValueChanged += (_, _) => ++changes;

        EditorOf(backend).SimulateUserInput("7");
        CanvasOf(backend).RaiseLostFocus();

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(upDown.Value, Is.EqualTo(7m));
        });
    }

    [Test]
    public void Stepping_commits_a_pending_edit_first()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var backend = Realize(upDown);

        EditorOf(backend).SimulateUserInput("30");
        CanvasOf(backend).RaiseKeyDown(Keys.Up);

        Assert.That(upDown.Value, Is.EqualTo(31m), "steps from the typed value, not the stale one");
    }

    [Test]
    public void Paints_field_border_and_spinner_glyphs()
    {
        var upDown = new NumericUpDown { Bounds = new(0, 0, 120, 24) };
        var canvas = CanvasOf(Realize(upDown));

        var g = canvas.RaisePaint();

        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("fill ")), Is.True, "fills the field");
            Assert.That(g.Operations.Exists(o => o.StartsWith("rect ")), Is.True, "draws the border");
            Assert.That(g.Operations.Exists(o => o.StartsWith("line ")), Is.True, "draws the arrows");
        });
    }

    [Test]
    public void ThousandsSeparator_formats_with_groups_and_parses_them_back()
    {
        var upDown = new NumericUpDown { Maximum = 10000, DecimalPlaces = 1, ThousandsSeparator = true, Value = 1234.5m };
        var editor = EditorOf(Realize(upDown));

        Assert.That(editor.Text, Is.EqualTo(1234.5m.ToString("N1")), "the editor groups digits");

        editor.SimulateUserInput(9876.5m.ToString("N1"));
        Assert.Multiple(() =>
        {
            Assert.That(upDown.Value, Is.EqualTo(9876.5m), "grouped input parses");
            Assert.That(editor.Text, Is.EqualTo(9876.5m.ToString("N1")), "the commit re-renders the grouping");
        });
    }
}
