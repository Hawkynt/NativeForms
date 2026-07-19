using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Backends;

/// <summary>
/// The native side of a single <see cref="Control"/>. A peer owns one platform widget (an HWND, a
/// GtkWidget*, an NSView …) and exposes only the operations the core needs to keep that widget in
/// sync with the managed control. All coordinates are in the parent's client space, top-left origin,
/// pixels — exactly like Windows Forms.
/// </summary>
public interface IControlPeer : IDisposable
{
    /// <summary>Positions and sizes the widget within its parent.</summary>
    void SetBounds(Rectangle bounds);

    /// <summary>Sets the caption/label text of the widget.</summary>
    void SetText(string text);

    /// <summary>Shows or hides the widget without removing it from the tree.</summary>
    void SetVisible(bool visible);

    /// <summary>Enables or greys out the widget for user interaction.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Maps a point in this widget's client space to screen coordinates.</summary>
    Point PointToScreen(Point clientPoint);
}

/// <summary>
/// The native side of a control that hosts other controls: a window's client area or an owner-drawn
/// surface. Mirrors Windows Forms, where any control is a potential parent — realizing a control
/// tree walks it depth-first, handing each child peer to its container.
/// </summary>
public interface IContainerPeer : IControlPeer
{
    /// <summary>Re-parents a child peer into this container's client area, creating its native widget.</summary>
    void AddChild(IControlPeer child);
}

/// <summary>A top-level window peer — the native side of a <see cref="Form"/>.</summary>
public interface IWindowPeer : IContainerPeer
{
    /// <summary>Makes the window visible and ready to receive input.</summary>
    void Show();

    /// <summary>
    /// Shows the window modally: input to <paramref name="owner"/> (when given) is blocked and a
    /// nested native message loop runs until the window closes. On close the window is hidden — not
    /// destroyed — so the caller still owns the peer and disposes it normally. Blocks until closed.
    /// </summary>
    void RunModal(IWindowPeer? owner);

    /// <summary>
    /// Closes the window as the native close button would: <see cref="Closed"/> is raised, and a
    /// modal loop started by <see cref="RunModal"/> ends.
    /// </summary>
    void Close();

    /// <summary>
    /// Applies the window frame. Win32 toggles the style bits (<c>WS_THICKFRAME</c>,
    /// <c>WS_CAPTION</c>, <c>WS_EX_TOOLWINDOW</c> …) on the live HWND and refreshes the frame; GTK
    /// maps to <c>gtk_window_set_resizable</c>/<c>set_decorated</c>/<c>set_type_hint</c>, where the
    /// window manager decides the final look.
    /// </summary>
    void SetBorderStyle(FormBorderStyle borderStyle);

    /// <summary>
    /// Minimizes, maximizes or restores the window. Before <see cref="Show"/> the wish is buffered
    /// and honored by the initial show; the resulting native state change is reported back through
    /// <see cref="WindowStateChanged"/>.
    /// </summary>
    void SetWindowState(FormWindowState state);

    /// <summary>
    /// Whether the caption shows a minimize button. Win32 toggles <c>WS_MINIMIZEBOX</c>; GTK cannot
    /// toggle individual caption buttons, so the value is advisory there — the peer folds it into the
    /// window's type hint (both boxes off reads as a dialog) and the window manager decides.
    /// </summary>
    void SetMinimizeBox(bool visible);

    /// <summary>
    /// Whether the caption shows a maximize button. Win32 toggles <c>WS_MAXIMIZEBOX</c>; GTK treats
    /// it as advisory exactly like <see cref="SetMinimizeBox"/>.
    /// </summary>
    void SetMaximizeBox(bool visible);

    /// <summary>
    /// Clamps user resizing to the given limits; an empty size (or a zero component) means
    /// unconstrained on that axis. Win32 answers <c>WM_GETMINMAXINFO</c> with the values; GTK
    /// forwards them as geometry hints.
    /// </summary>
    void SetSizeLimits(Size minimum, Size maximum);

    /// <summary>
    /// Replaces the window's caption/taskbar icon from 32-bit ARGB pixels (row-major, length =
    /// width * height) — the same decoder-free pipeline as <see cref="INotifyIconPeer.SetIcon"/>.
    /// </summary>
    void SetIcon(int width, int height, ReadOnlySpan<int> argb);

    /// <summary>Keeps the window above all normal windows.</summary>
    void SetTopMost(bool topMost);

    /// <summary>
    /// Sets the window's overall opacity, 0 (invisible) … 1 (opaque). Win32 uses a layered window
    /// (<c>WS_EX_LAYERED</c> + <c>SetLayeredWindowAttributes</c>); GTK uses
    /// <c>gtk_widget_set_opacity</c>, which needs a compositing window manager to show through.
    /// </summary>
    void SetOpacity(double opacity);

    /// <summary>Raised when the user closes the window (native close button, Alt+F4, ⌘Q …).</summary>
    event EventHandler? Closed;

    /// <summary>
    /// Raised when the native window is moved or resized (by the user or the window manager),
    /// carrying the new bounds in this platform's window coordinates. The core adopts them without
    /// echoing a <see cref="IControlPeer.SetBounds"/> back.
    /// </summary>
    event EventHandler<Rectangle>? BoundsChangedByUser;

    /// <summary>
    /// Raised when the native window is minimized, maximized or restored — from <c>WM_SIZE</c>'s
    /// state word on Win32, from the <c>window-state-event</c> signal on GTK.
    /// </summary>
    event EventHandler<FormWindowState>? WindowStateChanged;
}

/// <summary>A push-button peer — the native side of a <see cref="Button"/>.</summary>
public interface IButtonPeer : IControlPeer
{
    /// <summary>Raised when the button is activated (click, Space, Enter).</summary>
    event EventHandler? Clicked;

    /// <summary>
    /// Shows <paramref name="image"/> on the button face, or clears it for <see langword="null"/>.
    /// Each backend maps the triple as honestly as its toolkit allows: GTK renders image and text side
    /// by side and honors <paramref name="relation"/> through the button's image position; Win32
    /// attaches the bitmap via <c>BM_SETIMAGE</c> and renders it alone (<c>BS_BITMAP</c>) while the
    /// caption is empty — image and text together need themed common controls, and neither backend
    /// renders <paramref name="imageAlign"/>, which is forwarded as advisory state.
    /// </summary>
    void SetImage(IImage? image, ContentAlignment imageAlign, TextImageRelation relation);
}

/// <summary>A static text peer — the native side of a <see cref="Label"/>.</summary>
public interface ILabelPeer : IControlPeer
{
    /// <summary>
    /// Aligns the text within the label's bounds. Win32 static controls honor the horizontal component
    /// plus a coarse vertical centering only (<c>SS_CENTERIMAGE</c>); GTK maps to the label's
    /// x/y-alignment and honors all nine anchors.
    /// </summary>
    void SetTextAlign(ContentAlignment alignment);

    /// <summary>
    /// Draws (or removes) a single flat border around the label. Win32 applies the <c>WS_BORDER</c>
    /// style bit; GTK has no native frame on a label, so the value is not rendered there.
    /// </summary>
    void SetBorderStyle(BorderStyle borderStyle);

    /// <summary>
    /// Whether <c>&amp;</c> in the text marks the following character as a mnemonic and renders it
    /// underlined (<c>&amp;&amp;</c> escapes a literal ampersand).
    /// </summary>
    void SetUseMnemonic(bool useMnemonic);

    /// <summary>
    /// Shows <paramref name="image"/> in the label, or clears it for <see langword="null"/>. No
    /// toolkit renders image and text in one static widget, so backends render the image natively only
    /// while the caption is empty (Win32 <c>SS_BITMAP</c> static, GTK swaps in a <c>GtkImage</c>); a
    /// captioned label keeps its text and the image stays pending. <paramref name="imageAlign"/> is
    /// forwarded as advisory state — the native image-only renderings ignore it.
    /// </summary>
    void SetImage(IImage? image, ContentAlignment imageAlign);
}

/// <summary>
/// A text-input peer — the native side of a <see cref="TextBox"/>. Beyond the base state it carries
/// the edit-specific settings (multiline, placeholder, password masking, read-only, max length,
/// selection) and reports the live text and selection back from the widget.
/// </summary>
/// <remarks>
/// <see cref="SetMultiline"/> may recreate the native widget — some toolkits use different widgets
/// (or creation-time style bits) for single-line and multiline editing. Peers buffer their state, so
/// tearing the widget down and re-flushing the buffer into a fresh one is legal and invisible to the
/// core.
/// </remarks>
public interface ITextBoxPeer : IControlPeer
{
    /// <summary>Switches between a single-line entry and a multiline editor (may recreate the widget).</summary>
    void SetMultiline(bool multiline);

    /// <summary>Sets the greyed hint shown while the box is empty (single-line only on most platforms).</summary>
    void SetPlaceholder(string placeholder);

    /// <summary>Masks the displayed text with the given character; <c>'\0'</c> turns masking off.</summary>
    void SetPasswordChar(char passwordChar);

    /// <summary>Makes the text selectable and copyable but not editable.</summary>
    void SetReadOnly(bool readOnly);

    /// <summary>Caps the number of characters the user can type; 0 means unlimited.</summary>
    void SetMaxLength(int maxLength);

    /// <summary>Selects <paramref name="length"/> characters starting at <paramref name="start"/> (length 0 places the caret).</summary>
    void SetSelection(int start, int length);

    /// <summary>Returns the current selection as a start/length pair (length 0 = a bare caret).</summary>
    (int Start, int Length) GetSelection();

    /// <summary>Returns the live text held by the widget, including edits the user has made.</summary>
    string GetText();

    /// <summary>Raised whenever the widget's text changes, including edits typed by the user.</summary>
    event EventHandler? TextChangedByUser;
}

/// <summary>
/// A rich-text-editor peer — the native side of a <see cref="RichTextBox"/> (a Win32
/// <c>RICHEDIT50W</c>, a <c>GtkTextView</c> with text tags). On top of the plain text-box surface it
/// applies character and paragraph formatting to the widget's <em>current selection</em> and moves
/// whole documents in and out as RTF.
/// </summary>
/// <remarks>
/// The selection-formatting calls are commands, not state: they act on whatever the widget has
/// selected at the moment of the call and are not buffered — the owning control only issues them
/// once realized. RTF travels through the core's <see cref="Text.RtfSerializer"/> subset where the
/// platform has no native RTF engine.
/// </remarks>
public interface IRichTextBoxPeer : ITextBoxPeer
{
    /// <summary>Turns the given style flags (bold/italic/underline/strikeout) on or off over the current selection.</summary>
    void SetSelectionStyle(FontStyle style, bool enabled);

    /// <summary>Sets the text color of the current selection; <see cref="Color.Empty"/> restores the default.</summary>
    void SetSelectionColor(Color color);

    /// <summary>Sets the font size, in points, of the current selection.</summary>
    void SetSelectionFontSize(float sizeInPoints);

    /// <summary>
    /// Aligns the paragraphs the current selection touches. Only the horizontal component
    /// (left/center/right) is meaningful; the vertical component is ignored.
    /// </summary>
    void SetSelectionAlignment(ContentAlignment alignment);

    /// <summary>Turns bullet-list rendering on or off for the paragraphs the current selection touches.</summary>
    void SetSelectionBullet(bool bullet);

    /// <summary>Whether URLs in the text are detected, rendered as links and reported via <see cref="LinkClicked"/>.</summary>
    void SetDetectUrls(bool detectUrls);

    /// <summary>Scales the rendered text by the given factor (1.0 = normal size) without touching the document.</summary>
    void SetZoom(float factor);

    /// <summary>Returns the whole document as RTF (the core subset, or the platform's native writer where one exists).</summary>
    string GetRtf();

    /// <summary>Replaces the whole document from RTF; the resulting plain text is reported through
    /// <see cref="ITextBoxPeer.TextChangedByUser"/> like any other widget-side change.</summary>
    void SetRtf(string rtf);

    /// <summary>Raised when the user activates a detected link; the argument is the link's text (the URL).</summary>
    event EventHandler<string>? LinkClicked;
}

/// <summary>
/// A system tray/status-area icon — the native side of a <see cref="NotifyIcon"/>. The icon pixels
/// arrive as raw 32-bit ARGB so the peer can build whatever handle its shell wants (an
/// <c>HICON</c> on Windows); state setters are buffer-friendly and may arrive before the icon is
/// shown for the first time.
/// </summary>
public interface INotifyIconPeer : IDisposable
{
    /// <summary>Replaces the icon from 32-bit ARGB pixels (row-major, length = width * height).</summary>
    void SetIcon(int width, int height, ReadOnlySpan<int> argb);

    /// <summary>Sets the hover text the shell shows next to the icon.</summary>
    void SetToolTip(string text);

    /// <summary>Adds the icon to or removes it from the tray.</summary>
    void SetVisible(bool visible);

    /// <summary>Raised when the user clicks the icon with the primary button.</summary>
    event EventHandler? Click;

    /// <summary>Raised when the user double-clicks the icon with the primary button.</summary>
    event EventHandler? DoubleClick;
}

/// <summary>
/// A recurring UI-thread timer source — the native side of a <see cref="Timer"/>. Ticks are delivered
/// by the platform message loop, so they always arrive on the thread that pumps it. Starting a peer
/// that is already running restarts it with the new interval.
/// </summary>
public interface ITimerPeer : IDisposable
{
    /// <summary>Begins (or restarts) periodic ticking every <paramref name="intervalMs"/> milliseconds.</summary>
    void Start(int intervalMs);

    /// <summary>Stops ticking. The peer stays usable and can be started again.</summary>
    void Stop();

    /// <summary>Raised on the UI thread once per elapsed interval.</summary>
    event EventHandler? Tick;
}
