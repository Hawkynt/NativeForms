using System.Runtime.InteropServices;

namespace Hawkynt.NativeForms.Backends.Windows;

/// <summary>
/// The rich-edit slice of the backend: the <c>RICHEDIT50W</c> (msftedit.dll) messages, structures
/// and notification plumbing the <see cref="RichTextBoxPeer"/> needs on top of the plain EDIT
/// machinery. Kept in a separate partial so the windowing surface stays focused. Everything is
/// source-generated P/Invoke over blittable structs so the layer remains trim- and AOT-safe.
/// </summary>
internal static partial class NativeMethods
{
    // --- Window messages ---

    /// <summary>Sent to a parent when a common/rich control posts a structured notification (lParam → <see cref="NMHDR"/>).</summary>
    internal const uint WM_NOTIFY = 0x004E;

    // --- Rich-edit messages (WM_USER = 0x0400 based) ---

    /// <summary>Applies a <see cref="CHARFORMAT2W"/> to a scope; wParam <see cref="SCF_SELECTION"/> targets the selection.</summary>
    internal const uint EM_SETCHARFORMAT = 0x0444;

    /// <summary>Sets which notifications the rich edit sends to its parent (an <c>ENM_*</c> mask).</summary>
    internal const uint EM_SETEVENTMASK = 0x0445;

    /// <summary>Applies a <see cref="PARAFORMAT2"/> to the paragraphs the selection touches.</summary>
    internal const uint EM_SETPARAFORMAT = 0x0447;

    /// <summary>Streams text into the control through an <see cref="EDITSTREAM"/> callback (wParam: <c>SF_*</c> format).</summary>
    internal const uint EM_STREAMIN = 0x0449;

    /// <summary>Streams the control's content out through an <see cref="EDITSTREAM"/> callback (wParam: <c>SF_*</c> format).</summary>
    internal const uint EM_STREAMOUT = 0x044A;

    /// <summary>Copies a character range into a caller-provided buffer (lParam → <see cref="TEXTRANGE"/>).</summary>
    internal const uint EM_GETTEXTRANGE = 0x044B;

    /// <summary>Enables or disables automatic URL detection (wParam is a BOOL).</summary>
    internal const uint EM_AUTOURLDETECT = 0x045B;

    /// <summary>Scales the display by the ratio wParam/lParam; 0/0 restores the default zoom.</summary>
    internal const uint EM_SETZOOM = 0x04E1;

    // --- EM_SETCHARFORMAT scope and CHARFORMAT2W masks/effects ---

    /// <summary>Format the current selection (and, when empty, the insertion point's format).</summary>
    internal const nint SCF_SELECTION = 1;

    /// <summary>The bold effect participates in this format operation.</summary>
    internal const uint CFM_BOLD = 0x00000001;

    /// <summary>The italic effect participates in this format operation.</summary>
    internal const uint CFM_ITALIC = 0x00000002;

    /// <summary>The underline effect participates in this format operation.</summary>
    internal const uint CFM_UNDERLINE = 0x00000004;

    /// <summary>The strikeout effect participates in this format operation.</summary>
    internal const uint CFM_STRIKEOUT = 0x00000008;

    /// <summary>The text color (<see cref="CHARFORMAT2W.crTextColor"/>) participates.</summary>
    internal const uint CFM_COLOR = 0x40000000;

    /// <summary>The character height (<see cref="CHARFORMAT2W.yHeight"/>) participates.</summary>
    internal const uint CFM_SIZE = 0x80000000;

    /// <summary>Use the automatic (system) text color instead of <see cref="CHARFORMAT2W.crTextColor"/>.</summary>
    internal const uint CFE_AUTOCOLOR = 0x40000000;

    // --- PARAFORMAT2 masks and values ---

    /// <summary>The alignment (<see cref="PARAFORMAT2.wAlignment"/>) participates.</summary>
    internal const uint PFM_ALIGNMENT = 0x00000008;

    /// <summary>The numbering (<see cref="PARAFORMAT2.wNumbering"/>) participates.</summary>
    internal const uint PFM_NUMBERING = 0x00000020;

    /// <summary>Left-aligned paragraphs.</summary>
    internal const ushort PFA_LEFT = 1;

    /// <summary>Right-aligned paragraphs.</summary>
    internal const ushort PFA_RIGHT = 2;

    /// <summary>Centered paragraphs.</summary>
    internal const ushort PFA_CENTER = 3;

    /// <summary>Bulleted paragraphs (<see cref="PARAFORMAT2.wNumbering"/>).</summary>
    internal const ushort PFN_BULLET = 1;

    // --- Event mask bits and notification codes ---

    /// <summary>Send <c>EN_CHANGE</c> notifications (off by default on rich edits, unlike plain EDITs).</summary>
    internal const uint ENM_CHANGE = 0x00000001;

    /// <summary>Send <c>EN_LINK</c> notifications for mouse activity over detected links.</summary>
    internal const uint ENM_LINK = 0x04000000;

    /// <summary>A mouse/keyboard event happened over a detected link (via <see cref="WM_NOTIFY"/>, lParam → <see cref="ENLINK"/>).</summary>
    internal const int EN_LINK = 0x070B;

    // --- Stream formats ---

    /// <summary>The stream carries RTF.</summary>
    internal const nint SF_RTF = 0x0002;

    /// <summary>The common header every <see cref="WM_NOTIFY"/> notification starts with.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NMHDR
    {
        /// <summary>The HWND of the control sending the notification.</summary>
        public nint hwndFrom;

        /// <summary>The sending control's identifier (the HMENU control id).</summary>
        public nuint idFrom;

        /// <summary>The notification code (for example <see cref="EN_LINK"/>).</summary>
        public uint code;
    }

    /// <summary>A half-open character range (<c>cpMax</c> exclusive), rich edit's selection unit.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CHARRANGE
    {
        /// <summary>The first character of the range.</summary>
        public int cpMin;

        /// <summary>One past the last character of the range.</summary>
        public int cpMax;
    }

    /// <summary>The <c>EN_LINK</c> notification payload: which mouse message hit which character range.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ENLINK
    {
        /// <summary>The common notification header.</summary>
        public NMHDR nmhdr;

        /// <summary>The mouse/keyboard message that triggered the notification (for example <c>WM_LBUTTONUP</c>).</summary>
        public uint msg;

        /// <summary>The message's original wParam.</summary>
        public nint wParam;

        /// <summary>The message's original lParam.</summary>
        public nint lParam;

        /// <summary>The character range of the link the event happened over.</summary>
        public CHARRANGE chrg;
    }

    /// <summary>Names a character range and receives its text (<see cref="EM_GETTEXTRANGE"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TEXTRANGE
    {
        /// <summary>The range to copy.</summary>
        public CHARRANGE chrg;

        /// <summary>Caller-provided buffer receiving the NUL-terminated text.</summary>
        public nint lpstrText;
    }

    /// <summary>Drives <see cref="EM_STREAMIN"/>/<see cref="EM_STREAMOUT"/> through a callback.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct EDITSTREAM
    {
        /// <summary>Opaque caller state handed to every callback invocation (here: a <c>GCHandle</c>).</summary>
        public nint dwCookie;

        /// <summary>Set non-zero by the control or the callback to abort the stream.</summary>
        public uint dwError;

        /// <summary>The <c>EDITSTREAMCALLBACK</c> function pointer.</summary>
        public nint pfnCallback;
    }

    /// <summary>
    /// Character formatting for <see cref="EM_SETCHARFORMAT"/>. Only the fields whose
    /// <see cref="dwMask"/> bit is set participate; the rest are ignored by the control.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct CHARFORMAT2W
    {
        /// <summary>Size of this structure, in bytes; identifies the CHARFORMAT2 revision.</summary>
        public uint cbSize;

        /// <summary>Which fields/effects participate (<c>CFM_*</c>).</summary>
        public uint dwMask;

        /// <summary>The active effects (<c>CFE_*</c>, same bit values as the masks).</summary>
        public uint dwEffects;

        /// <summary>Character height in twips (1/20 point).</summary>
        public int yHeight;

        /// <summary>Baseline offset in twips (super/subscript).</summary>
        public int yOffset;

        /// <summary>Text color as COLORREF (0x00BBGGRR).</summary>
        public uint crTextColor;

        /// <summary>Character set of the font.</summary>
        public byte bCharSet;

        /// <summary>Pitch and family of the font.</summary>
        public byte bPitchAndFamily;

        /// <summary>Face name of the font (LF_FACESIZE = 32).</summary>
        public fixed char szFaceName[32];

        /// <summary>Font weight (LOGFONT values).</summary>
        public ushort wWeight;

        /// <summary>Horizontal spacing between letters, in twips.</summary>
        public short sSpacing;

        /// <summary>Background color as COLORREF.</summary>
        public uint crBackColor;

        /// <summary>Locale identifier.</summary>
        public uint lcid;

        /// <summary>Reserved / client cookie.</summary>
        public uint dwReserved;

        /// <summary>Character style handle (used with style sheets).</summary>
        public short sStyle;

        /// <summary>Kerning threshold in twips.</summary>
        public ushort wKerning;

        /// <summary>Underline kind (solid, dotted, …).</summary>
        public byte bUnderlineType;

        /// <summary>Text animation kind.</summary>
        public byte bAnimation;

        /// <summary>Revision author index.</summary>
        public byte bRevAuthor;

        /// <summary>Underline color index.</summary>
        public byte bUnderlineColor;
    }

    /// <summary>
    /// Paragraph formatting for <see cref="EM_SETPARAFORMAT"/>. Only the fields whose
    /// <see cref="dwMask"/> bit is set participate.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PARAFORMAT2
    {
        /// <summary>Size of this structure, in bytes; identifies the PARAFORMAT2 revision.</summary>
        public uint cbSize;

        /// <summary>Which fields participate (<c>PFM_*</c>).</summary>
        public uint dwMask;

        /// <summary>Numbering kind (<see cref="PFN_BULLET"/> for bullets, 0 for none).</summary>
        public ushort wNumbering;

        /// <summary>Effect flags (PARAFORMAT2) / reserved (PARAFORMAT).</summary>
        public ushort wEffects;

        /// <summary>Indentation of the first line, in twips.</summary>
        public int dxStartIndent;

        /// <summary>Indentation from the right margin, in twips.</summary>
        public int dxRightIndent;

        /// <summary>Indentation of lines after the first, relative to <see cref="dxStartIndent"/>, in twips.</summary>
        public int dxOffset;

        /// <summary>Paragraph alignment (<c>PFA_*</c>).</summary>
        public ushort wAlignment;

        /// <summary>Number of entries used in <see cref="rgxTabs"/>.</summary>
        public short cTabCount;

        /// <summary>Tab stop positions, in twips (MAX_TAB_STOPS = 32).</summary>
        public fixed int rgxTabs[32];

        /// <summary>Space before the paragraph, in twips.</summary>
        public int dySpaceBefore;

        /// <summary>Space after the paragraph, in twips.</summary>
        public int dySpaceAfter;

        /// <summary>Line spacing, interpreted per <see cref="bLineSpacingRule"/>.</summary>
        public int dyLineSpacing;

        /// <summary>Paragraph style handle (used with style sheets).</summary>
        public short sStyle;

        /// <summary>How <see cref="dyLineSpacing"/> is interpreted.</summary>
        public byte bLineSpacingRule;

        /// <summary>Outline level of the paragraph.</summary>
        public byte bOutlineLevel;

        /// <summary>Shading in hundredths of a percent.</summary>
        public ushort wShadingWeight;

        /// <summary>Shading style nibbles.</summary>
        public ushort wShadingStyle;

        /// <summary>Starting number/letter of numbered paragraphs.</summary>
        public ushort wNumberingStart;

        /// <summary>Numbering style (parentheses, period, …).</summary>
        public ushort wNumberingStyle;

        /// <summary>Distance between the number/bullet and the text, in twips.</summary>
        public ushort wNumberingTab;

        /// <summary>Space between the border and the text, in twips.</summary>
        public ushort wBorderSpace;

        /// <summary>Border pen width, in twips.</summary>
        public ushort wBorderWidth;

        /// <summary>Which borders are drawn and their style.</summary>
        public ushort wBorders;
    }

    /// <summary>Loads a DLL into the process (used once to pull in msftedit.dll's window class).</summary>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint LoadLibraryW(string lpLibFileName);
}
