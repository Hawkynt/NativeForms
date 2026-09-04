using System.Drawing;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="TokenBox"/> turns committed text into removable chips: Enter or comma commits, the ×
/// zone and Backspace-on-empty delete, duplicates are rejected by default, and an
/// <see cref="TokenBox.AutoCompleteSource"/> drops down suggestions.
/// </summary>
[TestFixture]
internal sealed class TokenBoxTests {
  private static TokenBox Create(out HeadlessBackend backend, out HeadlessTextBoxPeer editor) {
    var box = new TokenBox { Bounds = new(0, 0, 240, 80) };
    backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(box);
    Application.Run(form, backend);
    editor = backend.Created.OfType<HeadlessTextBoxPeer>().Single();
    return box;
  }

  [Test]
  public void Enter_commits_the_typed_text_as_a_chip_and_clears_the_editor() {
    var box = Create(out _, out var editor);
    editor.SimulateUserInput("alpha");

    editor.SimulateKeyDown(Keys.Enter);

    Assert.Multiple(() => {
      Assert.That(box.Tokens, Is.EqualTo(new[] { "alpha" }));
      Assert.That(editor.Text, Is.Empty);
    });
  }

  [Test]
  public void A_comma_commits_a_chip() {
    var box = Create(out _, out var editor);
    editor.SimulateUserInput("beta");

    editor.SimulateKeyDown(Keys.Oemcomma);

    Assert.That(box.Tokens, Is.EqualTo(new[] { "beta" }));
  }

  [Test]
  public void Backspace_over_an_empty_editor_removes_the_last_chip() {
    var box = Create(out _, out var editor);
    box.AddToken("one");
    box.AddToken("two");

    editor.SimulateKeyDown(Keys.Back);

    Assert.That(box.Tokens, Is.EqualTo(new[] { "one" }));
  }

  [Test]
  public void Clicking_the_remove_zone_deletes_that_chip() {
    var box = Create(out var backend, out _);
    box.AddToken("gamma");
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    // The single chip is (4,4,67,20): 8+8 padding + 5×7 text + 16 remove zone. The × zone is the
    // trailing 16 px (x 55..71), so click at x=63.
    canvas.RaiseMouseDown(63, 14);

    Assert.That(box.Tokens, Is.Empty);
  }

  [Test]
  public void Duplicates_are_rejected_unless_allowed() {
    var box = Create(out _, out _);
    box.AddToken("dup");
    box.AddToken("dup");
    Assert.That(box.Tokens.Count, Is.EqualTo(1), "the duplicate is dropped by default");

    box.AllowDuplicates = true;
    box.AddToken("dup");
    Assert.That(box.Tokens.Count, Is.EqualTo(2), "duplicates allowed once opted in");
  }

  [Test]
  public void TokensChanged_fires_on_add_and_remove() {
    var box = Create(out _, out _);
    var changes = 0;
    box.TokensChanged += (_, _) => ++changes;

    box.AddToken("x");
    box.RemoveToken(0);

    Assert.That(changes, Is.EqualTo(2));
  }

  [Test]
  public void The_autocomplete_source_drops_down_matching_suggestions() {
    var box = Create(out _, out var editor);
    box.AutoCompleteSource = prefix => new[] { "apple", "apricot", "banana" }
        .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();

    editor.SimulateUserInput("ap");

    Assert.Multiple(() => {
      Assert.That(box.SuggestionsShownForTest, Is.True);
      Assert.That(box.SuggestionsForTest, Is.EqualTo(new[] { "apple", "apricot" }));
    });
  }

  [Test]
  public void The_chip_style_provider_colours_a_chip() {
    var box = Create(out var backend, out _);
    box.ChipStyleProvider = t => t == "hot"
        ? new TokenChipStyle { BackColor = Color.FromArgb(0xFF, 0xE8, 0x11, 0x23), FontStyle = FontStyle.Italic }
        : default;
    box.AddToken("hot");
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(o => o.StartsWith("fillround") && o.Contains("#FFE81123")), Is.True, "the chip uses the provided fill");
  }

  [Test]
  public void A_committed_chip_is_painted_with_a_remove_x() {
    var box = Create(out var backend, out _);
    box.AddToken("tag");
    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.Operations.Exists(o => o.Contains("\"tag\"")), Is.True, "the chip label is drawn");
      Assert.That(g.Operations.Exists(o => o.StartsWith("line")), Is.True, "the × strokes are drawn");
    });
  }
}
