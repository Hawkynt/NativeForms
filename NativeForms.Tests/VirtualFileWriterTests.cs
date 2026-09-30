using System.Text;
using Hawkynt.NativeForms;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Writing <see cref="VirtualFile"/> entries onto disk (PRD §8), shared by the GTK direct-save drag,
/// the macOS file promises and the temporary-folder fallback. Into a destination, each top-level
/// entry is staged under a hidden temporary name in the destination itself and renamed only once
/// complete; nothing existing is ever replaced.
/// </summary>
[TestFixture]
internal sealed class VirtualFileWriterTests {
  private string _destination = string.Empty;

  [SetUp]
  public void CreateDestination() {
    _destination = Path.Combine(Path.GetTempPath(), "nf-vfw-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_destination);
  }

  [TearDown]
  public void DeleteDestination() {
    if (Directory.Exists(_destination))
      Directory.Delete(_destination, recursive: true);
  }

  private static VirtualFile Text(string path, string content, DateTime? time = null)
      => new(path, () => new MemoryStream(Encoding.UTF8.GetBytes(content)), Encoding.UTF8.GetByteCount(content), time);

  private string At(string relative) => Path.Combine(_destination, relative.Replace('/', Path.DirectorySeparatorChar));

  private string[] Listing() => Directory.GetFileSystemEntries(_destination, "*", SearchOption.AllDirectories)
      .Select(p => Path.GetRelativePath(_destination, p).Replace(Path.DirectorySeparatorChar, '/'))
      .Order(StringComparer.Ordinal)
      .ToArray();

  // --- Trees ---------------------------------------------------------------------------------------

  [Test]
  public void Given_a_tree_when_written_then_every_file_and_folder_arrives_byte_identical() {
    VirtualFile[] files = [
      Text("readme.txt", "hello"),
      VirtualFile.Directory("docs"),
      Text("docs/guide.md", "# guide"),
      VirtualFile.Directory("docs/empty"),
      Text("data/nested/deep.bin", "deep"), // "data" and "data/nested" are only implied
    ];

    var written = VirtualFileWriter.WriteTree(_destination, files);

    Assert.Multiple(() => {
      Assert.That(written, Is.EqualTo(new[] { At("readme.txt"), At("docs"), At("data") }));
      Assert.That(Listing(), Is.EqualTo(new[] {
        "data", "data/nested", "data/nested/deep.bin", "docs", "docs/empty", "docs/guide.md", "readme.txt",
      }));
      Assert.That(File.ReadAllText(At("readme.txt")), Is.EqualTo("hello"));
      Assert.That(File.ReadAllText(At("docs/guide.md")), Is.EqualTo("# guide"));
      Assert.That(File.ReadAllText(At("data/nested/deep.bin")), Is.EqualTo("deep"));
    });
  }

  [Test]
  public void Given_a_zero_length_file_when_written_then_an_empty_file_arrives() {
    VirtualFileWriter.WriteTree(_destination, [new VirtualFile("empty.bin", () => new MemoryStream(), 0)]);

    Assert.That(new FileInfo(At("empty.bin")).Length, Is.Zero);
  }

  [Test]
  public void Given_a_file_of_unknown_length_when_written_then_all_of_its_content_arrives() {
    var content = new byte[300_000];
    new Random(7).NextBytes(content);

    VirtualFileWriter.WriteTree(_destination, [new VirtualFile("blob.bin", () => new MemoryStream(content))]);

    Assert.That(File.ReadAllBytes(At("blob.bin")), Is.EqualTo(content));
  }

  [Test]
  public void Given_a_write_time_when_written_then_the_file_carries_it() {
    var time = new DateTime(2020, 2, 29, 13, 14, 16, DateTimeKind.Utc);

    VirtualFileWriter.WriteTree(_destination, [Text("dated.txt", "x", time), Text("folder/inner.txt", "y", time)]);

    Assert.Multiple(() => {
      Assert.That(File.GetLastWriteTimeUtc(At("dated.txt")), Is.EqualTo(time));
      Assert.That(File.GetLastWriteTimeUtc(At("folder/inner.txt")), Is.EqualTo(time));
    });
  }

  [Test]
  public void Given_a_Unicode_name_when_written_then_the_file_has_exactly_that_name() {
    VirtualFileWriter.WriteTree(_destination, [Text("Übersicht – 日本語.txt", "u")]);

    Assert.That(Listing(), Is.EqualTo(new[] { "Übersicht – 日本語.txt" }));
  }

  // --- Staging under a temporary name --------------------------------------------------------------

  [Test]
  public void Given_a_file_being_written_when_the_destination_is_listed_then_only_a_hidden_partial_name_exists() {
    string[] during = [];
    var file = new VirtualFile("slow.bin", () => new ObservingStream(() => during = Listing()));

    VirtualFileWriter.WriteTree(_destination, [file]);

    Assert.Multiple(() => {
      Assert.That(during, Has.Length.EqualTo(1));
      Assert.That(during[0], Does.StartWith(".slow.bin.").And.EndWith(".partial"));
      Assert.That(Listing(), Is.EqualTo(new[] { "slow.bin" }));
    });
  }

  [Test]
  public void Given_a_folder_being_written_when_the_destination_is_listed_then_it_is_staged_whole_under_a_partial_name() {
    string[] during = [];
    VirtualFile[] files = [
      Text("tree/first.txt", "1"),
      new("tree/second.bin", () => new ObservingStream(() => during = Listing())),
    ];

    VirtualFileWriter.WriteTree(_destination, files);

    Assert.Multiple(() => {
      Assert.That(during.Where(p => !p.Contains('/')).ToArray(), Has.Length.EqualTo(1));
      Assert.That(during[0], Does.StartWith(".tree.").And.EndWith(".partial"));
      Assert.That(during, Has.None.StartsWith("tree/"));
      Assert.That(Listing(), Is.EqualTo(new[] { "tree", "tree/first.txt", "tree/second.bin" }));
    });
  }

  [Test]
  public void Given_a_stream_that_fails_midway_when_written_then_neither_the_real_name_nor_a_partial_file_remains() {
    var file = new VirtualFile("broken.bin", () => new FailingStream());

    Assert.Multiple(() => {
      Assert.That(() => VirtualFileWriter.WriteTree(_destination, [file]), Throws.InstanceOf<IOException>());
      Assert.That(Listing(), Is.Empty);
    });
  }

  [Test]
  public void Given_a_folder_whose_last_file_fails_when_written_then_the_folder_does_not_appear_half_written() {
    VirtualFile[] files = [Text("tree/ok.txt", "fine"), new("tree/broken.bin", () => new FailingStream())];

    Assert.Multiple(() => {
      Assert.That(() => VirtualFileWriter.WriteTree(_destination, files), Throws.InstanceOf<IOException>());
      Assert.That(Listing(), Is.Empty);
    });
  }

  [Test]
  public void Given_a_long_name_when_staged_then_the_temporary_name_still_fits_a_255_character_name() {
    var name = new string('n', 250) + ".txt";

    Assert.That(VirtualFileWriter.TemporaryName(name).Length, Is.LessThanOrEqualTo(255));
  }

  // --- Existing names are never replaced -----------------------------------------------------------

  [Test]
  public void Given_an_existing_file_when_the_same_name_is_written_then_it_is_kept_and_the_new_file_is_numbered() {
    File.WriteAllText(At("a.txt"), "old");

    var written = VirtualFileWriter.WriteTree(_destination, [Text("a.txt", "new")]);

    Assert.Multiple(() => {
      Assert.That(written, Is.EqualTo(new[] { At("a (2).txt") }));
      Assert.That(File.ReadAllText(At("a.txt")), Is.EqualTo("old"));
      Assert.That(File.ReadAllText(At("a (2).txt")), Is.EqualTo("new"));
    });
  }

  [Test]
  public void Given_the_first_numbered_name_is_taken_too_when_written_then_the_next_number_is_used() {
    File.WriteAllText(At("a.txt"), "old");
    File.WriteAllText(At("a (2).txt"), "older");

    var written = VirtualFileWriter.WriteTree(_destination, [Text("a.txt", "new")]);

    Assert.That(written, Is.EqualTo(new[] { At("a (3).txt") }));
  }

  [Test]
  public void Given_an_existing_folder_when_a_folder_of_that_name_is_written_then_it_is_left_alone_and_the_new_one_is_numbered() {
    Directory.CreateDirectory(At("docs"));
    File.WriteAllText(At("docs/keep.txt"), "keep");

    var written = VirtualFileWriter.WriteTree(_destination, [Text("docs/keep.txt", "new")]);

    Assert.Multiple(() => {
      Assert.That(written, Is.EqualTo(new[] { At("docs (2)") }));
      Assert.That(File.ReadAllText(At("docs/keep.txt")), Is.EqualTo("keep"));
      Assert.That(File.ReadAllText(At("docs (2)/keep.txt")), Is.EqualTo("new"));
    });
  }

  [Test]
  public void Given_an_existing_folder_when_a_file_of_that_name_is_written_then_the_file_is_numbered() {
    Directory.CreateDirectory(At("clash"));

    var written = VirtualFileWriter.WriteTree(_destination, [Text("clash", "file")]);

    Assert.That(written, Is.EqualTo(new[] { At("clash (2)") }));
  }

  [TestCase("a.txt", false, "a (2).txt", TestName = "Given_a_file_with_an_extension_when_numbered_then_the_number_precedes_the_extension")]
  [TestCase("archive.tar.gz", false, "archive.tar (2).gz", TestName = "Given_a_double_extension_when_numbered_then_only_the_last_one_follows_the_number")]
  [TestCase("README", false, "README (2)", TestName = "Given_a_file_without_an_extension_when_numbered_then_the_number_ends_the_name")]
  [TestCase(".bashrc", false, ".bashrc (2)", TestName = "Given_a_dot_file_when_numbered_then_the_name_is_not_taken_for_an_extension")]
  [TestCase("folder.v2", true, "folder.v2 (2)", TestName = "Given_a_folder_with_a_dot_when_numbered_then_the_number_ends_the_name")]
  public void Numbered_names(string name, bool folder, string expected)
      => Assert.That(Path.GetFileName(VirtualFileWriter.Numbered(Path.Combine(_destination, name), 2, folder)), Is.EqualTo(expected));

  // --- One entry to a name the target chose --------------------------------------------------------

  [Test]
  public void Given_a_name_chosen_by_the_target_when_one_file_is_written_then_it_gets_that_name() {
    var written = VirtualFileWriter.WriteEntry(At("renamed.txt"), [Text("original.txt", "content")], "original.txt");

    Assert.Multiple(() => {
      Assert.That(written, Is.EqualTo(At("renamed.txt")));
      Assert.That(File.ReadAllText(written), Is.EqualTo("content"));
    });
  }

  [Test]
  public void Given_a_folder_entry_when_written_to_a_chosen_name_then_its_subtree_comes_along() {
    VirtualFile[] files = [VirtualFile.Directory("src"), Text("src/a.txt", "a"), Text("other.txt", "not mine")];

    VirtualFileWriter.WriteEntry(At("copy"), files, "src");

    Assert.That(Listing(), Is.EqualTo(new[] { "copy", "copy/a.txt" }));
  }

  [Test]
  public void Given_an_entry_that_is_both_file_and_folder_when_written_then_it_is_refused_and_nothing_remains() {
    VirtualFile[] files = [Text("x", "file"), Text("x/y", "below")];

    Assert.Multiple(() => {
      Assert.That(() => VirtualFileWriter.WriteEntry(At("x"), files, "x"), Throws.InvalidOperationException);
      Assert.That(Listing(), Is.Empty);
    });
  }

  // --- The temporary-folder fallback ---------------------------------------------------------------

  [Test]
  public void Given_virtual_files_when_materialized_then_their_top_level_paths_exist_in_a_fresh_private_folder() {
    VirtualFile[] files = [Text("one.txt", "1"), Text("two/three.txt", "3")];

    var first = VirtualFileWriter.Materialize(files);
    var second = VirtualFileWriter.Materialize(files);
    try {
      Assert.Multiple(() => {
        Assert.That(first.Select(Path.GetFileName), Is.EqualTo(new[] { "one.txt", "two" }));
        Assert.That(File.ReadAllText(first[0]), Is.EqualTo("1"));
        Assert.That(File.ReadAllText(Path.Combine(first[1], "three.txt")), Is.EqualTo("3"));
        Assert.That(Path.GetDirectoryName(first[0]), Is.Not.EqualTo(Path.GetDirectoryName(second[0])));
      });
    } finally {
      Directory.Delete(Path.GetDirectoryName(first[0])!, true);
      Directory.Delete(Path.GetDirectoryName(second[0])!, true);
    }
  }

  [Test]
  public void Given_a_path_listed_twice_when_materialized_then_both_contents_survive() {
    var paths = VirtualFileWriter.Materialize([Text("same.txt", "first"), Text("same.txt", "second")]);
    try {
      var folder = Path.GetDirectoryName(paths[0])!;

      Assert.Multiple(() => {
        Assert.That(paths, Has.Length.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(folder, "same.txt")), Is.EqualTo("first"));
        Assert.That(File.ReadAllText(Path.Combine(folder, "same (2).txt")), Is.EqualTo("second"));
      });
    } finally {
      Directory.Delete(Path.GetDirectoryName(paths[0])!, true);
    }
  }

  [Test]
  public void Given_a_stream_that_fails_when_materialized_then_the_folder_is_removed() {
    var before = Directory.GetDirectories(Path.GetTempPath(), "NativeForms-drag-*").Length;

    Assert.Multiple(() => {
      Assert.That(() => VirtualFileWriter.Materialize([new VirtualFile("x.bin", () => new FailingStream())]), Throws.InstanceOf<IOException>());
      Assert.That(Directory.GetDirectories(Path.GetTempPath(), "NativeForms-drag-*"), Has.Length.EqualTo(before));
    });
  }

  /// <summary>Two reads of content; the callback runs between them, while the file is half written.</summary>
  private sealed class ObservingStream(Action halfway) : Stream {
    private int _reads;

    public override int Read(byte[] buffer, int offset, int count) {
      switch (_reads++) {
        case 0:
          buffer[offset] = 1;
          return 1;
        case 1:
          halfway();
          buffer[offset] = 2;
          return 1;
        default:
          return 0;
      }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }

  /// <summary>Yields a little content, then fails the way a broken archive or network would.</summary>
  private sealed class FailingStream : Stream {
    private bool _served;

    public override int Read(byte[] buffer, int offset, int count) {
      if (_served)
        throw new IOException("The source broke off.");

      _served = true;
      buffer[offset] = 42;
      return 1;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
