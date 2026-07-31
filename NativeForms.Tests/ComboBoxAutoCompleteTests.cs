using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="ComboBox.AutoCompleteMode"/> (PRD §7.4): inline completion into the hosted editor, the
/// drop-down narrowing to the matches, and the rules that keep both usable — a deletion never
/// completes, no match closes the list rather than opening an empty one, and a committed row is the
/// item the filter left under the pointer rather than the one that position holds unfiltered.
/// </summary>
[TestFixture]
internal sealed class ComboBoxAutoCompleteTests
{
    private static readonly string[] _Fruit = ["Apple", "Apricot", "Banana", "Blueberry", "Cherry"];

    /// <summary>An editable combo over a headless backend, realized and ready to be typed into.</summary>
    private static ComboBox Open(out HeadlessBackend backend, AutoCompleteMode mode)
    {
        var combo = new ComboBox
        {
            Bounds = new(0, 0, 160, 24),
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = mode,
        };

        foreach (var fruit in _Fruit)
            combo.Items.Add(fruit);

        backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(combo);
        Application.Run(form, backend);
        return combo;
    }

    /// <summary>The hosted editor, which is the control the user actually types into.</summary>
    private static TextBox EditorOf(ComboBox combo) => combo.Controls.OfType<TextBox>().Single();

    /// <summary>Types <paramref name="text"/> the way a user does — one character at a time, each
    /// replacing whatever the previous keystroke left selected.</summary>
    private static void Type(ComboBox combo, string text)
    {
        var editor = EditorOf(combo);
        foreach (var c in text)
        {
            // A keystroke replaces what is selected — which after a completion is the part that was
            // filled in — and otherwise lands at the end.
            var keep = editor.SelectionLength > 0 ? editor.SelectionStart : editor.Text.Length;
            var typed = editor.Text[..keep] + c;
            editor.Text = typed;
            if (editor.Text == typed)
                editor.Select(typed.Length, 0); // nothing completed, so the caret is ours to place
        }
    }

    /// <summary>The captions the open drop-down is painting, in order.</summary>
    private static List<string> RowsOf(HeadlessBackend backend)
    {
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        return popup.RaisePaint()
            .TextDraws
            .Select(static draw => draw.Text)
            .Where(static text => _Fruit.Contains(text))
            .ToList();
    }

    // --- Append ------------------------------------------------------------------------------------

    [Test]
    public void Append_fills_in_the_rest_of_the_first_match()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "ban");

        Assert.That(combo.Text, Is.EqualTo("Banana"));
    }

    [Test]
    public void What_was_filled_in_is_left_selected_so_the_next_key_replaces_it()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "ban");
        var editor = EditorOf(combo);

        Assert.Multiple(() =>
        {
            Assert.That(editor.SelectionStart, Is.EqualTo(3), "the selection starts where the typing stopped");
            Assert.That(editor.SelectionLength, Is.EqualTo(3), "and covers exactly what was filled in");
            Assert.That(editor.SelectedText, Is.EqualTo("ana"));
        });
    }

    [Test]
    public void Typing_on_replaces_the_completion_rather_than_appending_to_it()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "ap");   // -> "Apple", with "ple" selected
        Type(combo, "r");    // the "r" replaces the selection: "Apr" -> "Apricot"

        Assert.That(combo.Text, Is.EqualTo("Apricot"));
    }

    [Test]
    public void Matching_ignores_case()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "CHE");

        Assert.That(combo.Text, Is.EqualTo("Cherry"), "the item's own casing wins — it is the item that is being named");
    }

    [Test]
    public void Deleting_does_not_complete_again()
    {
        var combo = Open(out _, AutoCompleteMode.Append);
        Type(combo, "ban");

        var editor = EditorOf(combo);
        editor.Text = "Ban"; // what Backspace over the selected completion leaves

        Assert.That(combo.Text, Is.EqualTo("Ban"), "re-completing a deletion would make the text impossible to shorten");
    }

    [Test]
    public void Text_that_matches_nothing_is_left_alone()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "zz");

        Assert.That(combo.Text, Is.EqualTo("zz"));
    }

    [Test]
    public void An_exact_match_is_not_re_completed()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "Cherry");

        Assert.Multiple(() =>
        {
            Assert.That(combo.Text, Is.EqualTo("Cherry"));
            Assert.That(EditorOf(combo).SelectionLength, Is.Zero, "there is nothing left to offer, so nothing is selected");
        });
    }

    [Test]
    public void TextChanged_reports_the_completed_text_once()
    {
        var combo = Open(out _, AutoCompleteMode.Append);
        var seen = new List<string>();
        combo.TextChanged += (_, _) => seen.Add(combo.Text);

        Type(combo, "ban");

        Assert.That(seen[^1], Is.EqualTo("Banana"), "the completion must not arrive as a second event after the typed text");
        Assert.That(seen, Has.Count.EqualTo(3), "one event per keystroke, not two");
    }

    [Test]
    public void Append_alone_does_not_open_the_drop_down()
    {
        var combo = Open(out _, AutoCompleteMode.Append);

        Type(combo, "ban");

        Assert.That(combo.DroppedDown, Is.False);
    }

    // --- Suggest -----------------------------------------------------------------------------------

    [Test]
    public void Suggest_opens_the_drop_down_narrowed_to_the_matches()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);

        Type(combo, "b");

        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.True);
            Assert.That(RowsOf(backend), Is.EqualTo(new[] { "Banana", "Blueberry" }));
        });
    }

    [Test]
    public void The_list_re_fits_itself_as_the_filter_narrows()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        var twoRows = popup.ResizeCalls.Count > 0 ? popup.ResizeCalls[^1].Height : popup.ShowCalls[^1].Size.Height;

        Type(combo, "l"); // "bl" -> Blueberry alone

        Assert.Multiple(() =>
        {
            Assert.That(popup.ResizeCalls[^1].Height, Is.LessThan(twoRows), "one row is shorter than two");
            Assert.That(popup.ShowCalls, Has.Count.EqualTo(1), "resized in place, never re-shown — that would hand the grab round");
        });
    }

    [Test]
    public void No_match_closes_the_list_rather_than_showing_an_empty_one()
    {
        var combo = Open(out _, AutoCompleteMode.Suggest);
        Type(combo, "b");

        Type(combo, "z"); // "bz" matches nothing

        Assert.That(combo.DroppedDown, Is.False);
    }

    [Test]
    public void Emptying_the_field_closes_the_list_and_drops_the_filter()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");

        EditorOf(combo).Text = string.Empty;
        combo.OpenDropDown();

        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.True);
            Assert.That(RowsOf(backend), Is.EqualTo(_Fruit), "an empty field matches everything, which is no filter at all");
        });
    }

    [Test]
    public void Arrows_walk_only_the_rows_that_survived_the_filter()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");

        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseKeyDown(Keys.Down); // Banana
        popup.RaiseKeyDown(Keys.Down); // Blueberry, skipping the three the filter dropped
        popup.RaiseKeyDown(Keys.Enter);

        Assert.That(combo.SelectedItem, Is.EqualTo("Blueberry"));
    }

    [Test]
    public void A_click_lands_on_the_row_the_filter_left_there()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");

        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(10, backend.Theme.RowHeight + 2); // the second surviving row

        Assert.Multiple(() =>
        {
            Assert.That(combo.SelectedItem, Is.EqualTo("Blueberry"), "not the second item of the unfiltered list");
            Assert.That(combo.DroppedDown, Is.False);
        });
    }

    [Test]
    public void The_next_open_after_a_commit_shows_the_whole_list_again()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");
        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        popup.RaiseMouseDown(10, 2);

        combo.OpenDropDown();

        Assert.That(RowsOf(backend), Is.EqualTo(_Fruit), "the filter belonged to the edit, which is over");
    }

    [Test]
    public void Committing_a_suggestion_does_not_re_open_the_list_it_just_closed()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");

        backend.Created.OfType<HeadlessPopupPeer>().Single().RaiseMouseDown(10, 2);

        Assert.Multiple(() =>
        {
            Assert.That(combo.DroppedDown, Is.False, "pushing the selection into the editor is not typing");
            Assert.That(combo.Text, Is.EqualTo("Banana"));
        });
    }

    // --- Both, and the off switch ------------------------------------------------------------------

    [Test]
    public void SuggestAppend_does_both()
    {
        var combo = Open(out var backend, AutoCompleteMode.SuggestAppend);

        Type(combo, "b");

        Assert.Multiple(() =>
        {
            Assert.That(combo.Text, Is.EqualTo("Banana"), "filled in");
            Assert.That(RowsOf(backend), Is.EqualTo(new[] { "Banana", "Blueberry" }), "and narrowed");
        });
    }

    [Test]
    public void None_completes_nothing_and_opens_nothing()
    {
        var combo = Open(out _, AutoCompleteMode.None);

        Type(combo, "ban");

        Assert.Multiple(() =>
        {
            Assert.That(combo.Text, Is.EqualTo("ban"));
            Assert.That(combo.DroppedDown, Is.False);
        });
    }

    [Test]
    public void Turning_the_mode_off_drops_a_filter_it_had_left_showing()
    {
        var combo = Open(out var backend, AutoCompleteMode.Suggest);
        Type(combo, "b");

        combo.AutoCompleteMode = AutoCompleteMode.None;

        Assert.That(RowsOf(backend), Is.EqualTo(_Fruit));
    }

    [Test]
    public void A_closed_style_combo_is_unaffected()
    {
        var combo = new ComboBox { Bounds = new(0, 0, 160, 24), AutoCompleteMode = AutoCompleteMode.SuggestAppend };
        foreach (var fruit in _Fruit)
            combo.Items.Add(fruit);

        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(combo);
        Application.Run(form, backend);

        combo.OpenDropDown();

        Assert.Multiple(() =>
        {
            Assert.That(combo.Controls.OfType<TextBox>().Any(), Is.False, "nothing to type into");
            Assert.That(RowsOf(backend), Is.EqualTo(_Fruit), "and so nothing to filter by");
        });
    }
}
