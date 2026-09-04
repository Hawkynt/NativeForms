using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A <see cref="CodeTextBox"/> edits multi-line text with a line-number gutter and a delegate
/// tokenizer: typing inserts at the caret, Enter splits with auto-indent, Backspace merges, Tab inserts
/// spaces, and a tokenizer colours keyword / string / comment spans.
/// </summary>
[TestFixture]
internal sealed class CodeTextBoxTests {
  private static CodeTextBox Create(out HeadlessCanvasPeer canvas) {
    var box = new CodeTextBox { Bounds = new(0, 0, 400, 200) };
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(box);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
    return box;
  }

  [Test]
  public void Text_round_trips_through_lines() {
    var box = Create(out _);
    box.Text = "alpha\nbeta\ngamma";

    Assert.Multiple(() => {
      Assert.That(box.Lines.Count, Is.EqualTo(3));
      Assert.That(box.Lines[1], Is.EqualTo("beta"));
      Assert.That(box.Text, Is.EqualTo("alpha\nbeta\ngamma"));
    });
  }

  [Test]
  public void Typing_inserts_characters_at_the_caret() {
    var box = Create(out var canvas);

    canvas.RaiseKeyPress('h');
    canvas.RaiseKeyPress('i');

    Assert.Multiple(() => {
      Assert.That(box.Lines[0], Is.EqualTo("hi"));
      Assert.That(box.CaretColumn, Is.EqualTo(2));
    });
  }

  [Test]
  public void Enter_splits_the_line_and_copies_the_indent() {
    var box = Create(out var canvas);
    box.Text = "  foo";
    canvas.RaiseKeyDown(Keys.End);

    canvas.RaiseKeyDown(Keys.Enter);

    Assert.Multiple(() => {
      Assert.That(box.Lines.Count, Is.EqualTo(2));
      Assert.That(box.Lines[1], Is.EqualTo("  "), "the new line keeps the two-space indent");
      Assert.That(box.CaretColumn, Is.EqualTo(2));
    });
  }

  [Test]
  public void Backspace_at_the_start_of_a_line_merges_it_upward() {
    var box = Create(out var canvas);
    box.Text = "ab\ncd";
    canvas.RaiseKeyDown(Keys.Down);
    canvas.RaiseKeyDown(Keys.Home);

    canvas.RaiseKeyDown(Keys.Back);

    Assert.Multiple(() => {
      Assert.That(box.Lines.Count, Is.EqualTo(1));
      Assert.That(box.Lines[0], Is.EqualTo("abcd"));
      Assert.That(box.CaretColumn, Is.EqualTo(2));
    });
  }

  [Test]
  public void Tab_inserts_spaces_to_the_tab_width() {
    var box = Create(out var canvas);
    box.TabWidth = 4;

    canvas.RaiseKeyDown(Keys.Tab);

    Assert.That(box.Lines[0], Is.EqualTo("    "));
  }

  [Test]
  public void Shift_selection_then_typing_replaces_the_selection() {
    var box = Create(out var canvas);
    box.Text = "hello";
    canvas.RaiseKeyDown(Keys.Home);
    canvas.RaiseKeyDown(Keys.Right, KeyModifiers.Shift);
    canvas.RaiseKeyDown(Keys.Right, KeyModifiers.Shift); // selects "he"

    canvas.RaiseKeyPress('X');

    Assert.That(box.Lines[0], Is.EqualTo("Xllo"));
  }

  [Test]
  public void The_tokenizer_colours_a_keyword_span() {
    var box = Create(out var canvas);
    box.Text = "int x";
    box.Tokenizer = line => line.StartsWith("int", StringComparison.Ordinal)
        ? new[] { new CodeToken(0, 3, CodeTokenKind.Keyword) }
        : (IReadOnlyList<CodeToken>)System.Array.Empty<CodeToken>();

    var g = canvas.RaisePaint();

    Assert.That(g.Operations.Exists(o => o.Contains("\"int\"") && o.Contains("#FF0000FF")), Is.True,
        "the keyword span is drawn in the keyword colour");
  }

  private static CodeTextBox CreateWithBackend(out HeadlessBackend backend, out HeadlessCanvasPeer canvas) {
    var box = new CodeTextBox { Bounds = new(0, 0, 400, 200) };
    backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(box);
    Application.Run(form, backend);
    canvas = backend.Created.OfType<HeadlessCanvasPeer>().First();
    return box;
  }

  [Test]
  public void Ctrl_Space_opens_the_completion_list() {
    var box = CreateWithBackend(out _, out var canvas);
    box.CompletionProvider = _ => new[] { "foo", "bar" };

    canvas.RaiseKeyDown(Keys.Space, KeyModifiers.Control);

    Assert.Multiple(() => {
      Assert.That(box.CompletionShownForTest, Is.True);
      Assert.That(box.CompletionsForTest, Is.EqualTo(new[] { "foo", "bar" }));
    });
  }

  [Test]
  public void Typing_an_identifier_opens_completion_and_Enter_accepts_it() {
    var box = CreateWithBackend(out var backend, out var canvas);
    box.CompletionProvider = p => new[] { "Console", "Convert" }
        .Where(s => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)).ToArray();

    canvas.RaiseKeyPress('C'); // auto-opens
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Last();
    popup.RaiseKeyDown(Keys.Enter); // accept the first candidate

    Assert.Multiple(() => {
      Assert.That(box.Lines[0], Is.EqualTo("Console"));
      Assert.That(box.CompletionShownForTest, Is.False);
    });
  }

  [Test]
  public void Clicking_a_completion_row_inserts_that_candidate() {
    var box = CreateWithBackend(out var backend, out var canvas);
    box.CompletionProvider = _ => new[] { "alpha", "beta" };

    canvas.RaiseKeyDown(Keys.Space, KeyModifiers.Control);
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Last();
    popup.RaiseMouseDown(5, 20); // second row (16-px lines) → "beta"

    Assert.That(box.Lines[0], Is.EqualTo("beta"));
  }

  [Test]
  public void The_gutter_shows_line_numbers() {
    var box = Create(out var canvas);
    box.Text = "one\ntwo";

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.Operations.Exists(o => o.Contains("\"1\"")), Is.True);
      Assert.That(g.Operations.Exists(o => o.Contains("\"2\"")), Is.True);
    });
  }
}
