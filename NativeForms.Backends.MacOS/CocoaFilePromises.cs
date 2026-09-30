using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// File promises, AppKit's way of moving files that do not exist yet (PRD §8): virtual files dragged
/// out become <c>NSFilePromiseProvider</c>s whose delegate writes each one when the destination asks,
/// and promises dropped in are received through <c>NSFilePromiseReceiver</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Out.</b> One provider per top-level entry. The shared delegate, a class built at run time
/// conforming to <c>NSFilePromiseProviderDelegate</c>, answers <c>filePromiseProvider:fileNameForType:</c>
/// with the entry's name and, when the destination asks, writes the entry — a file, or a folder with
/// everything below it — in <c>filePromiseProvider:writePromiseToURL:completionHandler:</c>: into the
/// destination folder under a hidden temporary name, renamed to the final name only once complete
/// (<see cref="VirtualFileWriter"/>), never replacing an existing file. The completion handler hears
/// <c>nil</c> or an <c>NSError</c>. AppKit calls the delegate on the main queue, where the
/// application's streams expect to be read.
/// </para>
/// <para>
/// <b>In.</b> Promised files are received into a private temporary folder while the drop is being
/// performed — the main thread waits for them, up to a minute — then delivered as a
/// <c>VirtualFile[]</c> over the received files, and the folder is removed once the drop handler has
/// returned. The source writes on its own queue, so a slow source keeps the drop waiting.
/// </para>
/// <para>
/// The blocks AppKit wants (the reader of received files) and calls (the write completion) are the
/// C block ABI: a global block literal is a static struct whose <c>invoke</c> is an
/// <see cref="UnmanagedCallersOnlyAttribute"/> function pointer, so nothing is marshalled.
/// </para>
/// </remarks>
internal static unsafe class CocoaFilePromises {
  /// <summary><c>BLOCK_IS_GLOBAL</c>: the literal lives forever and copying it returns itself.</summary>
  private const int BlockIsGlobal = 1 << 28;

  /// <summary>How long a drop waits for promised files to arrive.</summary>
  private static readonly TimeSpan _ReceiveTimeout = TimeSpan.FromMinutes(1);

  /// <summary>The C block literal layout (<c>Block_literal_1</c>) of a block without captures.</summary>
  [StructLayout(LayoutKind.Sequential)]
  private struct BlockLiteral {
    public nint Isa;
    public int Flags;
    public int Reserved;
    public nint Invoke;
    public nint Descriptor;
  }

  /// <summary>The C block descriptor (<c>Block_descriptor_1</c>): reserved, and the literal's size.</summary>
  [StructLayout(LayoutKind.Sequential)]
  private struct BlockDescriptor {
    public nuint Reserved;
    public nuint Size;
  }

  /// <summary>The runtime delegate class, and its one instance — the provider only holds it weakly.</summary>
  private static nint _delegateClass;
  private static nint _delegate;

  /// <summary>What each provider promises: the dragged entries and the top-level name it stands for.</summary>
  private static readonly ConcurrentDictionary<nint, (VirtualFile[] Files, string Top)> _promises = new();

  /// <summary>
  /// The providers for <paramref name="files"/>, one per top-level entry, each owned by the caller
  /// (one reference), or an empty array when AppKit has no file promises.
  /// </summary>
  internal static nint[] CreateProviders(VirtualFile[] files) {
    var promiseDelegate = EnsureDelegate();
    if (promiseDelegate == 0)
      return [];

    var names = VirtualFileWriter.TopLevelNames(files);
    var providers = new List<nint>(names.Count);
    foreach (var name in names) {
      var type = CocoaRuntime.NSString(VirtualFileWriter.IsFolder(files, name) ? "public.folder" : "public.data");
      var allocated = CocoaRuntime.Allocate("NSFilePromiseProvider");
      var provider = allocated == 0 || type == 0
          ? 0
          : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("initWithFileType:delegate:"), type, promiseDelegate);
      if (type != 0)
        CocoaRuntime.SendVoid(type, CocoaRuntime.sel_registerName("release"));

      if (provider == 0) {
        Forget([.. providers]);
        return [];
      }

      _promises[provider] = (files, name);
      providers.Add(provider);
    }

    return [.. providers];
  }

  /// <summary>Drops the promises of providers whose drag ended without a drop.</summary>
  internal static void Forget(nint[] providers) {
    foreach (var provider in providers)
      _promises.TryRemove(provider, out _);
  }

  /// <summary>The delegate instance, building its class on first use; zero without AppKit's promise support.</summary>
  internal static nint EnsureDelegate() {
    if (_delegate != 0)
      return _delegate;

    if (CocoaRuntime.objc_getClass("NSFilePromiseProvider") == 0)
      return 0;

    if (_delegateClass == 0) {
      var superclass = CocoaRuntime.objc_getClass("NSObject");
      var created = superclass == 0 ? 0 : CocoaRuntime.objc_allocateClassPair(superclass, "NativeFormsFilePromiseDelegate", 0);
      if (created == 0)
        return 0;

      if (CocoaRuntime.objc_getProtocol("NSFilePromiseProviderDelegate") is var protocol and not 0)
        CocoaRuntime.class_addProtocol(created, protocol);

      // "@@:@@": answers an NSString for self, _cmd, the provider and the file type.
      CocoaRuntime.class_addMethod(
          created,
          CocoaRuntime.sel_registerName("filePromiseProvider:fileNameForType:"),
          (nint)(delegate* unmanaged<nint, nint, nint, nint, nint>)&FileName,
          "@@:@@");

      // "v@:@@@?": the provider, the destination URL, and the completion block.
      CocoaRuntime.class_addMethod(
          created,
          CocoaRuntime.sel_registerName("filePromiseProvider:writePromiseToURL:completionHandler:"),
          (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&WritePromise,
          "v@:@@@?");
      CocoaRuntime.objc_registerClassPair(created);
      _delegateClass = created;
    }

    var allocated = CocoaRuntime.SendPointer(_delegateClass, CocoaRuntime.sel_registerName("alloc"));
    _delegate = allocated == 0 ? 0 : CocoaRuntime.SendPointer(allocated, CocoaRuntime.sel_registerName("init"));
    return _delegate;
  }

  [UnmanagedCallersOnly]
  private static nint FileName(nint self, nint selector, nint provider, nint fileType) {
    try {
      if (!_promises.TryGetValue(provider, out var promise))
        return 0;

      var name = CocoaRuntime.NSString(promise.Top);
      return name == 0 ? 0 : CocoaRuntime.SendPointer(name, CocoaRuntime.sel_registerName("autorelease"));
    } catch {
      return 0;
    }
  }

  [UnmanagedCallersOnly]
  private static void WritePromise(nint self, nint selector, nint provider, nint url, nint completion) {
    nint error = 0;
    try {
      error = Write(provider, url);
    } catch {
      error = Error("The promised file could not be written.");
    }

    Complete(completion, error);
  }

  /// <summary>Writes the entry <paramref name="provider"/> promised to <paramref name="url"/>; answers zero or an autoreleased <c>NSError</c>.</summary>
  internal static nint Write(nint provider, nint url) {
    if (!_promises.TryRemove(provider, out var promise))
      return Error("Nothing is promised by this provider.");

    var path = PathOf(url);
    if (path is null)
      return Error("The destination is not a file URL.");

    try {
      VirtualFileWriter.WriteEntry(path, promise.Files, promise.Top);
      return 0;
    } catch (Exception exception) {
      return Error(exception.Message);
    }
  }

  /// <summary>Calls a completion block <c>void (^)(NSError *)</c>.</summary>
  private static void Complete(nint block, nint error) {
    if (block == 0)
      return;

    var invoke = ((BlockLiteral*)block)->Invoke;
    ((delegate* unmanaged<nint, nint, void>)invoke)(block, error);
  }

  /// <summary>An autoreleased <c>NSError</c> in <c>NSCocoaErrorDomain</c> (<c>NSFileWriteUnknownError</c>) with a description.</summary>
  private static nint Error(string description) {
    var errors = CocoaRuntime.objc_getClass("NSError");
    var dictionaries = CocoaRuntime.objc_getClass("NSDictionary");
    var domain = CocoaRuntime.NSString("NSCocoaErrorDomain");
    var message = CocoaRuntime.NSString(description);
    var key = CocoaRuntime.NSString("NSLocalizedDescription");
    try {
      if (errors == 0 || dictionaries == 0 || domain == 0)
        return 0;

      var info = message == 0 || key == 0
          ? 0
          : CocoaRuntime.SendPointer(dictionaries, CocoaRuntime.sel_registerName("dictionaryWithObject:forKey:"), message, key);
      return CocoaRuntime.SendPointer(errors, CocoaRuntime.sel_registerName("errorWithDomain:code:userInfo:"), domain, 512, info);
    } finally {
      foreach (var owned in new[] { domain, message, key })
        if (owned != 0)
          CocoaRuntime.SendVoid(owned, CocoaRuntime.sel_registerName("release"));
    }
  }

  /// <summary>The file-system path of a file URL, or null.</summary>
  internal static string? PathOf(nint url) {
    if (url == 0 || !CocoaRuntime.SendBool(url, CocoaRuntime.sel_registerName("isFileURL")))
      return null;

    var path = CocoaRuntime.SendPointer(url, CocoaRuntime.sel_registerName("path"));
    var utf8 = path == 0 ? 0 : CocoaRuntime.SendPointer(path, CocoaRuntime.sel_registerName("UTF8String"));
    return utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
  }

  // --- Receiving -----------------------------------------------------------------------------------

  /// <summary>The reader block handed to every receiver, built on first use.</summary>
  private static BlockLiteral* _reader;

  /// <summary>The reception in flight: where files arrived, and how many are still expected.</summary>
  private static Reception? _reception;

  private sealed class Reception(int expected) {
    public readonly List<string> Arrived = [];
    public readonly CountdownEvent Remaining = new(expected);
  }

  /// <summary>
  /// Receives the file promises on <paramref name="pasteboard"/> into a new temporary folder and
  /// answers the entries that arrived as virtual files over it, together with the folder the caller
  /// removes once the drop is delivered; <see langword="null"/> when there are no promises or nothing
  /// arrived.
  /// </summary>
  internal static (VirtualFile[] Files, string Folder)? Receive(nint pasteboard) {
    var receiverClass = CocoaRuntime.objc_getClass("NSFilePromiseReceiver");
    var arrays = CocoaRuntime.objc_getClass("NSArray");
    if (pasteboard == 0 || receiverClass == 0 || arrays == 0 || Reader() == null)
      return null;

    var classes = CocoaRuntime.SendPointer(arrays, CocoaRuntime.sel_registerName("arrayWithObject:"), receiverClass);
    var receivers = classes == 0
        ? 0
        : CocoaRuntime.SendPointer(pasteboard, CocoaRuntime.sel_registerName("readObjectsForClasses:options:"), classes, 0);
    var count = receivers == 0 ? 0 : CocoaRuntime.SendInteger(receivers, CocoaRuntime.sel_registerName("count"));
    if (count <= 0)
      return null;

    var expected = 0;
    for (nint i = 0; i < count; ++i) {
      var receiver = CocoaRuntime.SendIndex(receivers, CocoaRuntime.sel_registerName("objectAtIndex:"), i);
      var types = receiver == 0 ? 0 : CocoaRuntime.SendPointer(receiver, CocoaRuntime.sel_registerName("fileTypes"));
      expected += types == 0 ? 1 : (int)Math.Max(1, CocoaRuntime.SendInteger(types, CocoaRuntime.sel_registerName("count")));
    }

    var folder = Path.Combine(Path.GetTempPath(), "NativeForms-promise-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var reception = new Reception(expected);
    _reception = reception;
    var destination = CocoaRuntime.NSString(folder);
    var urls = CocoaRuntime.objc_getClass("NSURL");
    var url = destination == 0 || urls == 0 ? 0 : CocoaRuntime.SendPointer(urls, CocoaRuntime.sel_registerName("fileURLWithPath:"), destination);
    var options = CocoaRuntime.SendToClass("NSDictionary", "dictionary");
    var queueAllocated = CocoaRuntime.Allocate("NSOperationQueue");
    var queue = queueAllocated == 0 ? 0 : CocoaRuntime.SendPointer(queueAllocated, CocoaRuntime.sel_registerName("init"));
    try {
      if (url == 0 || queue == 0)
        return null;

      for (nint i = 0; i < count; ++i) {
        var receiver = CocoaRuntime.SendIndex(receivers, CocoaRuntime.sel_registerName("objectAtIndex:"), i);
        CocoaRuntime.SendVoid(
            receiver,
            CocoaRuntime.sel_registerName("receivePromisedFilesAtDestination:options:operationQueue:reader:"),
            url,
            options,
            queue,
            (nint)_reader);
      }

      reception.Remaining.Wait(_ReceiveTimeout);
      string[] arrived;
      lock (reception.Arrived)
        arrived = [.. reception.Arrived];

      var files = ToVirtualFiles(folder, arrived);
      if (files.Length > 0)
        return (files, folder);

      TryDelete(folder);
      return null;
    } finally {
      _reception = null;
      if (destination != 0)
        CocoaRuntime.SendVoid(destination, CocoaRuntime.sel_registerName("release"));
      if (queue != 0)
        CocoaRuntime.SendVoid(queue, CocoaRuntime.sel_registerName("release"));
    }
  }

  /// <summary>
  /// The virtual files over what arrived in <paramref name="folder"/>: each received file, and each
  /// received folder with everything below it, relative to the folder.
  /// </summary>
  internal static VirtualFile[] ToVirtualFiles(string folder, string[] arrived) {
    var files = new List<VirtualFile>();
    foreach (var path in arrived) {
      if (Directory.Exists(path)) {
        files.Add(VirtualFile.Directory(Path.GetRelativePath(folder, path)));
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
          files.Add(Entry(folder, entry));
      } else if (File.Exists(path)) {
        files.Add(Entry(folder, path));
      }
    }

    return [.. files];
  }

  private static VirtualFile Entry(string folder, string path) {
    var relative = Path.GetRelativePath(folder, path);
    if (Directory.Exists(path))
      return VirtualFile.Directory(relative);

    var info = new FileInfo(path);
    return new VirtualFile(relative, () => File.OpenRead(path), info.Length, info.LastWriteTimeUtc);
  }

  /// <summary>Removes a reception folder once its drop has been delivered.</summary>
  internal static void TryDelete(string folder) {
    try {
      Directory.Delete(folder, recursive: true);
    } catch (IOException) {
    } catch (UnauthorizedAccessException) {
    }
  }

  /// <summary>The global reader block <c>void (^)(NSURL *fileURL, NSError *errorOrNil)</c>, or null.</summary>
  private static BlockLiteral* Reader() {
    if (_reader != null)
      return _reader;

    if (!NativeLibrary.TryLoad("/usr/lib/libSystem.B.dylib", out var system)
        || !NativeLibrary.TryGetExport(system, "_NSConcreteGlobalBlock", out var globalBlockClass))
      return null;

    var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
    descriptor->Size = (nuint)sizeof(BlockLiteral);
    var reader = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
    reader->Isa = globalBlockClass;
    reader->Flags = BlockIsGlobal;
    reader->Invoke = (nint)(delegate* unmanaged<nint, nint, nint, void>)&Received;
    reader->Descriptor = (nint)descriptor;
    _reader = reader;
    return reader;
  }

  /// <summary>The reader block's body: one promised file arrived (or failed), on the receiving queue.</summary>
  [UnmanagedCallersOnly]
  private static void Received(nint block, nint url, nint error) {
    try {
      if (_reception is not { } reception)
        return;

      if (error == 0 && PathOf(url) is { } path)
        lock (reception.Arrived)
          reception.Arrived.Add(path);

      if (!reception.Remaining.IsSet)
        reception.Remaining.Signal();
    } catch {
      // An exception must not unwind into AppKit's frames.
    }
  }
}
