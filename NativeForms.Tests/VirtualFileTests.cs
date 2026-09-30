using Hawkynt.NativeForms;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="VirtualFile"/> (PRD §8): a file whose content is produced on demand. The relative path
/// is the one thing every platform builds a destination name from, so it is validated up front —
/// equivalence classes: plain names, nested folders, backslash separators, rooted paths, parent and
/// current segments, empty segments, Unicode — and the optional metadata keeps its boundaries (zero
/// length, unknown length, lengths beyond 4 GB).
/// </summary>
[TestFixture]
internal sealed class VirtualFileTests {
  private static Stream Empty() => new MemoryStream();

  // --- Paths that are accepted ---------------------------------------------------------------------

  [TestCase("readme.txt", "readme.txt", TestName = "Given_a_plain_name_when_created_then_the_path_is_kept")]
  [TestCase("docs/readme.txt", "docs/readme.txt", TestName = "Given_a_nested_path_when_created_then_the_folders_are_kept")]
  [TestCase(@"docs\sub\readme.txt", "docs/sub/readme.txt", TestName = "Given_backslash_separators_when_created_then_they_become_forward_slashes")]
  [TestCase("a/b/c/d/e/f.bin", "a/b/c/d/e/f.bin", TestName = "Given_a_deep_path_when_created_then_every_level_is_kept")]
  [TestCase("Übersicht – 日本語 🎉.txt", "Übersicht – 日本語 🎉.txt", TestName = "Given_a_Unicode_name_when_created_then_it_is_kept_verbatim")]
  [TestCase("...txt", "...txt", TestName = "Given_a_name_made_of_dots_and_text_when_created_then_it_is_not_a_parent_segment")]
  [TestCase(".hidden", ".hidden", TestName = "Given_a_dot_file_when_created_then_it_is_accepted")]
  [TestCase("with space /x", "with space /x", TestName = "Given_a_folder_name_with_a_trailing_space_when_created_then_it_is_kept")]
  public void Valid_relative_paths_are_accepted(string path, string expected)
      => Assert.That(new VirtualFile(path, Empty).RelativePath, Is.EqualTo(expected));

  // --- Paths that are refused ----------------------------------------------------------------------

  [TestCase("", TestName = "Given_an_empty_path_when_created_then_it_is_refused")]
  [TestCase("/etc/passwd", TestName = "Given_a_rooted_unix_path_when_created_then_it_is_refused")]
  [TestCase(@"\windows\win.ini", TestName = "Given_a_rooted_backslash_path_when_created_then_it_is_refused")]
  [TestCase(@"C:\temp\a.txt", TestName = "Given_a_drive_path_when_created_then_it_is_refused")]
  [TestCase("C:a.txt", TestName = "Given_a_drive_relative_path_when_created_then_it_is_refused")]
  [TestCase(@"\\server\share\a.txt", TestName = "Given_a_UNC_path_when_created_then_it_is_refused")]
  [TestCase("..", TestName = "Given_only_a_parent_segment_when_created_then_it_is_refused")]
  [TestCase("../a.txt", TestName = "Given_a_leading_parent_segment_when_created_then_it_is_refused")]
  [TestCase("docs/../../a.txt", TestName = "Given_an_inner_parent_segment_when_created_then_it_is_refused")]
  [TestCase(@"docs\..\a.txt", TestName = "Given_a_parent_segment_behind_a_backslash_when_created_then_it_is_refused")]
  [TestCase("./a.txt", TestName = "Given_a_current_directory_segment_when_created_then_it_is_refused")]
  [TestCase("docs//a.txt", TestName = "Given_an_empty_inner_segment_when_created_then_it_is_refused")]
  [TestCase("docs/", TestName = "Given_a_trailing_separator_on_a_file_when_created_then_it_is_refused")]
  [TestCase("a\0b.txt", TestName = "Given_a_NUL_character_when_created_then_it_is_refused")]
  public void Invalid_relative_paths_are_refused(string path)
      => Assert.That(() => new VirtualFile(path, Empty), Throws.ArgumentException);

  [Test]
  public void Given_a_null_path_when_created_then_it_is_refused()
      => Assert.That(() => new VirtualFile(null!, Empty), Throws.ArgumentNullException);

  [Test]
  public void Given_no_content_factory_when_a_file_is_created_then_it_is_refused()
      => Assert.That(() => new VirtualFile("a.txt", null!), Throws.ArgumentNullException);

  // --- Metadata ------------------------------------------------------------------------------------

  [TestCase(0L, TestName = "Given_a_zero_length_when_created_then_it_is_kept")]
  [TestCase(1L, TestName = "Given_a_one_byte_length_when_created_then_it_is_kept")]
  [TestCase(0xFFFF_FFFFL, TestName = "Given_the_largest_32_bit_length_when_created_then_it_is_kept")]
  [TestCase(0x1_0000_0000L, TestName = "Given_a_length_just_beyond_4_GB_when_created_then_it_is_kept")]
  [TestCase(long.MaxValue, TestName = "Given_the_largest_length_when_created_then_it_is_kept")]
  public void Known_lengths_are_kept(long length)
      => Assert.That(new VirtualFile("a.bin", Empty, length).Length, Is.EqualTo(length));

  [Test]
  public void Given_no_length_when_created_then_the_length_is_unknown()
      => Assert.That(new VirtualFile("a.bin", Empty).Length, Is.Null);

  [Test]
  public void Given_a_negative_length_when_created_then_it_is_refused()
      => Assert.That(() => new VirtualFile("a.bin", Empty, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());

  [Test]
  public void Given_a_UTC_write_time_when_created_then_it_is_kept() {
    var time = new DateTime(2026, 9, 30, 12, 34, 56, DateTimeKind.Utc);

    Assert.That(new VirtualFile("a.bin", Empty, lastWriteTimeUtc: time).LastWriteTimeUtc, Is.EqualTo(time));
  }

  [Test]
  public void Given_a_local_write_time_when_created_then_it_is_converted_to_UTC() {
    var local = new DateTime(2026, 9, 30, 12, 34, 56, DateTimeKind.Local);

    var kept = new VirtualFile("a.bin", Empty, lastWriteTimeUtc: local).LastWriteTimeUtc!.Value;

    Assert.Multiple(() => {
      Assert.That(kept.Kind, Is.EqualTo(DateTimeKind.Utc));
      Assert.That(kept, Is.EqualTo(local.ToUniversalTime()));
    });
  }

  [Test]
  public void Given_an_unspecified_write_time_when_created_then_it_is_taken_as_UTC() {
    var unspecified = new DateTime(2026, 9, 30, 12, 34, 56, DateTimeKind.Unspecified);

    var kept = new VirtualFile("a.bin", Empty, lastWriteTimeUtc: unspecified).LastWriteTimeUtc!.Value;

    Assert.Multiple(() => {
      Assert.That(kept.Kind, Is.EqualTo(DateTimeKind.Utc));
      Assert.That(kept.Ticks, Is.EqualTo(unspecified.Ticks));
    });
  }

  // --- Content -------------------------------------------------------------------------------------

  [Test]
  public void Given_a_file_when_opened_then_every_call_asks_the_factory_for_a_fresh_stream() {
    var calls = 0;
    var file = new VirtualFile("a.bin", () => new MemoryStream([(byte)++calls]));

    using var first = file.OpenRead();
    using var second = file.OpenRead();

    Assert.Multiple(() => {
      Assert.That(first.ReadByte(), Is.EqualTo(1));
      Assert.That(second.ReadByte(), Is.EqualTo(2));
    });
  }

  [Test]
  public void Given_a_file_when_created_then_the_factory_is_not_called_until_opened() {
    var calls = 0;

    _ = new VirtualFile("a.bin", () => {
      ++calls;
      return new MemoryStream();
    });

    Assert.That(calls, Is.Zero);
  }

  [Test]
  public void Given_a_factory_that_returns_null_when_opened_then_it_throws()
      => Assert.That(() => new VirtualFile("a.bin", () => null!).OpenRead(), Throws.InvalidOperationException);

  [Test]
  public void Given_a_file_when_created_then_it_is_not_a_directory()
      => Assert.That(new VirtualFile("a.bin", Empty).IsDirectory, Is.False);

  // --- Directory entries ---------------------------------------------------------------------------

  [Test]
  public void Given_a_directory_entry_when_created_then_it_has_no_length_and_is_a_directory() {
    var folder = VirtualFile.Directory("docs/sub");

    Assert.Multiple(() => {
      Assert.That(folder.RelativePath, Is.EqualTo("docs/sub"));
      Assert.That(folder.IsDirectory, Is.True);
      Assert.That(folder.Length, Is.Null);
      Assert.That(folder.LastWriteTimeUtc, Is.Null);
    });
  }

  [TestCase("docs/", "docs", TestName = "Given_a_directory_with_a_trailing_slash_when_created_then_the_slash_is_dropped")]
  [TestCase(@"docs\", "docs", TestName = "Given_a_directory_with_a_trailing_backslash_when_created_then_it_is_dropped")]
  public void A_directory_may_end_in_one_separator(string path, string expected)
      => Assert.That(VirtualFile.Directory(path).RelativePath, Is.EqualTo(expected));

  [TestCase("", TestName = "Given_an_empty_directory_path_when_created_then_it_is_refused")]
  [TestCase("/", TestName = "Given_the_root_as_a_directory_when_created_then_it_is_refused")]
  [TestCase("docs//", TestName = "Given_a_directory_with_two_trailing_slashes_when_created_then_it_is_refused")]
  [TestCase("../docs", TestName = "Given_a_directory_outside_the_drop_when_created_then_it_is_refused")]
  public void Invalid_directory_paths_are_refused(string path)
      => Assert.That(() => VirtualFile.Directory(path), Throws.ArgumentException);

  [Test]
  public void Given_a_directory_entry_when_opened_then_it_throws()
      => Assert.That(() => VirtualFile.Directory("docs").OpenRead(), Throws.InvalidOperationException);
}
