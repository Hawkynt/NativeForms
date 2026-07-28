using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// Keeps an accessibility shadow control out of the way: it paints nothing and refuses the mouse, so the
/// owner-drawn control it stands beside keeps every pixel and every click (PRD §8).
/// </summary>
/// <remarks>
/// The shadow has to be a real, visible window — MSAA leaves an invisible one out of the tree — but it
/// must not look or behave like one. Swallowing the two paint messages leaves the canvas's own painting
/// showing through (the canvas class carries no <c>WS_CLIPCHILDREN</c>, so it draws under its children),
/// and answering <c>WM_NCHITTEST</c> with <c>HTTRANSPARENT</c> sends the pointer to the parent as though
/// the shadow were not there.
/// </remarks>
internal static unsafe class Win32ShadowProc
{
    /// <summary>The subclass identity for the shadow procedure.</summary>
    private const nuint _SubclassId = 3;

    /// <summary>The mouse is not over this window; try the one beneath.</summary>
    private const nint HTTRANSPARENT = -1;

    /// <summary>Live shadow windows, so the static procedure knows one when it sees it.</summary>
    private static readonly ConcurrentDictionary<nint, byte> _shadows = new();

    /// <summary>Subclasses a freshly created shadow so it stops behaving like a control.</summary>
    public static void Install(nint handle)
    {
        _shadows[handle] = 0;
        NativeMethods.SetWindowSubclass(
            handle,
            (nint)(delegate* unmanaged<nint, uint, nint, nint, nuint, nint, nint>)&ShadowProc,
            _SubclassId,
            0);
    }

    /// <summary>Forgets a shadow whose window is going away.</summary>
    public static void Forget(nint handle) => _shadows.TryRemove(handle, out _);

    [UnmanagedCallersOnly]
    private static nint ShadowProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nint refData)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                return HTTRANSPARENT;

            case NativeMethods.WM_ERASEBKGND:
                return 1; // reported erased, so nothing is painted over the canvas

            case NativeMethods.WM_PAINT:
                // The update region has to be consumed or Windows keeps asking; consuming it without
                // drawing is exactly what leaves the canvas's own paint visible.
                NativeMethods.ValidateRect(hwnd, 0);
                return 0;

            default:
                return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
        }
    }
}
