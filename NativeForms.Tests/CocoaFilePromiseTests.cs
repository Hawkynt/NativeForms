using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Hawkynt.NativeForms.Backends.MacOS;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// The file promises behind dragging virtual files out on macOS (PRD §8), driven the way AppKit drives
/// them: the delegate of a real <c>NSFilePromiseProvider</c> is asked for the file name and then told
/// to write the promise to a destination URL, with a completion block, all through
/// <c>objc_msgSend</c>. That proves the runtime class, its method encodings, the block call and the
/// temporary-name write on a real Objective-C runtime; a drag into Finder still needs a person. Off
/// macOS the fixture reports itself ignored.
/// </summary>
[TestFixture]
internal sealed unsafe partial class CocoaFilePromiseTests {
  private const string ObjC = "/usr/lib/libobjc.A.dylib";

  [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
  private static partial nint objc_getClass(string name);

  [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
  private static partial nint sel_registerName(string name);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  private static partial nint Send(nint receiver, nint selector, nint argument);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  private static partial nint Send(nint receiver, nint selector, nint first, nint second);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  private static partial void SendVoid(nint receiver, nint selector, nint first, nint second, nint third);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  private static partial nint Send(nint receiver, nint selector);

  private static readonly Type _Promises = typeof(CocoaBackend).Assembly
      .GetType("Hawkynt.NativeForms.Backends.MacOS.CocoaFilePromises", throwOnError: true)!;

  [StructLayout(LayoutKind.Sequential)]
  private struct Block {
    public nint Isa;
    public int Flags;
    public int Reserved;
    public nint Invoke;
    public nint Descriptor;
  }

  /// <summary>What the completion block heard: -1 not called, 0 nil, 1 an error.</summary>
  private static int _completion;

  [UnmanagedCallersOnly]
  private static void Completed(nint block, nint error) => _completion = error == 0 ? 0 : 1;

  private string _destination = string.Empty;

  [SetUp]
  public void RequireMacOS() {
    if (!OperatingSystem.IsMacOS())
      Assert.Ignore("File promises exist only on macOS.");

    _destination = Path.Combine(Path.GetTempPath(), "nf-promise-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_destination);
  }

  [TearDown]
  public void DeleteDestination() {
    if (Directory.Exists(_destination))
      Directory.Delete(_destination, recursive: true);
  }

  private static nint NSString(string text) {
    var bytes = Encoding.UTF8.GetBytes(text + "\0");
    fixed (byte* utf8 = bytes)
      return Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), (nint)utf8);
  }

  private static string? ManagedString(nint nsString)
      => nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, sel_registerName("UTF8String")));

  private static nint FileUrl(string path) => Send(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), NSString(path));

  private static nint[] CreateProviders(VirtualFile[] files)
      => (nint[])_Promises.GetMethod("CreateProviders", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [files])!;

  private static nint Delegate()
      => (nint)_Promises.GetMethod("EnsureDelegate", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

  /// <summary>A global block <c>void (^)(NSError *)</c> whose body records what it heard.</summary>
  private static Block* CompletionBlock() {
    Assert.That(NativeLibrary.TryLoad("/usr/lib/libSystem.B.dylib", out var system), Is.True);
    Assert.That(NativeLibrary.TryGetExport(system, "_NSConcreteGlobalBlock", out var isa), Is.True);
    var descriptor = (nuint*)NativeMemory.AllocZeroed(2, (nuint)sizeof(nuint));
    descriptor[1] = (nuint)sizeof(Block);
    var block = (Block*)NativeMemory.AllocZeroed((nuint)sizeof(Block));
    block->Isa = isa;
    block->Flags = 1 << 28;
    block->Invoke = (nint)(delegate* unmanaged<nint, nint, void>)&Completed;
    block->Descriptor = (nint)descriptor;
    return block;
  }

  private static VirtualFile Text(string path, string content) => new(path, () => new MemoryStream(Encoding.UTF8.GetBytes(content)));

  [Test]
  public void Given_virtual_files_when_providers_are_made_then_there_is_one_per_top_level_entry_named_after_it() {
    var providers = CreateProviders([Text("a.txt", "a"), Text("docs/b.txt", "b"), Text("docs/c.txt", "c")]);
    var promiseDelegate = Delegate();

    var names = providers.Select(p => ManagedString(Send(promiseDelegate, sel_registerName("filePromiseProvider:fileNameForType:"), p, NSString("public.data")))).ToArray();

    Assert.Multiple(() => {
      Assert.That(names, Is.EqualTo(new[] { "a.txt", "docs" }));
      Assert.That(Send(providers[0], sel_registerName("delegate")), Is.EqualTo(promiseDelegate), "the provider calls our delegate");
    });
  }

  [Test]
  public void Given_a_promised_file_when_AppKit_asks_for_it_then_it_is_written_to_the_destination_and_the_completion_hears_nil() {
    var providers = CreateProviders([Text("Übersicht.txt", "promised content")]);
    var target = Path.Combine(_destination, "Übersicht.txt");
    _completion = -1;

    SendVoid(Delegate(), sel_registerName("filePromiseProvider:writePromiseToURL:completionHandler:"), providers[0], FileUrl(target), (nint)CompletionBlock());

    Assert.Multiple(() => {
      Assert.That(_completion, Is.Zero);
      Assert.That(File.ReadAllText(target), Is.EqualTo("promised content"));
      Assert.That(Directory.GetFileSystemEntries(_destination), Has.Length.EqualTo(1), "no temporary name is left behind");
    });
  }

  [Test]
  public void Given_a_promised_folder_when_AppKit_asks_for_it_then_its_tree_is_written() {
    var providers = CreateProviders([Text("tree/one.txt", "1"), Text("tree/sub/two.txt", "2")]);
    _completion = -1;

    SendVoid(Delegate(), sel_registerName("filePromiseProvider:writePromiseToURL:completionHandler:"), providers[0], FileUrl(Path.Combine(_destination, "tree")), (nint)CompletionBlock());

    Assert.Multiple(() => {
      Assert.That(_completion, Is.Zero);
      Assert.That(File.ReadAllText(Path.Combine(_destination, "tree", "one.txt")), Is.EqualTo("1"));
      Assert.That(File.ReadAllText(Path.Combine(_destination, "tree", "sub", "two.txt")), Is.EqualTo("2"));
    });
  }

  [Test]
  public void Given_content_that_fails_when_AppKit_asks_for_it_then_the_completion_hears_an_error_and_nothing_remains() {
    var providers = CreateProviders([new VirtualFile("broken.bin", () => throw new IOException("damaged"))]);
    _completion = -1;

    SendVoid(Delegate(), sel_registerName("filePromiseProvider:writePromiseToURL:completionHandler:"), providers[0], FileUrl(Path.Combine(_destination, "broken.bin")), (nint)CompletionBlock());

    Assert.Multiple(() => {
      Assert.That(_completion, Is.EqualTo(1));
      Assert.That(Directory.GetFileSystemEntries(_destination), Is.Empty);
    });
  }
}
