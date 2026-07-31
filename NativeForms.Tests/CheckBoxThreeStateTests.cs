using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The tri-state <see cref="CheckBox"/> (PRD §7.3): <see cref="CheckBox.CheckState"/> beside the
/// boolean, the click cycle <see cref="CheckBox.ThreeState"/> turns on, the two events and how they
/// differ, and the same behaviour on the promoted native path (PRD §12).
/// </summary>
[TestFixture]
internal sealed class CheckBoxThreeStateTests
{
    /// <summary>Realizes a box on a headless backend, optionally letting it promote to a widget.</summary>
    private static CheckBox Realize(out HeadlessBackend backend, bool native = false)
    {
        var box = new CheckBox { Text = "Read-only", Bounds = new(0, 0, 120, 20) };
        backend = new HeadlessBackend { OfferNativeCheckBox = native };
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);
        return box;
    }

    [Test]
    public void A_new_box_is_unchecked()
    {
        var box = new CheckBox();

        Assert.Multiple(() =>
        {
            Assert.That(box.CheckState, Is.EqualTo(CheckState.Unchecked));
            Assert.That(box.Checked, Is.False);
            Assert.That(box.ThreeState, Is.False, "the third state is opt-in — a plain box keeps toggling between two");
        });
    }

    [Test]
    public void Indeterminate_reads_as_checked()
    {
        var box = new CheckBox { CheckState = CheckState.Indeterminate };

        Assert.That(box.Checked, Is.True, "the Windows Forms rule: code asking only the boolean question must not read mixed as off");
    }

    [Test]
    public void Assigning_Checked_projects_onto_the_two_plain_states()
    {
        var box = new CheckBox { CheckState = CheckState.Indeterminate };

        box.Checked = true;
        var afterTrue = box.CheckState;
        box.Checked = false;

        Assert.Multiple(() =>
        {
            Assert.That(afterTrue, Is.EqualTo(CheckState.Checked), "assigning the boolean leaves no way to land back on mixed");
            Assert.That(box.CheckState, Is.EqualTo(CheckState.Unchecked));
        });
    }

    [Test]
    public void A_two_state_box_flips_on_click()
    {
        var box = new CheckBox();

        box.PerformClick();
        var first = box.CheckState;
        box.PerformClick();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(CheckState.Checked));
            Assert.That(box.CheckState, Is.EqualTo(CheckState.Unchecked), "without ThreeState the cycle never reaches mixed");
        });
    }

    [Test]
    public void A_three_state_box_walks_unchecked_checked_indeterminate()
    {
        var box = new CheckBox { ThreeState = true };
        var walk = new List<CheckState>();

        for (var i = 0; i < 4; ++i)
        {
            box.PerformClick();
            walk.Add(box.CheckState);
        }

        Assert.That(walk, Is.EqualTo(new[] { CheckState.Checked, CheckState.Indeterminate, CheckState.Unchecked, CheckState.Checked }));
    }

    [Test]
    public void A_box_left_indeterminate_without_ThreeState_clears_on_the_next_click()
    {
        var box = new CheckBox { CheckState = CheckState.Indeterminate };

        box.PerformClick();

        Assert.That(box.CheckState, Is.EqualTo(CheckState.Unchecked), "an application that assigned mixed must not trap the user in it");
    }

    [Test]
    public void CheckStateChanged_reports_the_move_between_checked_and_indeterminate_and_CheckedChanged_does_not()
    {
        var box = new CheckBox { ThreeState = true, Checked = true };
        var states = 0;
        var checks = 0;
        box.CheckStateChanged += (_, _) => ++states;
        box.CheckedChanged += (_, _) => ++checks;

        box.PerformClick(); // checked -> indeterminate: both are "checked"

        Assert.Multiple(() =>
        {
            Assert.That(states, Is.EqualTo(1), "the state moved");
            Assert.That(checks, Is.Zero, "but the boolean answer did not, so the boolean event must stay quiet");
            Assert.That(box.Checked, Is.True);
        });
    }

    [Test]
    public void Both_events_report_the_move_out_of_indeterminate()
    {
        var box = new CheckBox { ThreeState = true, CheckState = CheckState.Indeterminate };
        var states = 0;
        var checks = 0;
        box.CheckStateChanged += (_, _) => ++states;
        box.CheckedChanged += (_, _) => ++checks;

        box.PerformClick(); // indeterminate -> unchecked

        Assert.Multiple(() =>
        {
            Assert.That(states, Is.EqualTo(1));
            Assert.That(checks, Is.EqualTo(1), "Checked went from true to false");
        });
    }

    [Test]
    public void Assigning_the_state_it_already_has_raises_nothing()
    {
        var box = new CheckBox { CheckState = CheckState.Indeterminate };
        var raised = 0;
        box.CheckStateChanged += (_, _) => ++raised;
        box.CheckedChanged += (_, _) => ++raised;

        box.CheckState = CheckState.Indeterminate;
        box.Checked = true; // already true, and mixed projects to checked — this one does move the state

        Assert.That(raised, Is.EqualTo(1), "only the assignment that actually moved the state reported");
    }

    [Test]
    public void The_indeterminate_glyph_is_a_filled_square_rather_than_a_check()
    {
        var unchecked_ = Paint(CheckState.Unchecked);
        var mixed = Paint(CheckState.Indeterminate);
        var checked_ = Paint(CheckState.Checked);

        Assert.Multiple(() =>
        {
            Assert.That(mixed.Fills, Is.GreaterThan(unchecked_.Fills), "mixed adds a filled mark the empty box does not have");
            Assert.That(mixed.Lines, Is.EqualTo(unchecked_.Lines), "and draws no checkmark strokes");
            Assert.That(checked_.Lines, Is.GreaterThan(unchecked_.Lines), "where checked is strokes rather than a fill");
            Assert.That(checked_.Fills, Is.EqualTo(unchecked_.Fills));
        });

        static (int Fills, int Lines) Paint(CheckState state)
        {
            var box = Realize(out var backend);
            box.CheckState = state;
            var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
            var operations = canvas.RaisePaint().Operations;
            return (operations.Count(static op => op.StartsWith("fill ")), operations.Count(static op => op.StartsWith("line ")));
        }
    }

    // --- the promoted native path (PRD §12) --------------------------------------------------------

    [Test]
    public void A_promoted_box_is_told_about_the_third_state_before_it_is_given_one()
    {
        var box = new CheckBox { ThreeState = true, CheckState = CheckState.Indeterminate };
        var backend = new HeadlessBackend { OfferNativeCheckBox = true };
        var form = new Form();
        form.Controls.Add(box);
        Application.Run(form, backend);

        var peer = backend.LastCheckBox!;

        Assert.Multiple(() =>
        {
            Assert.That(box.IsNativeWidget, Is.True);
            Assert.That(peer.ThreeState, Is.True, "a widget asked to hold mixed before being handed it");
            Assert.That(peer.GetCheckState(), Is.EqualTo(CheckState.Indeterminate));
        });
    }

    [Test]
    public void Turning_the_third_state_on_afterwards_reaches_the_widget()
    {
        var box = Realize(out var backend, native: true);

        box.ThreeState = true;

        Assert.That(backend.LastCheckBox!.ThreeState, Is.True);
    }

    [Test]
    public void The_state_the_widget_reached_by_itself_is_mirrored_into_the_core()
    {
        var box = Realize(out var backend, native: true);
        box.ThreeState = true;
        var seen = new List<CheckState>();
        box.CheckStateChanged += (_, _) => seen.Add(box.CheckState);

        var peer = backend.LastCheckBox!;
        peer.RaiseUserToggle(); // the platform's own cycle: -> checked
        peer.RaiseUserToggle(); // -> indeterminate

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new[] { CheckState.Checked, CheckState.Indeterminate }));
            Assert.That(box.CheckState, Is.EqualTo(CheckState.Indeterminate), "read back from the widget, not inferred");
        });
    }

    [Test]
    public void A_programmatic_state_change_does_not_come_back_round_through_the_widget()
    {
        var box = Realize(out var backend, native: true);
        box.ThreeState = true;
        var raised = 0;
        box.CheckStateChanged += (_, _) => ++raised;

        box.CheckState = CheckState.Indeterminate;

        Assert.Multiple(() =>
        {
            Assert.That(raised, Is.EqualTo(1), "exactly once — the peer must not re-raise what the core already reported");
            Assert.That(backend.LastCheckBox!.GetCheckState(), Is.EqualTo(CheckState.Indeterminate));
        });
    }
}
