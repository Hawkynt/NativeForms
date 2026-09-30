using System.Buffers.Binary;
using System.Drawing;
using System.Text;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends.Windows;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// Receiving drops on Win32 through OLE (PRD §8): parsing <c>CFSTR_FILEDESCRIPTORW</c> as Outlook,
/// browsers and archive managers write it, keeping hostile paths out, and the window's
/// <c>IDropTarget</c> registration. Parsing is pure memory and runs everywhere; whatever needs OLE
/// reports itself ignored off Windows. Feeding the drop target a real data object is covered with the
/// drag-out data object as the source, in the tests of dragging virtual files out.
/// </summary>
[TestFixture]
internal sealed unsafe class Win32VirtualFileDropTests {
  private static void RequireWindows() {
    if (!OperatingSystem.IsWindows())
      Assert.Ignore("OLE exists only on Windows.");
  }

  private static void* Slot(nint instance, int slot) => ((void**)*(void**)instance)[slot];

  /// <summary>One <c>FILEDESCRIPTORW</c> as a source writes it: flags, attributes, write time, size halves, name.</summary>
  private static byte[] Descriptor(uint flags, string name, uint attributes = 0x80, ulong fileTime = 0, uint sizeHigh = 0, uint sizeLow = 0) {
    var bytes = new byte[592];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, flags);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), attributes);
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), fileTime);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), sizeHigh);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), sizeLow);
    Encoding.Unicode.GetBytes(name).CopyTo(bytes, 72);
    return bytes;
  }

  /// <summary>A <c>FILEGROUPDESCRIPTORW</c>: the count, then the descriptors.</summary>
  private static byte[] Group(params byte[][] descriptors) {
    var bytes = new byte[4 + descriptors.Length * 592];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)descriptors.Length);
    for (var i = 0; i < descriptors.Length; ++i)
      descriptors[i].CopyTo(bytes, 4 + i * 592);
    return bytes;
  }

  private const uint Attributes = 0x04, WriteTime = 0x20, FileSize = 0x40, Unicode = 0x80000000;

  [Test]
  public void Given_a_descriptor_with_a_size_beyond_4_GB_when_parsed_then_its_high_and_low_parts_combine() {
    var parsed = Win32DroppedData.ParseDescriptors(Group(Descriptor(Unicode | FileSize, "disk.iso", sizeHigh: 2, sizeLow: 0x8000_0001)));

    Assert.That(parsed[0].Length, Is.EqualTo(0x2_8000_0001L));
  }

  [Test]
  public void Given_a_descriptor_without_the_size_flag_when_parsed_then_the_length_is_unknown_even_if_the_fields_are_set() {
    var parsed = Win32DroppedData.ParseDescriptors(Group(Descriptor(Unicode, "mail.msg", sizeLow: 1234)));

    Assert.That(parsed[0].Length, Is.Null);
  }

  [Test]
  public void Given_a_descriptor_with_a_write_time_when_parsed_then_it_is_UTC() {
    var time = new DateTime(2025, 12, 24, 18, 0, 0, DateTimeKind.Utc);

    var parsed = Win32DroppedData.ParseDescriptors(Group(Descriptor(Unicode | WriteTime, "a.txt", fileTime: (ulong)time.ToFileTimeUtc())));

    Assert.That(parsed[0].Time, Is.EqualTo(time));
  }

  [Test]
  public void Given_a_folder_descriptor_when_parsed_then_it_is_a_directory_only_when_attributes_are_flagged() {
    var parsed = Win32DroppedData.ParseDescriptors(Group(
        Descriptor(Unicode | Attributes, "folder", attributes: 0x10),
        Descriptor(Unicode, "unflagged", attributes: 0x10)));

    Assert.That(parsed.Select(d => d.IsDirectory), Is.EqualTo(new[] { true, false }));
  }

  [Test]
  public void Given_backslash_separated_Unicode_names_when_parsed_then_they_use_forward_slashes() {
    var parsed = Win32DroppedData.ParseDescriptors(Group(Descriptor(Unicode, @"Anhänge\日本語\Bericht.pdf")));

    Assert.That(parsed[0].Path, Is.EqualTo("Anhänge/日本語/Bericht.pdf"));
  }

  [Test]
  public void Given_a_name_that_fills_the_whole_field_without_a_terminator_when_parsed_then_all_260_characters_are_kept() {
    var parsed = Win32DroppedData.ParseDescriptors(Group(Descriptor(Unicode, new string('n', 260))));

    Assert.That(parsed[0].Path, Has.Length.EqualTo(260));
  }

  [Test]
  public void Given_a_count_larger_than_the_block_when_parsed_then_only_complete_descriptors_are_read() {
    var bytes = Group(Descriptor(Unicode, "one.txt"), Descriptor(Unicode, "two.txt"));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1000);

    Assert.That(Win32DroppedData.ParseDescriptors(bytes.AsSpan(0, 4 + 592 + 100)).Select(d => d.Path), Is.EqualTo(new[] { "one.txt" }));
  }

  [TestCase(0, TestName = "Given_an_empty_block_when_parsed_then_there_are_no_entries")]
  [TestCase(3, TestName = "Given_a_block_shorter_than_its_count_field_when_parsed_then_there_are_no_entries")]
  [TestCase(4, TestName = "Given_a_block_with_only_a_count_when_parsed_then_there_are_no_entries")]
  public void Truncated_descriptor_blocks(int size)
      => Assert.That(Win32DroppedData.ParseDescriptors(new byte[size]), Is.Empty);

  [Test]
  public void Given_descriptors_that_escape_the_destination_when_turned_into_files_then_they_are_left_out() {
    var files = Win32DroppedData.CreateFiles(
        [("ok.txt", false, 1L, null), ("../evil.txt", false, 1L, null), ("/etc/passwd", false, 1L, null), ("C:/x.txt", false, 1L, null), ("", false, null, null), ("dir", true, null, null)],
        (_, _, _) => Stream.Null);

    Assert.That(files.Select(f => f.ToString()), Is.EqualTo(new[] { "ok.txt", "dir/" }));
  }

  [Test]
  public void Given_parsed_descriptors_when_a_file_is_opened_then_its_own_index_path_and_length_are_asked_for() {
    var asked = new List<(int, string, long?)>();
    var files = Win32DroppedData.CreateFiles(
        [("folder", true, null, null), ("folder/a.txt", false, 3L, null), ("b.bin", false, null, null)],
        (index, path, length) => {
          asked.Add((index, path, length));
          return Stream.Null;
        });

    files[2].OpenRead().Dispose();
    files[1].OpenRead().Dispose();

    Assert.That(asked, Is.EqualTo(new (int, string, long?)[] { (2, "b.bin", null), (1, "folder/a.txt", 3L) }));
  }

  // --- The drop target -----------------------------------------------------------------------------

  private sealed class DropSurface : OwnerDrawnControl;

  private static (Hawkynt.NativeForms.Backends.IWindowPeer Window, DropSurface Target) CreateScene() {
    var form = new Form { Bounds = new(0, 0, 300, 200) };
    var target = new DropSurface { Bounds = new(0, 0, 300, 200), AllowDrop = true };
    form.Controls.Add(target);
    var window = form.RealizeWindow(new HeadlessBackend());
    ((HeadlessPeer)target.Peer!).ScreenOrigin = Point.Empty;
    return (window, target);
  }

  [Test]
  public void Given_no_data_object_when_a_drag_enters_then_it_is_refused_for_the_whole_hover() {
    var (window, target) = CreateScene();
    var entered = false;
    target.DragEnter += (_, _) => entered = true;
    var dropTarget = Win32DropTarget.Create(window);
    try {
      var point = new NativeMethods.POINT { x = 10, y = 10 };
      uint enterEffect = 1, overEffect = 1;
      var enter = ((delegate* unmanaged<nint, nint, uint, NativeMethods.POINT, uint*, int>)Slot(dropTarget, 3))(dropTarget, 0, 1, point, &enterEffect);
      var over = ((delegate* unmanaged<nint, uint, NativeMethods.POINT, uint*, int>)Slot(dropTarget, 4))(dropTarget, 1, point, &overEffect);
      var (enteredEffect, movedEffect) = (enterEffect, overEffect);

      Assert.Multiple(() => {
        Assert.That(enter, Is.EqualTo(NativeMethods.S_OK));
        Assert.That(over, Is.EqualTo(NativeMethods.S_OK));
        Assert.That(enteredEffect, Is.Zero);
        Assert.That(movedEffect, Is.Zero);
        Assert.That(entered, Is.False);
      });
    } finally {
      NativeMethods.Release(dropTarget);
    }
  }

  [Test]
  public void Given_a_null_effect_pointer_when_a_drag_enters_then_the_call_is_refused() {
    var (window, _) = CreateScene();
    var dropTarget = Win32DropTarget.Create(window);
    try {
      var point = new NativeMethods.POINT();
      Assert.That(((delegate* unmanaged<nint, nint, uint, NativeMethods.POINT, uint*, int>)Slot(dropTarget, 3))(dropTarget, 0, 1, point, null),
          Is.EqualTo(NativeMethods.E_INVALIDARG));
    } finally {
      NativeMethods.Release(dropTarget);
    }
  }

  [Test]
  public void Given_the_drop_target_when_queried_then_it_answers_IDropTarget_and_IUnknown_only() {
    var (window, _) = CreateScene();
    var dropTarget = Win32DropTarget.Create(window);
    try {
      Assert.Multiple(() => {
        Assert.That(QueryAndRelease(dropTarget, NativeMethods.IID_IDropTarget), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(QueryAndRelease(dropTarget, NativeMethods.IID_IUnknown), Is.EqualTo(NativeMethods.S_OK));
        Assert.That(QueryAndRelease(dropTarget, NativeMethods.IID_IStream), Is.EqualTo(NativeMethods.E_NOINTERFACE));
        Assert.That(Win32ComObject.References(dropTarget), Is.EqualTo(1), "every successful query was balanced");
      });
    } finally {
      NativeMethods.Release(dropTarget);
    }
  }

  private static int QueryAndRelease(nint instance, Guid iid) {
    var result = NativeMethods.QueryInterface(instance, iid, out var other);
    if (result == NativeMethods.S_OK)
      NativeMethods.Release(other);

    return result;
  }

  // --- Registration on a real window ---------------------------------------------------------------

  /// <summary><c>DRAGDROP_E_ALREADYREGISTERED</c>.</summary>
  private const int DragDropAlreadyRegistered = unchecked((int)0x80040101);

  [Test]
  [Apartment(ApartmentState.STA)]
  public void Given_an_STA_UI_thread_when_a_window_is_created_then_it_registers_an_OLE_drop_target() {
    RequireWindows();
    var window = new WindowPeer();
    var probe = Win32DropTarget.Create(window);
    try {
      Assert.That(NativeMethods.RegisterDragDrop(window.Handle, probe), Is.EqualTo(DragDropAlreadyRegistered));
    } finally {
      NativeMethods.Release(probe);
      window.Dispose();
    }
  }

  [Test]
  [Apartment(ApartmentState.MTA)]
  public void Given_an_MTA_UI_thread_when_a_window_is_created_then_it_keeps_WM_DROPFILES_without_an_OLE_drop_target() {
    RequireWindows();
    var window = new WindowPeer();
    try {
      Assert.That(NativeMethods.RevokeDragDrop(window.Handle), Is.Not.EqualTo(NativeMethods.S_OK), "nothing was registered to revoke");
    } finally {
      window.Dispose();
    }
  }
}
