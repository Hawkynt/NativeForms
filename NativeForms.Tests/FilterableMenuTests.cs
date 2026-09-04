using System.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Type-to-filter menus (PRD §14): a <see cref="ContextMenuStrip.ShowSearchBox"/> menu carries a
/// search field as its first row, typing narrows the rows below it instead of running mnemonics, the
/// popup re-fits itself in place as it narrows, and Escape backs out the filter before the menu.
/// </summary>
[TestFixture]
internal sealed class FilterableMenuTests {
  private static readonly string[] _Columns = ["Name", "Size", "Modified", "Kind", "Nickname"];

  /// <summary>Opens a searchable menu over a realized panel and hands back the actors.</summary>
  private static ContextMenuStrip Open(out HeadlessPopupPeer popup, out HeadlessBackend backend, bool searchable = true) {
    var menu = new ContextMenuStrip { ShowSearchBox = searchable };
    foreach (var column in _Columns)
      menu.Items.Add(new ToolStripMenuItem(column));

    var panel = new Panel { Bounds = new(10, 10, 200, 150), ContextMenuStrip = menu };
    backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(panel);
    Application.Run(form, backend);

    var canvas = backend.Created.OfType<HeadlessCanvasPeer>().Single();
    canvas.RaiseMouseDown(30, 40, MouseButtons.Right);
    popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    return menu;
  }

  private static void Type(HeadlessPopupPeer popup, string text) {
    foreach (var c in text)
      popup.RaiseKeyPress(c);
  }

  /// <summary>The item captions the popup actually painted, in order.</summary>
  private static List<string> RowsOf(HeadlessPopupPeer popup) {
    var painted = popup.RaisePaint();
    return painted.TextDraws
        .Select(static draw => draw.Text)
        .Where(static text => _Columns.Contains(text))
        .ToList();
  }

  [Test]
  public void A_searchable_menu_paints_a_search_field_above_its_items() {
    Open(out var popup, out _);

    var painted = popup.RaisePaint();

    Assert.That(
        painted.TextDraws.Select(static draw => draw.Text),
        Does.Contain(Strings.SearchPlaceholder),
        "the placeholder stands in until something is typed");
  }

  [Test]
  public void A_plain_menu_does_not() {
    Open(out var popup, out _, searchable: false);

    var painted = popup.RaisePaint();

    Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Not.Contain(Strings.SearchPlaceholder));
  }

  [Test]
  public void Typing_narrows_the_rows_to_what_matches() {
    Open(out var popup, out _);

    Type(popup, "na");

    Assert.That(RowsOf(popup), Is.EqualTo(new[] { "Name", "Nickname" }), "matched anywhere in the caption, not just at the start");
  }

  [Test]
  public void Matching_ignores_case() {
    Open(out var popup, out _);

    Type(popup, "SIZE");

    Assert.That(RowsOf(popup), Is.EqualTo(new[] { "Size" }));
  }

  [Test]
  public void What_was_typed_replaces_the_placeholder() {
    Open(out var popup, out _);

    Type(popup, "mod");
    var painted = popup.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Contain("mod"));
      Assert.That(painted.TextDraws.Select(static draw => draw.Text), Does.Not.Contain(Strings.SearchPlaceholder));
    });
  }

  [Test]
  public void Backspace_widens_it_again() {
    Open(out var popup, out _);

    Type(popup, "nam");
    popup.RaiseKeyDown(Keys.Back);

    Assert.That(RowsOf(popup), Is.EqualTo(new[] { "Name", "Nickname" }));
  }

  [Test]
  public void The_popup_re_fits_itself_as_the_filter_narrows() {
    Open(out var popup, out _);
    var opened = popup.ShowCalls.Single().Size;

    Type(popup, "size");

    Assert.Multiple(() => {
      Assert.That(popup.ResizeCalls, Is.Not.Empty, "a filtered menu shrinks rather than leaving a tall empty box");
      Assert.That(popup.ResizeCalls[^1].Height, Is.LessThan(opened.Height));
      Assert.That(popup.ShowCalls, Has.Count.EqualTo(1), "resized in place, never re-shown — that would hand the grab round");
    });
  }

  [Test]
  public void Escape_clears_the_filter_before_it_closes_the_menu() {
    var menu = Open(out var popup, out _);
    Type(popup, "size");

    popup.RaiseKeyDown(Keys.Escape);
    var stillOpen = menu.IsOpen;
    var rows = RowsOf(popup);

    popup.RaiseKeyDown(Keys.Escape);

    Assert.Multiple(() => {
      Assert.That(stillOpen, Is.True, "the first Escape spends itself on what was typed");
      Assert.That(rows, Is.EqualTo(_Columns), "which restores every row");
      Assert.That(menu.IsOpen, Is.False, "and the second closes the menu");
    });
  }

  [Test]
  public void Escape_closes_a_searchable_menu_that_has_nothing_typed() {
    var menu = Open(out var popup, out _);

    popup.RaiseKeyDown(Keys.Escape);

    Assert.That(menu.IsOpen, Is.False);
  }

  [Test]
  public void Arrows_walk_only_the_rows_that_survived_the_filter() {
    var clicked = (string?)null;
    var menu = Open(out var popup, out _);
    foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
      item.Click += (sender, _) => clicked = ((ToolStripMenuItem)sender!).Text;

    Type(popup, "na");
    popup.RaiseKeyDown(Keys.Down); // Name
    popup.RaiseKeyDown(Keys.Down); // Nickname, skipping the three filtered out between them
    popup.RaiseKeyDown(Keys.Enter);

    Assert.That(clicked, Is.EqualTo("Nickname"));
  }

  [Test]
  public void A_click_lands_on_the_row_the_filter_left_there() {
    var clicked = (string?)null;
    var menu = Open(out var popup, out _);
    foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
      item.Click += (sender, _) => clicked = ((ToolStripMenuItem)sender!).Text;

    Type(popup, "na");

    // The first row below the search field, which the filter has made "Name" rather than the
    // item that occupies that position unfiltered.
    var theme = new HeadlessBackend().Theme;
    popup.RaiseMouseDown(20, 1 + theme.RowHeight + (theme.RowHeight / 2));

    Assert.That(clicked, Is.EqualTo("Name"));
  }

  [Test]
  public void Typing_into_a_plain_menu_still_runs_its_mnemonics() {
    var clicked = (string?)null;
    var menu = new ContextMenuStrip();
    foreach (var caption in new[] { "&Name", "&Size", "&Modified" }) {
      var item = new ToolStripMenuItem(caption);
      item.Click += (sender, _) => clicked = ((ToolStripMenuItem)sender!).DisplayText;
      menu.Items.Add(item);
    }

    var panel = new Panel { Bounds = new(10, 10, 200, 150), ContextMenuStrip = menu };
    var backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(panel);
    Application.Run(form, backend);
    backend.Created.OfType<HeadlessCanvasPeer>().Single().RaiseMouseDown(30, 40, MouseButtons.Right);

    backend.Created.OfType<HeadlessPopupPeer>().Single().RaiseKeyPress('s');

    Assert.That(clicked, Is.EqualTo("Size"), "type-to-filter must not have taken the mnemonics from every other menu");
  }
}
