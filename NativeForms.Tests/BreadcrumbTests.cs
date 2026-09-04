using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="Breadcrumb"/> must lay path segments left to right separated by chevrons, hit-test a
/// click to its segment, trim the path to a clicked segment (the navigate-up gesture) and fold the
/// leading segments behind a "…" chip when they outgrow the width.
/// </summary>
[TestFixture]
internal sealed class BreadcrumbTests {
  // RecordingGraphics measures 7 px per character; segment = 2*8 padding + text; chevron advance 16.
  private const int _Pad = 8;
  private const int _Char = 7;
  private const int _Chevron = 16;

  private static HeadlessCanvasPeer Realize(Breadcrumb crumb, out HeadlessBackend backend) {
    backend = new HeadlessBackend();
    var form = new Form();
    form.Controls.Add(crumb);
    Application.Run(form, backend);
    return (HeadlessCanvasPeer)crumb.Peer!;
  }

  private static Breadcrumb ThreeSegments(int width = 400) {
    var crumb = new Breadcrumb { Bounds = new(0, 0, width, 24) };
    crumb.Items.AddRange("Home", "Docs", "Sub");
    return crumb;
  }

  [Test]
  public void Clicking_a_segment_raises_ItemClicked_with_its_index() {
    var crumb = ThreeSegments();
    var canvas = Realize(crumb, out _);
    canvas.RaisePaint();
    BreadcrumbItemEventArgs? clicked = null;
    crumb.ItemClicked += (_, e) => clicked = e;

    // "Home" spans x in [0, 2*8+4*7=44); the chevron then "Docs" begins at 44+16=60.
    canvas.RaiseMouseDown(70, 12); // inside "Docs"

    Assert.Multiple(() => {
      Assert.That(clicked, Is.Not.Null);
      Assert.That(clicked!.Index, Is.EqualTo(1));
      Assert.That(clicked.Item.Text, Is.EqualTo("Docs"));
    });
  }

  [Test]
  public void Clicking_trims_the_path_to_the_clicked_segment() {
    var crumb = ThreeSegments();
    var canvas = Realize(crumb, out _);
    canvas.RaisePaint();

    canvas.RaiseMouseDown(10, 12); // "Home", the first segment

    Assert.Multiple(() => {
      Assert.That(crumb.Items, Has.Count.EqualTo(1), "the path is trimmed to Home");
      Assert.That(crumb.Items[0].Text, Is.EqualTo("Home"));
    });
  }

  [Test]
  public void TrimOnClick_off_keeps_the_whole_path() {
    var crumb = ThreeSegments();
    crumb.TrimOnClick = false;
    var canvas = Realize(crumb, out _);
    canvas.RaisePaint();

    canvas.RaiseMouseDown(10, 12); // "Home"

    Assert.That(crumb.Items, Has.Count.EqualTo(3));
  }

  [Test]
  public void Paints_captions_and_a_chevron_between_segments() {
    var crumb = ThreeSegments();
    var canvas = Realize(crumb, out _);

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.DrewText("Home"), Is.True);
      Assert.That(g.DrewText("Docs"), Is.True);
      Assert.That(g.DrewText("Sub"), Is.True);
      Assert.That(g.Operations.Exists(o => o.StartsWith("line")), Is.True, "chevron separators are painted as glyphs");
    });
  }

  [Test]
  public void Overflowing_segments_fold_behind_an_ellipsis_and_keep_the_last() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 120, 24) };
    crumb.Items.AddRange("Root", "Level1", "Level2", "Level3", "Leaf");
    var canvas = Realize(crumb, out _);

    var g = canvas.RaisePaint();

    Assert.Multiple(() => {
      Assert.That(g.DrewText("…"), Is.True, "leading segments fold behind the overflow chip");
      Assert.That(g.DrewText("Leaf"), Is.True, "the last segment always stays visible");
      Assert.That(g.DrewText("Root"), Is.False, "a folded leading segment is not painted");
    });
  }

  [Test]
  public void An_empty_breadcrumb_paints_without_error() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 200, 24) };
    var canvas = Realize(crumb, out _);

    Assert.DoesNotThrow(() => canvas.RaisePaint());
  }

  [Test]
  public void TrimAfter_removes_the_trailing_segments() {
    var crumb = ThreeSegments();
    Realize(crumb, out _);

    crumb.Items.TrimAfter(0);

    Assert.That(crumb.Items, Has.Count.EqualTo(1));
  }

  // --- Folder walk (SubItemsProvider) --------------------------------------------------------

  [Test]
  public void PathSeparator_defaults_to_slash_and_is_settable() {
    var crumb = new Breadcrumb();
    Assert.That(crumb.PathSeparator, Is.EqualTo("/"));

    crumb.PathSeparator = "\\";
    Assert.That(crumb.PathSeparator, Is.EqualTo("\\"));
  }

  [Test]
  public void Clicking_a_chevron_with_a_provider_drops_down_the_child_menu() {
    var crumb = ThreeSegments();
    crumb.SubItemsProvider = _ => [new BreadcrumbItem("Pictures"), new BreadcrumbItem("Music")];
    var canvas = Realize(crumb, out var backend);
    canvas.RaisePaint();

    canvas.RaiseMouseDown(50, 12); // the chevron just after "Home" (spans 44..60)

    Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Any(), Is.True, "the folder-walk drop-down opened");
  }

  [Test]
  public void Without_a_provider_a_chevron_click_is_inert() {
    var crumb = ThreeSegments();
    var canvas = Realize(crumb, out var backend);
    canvas.RaisePaint();
    BreadcrumbItemEventArgs? clicked = null;
    crumb.ItemClicked += (_, e) => clicked = e;

    canvas.RaiseMouseDown(50, 12); // the chevron gap

    Assert.Multiple(() => {
      Assert.That(backend.Created.OfType<HeadlessPopupPeer>().Any(), Is.False, "no drop-down without a provider");
      Assert.That(clicked, Is.Null, "the chevron is not a navigable segment");
    });
  }

  // --- Edit mode + autocompletion -----------------------------------------------------------

  [Test]
  public void Clicking_empty_space_enters_edit_mode_when_editable() {
    var crumb = ThreeSegments();
    crumb.Editable = true;
    var canvas = Realize(crumb, out _);
    canvas.RaisePaint();

    canvas.RaiseMouseDown(380, 12); // past the last segment — empty space

    Assert.That(crumb.IsEditing, Is.True, "the empty area opens the path field");
  }

  [Test]
  public void Committing_the_edit_field_reparses_the_path_into_segments() {
    var crumb = ThreeSegments(); // Home / Docs / Sub
    Realize(crumb, out _);
    string? entered = null;
    crumb.PathEntered += (_, e) => entered = e.Path;

    crumb.BeginEdit();
    Assert.That(crumb.EditorText, Is.EqualTo("Home/Docs/Sub"), "the field starts from the full path");

    crumb.TypeIntoEditorForTest("Users/Alex/Pictures");
    crumb.EndEdit(commit: true);

    Assert.Multiple(() => {
      Assert.That(crumb.IsEditing, Is.False);
      Assert.That(entered, Is.EqualTo("Users/Alex/Pictures"));
      Assert.That(crumb.Items.Select(i => i.Text), Is.EqualTo(new[] { "Users", "Alex", "Pictures" }), "split on the separator");
    });
  }

  [Test]
  public void Escaping_the_edit_field_restores_the_crumbs_unchanged() {
    var crumb = ThreeSegments();
    Realize(crumb, out _);

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("Something/Else");
    crumb.EndEdit(commit: false);

    Assert.Multiple(() => {
      Assert.That(crumb.IsEditing, Is.False);
      Assert.That(crumb.Items.Select(i => i.Text), Is.EqualTo(new[] { "Home", "Docs", "Sub" }), "cancel keeps the path");
    });
  }

  [Test]
  public void The_edit_field_lists_every_match_in_a_suggestion_drop_down() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 400, 24) };
    crumb.AutoCompleteSource = _ => ["Documents", "Downloads", "Music"];
    Realize(crumb, out _);

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("Do"); // matches Documents and Downloads

    Assert.Multiple(() => {
      Assert.That(crumb.SuggestionsShownForTest, Is.True, "the suggestion drop-down opened");
      Assert.That(crumb.SuggestionsForTest, Is.EqualTo(new[] { "Documents", "Downloads" }), "every prefix match is listed");
    });
  }

  [Test]
  public void Tab_completes_with_the_highlighted_suggestion_but_stays_editing() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 400, 24) };
    crumb.AutoCompleteSource = _ => ["Documents", "Downloads", "Music"];
    Realize(crumb, out var backend);

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("Do"); // opens the drop-down with Documents, Downloads
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    popup.RaiseKeyDown(Keys.Down); // highlight "Documents"
    popup.RaiseKeyDown(Keys.Tab);  // complete with it

    Assert.Multiple(() => {
      Assert.That(crumb.EditorText, Is.EqualTo("Documents"), "the field is completed with the highlighted suggestion");
      Assert.That(crumb.IsEditing, Is.True, "Tab stays in the edit field rather than committing");
    });
  }

  [Test]
  public void A_second_Tab_with_nothing_left_to_complete_leaves_the_edit_field() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 400, 24) };
    crumb.AutoCompleteSource = _ => ["Documents", "Downloads", "Music"];
    Realize(crumb, out var backend);

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("Do");
    var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
    popup.RaiseKeyDown(Keys.Down); // highlight "Documents"
    popup.RaiseKeyDown(Keys.Tab);  // completes → "Documents", stays editing
    Assert.That(crumb.IsEditing, Is.True);

    popup.RaiseKeyDown(Keys.Tab);  // nothing left to complete → leaves the edit field

    Assert.That(crumb.IsEditing, Is.False, "the field no longer traps Tab once the completion is in");
  }

  [Test]
  public void Picking_a_suggestion_commits_it_as_the_path() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 400, 24) };
    crumb.AutoCompleteSource = _ => ["Documents", "Downloads", "Music"];
    string? entered = null;
    crumb.PathEntered += (_, e) => entered = e.Path;
    Realize(crumb, out _);

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("Do");
    crumb.PickSuggestionForTest(1); // "Downloads"

    Assert.Multiple(() => {
      Assert.That(entered, Is.EqualTo("Downloads"), "the chosen suggestion is committed as the path");
      Assert.That(crumb.IsEditing, Is.False, "committing the suggestion ends the edit");
      Assert.That(crumb.SuggestionsShownForTest, Is.False, "the drop-down closes once a suggestion is picked");
    });
  }

  [Test]
  public void A_custom_parser_labels_the_committed_segments() {
    var crumb = new Breadcrumb { Bounds = new(0, 0, 400, 24) };
    crumb.PathParser = text => text.Split(':', System.StringSplitOptions.RemoveEmptyEntries)
        .Select(p => new BreadcrumbItem(p.ToUpperInvariant())).ToArray();
    Realize(crumb, out _);

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("archive:folder:file");
    crumb.EndEdit(commit: true);

    Assert.That(crumb.Items.Select(i => i.Text), Is.EqualTo(new[] { "ARCHIVE", "FOLDER", "FILE" }), "the custom parser drives virtual paths");
  }

  [Test]
  public void Navigating_into_a_child_trims_to_the_parent_and_appends_it() {
    var crumb = ThreeSegments(); // Home / Docs / Sub
    Realize(crumb, out _);
    BreadcrumbItemEventArgs? sub = null;
    crumb.SubItemSelected += (_, e) => sub = e;

    crumb.NavigateInto(0, new BreadcrumbItem("Pictures")); // a child of segment 0 ("Home")

    Assert.Multiple(() => {
      Assert.That(crumb.Items, Has.Count.EqualTo(2), "the path is trimmed to Home, then the child appended");
      Assert.That(crumb.Items[0].Text, Is.EqualTo("Home"));
      Assert.That(crumb.Items[1].Text, Is.EqualTo("Pictures"));
      Assert.That(sub?.Item.Text, Is.EqualTo("Pictures"), "SubItemSelected reports the chosen child");
    });
  }

  [Test]
  public void A_root_segment_does_not_double_the_separator() {
    // The first crumb of the most ordinary POSIX path there is: "/" then "home" then "user".
    // A plain join produces "//home/user", which is what the path field would then be seeded
    // with the moment the bar is clicked.
    var crumb = new Breadcrumb();
    crumb.Items.AddRange("/", "home", "user");

    Assert.That(crumb.FullPath, Is.EqualTo("/home/user"));
  }

  [Test]
  public void A_windows_root_does_not_double_its_separator_either() {
    var crumb = new Breadcrumb { PathSeparator = "\\" };
    crumb.Items.AddRange("C:\\", "Users", "hawky");

    Assert.That(crumb.FullPath, Is.EqualTo("C:\\Users\\hawky"));
  }

  [Test]
  public void An_ordinary_path_still_joins_with_one_separator() {
    var crumb = new Breadcrumb();
    crumb.Items.AddRange("Home", "Docs", "Sub");

    Assert.That(crumb.FullPath, Is.EqualTo("Home/Docs/Sub"));
  }

  [Test]
  public void A_caller_that_composes_its_own_path_seeds_the_field_with_it() {
    // A namespace no single separator can join: a filesystem path down to an archive file, then
    // the archive's own entry names, which on Windows means a backslash above and a slash within.
    // Joining captions produced a path that did not exist, so the caller says what it is showing.
    var crumb = new Breadcrumb { PathSeparator = "\\" };
    crumb.Items.AddRange("C:\\", "tmp", "sample.zip", "sub");
    crumb.PathComposer = () => "C:\\tmp\\sample.zip/sub";
    Realize(crumb, out _);

    crumb.BeginEdit();

    Assert.Multiple(() => {
      Assert.That(crumb.EditorText, Is.EqualTo("C:\\tmp\\sample.zip/sub"));
      Assert.That(crumb.FullPath, Is.EqualTo("C:\\tmp\\sample.zip\\sub"),
          "joining the captions is still what FullPath means; only the seed is the caller's");
    });
  }

  [Test]
  public void Without_a_composer_the_field_still_starts_from_the_joined_path() {
    var crumb = ThreeSegments(); // Home / Docs / Sub
    Realize(crumb, out _);

    crumb.BeginEdit();

    Assert.That(crumb.EditorText, Is.EqualTo("Home/Docs/Sub"));
  }

  /// <summary>
  /// The path bar draws with the font it inherits, like the toolbar it sits in and the list below it.
  /// </summary>
  /// <remarks>
  /// It took the theme's default instead, so the one strip of text a file browser puts across the
  /// top of its window was the one strip that came out a different size from everything around it.
  /// </remarks>
  [Test]
  public void The_path_draws_with_the_font_it_inherits() {
    var crumb = ThreeSegments();
    var wanted = new Hawkynt.NativeForms.Drawing.Font("Sans", 21f);
    crumb.Font = wanted;

    var g = Realize(crumb, out _).RaisePaint();

    Assert.That(g.TextDraws, Is.Not.Empty, "the path drew something");
    Assert.That(
        g.TextDraws.Select(d => d.Font.SizeInPoints),
        Has.All.EqualTo(wanted.SizeInPoints),
        "every segment uses the inherited font");
  }

  [Test]
  public void A_path_with_no_font_of_its_own_follows_the_form() {
    var crumb = ThreeSegments();
    var backend = new HeadlessBackend();
    var form = new Form { Font = new Hawkynt.NativeForms.Drawing.Font("Sans", 17f) };
    form.Controls.Add(crumb);
    Application.Run(form, backend);

    var g = ((HeadlessCanvasPeer)crumb.Peer!).RaisePaint();

    Assert.That(g.TextDraws.Select(d => d.Font.SizeInPoints), Has.All.EqualTo(17f));
  }
  // --- Leaving the path field ------------------------------------------------------------------

  /// <summary>
  /// Clicking away from the path field closes it, leaving the crumbs alone.
  /// </summary>
  /// <remarks>
  /// Only Enter and Escape closed it, so clicking into the file list left the field open behind the
  /// click with its text still highlighted — two selections on screen at once, in two controls,
  /// with no way to tell which one the next keystroke would reach.
  /// </remarks>
  [Test]
  public void Focus_leaving_the_field_closes_it() {
    var crumb = ThreeSegments();
    crumb.Editable = true;
    var backend = new HeadlessBackend();
    var form = new Form();
    var elsewhere = new TextBox();
    form.Controls.Add(crumb);
    form.Controls.Add(elsewhere);
    Application.Run(form, backend);

    crumb.BeginEdit();
    Assert.That(crumb.IsEditing, Is.True);

    elsewhere.Focus();

    Assert.That(crumb.IsEditing, Is.False);
  }

  /// <summary>Half a typed path is not a request to go anywhere, so leaving abandons it.</summary>
  [Test]
  public void Focus_leaving_the_field_abandons_what_was_typed() {
    var crumb = ThreeSegments(); // Home / Docs / Sub
    crumb.Editable = true;
    var backend = new HeadlessBackend();
    var form = new Form();
    var elsewhere = new TextBox();
    form.Controls.Add(crumb);
    form.Controls.Add(elsewhere);
    Application.Run(form, backend);

    string? entered = null;
    crumb.PathEntered += (_, e) => entered = e.Path;

    crumb.BeginEdit();
    crumb.TypeIntoEditorForTest("Users/Alex/Half");
    elsewhere.Focus();

    Assert.Multiple(() => {
      Assert.That(entered, Is.Null, "nothing was entered");
      Assert.That(crumb.Items.Select(i => i.Text), Is.EqualTo(new[] { "Home", "Docs", "Sub" }));
    });
  }
}
