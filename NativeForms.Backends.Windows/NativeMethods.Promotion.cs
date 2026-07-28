using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The Win32 surface the native-peer promotions need (PRD §12): the check-box and group-box styles of
/// the <c>BUTTON</c> class, and the common controls <c>msctls_progress32</c> and
/// <c>msctls_trackbar32</c>.
/// </summary>
/// <remarks>
/// The common controls live in <c>comctl32</c> and only register their window classes once
/// <c>InitCommonControlsEx</c> has run for the matching class block, so the backend calls it before the
/// first progress bar or slider is created rather than relying on a manifest.
/// </remarks>
internal static partial class NativeMethods
{
    // --- BUTTON styles -----------------------------------------------------------------------------

    /// <summary>A check box that toggles itself on click.</summary>
    internal const uint BS_AUTOCHECKBOX = 0x00000003;

    /// <summary>A radio button that checks itself and clears its group siblings on click.</summary>
    internal const uint BS_AUTORADIOBUTTON = 0x00000009;

    /// <summary>A frame with a caption; the classic group box.</summary>
    internal const uint BS_GROUPBOX = 0x00000007;

    /// <summary>Starts a new tab/arrow-key group — what makes a run of radio buttons one group.</summary>
    internal const uint WS_GROUP = 0x00020000;

    /// <summary>Sets a check box's state.</summary>
    internal const uint BM_SETCHECK = 0x00F1;

    /// <summary>Reads a check box's state.</summary>
    internal const uint BM_GETCHECK = 0x00F0;

    /// <summary>Unchecked.</summary>
    internal const nint BST_UNCHECKED = 0;

    /// <summary>Checked.</summary>
    internal const nint BST_CHECKED = 1;

    // --- Common controls ---------------------------------------------------------------------------

    /// <summary>The window class of the common-controls progress bar.</summary>
    internal const string PROGRESS_CLASS = "msctls_progress32";

    /// <summary>The window class of the common-controls trackbar (slider).</summary>
    internal const string TRACKBAR_CLASS = "msctls_trackbar32";

    /// <summary>Progress bar: an indeterminate, scrolling block.</summary>
    internal const uint PBS_MARQUEE = 0x08;

    /// <summary>Progress bar: sets the position.</summary>
    internal const uint PBM_SETPOS = 0x0402;

    /// <summary>Progress bar: sets a 32-bit range.</summary>
    internal const uint PBM_SETRANGE32 = 0x0406;

    /// <summary>Progress bar: starts or stops the marquee animation.</summary>
    internal const uint PBM_SETMARQUEE = 0x040A;

    /// <summary>Trackbar: sets the thumb position (<c>wParam</c> non-zero redraws).</summary>
    internal const uint TBM_SETPOS = 0x0405;

    /// <summary>Trackbar: reads the thumb position.</summary>
    internal const uint TBM_GETPOS = 0x0400;

    /// <summary>Trackbar: sets the low end of the range.</summary>
    internal const uint TBM_SETRANGEMIN = 0x0407;

    /// <summary>Trackbar: sets the high end of the range.</summary>
    internal const uint TBM_SETRANGEMAX = 0x0408;

    /// <summary>Trackbar: sets the arrow-key step.</summary>
    internal const uint TBM_SETLINESIZE = 0x0417;

    /// <summary>Trackbar: sets the page step.</summary>
    internal const uint TBM_SETPAGESIZE = 0x0415;

    /// <summary>Trackbar: no tick marks (the control draws its own where it wants them).</summary>
    internal const uint TBS_NOTICKS = 0x0010;

    /// <summary>Trackbar: runs vertically.</summary>
    internal const uint TBS_VERT = 0x0002;

    /// <summary>Horizontal scroll notification, sent to the parent of a slider or scroll bar.</summary>
    internal const uint WM_HSCROLL = 0x0114;

    /// <summary>Vertical scroll notification, sent to the parent of a slider or scroll bar.</summary>
    internal const uint WM_VSCROLL = 0x0115;

    /// <summary>The window class of the common-controls hyperlink.</summary>
    internal const string WC_LINK = "SysLink";

    /// <summary>Class block: hyperlink.</summary>
    internal const uint ICC_LINK_CLASS = 0x00008000;

    /// <summary>A link inside a <c>SysLink</c> was clicked.</summary>
    internal const int NM_CLICK = -2;

    /// <summary>A link inside a <c>SysLink</c> was activated with Enter.</summary>
    internal const int NM_RETURN = -4;

    /// <summary>Sets one of a <c>SysLink</c>'s items.</summary>
    internal const uint LM_SETITEM = 0x0400 + 0x0302;

    /// <summary><see cref="LITEM.iLink"/> is meaningful.</summary>
    internal const uint LIF_ITEMINDEX = 0x00000001;

    /// <summary><see cref="LITEM.state"/> and <see cref="LITEM.stateMask"/> are meaningful.</summary>
    internal const uint LIF_STATE = 0x00000002;

    /// <summary>The link has been followed.</summary>
    internal const uint LIS_VISITED = 0x00000008;

    /// <summary>Describes one link inside a <c>SysLink</c> control.</summary>
    /// <remarks>The two trailing buffers are fixed-size by contract, so the struct is blittable and the
    /// message can be sent without any marshalling.</remarks>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct LITEM
    {
        /// <summary>Which of the other members carry meaning (the <c>LIF_*</c> flags).</summary>
        internal uint mask;

        /// <summary>The zero-based index of the link within the control's caption.</summary>
        internal int iLink;

        /// <summary>The <c>LIS_*</c> state bits.</summary>
        internal uint state;

        /// <summary>Which bits of <see cref="state"/> to apply.</summary>
        internal uint stateMask;

        /// <summary>The link's <c>id</c> attribute; <c>MAX_LINKID_TEXT</c> characters.</summary>
        internal fixed char szID[48];

        /// <summary>The link's <c>href</c> attribute; <c>L_MAX_URL_LENGTH</c> characters.</summary>
        internal fixed char szUrl[2084];
    }

    /// <summary>Class block: progress bar.</summary>
    internal const uint ICC_PROGRESS_CLASS = 0x00000020;

    /// <summary>Class block: trackbar.</summary>
    internal const uint ICC_BAR_CLASSES = 0x00000004;

    /// <summary>The argument of <see cref="InitCommonControlsEx"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct INITCOMMONCONTROLSEX
    {
        /// <summary>The size of this structure, in bytes.</summary>
        internal uint dwSize;

        /// <summary>The <c>ICC_*</c> class blocks to register.</summary>
        internal uint dwICC;
    }

    /// <summary>Registers the requested common-control window classes.</summary>
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);
}
