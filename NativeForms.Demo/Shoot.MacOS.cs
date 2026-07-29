using System.Drawing;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// Writes a PNG of the gallery's window on macOS by asking AppKit to cache the view's drawing into a
/// bitmap.
/// </summary>
/// <remarks>
/// <para>
/// In-process for the reason the other two capture paths give: a CI runner has no desktop session to
/// point a screenshot tool at. <c>cacheDisplayInRect:toBitmapImageRep:</c> asks the view to draw
/// itself, which is the AppKit equivalent of <c>gtk_widget_draw</c> and, like it, sidesteps the
/// display server entirely — so what lands in the file is what the toolkit painted rather than
/// whatever was stacked on a desktop.
/// </para>
/// <para>
/// The rep can encode itself as PNG, so nothing here writes an image format. That is worth taking:
/// AppKit's encoder is right about colour space and premultiplication in a way a hand-rolled writer
/// would have to be taught.
/// </para>
/// </remarks>
internal static unsafe partial class ShootMacOS
{
    private const string _ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSBitmapImageFileTypePNG.</summary>
    private const nint _Png = 4;

    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(_ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint first, nint second);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendRect(nint receiver, nint selector, Rect frame);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendRect(nint receiver, nint selector, Rect frame, nint rep);

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SendBool(nint receiver, nint selector);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public double X, Y, Width, Height;
    }

    /// <summary>
    /// Why the last capture produced nothing, so a failure in the artifact names the step that gave
    /// up rather than only the fact that one did.
    /// </summary>
    public static string? Diagnosis { get; private set; }

    /// <summary>Captures the application's gallery window, returning the size written or null.</summary>
    public static Size? Window(string path)
    {
        Diagnosis = null;
        var application = objc_getClass("NSApplication");
        if (application == 0)
            return Failed("AppKit is not loaded in this process");

        var app = Send(application, sel_registerName("sharedApplication"));
        if (app == 0)
            return Failed("NSApplication has no shared instance");

        var window = Gallery(app);
        if (window == 0)
            return Failed("the application owns no window to photograph");

        var view = Send(window, sel_registerName("contentView"));
        if (view == 0)
            return Failed("the window has no content view");

        // The view's own bounds, so the shot is the client area rather than the framed window.
        var size = SizeFromFrame(view);
        if (size.Width <= 0 || size.Height <= 0)
            return Failed($"the content view measures {size.Width}x{size.Height}");

        var bounds = new Rect { X = 0, Y = 0, Width = size.Width, Height = size.Height };

        // The shutter fires from a queued tick, so a pending frame may not have rendered when the
        // capture asks for it — caching an undrawn view yields an empty rep. So the whole attempt
        // repeats, each round letting the run loop breathe and the view finish laying out first, and
        // the first one that produces bytes wins.
        for (var attempt = 0; attempt < 5; ++attempt)
        {
            ShootInputMac.Drain();
            Send(view, sel_registerName("layoutSubtreeIfNeeded"));
            Send(view, sel_registerName("display"));

            var rep = SendRect(view, sel_registerName("bitmapImageRepForCachingDisplayInRect:"), bounds);
            if (rep == 0)
            {
                Failed("the view declined to make a bitmap rep for its own bounds");
                continue;
            }

            SendRect(view, sel_registerName("cacheDisplayInRect:toBitmapImageRep:"), bounds, rep);

            var data = Send(rep, sel_registerName("representationUsingType:properties:"), _Png, EmptyDictionary());
            if (data == 0)
            {
                Failed("the bitmap rep produced no PNG representation");
                continue;
            }

            var bytes = Send(data, sel_registerName("bytes"));
            var length = (int)Send(data, sel_registerName("length"));
            if (bytes == 0 || length <= 0)
            {
                Failed($"the PNG representation is {length} byte(s) long");
                continue;
            }

            using (var file = File.Create(path))
                file.Write(new ReadOnlySpan<byte>((void*)bytes, length));

            Diagnosis = null;
            return size;
        }

        return null;
    }

    /// <summary>Records why a capture gave up and answers null, so a caller can return in one line.</summary>
    private static Size? Failed(string reason)
    {
        Diagnosis = reason;
        return null;
    }

    /// <summary>
    /// The window to photograph, picked from every window the application owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <c>mainWindow</c>, and not <c>keyWindow</c>: both are nil while the application is
    /// inactive, and on a runner it is inactive for the first several seconds after
    /// <c>activateIgnoringOtherApps:</c> — nothing else is competing for the focus, so nothing hurries
    /// the window server along. That is the exact shape the shot counts had, and it is a suffix rather
    /// than a scatter: fourteen pages with no window to find, then the last two once activation
    /// finally landed. The window list does not depend on activation, so the capture no longer waits
    /// for a focus change that may never come.
    /// </para>
    /// <para>
    /// The largest visible one is the gallery. Choosing by size rather than by title keeps this from
    /// needing to read an <c>NSString</c> back, and it is not a guess: every other window this process
    /// owns is a popup, which is borderless, tiny beside the window it hangs off, and only visible
    /// while it is open.
    /// </para>
    /// </remarks>
    private static nint Gallery(nint app)
    {
        var windows = Send(app, sel_registerName("windows"));
        var count = windows == 0 ? 0 : (int)Send(windows, sel_registerName("count"));
        var largest = (nint)0;
        var largestArea = 0L;

        for (var i = 0; i < count; ++i)
        {
            var window = Send(windows, sel_registerName("objectAtIndex:"), i);
            if (window == 0 || !SendBool(window, sel_registerName("isVisible")))
                continue;

            var size = SizeFromFrame(Send(window, sel_registerName("contentView")));
            var area = (long)size.Width * size.Height;
            if (area <= largestArea)
                continue;

            largest = window;
            largestArea = area;
        }

        if (largest != 0)
            return largest;

        // Nothing visible: ask the way that needs activation after all, rather than give up on a run
        // whose window is merely ordered out.
        var main = Send(app, sel_registerName("mainWindow"));
        return main != 0 ? main : Send(app, sel_registerName("keyWindow"));
    }

    /// <summary>An empty <c>NSDictionary</c> for the encoder's options.</summary>
    private static nint EmptyDictionary()
    {
        var dictionary = objc_getClass("NSDictionary");
        return dictionary == 0 ? 0 : Send(dictionary, sel_registerName("dictionary"));
    }

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendStructReturn(out Rect result, nint receiver, nint selector);

    /// <summary>Reads the view's frame, using the struct-return entry point where the ABI needs one.</summary>
    private static Size SizeFromFrame(nint view)
    {
        if (view == 0)
            return Size.Empty;

        // On arm64 a four-double struct comes back in registers, so the ordinary send is correct.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            var frame = SendFrame(view, sel_registerName("frame"));
            return new((int)frame.Width, (int)frame.Height);
        }

        SendStructReturn(out var wide, view, sel_registerName("frame"));
        return new((int)wide.Width, (int)wide.Height);
    }

    [LibraryImport(_ObjC, EntryPoint = "objc_msgSend")]
    private static partial Rect SendFrame(nint receiver, nint selector);
}
