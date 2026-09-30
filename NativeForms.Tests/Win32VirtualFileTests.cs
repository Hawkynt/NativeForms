using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends.Windows;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Virtual files through OLE on Win32 (PRD §8), asserted through the COM objects' own vtables — the
/// exact calls Explorer, Outlook or any OLE drop target makes — without a drag and without touching
/// the desktop. The drag-out <c>IDataObject</c> serves <c>CFSTR_FILEDESCRIPTORW</c> and
/// <c>CFSTR_FILECONTENTS</c>; the drop-in <c>IDropTarget</c> is fed that same data object and must
/// deliver the <c>VirtualFile[]</c> it describes. The descriptor layout and the stream are pure
/// memory and run everywhere; whatever needs global memory or OLE reports itself ignored off Windows.
/// </summary>
[TestFixture]
internal sealed unsafe class Win32VirtualFileTests {
  private static readonly DateTime _time = new(2026, 9, 30, 8, 15, 30, DateTimeKind.Utc);

  private static void RequireWindows() {
    if (!OperatingSystem.IsWindows())
      Assert.Ignore("Global memory and OLE exist only on Windows.");
  }

  private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

  private static VirtualFile File(string path, byte[] content, bool withLength = true, DateTime? time = null)
      => new(path, () => new MemoryStream(content), withLength ? content.Length : null, time);

  private static void* Slot(nint instance, int slot) => ((void**)*(void**)instance)[slot];

  // --- The descriptor layout -----------------------------------------------------------------------

  [Test]
  public void Given_entries_when_described_then_parsing_the_descriptor_gives_them_back() {
    VirtualFile[] entries = [
      VirtualFile.Directory("docs"),
      File("docs/readme.txt", Bytes("hello"), time: _time),
      File("unknown.bin", [], withLength: false),
      File("Übersicht – 日本語.txt", Bytes("u")),
      new("huge.iso", () => Stream.Null, 0x1_2345_6789L),
      new("biggest.bin", () => Stream.Null, long.MaxValue),
    ];

    var parsed = Win32DroppedData.ParseDescriptors(Win32VirtualFileDataObject.BuildDescriptor(entries));

    Assert.That(parsed, Is.EqualTo(new (string, bool, long?, DateTime?)[] {
      ("docs", true, null, null),
      ("docs/readme.txt", false, 5L, _time),
      ("unknown.bin", false, null, null),
      ("Übersicht – 日本語.txt", false, 1L, null),
      ("huge.iso", false, 0x1_2345_6789L, null),
      ("biggest.bin", false, long.MaxValue, null),
    }));
  }

  [Test]
  public void Given_a_length_beyond_4_GB_when_described_then_it_is_split_into_high_and_low_parts() {
    var bytes = Win32VirtualFileDataObject.BuildDescriptor([new("huge.iso", () => Stream.Null, 0x1_2345_6789L)]);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(4 + 592));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes), Is.EqualTo(1u), "cItems");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 + 64)), Is.EqualTo(1u), "nFileSizeHigh");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 + 68)), Is.EqualTo(0x2345_6789u), "nFileSizeLow");
    });
  }

  [Test]
  public void Given_a_file_and_a_folder_when_described_then_the_flags_and_attributes_follow_what_is_known() {
    var bytes = Win32VirtualFileDataObject.BuildDescriptor([File("a.txt", Bytes("a"), time: _time), VirtualFile.Directory("d")]);
    var fileFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    var fileAttributes = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 + 36));
    var folderFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 + 592));
    var folderAttributes = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 + 592 + 36));

    Assert.Multiple(() => {
      Assert.That(fileFlags, Is.EqualTo(0x80000000u | 0x4000 | 0x40 | 0x20 | 0x04), "unicode, progress, size, time, attributes");
      Assert.That(fileAttributes, Is.EqualTo(0x80u), "FILE_ATTRIBUTE_NORMAL");
      Assert.That(folderFlags, Is.EqualTo(0x80000000u | 0x4000 | 0x04), "a folder carries no size");
      Assert.That(folderAttributes, Is.EqualTo(0x10u), "FILE_ATTRIBUTE_DIRECTORY");
    });
  }

  [Test]
  public void Given_nested_files_when_described_then_names_use_backslashes() {
    var bytes = Win32VirtualFileDataObject.BuildDescriptor([File("a/b/c.txt", Bytes("c"))]);
    var name = MemoryMarshal.Cast<byte, char>(bytes.AsSpan(4 + 72, 520));

    Assert.That(new string(name[..name.IndexOf('\0')]), Is.EqualTo(@"a\b\c.txt"));
  }

  // --- Descriptor order ----------------------------------------------------------------------------

  [Test]
  public void Given_files_in_folders_that_are_not_listed_when_ordered_then_their_folders_come_first_once() {
    var ordered = Win32VirtualFileDataObject.Order([File("a/b/one.txt", []), File("a/two.txt", []), VirtualFile.Directory("a/b"), File("top.txt", [])]);

    Assert.That(ordered.Select(e => e.ToString()), Is.EqualTo(new[] { "a/", "a/b/", "a/b/one.txt", "a/two.txt", "top.txt" }));
  }

  [Test]
  public void Given_a_path_that_fills_a_descriptor_name_when_ordered_then_it_is_accepted() {
    var path = new string('p', 259);

    Assert.That(Win32VirtualFileDataObject.Order([File(path, [])]), Has.Length.EqualTo(1));
  }

  [Test]
  public void Given_a_path_longer_than_a_descriptor_name_when_ordered_then_it_is_refused()
      => Assert.That(() => Win32VirtualFileDataObject.Order([File(new string('p', 260), [])]), Throws.ArgumentException);

  // --- The IStream over a managed stream -----------------------------------------------------------

  private static int Read(nint stream, byte[] buffer, out uint read) {
    uint got;
    int result;
    fixed (byte* b = buffer)
      result = NativeMethods.StreamRead(stream, b, (uint)buffer.Length, &got);
    read = got;
    return result;
  }

  private static byte[] ReadAll(nint stream) {
    using var all = new MemoryStream();
    var buffer = new byte[4096];
    while (true) {
      var result = Read(stream, buffer, out var read);
      Assert.That(result, Is.GreaterThanOrEqualTo(0), "IStream::Read");
      if (read == 0)
        return all.ToArray();

      all.Write(buffer, 0, (int)read);
    }
  }

  private static int Seek(nint stream, long move, uint origin, out ulong position) {
    ulong at;
    var result = ((delegate* unmanaged<nint, long, uint, ulong*, int>)Slot(stream, 5))(stream, move, origin, &at);
    position = at;
    return result;
  }

  private static int Stat(nint stream, out NativeMethods.STATSTG stat, uint flags) {
    NativeMethods.STATSTG s;
    var result = ((delegate* unmanaged<nint, NativeMethods.STATSTG*, uint, int>)Slot(stream, 12))(stream, &s, flags);
    stat = s;
    return result;
  }

  [Test]
  public void Given_content_larger_than_one_read_when_read_through_the_IStream_then_every_byte_arrives() {
    var content = new byte[100_003];
    new Random(3).NextBytes(content);
    var stream = Win32ComStream.Create(new MemoryStream(content), "blob.bin");
    try {
      Assert.That(ReadAll(stream), Is.EqualTo(content));
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_a_forward_only_stream_when_asked_for_its_position_then_it_answers_but_refuses_to_move() {
    var stream = Win32ComStream.Create(new ForwardOnlyStream(new byte[10]), "f.bin");
    try {
      Read(stream, new byte[4], out _);

      Assert.Multiple(() => {
        Assert.That(Seek(stream, 0, NativeMethods.STREAM_SEEK_CUR, out var at), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(at, Is.EqualTo(4ul));
        Assert.That(Seek(stream, 4, NativeMethods.STREAM_SEEK_SET, out _), Is.EqualTo(NativeMethods.S_OK), "seeking to where it is");
        Assert.That(Seek(stream, 0, NativeMethods.STREAM_SEEK_SET, out _), Is.EqualTo(NativeMethods.STG_E_INVALIDFUNCTION));
      });
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_a_seekable_stream_when_seeked_then_reading_continues_from_there() {
    var stream = Win32ComStream.Create(new MemoryStream([1, 2, 3, 4, 5]), "s.bin");
    try {
      Seek(stream, -2, NativeMethods.STREAM_SEEK_END, out var at);

      Assert.Multiple(() => {
        Assert.That(at, Is.EqualTo(3ul));
        Assert.That(ReadAll(stream), Is.EqualTo(new byte[] { 4, 5 }));
      });
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [TestCase(5L, 5ul, TestName = "Given_a_known_length_when_statted_then_the_size_is_that_length")]
  [TestCase(null, 0ul, TestName = "Given_an_unknown_length_on_a_forward_only_stream_when_statted_then_the_size_is_zero")]
  public void Stat_reports_the_size(long? length, ulong expected) {
    var stream = Win32ComStream.Create(new ForwardOnlyStream(new byte[5]), "s.bin", length, _time);
    try {
      Assert.Multiple(() => {
        Assert.That(Stat(stream, out var stat, NativeMethods.STATFLAG_NONAME), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(stat.type, Is.EqualTo(NativeMethods.STGTY_STREAM));
        Assert.That(stat.cbSize, Is.EqualTo(expected));
        Assert.That(stat.pwcsName, Is.EqualTo((nint)0), "STATFLAG_NONAME");
        Assert.That(DateTime.FromFileTimeUtc(((long)stat.mtime.dwHighDateTime << 32) | stat.mtime.dwLowDateTime), Is.EqualTo(_time));
      });
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_a_seekable_stream_of_unknown_length_when_statted_then_the_size_is_the_streams() {
    var stream = Win32ComStream.Create(new MemoryStream(new byte[7]), "s.bin");
    try {
      Stat(stream, out var stat, NativeMethods.STATFLAG_NONAME);

      Assert.That(stat.cbSize, Is.EqualTo(7ul));
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_a_stat_that_wants_the_name_when_statted_then_the_name_comes_in_task_memory() {
    RequireWindows();
    var stream = Win32ComStream.Create(Stream.Null, "Übersicht.txt");
    try {
      Stat(stream, out var stat, 0);
      try {
        Assert.That(Marshal.PtrToStringUni(stat.pwcsName), Is.EqualTo("Übersicht.txt"));
      } finally {
        NativeMethods.CoTaskMemFree(stat.pwcsName);
      }
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_a_stream_that_fails_when_read_through_the_IStream_then_a_read_fault_is_answered() {
    var stream = Win32ComStream.Create(new ThrowingStream(), "broken.bin");
    try {
      Assert.That(Read(stream, new byte[16], out var read), Is.EqualTo(NativeMethods.STG_E_READFAULT));
      Assert.That(read, Is.Zero);
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_the_IStream_when_written_to_or_cloned_then_it_refuses() {
    var stream = Win32ComStream.Create(new MemoryStream(), "s.bin");
    try {
      uint written;
      nint clone;
      var data = stackalloc byte[1];
      var write = ((delegate* unmanaged<nint, byte*, uint, uint*, int>)Slot(stream, 4))(stream, data, 1, &written);
      var cloned = ((delegate* unmanaged<nint, nint*, int>)Slot(stream, 13))(stream, &clone);
      var cloneAfter = clone;

      Assert.Multiple(() => {
        Assert.That(write, Is.EqualTo(NativeMethods.STG_E_ACCESSDENIED));
        Assert.That(cloned, Is.EqualTo(NativeMethods.E_NOTIMPL));
        Assert.That(cloneAfter, Is.EqualTo((nint)0));
      });
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_the_IStream_when_queried_then_it_answers_IStream_ISequentialStream_and_IUnknown_only() {
    var stream = Win32ComStream.Create(new MemoryStream(), "s.bin");
    try {
      Assert.Multiple(() => {
        Assert.That(QueryAndRelease(stream, NativeMethods.IID_IStream), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(QueryAndRelease(stream, NativeMethods.IID_ISequentialStream), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(QueryAndRelease(stream, NativeMethods.IID_IUnknown), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(QueryAndRelease(stream, NativeMethods.IID_IDropTarget), Is.EqualTo(NativeMethods.E_NOINTERFACE));
        Assert.That(Win32ComObject.References(stream), Is.EqualTo(1), "every successful query was balanced");
      });
    } finally {
      NativeMethods.Release(stream);
    }
  }

  [Test]
  public void Given_the_last_reference_when_released_then_the_managed_stream_is_disposed() {
    var managed = new MemoryStream();
    var stream = Win32ComStream.Create(managed, "s.bin");
    NativeMethods.AddRef(stream);

    NativeMethods.Release(stream);
    var stillOpen = managed.CanRead;
    NativeMethods.Release(stream);

    Assert.Multiple(() => {
      Assert.That(stillOpen, Is.True);
      Assert.That(managed.CanRead, Is.False);
    });
  }

  private static int QueryAndRelease(nint instance, Guid iid) {
    var result = NativeMethods.QueryInterface(instance, iid, out var other);
    if (result == NativeMethods.S_OK)
      NativeMethods.Release(other);

    return result;
  }

  // --- The drag-out IDataObject --------------------------------------------------------------------

  private static NativeMethods.FORMATETC Format(ushort format, int index = -1, uint tymed = NativeMethods.TYMED_HGLOBAL, uint aspect = NativeMethods.DVASPECT_CONTENT)
      => new() { cfFormat = format, dwAspect = aspect, lindex = index, tymed = tymed };

  private static int GetData(nint data, NativeMethods.FORMATETC format, out NativeMethods.STGMEDIUM medium) {
    NativeMethods.STGMEDIUM m;
    var result = NativeMethods.GetData(data, &format, &m);
    medium = m;
    return result;
  }

  private static int QueryGetData(nint data, NativeMethods.FORMATETC format) => NativeMethods.QueryGetData(data, &format);

  private static byte[] GlobalBytes(nint global) {
    var size = (int)NativeMethods.GlobalSize(global);
    var block = NativeMethods.GlobalLock(global);
    try {
      return new ReadOnlySpan<byte>((void*)block, size).ToArray();
    } finally {
      NativeMethods.GlobalUnlock(global);
    }
  }

  private static void Release(ref NativeMethods.STGMEDIUM medium) {
    fixed (NativeMethods.STGMEDIUM* m = &medium)
      NativeMethods.ReleaseStgMedium(m);
  }

  private static ushort Descriptor => Win32Ole.DescriptorFormat;
  private static ushort Contents => Win32Ole.ContentsFormat;

  private static VirtualFile[] SampleTree() => [
    File("readme.txt", Bytes("read me"), time: _time),
    File("docs/guide.md", Bytes("# guide")),
    File("docs/empty.bin", []),
    File("unknown-length.dat", Bytes("no length given"), withLength: false),
  ];

  [Test]
  public void Given_virtual_files_when_the_descriptor_is_fetched_then_it_lists_every_entry_with_implied_folders() {
    RequireWindows();
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    try {
      Assert.That(GetData(data, Format(Descriptor), out var medium), Is.EqualTo(NativeMethods.S_OK));
      try {
        Assert.Multiple(() => {
          Assert.That(medium.tymed, Is.EqualTo(NativeMethods.TYMED_HGLOBAL));
          Assert.That(medium.pUnkForRelease, Is.EqualTo((nint)0));
          Assert.That(Win32DroppedData.ParseDescriptors(GlobalBytes(medium.handle)).Select(d => (d.Path, d.IsDirectory, d.Length)), Is.EqualTo(new (string, bool, long?)[] {
            ("readme.txt", false, 7L),
            ("docs", true, null),
            ("docs/guide.md", false, 7L),
            ("docs/empty.bin", false, 0L),
            ("unknown-length.dat", false, null),
          }));
        });
      } finally {
        Release(ref medium);
      }
    } finally {
      NativeMethods.Release(data);
    }
  }

  [TestCase(0, "read me", TestName = "Given_the_first_index_when_its_contents_are_fetched_then_its_stream_has_the_files_bytes")]
  [TestCase(2, "# guide", TestName = "Given_a_file_inside_a_folder_when_its_contents_are_fetched_then_its_stream_has_the_files_bytes")]
  [TestCase(3, "", TestName = "Given_a_zero_length_file_when_its_contents_are_fetched_then_its_stream_is_empty")]
  [TestCase(4, "no length given", TestName = "Given_a_file_of_unknown_length_when_its_contents_are_fetched_then_the_stream_runs_to_its_end")]
  public void File_contents_come_as_an_IStream_per_index(int index, string expected) {
    RequireWindows();
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    try {
      Assert.That(GetData(data, Format(Contents, index, NativeMethods.TYMED_ISTREAM | NativeMethods.TYMED_HGLOBAL), out var medium), Is.EqualTo(NativeMethods.S_OK));
      try {
        Assert.Multiple(() => {
          Assert.That(medium.tymed, Is.EqualTo(NativeMethods.TYMED_ISTREAM), "a stream is preferred when both are allowed");
          Assert.That(ReadAll(medium.handle), Is.EqualTo(Bytes(expected)));
        });
      } finally {
        Release(ref medium);
      }
    } finally {
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_target_that_only_takes_global_memory_when_contents_are_fetched_then_the_bytes_come_in_an_HGLOBAL() {
    RequireWindows();
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    try {
      Assert.That(GetData(data, Format(Contents, 0, NativeMethods.TYMED_HGLOBAL), out var medium), Is.EqualTo(NativeMethods.S_OK));
      try {
        Assert.Multiple(() => {
          Assert.That(medium.tymed, Is.EqualTo(NativeMethods.TYMED_HGLOBAL));
          Assert.That(GlobalBytes(medium.handle).AsSpan(0, 7).ToArray(), Is.EqualTo(Bytes("read me")));
        });
      } finally {
        Release(ref medium);
      }
    } finally {
      NativeMethods.Release(data);
    }
  }

  private static IEnumerable<TestCaseData> RefusedRequests() {
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(Contents, 1, NativeMethods.TYMED_ISTREAM)), NativeMethods.DV_E_LINDEX)
        .SetName("Given_the_index_of_a_folder_when_its_contents_are_requested_then_the_index_is_refused");
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(Contents, 5, NativeMethods.TYMED_ISTREAM)), NativeMethods.DV_E_LINDEX)
        .SetName("Given_an_index_past_the_last_entry_when_its_contents_are_requested_then_the_index_is_refused");
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(Contents, -2, NativeMethods.TYMED_ISTREAM)), NativeMethods.DV_E_LINDEX)
        .SetName("Given_a_negative_index_when_contents_are_requested_then_the_index_is_refused");
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(Contents, 0, NativeMethods.TYMED_ISTORAGE)), NativeMethods.DV_E_TYMED)
        .SetName("Given_only_structured_storage_when_contents_are_requested_then_the_medium_is_refused");
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(Descriptor, tymed: NativeMethods.TYMED_ISTREAM)), NativeMethods.DV_E_TYMED)
        .SetName("Given_a_descriptor_request_without_global_memory_when_asked_then_the_medium_is_refused");
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(Descriptor, aspect: 4)), NativeMethods.DV_E_DVASPECT)
        .SetName("Given_an_aspect_other_than_content_when_asked_then_the_aspect_is_refused");
    yield return new TestCaseData(new Func<NativeMethods.FORMATETC>(() => Format(NativeMethods.CF_HDROP)), NativeMethods.DV_E_FORMATETC)
        .SetName("Given_a_format_that_is_not_offered_when_asked_then_the_format_is_refused");
  }

  [TestCaseSource(nameof(RefusedRequests))]
  public void Requests_the_data_object_cannot_serve_are_refused_by_query_and_fetch(Func<NativeMethods.FORMATETC> request, int expected) {
    RequireWindows();
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    try {
      Assert.Multiple(() => {
        Assert.That(QueryGetData(data, request()), Is.EqualTo(expected), "QueryGetData");
        Assert.That(GetData(data, request(), out var medium), Is.EqualTo(expected), "GetData");
        Assert.That(medium.handle, Is.EqualTo((nint)0));
      });
    } finally {
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_content_that_cannot_be_produced_when_fetched_then_a_read_fault_is_answered() {
    RequireWindows();
    var data = Win32VirtualFileDataObject.Create([new VirtualFile("broken.bin", () => throw new IOException("damaged"))]);
    try {
      Assert.That(GetData(data, Format(Contents, 0, NativeMethods.TYMED_ISTREAM), out _), Is.EqualTo(NativeMethods.STG_E_READFAULT));
    } finally {
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_format_the_shell_stores_when_fetched_back_then_a_copy_of_it_is_served_and_enumerated() {
    RequireWindows();
    var preferred = (ushort)NativeMethods.RegisterClipboardFormatW("Preferred DropEffect");
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    try {
      var global = NativeMethods.GlobalAlloc(NativeMethods.GHND, 4);
      BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>((void*)NativeMethods.GlobalLock(global), 4), 2);
      NativeMethods.GlobalUnlock(global);
      var format = Format(preferred);
      var medium = new NativeMethods.STGMEDIUM { tymed = NativeMethods.TYMED_HGLOBAL, handle = global };
      var stored = ((delegate* unmanaged<nint, NativeMethods.FORMATETC*, NativeMethods.STGMEDIUM*, int, int>)Slot(data, 7))(data, &format, &medium, 1);

      Assert.That(stored, Is.EqualTo(NativeMethods.S_OK));
      Assert.That(GetData(data, Format(preferred), out var back), Is.EqualTo(NativeMethods.S_OK));
      try {
        Assert.Multiple(() => {
          Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(GlobalBytes(back.handle)), Is.EqualTo(2u));
          Assert.That(Enumerate(data), Is.EqualTo(new[] { Descriptor, Contents, preferred }));
        });
      } finally {
        Release(ref back);
      }
    } finally {
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_the_data_object_when_asked_for_advise_connections_then_they_are_not_supported() {
    RequireWindows();
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    try {
      uint connection;
      var format = Format(Descriptor);
      Assert.That(((delegate* unmanaged<nint, NativeMethods.FORMATETC*, uint, nint, uint*, int>)Slot(data, 9))(data, &format, 0, 0, &connection),
          Is.EqualTo(NativeMethods.OLE_E_ADVISENOTSUPPORTED));
    } finally {
      NativeMethods.Release(data);
    }
  }

  /// <summary>The clipboard formats <c>IDataObject::EnumFormatEtc(DATADIR_GET)</c> lists, through <c>IEnumFORMATETC::Next</c>.</summary>
  private static List<ushort> Enumerate(nint data) {
    nint enumerator;
    Assert.That(((delegate* unmanaged<nint, uint, nint*, int>)Slot(data, 8))(data, NativeMethods.DATADIR_GET, &enumerator), Is.EqualTo(NativeMethods.S_OK));
    var formats = new List<ushort>();
    try {
      NativeMethods.FORMATETC format;
      uint fetched;
      while (((delegate* unmanaged<nint, uint, NativeMethods.FORMATETC*, uint*, int>)Slot(enumerator, 3))(enumerator, 1, &format, &fetched) == NativeMethods.S_OK && fetched == 1)
        formats.Add(format.cfFormat);
    } finally {
      NativeMethods.Release(enumerator);
    }

    return formats;
  }

  // --- The drop-in IDropTarget ---------------------------------------------------------------------

  private sealed class DropSurface : OwnerDrawnControl;

  /// <summary>A realized headless form hosting one drop target over its whole client area.</summary>
  private static (Hawkynt.NativeForms.Backends.IWindowPeer Window, DropSurface Target) CreateScene() {
    var form = new Form { Bounds = new(0, 0, 300, 200) };
    var target = new DropSurface { Bounds = new(0, 0, 300, 200), AllowDrop = true };
    form.Controls.Add(target);
    var window = form.RealizeWindow(new HeadlessBackend());
    ((HeadlessPeer)target.Peer!).ScreenOrigin = Point.Empty;
    return (window, target);
  }

  private static uint Enter(nint dropTarget, nint data, uint effect) {
    var point = new NativeMethods.POINT { x = 10, y = 10 };
    Assert.That(((delegate* unmanaged<nint, nint, uint, NativeMethods.POINT, uint*, int>)Slot(dropTarget, 3))(dropTarget, data, 1, point, &effect), Is.EqualTo(NativeMethods.S_OK));
    return effect;
  }

  private static uint Over(nint dropTarget, uint effect) {
    var point = new NativeMethods.POINT { x = 11, y = 12 };
    Assert.That(((delegate* unmanaged<nint, uint, NativeMethods.POINT, uint*, int>)Slot(dropTarget, 4))(dropTarget, 1, point, &effect), Is.EqualTo(NativeMethods.S_OK));
    return effect;
  }

  private static void Leave(nint dropTarget)
      => Assert.That(((delegate* unmanaged<nint, int>)Slot(dropTarget, 5))(dropTarget), Is.EqualTo(NativeMethods.S_OK));

  private static uint Drop(nint dropTarget, nint data, uint effect) {
    var point = new NativeMethods.POINT { x = 11, y = 12 };
    Assert.That(((delegate* unmanaged<nint, nint, uint, NativeMethods.POINT, uint*, int>)Slot(dropTarget, 6))(dropTarget, data, 0, point, &effect), Is.EqualTo(NativeMethods.S_OK));
    return effect;
  }

  [Test]
  public void Given_virtual_files_dropped_when_the_handler_reads_them_then_it_gets_every_entry_and_its_bytes() {
    RequireWindows();
    var (window, target) = CreateScene();
    var events = new List<string>();
    var read = new Dictionary<string, byte[]>();
    VirtualFile[]? delivered = null;
    target.DragEnter += (_, e) => {
      events.Add("enter " + e.Data.GetType().Name);
      e.Effect = DragDropEffects.Copy;
    };
    target.DragOver += (_, e) => events.Add($"over {e.X},{e.Y}");
    target.DragDrop += (_, e) => {
      events.Add("drop " + e.Effect);
      delivered = (VirtualFile[])e.Data;
      foreach (var file in delivered.Where(f => !f.IsDirectory)) {
        using var content = file.OpenRead();
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        read[file.RelativePath] = copy.ToArray();
      }
    };
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    var dropTarget = Win32DropTarget.Create(window);
    try {
      var entered = Enter(dropTarget, data, 1 | 2);
      var moved = Over(dropTarget, 1 | 2);
      var dropped = Drop(dropTarget, data, 1 | 2);

      Assert.Multiple(() => {
        Assert.That(entered, Is.EqualTo(1u));
        Assert.That(moved, Is.EqualTo(1u));
        Assert.That(dropped, Is.EqualTo(1u));
        Assert.That(events, Is.EqualTo(new[] { "enter VirtualFile[]", "over 11,12", "drop Copy" }));
        Assert.That(delivered!.Select(f => f.ToString()), Is.EqualTo(new[] { "readme.txt", "docs/", "docs/guide.md", "docs/empty.bin", "unknown-length.dat" }));
        Assert.That(delivered![0].Length, Is.EqualTo(7));
        Assert.That(delivered![0].LastWriteTimeUtc, Is.EqualTo(_time));
        Assert.That(delivered![4].Length, Is.Null);
        Assert.That(read["readme.txt"], Is.EqualTo(Bytes("read me")));
        Assert.That(read["docs/guide.md"], Is.EqualTo(Bytes("# guide")));
        Assert.That(read["docs/empty.bin"], Is.Empty);
        Assert.That(read["unknown-length.dat"], Is.EqualTo(Bytes("no length given")));
      });
    } finally {
      NativeMethods.Release(dropTarget);
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_delivered_drop_when_a_dropped_file_is_opened_afterwards_then_it_throws_and_the_source_is_released() {
    RequireWindows();
    var (window, target) = CreateScene();
    VirtualFile[]? delivered = null;
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    target.DragDrop += (_, e) => delivered = (VirtualFile[])e.Data;
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    var dropTarget = Win32DropTarget.Create(window);
    try {
      Enter(dropTarget, data, 1);
      var heldDuringHover = Win32ComObject.References(data);
      Drop(dropTarget, data, 1);

      Assert.Multiple(() => {
        Assert.That(heldDuringHover, Is.EqualTo(2), "the drop target holds the source while the drag hovers");
        Assert.That(Win32ComObject.References(data), Is.EqualTo(1), "and lets go once the drop is delivered");
        Assert.That(() => delivered![0].OpenRead(), Throws.InvalidOperationException);
      });
    } finally {
      NativeMethods.Release(dropTarget);
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_stream_opened_during_the_drop_when_read_from_another_thread_then_it_throws() {
    RequireWindows();
    var (window, target) = CreateScene();
    Exception? fromWorker = null;
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    target.DragDrop += (_, e) => {
      using var content = ((VirtualFile[])e.Data)[0].OpenRead();
      var worker = new Thread(() => {
        try {
          content.ReadByte();
        } catch (Exception ex) {
          fromWorker = ex;
        }
      });
      worker.Start();
      worker.Join();
    };
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    var dropTarget = Win32DropTarget.Create(window);
    try {
      Enter(dropTarget, data, 1);
      Drop(dropTarget, data, 1);

      Assert.That(fromWorker, Is.InstanceOf<InvalidOperationException>());
    } finally {
      NativeMethods.Release(dropTarget);
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_drag_that_leaves_when_the_hover_ends_then_the_target_is_left_and_the_source_released() {
    RequireWindows();
    var (window, target) = CreateScene();
    var events = new List<string>();
    target.DragEnter += (_, e) => {
      events.Add("enter");
      e.Effect = DragDropEffects.Copy;
    };
    target.DragLeave += (_, _) => events.Add("leave");
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    var dropTarget = Win32DropTarget.Create(window);
    try {
      Enter(dropTarget, data, 1);
      Leave(dropTarget);

      Assert.Multiple(() => {
        Assert.That(events, Is.EqualTo(new[] { "enter", "leave" }));
        Assert.That(Win32ComObject.References(data), Is.EqualTo(1));
      });
    } finally {
      NativeMethods.Release(dropTarget);
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_target_that_refuses_when_dropped_then_OLE_is_told_none() {
    RequireWindows();
    var (window, _) = CreateScene(); // nobody sets an effect
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    var dropTarget = Win32DropTarget.Create(window);
    try {
      Assert.Multiple(() => {
        Assert.That(Enter(dropTarget, data, 7), Is.Zero);
        Assert.That(Drop(dropTarget, data, 7), Is.Zero);
      });
    } finally {
      NativeMethods.Release(dropTarget);
      NativeMethods.Release(data);
    }
  }

  [Test]
  public void Given_a_source_offering_real_paths_and_descriptors_when_dropped_then_the_paths_are_delivered() {
    RequireWindows();
    var (window, target) = CreateScene();
    object? delivered = null;
    target.DragEnter += (_, e) => e.Effect = DragDropEffects.Copy;
    target.DragDrop += (_, e) => delivered = e.Data;
    var data = Win32VirtualFileDataObject.Create(SampleTree());
    var dropTarget = Win32DropTarget.Create(window);
    string[] paths = [@"C:\first.txt", @"D:\folder\Übersicht.txt"];
    try {
      Store(data, NativeMethods.CF_HDROP, DropFiles(paths));
      Enter(dropTarget, data, 1);
      Drop(dropTarget, data, 1);

      Assert.That(delivered, Is.EqualTo(paths));
    } finally {
      NativeMethods.Release(dropTarget);
      NativeMethods.Release(data);
    }
  }

  /// <summary>A <c>DROPFILES</c> block: the header (offset 20, wide), then the zero-separated, double-zero-terminated paths.</summary>
  private static byte[] DropFiles(string[] paths) {
    var text = string.Join('\0', paths) + "\0\0";
    var bytes = new byte[20 + text.Length * 2];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, 20); // pFiles
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1); // fWide
    Encoding.Unicode.GetBytes(text).CopyTo(bytes, 20);
    return bytes;
  }

  /// <summary>Stores <paramref name="bytes"/> on the data object the way the shell does, through <c>SetData</c>.</summary>
  private static void Store(nint data, ushort format, byte[] bytes) {
    var global = NativeMethods.GlobalAlloc(NativeMethods.GHND, (nuint)bytes.Length);
    bytes.CopyTo(new Span<byte>((void*)NativeMethods.GlobalLock(global), bytes.Length));
    NativeMethods.GlobalUnlock(global);
    var formatEtc = Format(format);
    var medium = new NativeMethods.STGMEDIUM { tymed = NativeMethods.TYMED_HGLOBAL, handle = global };
    Assert.That(((delegate* unmanaged<nint, NativeMethods.FORMATETC*, NativeMethods.STGMEDIUM*, int, int>)Slot(data, 7))(data, &formatEtc, &medium, 1), Is.EqualTo(NativeMethods.S_OK));
  }

  private sealed class ForwardOnlyStream(byte[] content) : Stream {
    private readonly MemoryStream _inner = new(content);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
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

  private sealed class ThrowingStream : Stream {
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("the archive is damaged");
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
