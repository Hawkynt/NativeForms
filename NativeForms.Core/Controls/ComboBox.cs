using System.Collections;
using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.ComponentModel;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// A drop-down selector: an owner-drawn field in the native theme whose list opens as a light-dismiss
/// popup (<see cref="IPopupPeer"/>) below the field. Rows are painted by the same renderer as
/// <see cref="ListBox"/> rows — icons, hover highlight, theme selection colors — so the drop-down is
/// pixel-identical to a list. <see cref="ComboBoxStyle.DropDownList"/> keeps the field closed and
/// owner-painted; <see cref="ComboBoxStyle.DropDown"/> hosts a native <see cref="TextBox"/> over the
/// field area for free-text editing. Items are arbitrary objects; text, icon and value come from
/// reflection-free selector delegates, so binding stays trim/AOT-safe.
/// </summary>
public class ComboBox : OwnerDrawnControl
{
    private int _selectedIndex = -1;
    private bool _droppedDown;
    private bool _focused;

    private IPopupPeer? _popup;
    private int _hoverIndex = -1;
    private int _popupTopIndex;
    private int _popupVisibleRows;
    private Size _popupSize;

    private TextBox? _editor;

    /// <summary>Creates a combo box in the closed, non-editable <see cref="ComboBoxStyle.DropDownList"/> style.</summary>
    public ComboBox()
    {
        this.Items = new();
        this.Items.ListChanged += this.OnItemsListChanged;
    }

    /// <summary>The items offered by the drop-down. Mutating this collection repaints the control.</summary>
    public ObservableList<object?> Items { get; }

    /// <summary>Produces the display text for an item. Defaults to <c>ToString()</c>.</summary>
    public Func<object?, string> DisplaySelector
    {
        get => field;
        set
        {
            field = value ?? (static item => item?.ToString() ?? string.Empty);
            this.Invalidate();
        }
    } = static item => item?.ToString() ?? string.Empty;

    /// <summary>Optional selector producing an icon for an item; <see langword="null"/> for none.</summary>
    public Func<object?, IImage?>? ImageSelector { get; set; }

    /// <summary>The icon store <see cref="ImageIndexSelector"/> indexes into, or <see langword="null"/> for none.</summary>
    public ImageList? ImageList { get; set; }

    /// <summary>Optional selector mapping an item to its <see cref="ImageList"/> index; a negative
    /// index means no icon. <see cref="ImageSelector"/> wins when both are set.</summary>
    public Func<object?, int>? ImageIndexSelector { get; set; }

    /// <summary>Optional selector producing the binding value of an item, the reflection-free stand-in
    /// for <c>ValueMember</c>; <see langword="null"/> makes the item its own value.</summary>
    public Func<object?, object?>? ValueSelector { get; set; }

    /// <summary>
    /// How the field presents itself: closed and owner-painted (<see cref="ComboBoxStyle.DropDownList"/>,
    /// the default) or editable through a hosted native <see cref="TextBox"/>
    /// (<see cref="ComboBoxStyle.DropDown"/>).
    /// </summary>
    /// <exception cref="NotSupportedException"><see cref="ComboBoxStyle.Simple"/> is not implemented yet.</exception>
    public ComboBoxStyle DropDownStyle
    {
        get => field;
        set
        {
            if (field == value)
                return;

            if (value == ComboBoxStyle.Simple)
                throw new NotSupportedException("ComboBoxStyle.Simple is not implemented yet.");

            field = value;
            if (value == ComboBoxStyle.DropDown)
                this.CreateEditor();
            else
                this.RemoveEditor();

            this.Invalidate();
        }
    } = ComboBoxStyle.DropDownList;

    /// <summary>The greyed hint shown while nothing is selected (closed style) or the editor is empty.</summary>
    public string PlaceholderText
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            if (_editor is not null)
                _editor.PlaceholderText = value;

            this.Invalidate();
        }
    } = string.Empty;

    /// <summary>The maximum number of rows the drop-down shows before it scrolls. Defaults to 8.</summary>
    public int MaxDropDownItems
    {
        get => field;
        set => field = Math.Max(1, value);
    } = 8;

    /// <summary>The selected item's index, or -1 for none. Setting it repaints and raises
    /// <see cref="SelectedIndexChanged"/> when the value actually changes.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
            if (_selectedIndex == clamped)
                return;

            _selectedIndex = clamped;
            this.PushSelectionIntoEditor();
            this.Invalidate();
            this.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>The selected item, or <see langword="null"/> for none.</summary>
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 ? this.Items[_selectedIndex] : null;
        set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
    }

    /// <summary>
    /// The selected item's binding value — <see cref="ValueSelector"/> applied to
    /// <see cref="SelectedItem"/>, or the item itself without a selector. Assigning selects the first
    /// item whose value <see cref="object.Equals(object?, object?)"/> the given one (none clears the
    /// selection), closing the classic <c>ValueMember</c>/<c>SelectedValue</c> loop without reflection.
    /// </summary>
    public object? SelectedValue
    {
        get
        {
            var item = this.SelectedItem;
            return _selectedIndex < 0 ? null : this.ValueSelector is { } selector ? selector(item) : item;
        }
        set
        {
            var selector = this.ValueSelector;
            for (var i = 0; i < this.Items.Count; ++i)
            {
                var item = this.Items[i];
                if (Equals(selector is null ? item : selector(item), value))
                {
                    this.SelectedIndex = i;
                    return;
                }
            }

            this.SelectedIndex = -1;
        }
    }

    /// <summary>Replaces the items from a snapshot of any sequence (one-way binding convenience).</summary>
    public IEnumerable? DataSource
    {
        set
        {
            this.Items.Clear();
            if (value is null)
                return;

            foreach (var item in value)
                this.Items.Add(item);
        }
    }

    /// <summary>Whether the drop-down list is currently open. Settable, like its WinForms namesake.</summary>
    public bool DroppedDown
    {
        get => _droppedDown;
        set
        {
            if (value)
                this.OpenDropDown();
            else
                this.CloseDropDown();
        }
    }

    /// <summary>
    /// The visible text. In the editable style this mirrors the hosted editor; in the closed style it
    /// is the selected item's display text, and assigning selects the item with that text.
    /// </summary>
    public override string Text
    {
        get => _editor?.Text ?? (_selectedIndex >= 0 ? this.DisplaySelector(this.Items[_selectedIndex]) : string.Empty);
        set
        {
            value ??= string.Empty;
            var editor = _editor;
            if (editor is not null)
            {
                editor.Text = value;
                return;
            }

            for (var i = 0; i < this.Items.Count; ++i)
                if (this.DisplaySelector(this.Items[i]) == value)
                {
                    this.SelectedIndex = i;
                    return;
                }
        }
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes, by user gesture or assignment.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised when the drop-down list opens.</summary>
    public event EventHandler? DropDown;

    /// <summary>Raised when the drop-down list closes — by commit, cancel or light dismissal alike.</summary>
    public event EventHandler? DropDownClosed;

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>An open list claims Enter (commit) and Escape (close) ahead of the form's dialog keys.</summary>
    protected override bool IsInputKey(Keys keyData)
        => this.DroppedDown && keyData is Keys.Enter or Keys.Escape;

    /// <summary>The width of the arrow-button zone at the right edge of the field.</summary>
    private int ButtonWidth => this.Theme.ScrollBarSize + 1;

    /// <summary>Raises <see cref="SelectedIndexChanged"/>.</summary>
    protected virtual void OnSelectedIndexChanged(EventArgs e) => this.SelectedIndexChanged?.Invoke(this, e);

    /// <summary>Raises <see cref="DropDown"/>.</summary>
    protected virtual void OnDropDown(EventArgs e) => this.DropDown?.Invoke(this, e);

    /// <summary>Raises <see cref="DropDownClosed"/>.</summary>
    protected virtual void OnDropDownClosed(EventArgs e) => this.DropDownClosed?.Invoke(this, e);

    private protected override void OnRealized(IControlPeer peer)
    {
        base.OnRealized(peer);
        if (_editor is { } editor)
            this.SyncEditorBounds(editor);
    }

    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _droppedDown = false;
        _popup?.Dispose();
        _popup = null;
    }

    // --- The closed field --------------------------------------------------------------------------

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var width = this.Width;
        var height = this.Height;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, width, height));

        var buttonWidth = this.ButtonWidth;
        if (_editor is null) // the editable style shows its content through the hosted editor instead
        {
            var fieldRect = new Rectangle(0, 0, width - buttonWidth, height);
            if (_selectedIndex >= 0)
            {
                var item = this.Items[_selectedIndex];
                ListBox.DrawRowContent(g, theme, fieldRect, this.DisplaySelector(item), this.IconOf(item), false);
            }
            else if (this.PlaceholderText.Length > 0)
                g.DrawText(this.PlaceholderText, theme.DefaultFont, theme.DisabledText, new Rectangle(fieldRect.X + 2, fieldRect.Y, fieldRect.Width - 2, fieldRect.Height), ContentAlignment.MiddleLeft);
        }

        // The drop-down arrow, centered in the button zone.
        var arrowColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        GlyphRenderer.DrawComboArrow(g, arrowColor, new Rectangle(width - buttonWidth, 0, buttonWidth, height));

        g.DrawRectangle(_focused ? theme.Accent : theme.Border, new Rectangle(0, 0, width - 1, height - 1));
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        this.Focus();
        if (e.Button != MouseButtons.Left)
            return;

        if (_droppedDown)
            this.CloseDropDown();
        else
            this.OpenDropDown();
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F4 || (e.KeyCode == Keys.Down && e.Alt))
        {
            if (_droppedDown && e.KeyCode == Keys.F4)
                this.CloseDropDown();
            else
                this.OpenDropDown();

            e.Handled = true;
            return;
        }

        if (_droppedDown)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    this.CloseDropDown();
                    e.Handled = true;
                    break;

                case Keys.Enter:
                    if (_hoverIndex >= 0 && _hoverIndex < this.Items.Count)
                        this.CommitAndClose(_hoverIndex);
                    else
                        this.CloseDropDown();

                    e.Handled = true;
                    break;

                case Keys.Down:
                    this.MoveHover(+1);
                    e.Handled = true;
                    break;

                case Keys.Up:
                    this.MoveHover(-1);
                    e.Handled = true;
                    break;
            }

            return;
        }

        var count = this.Items.Count;
        if (count == 0)
            return;

        switch (e.KeyCode)
        {
            case Keys.Down: // closed arrows move the selection directly, like the classic control
                this.SelectedIndex = Math.Min(count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;

            case Keys.Up:
                this.SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || this.Items.Count == 0)
            return;

        var match = this.FindPrefixMatch(e.KeyChar, _droppedDown ? _hoverIndex : _selectedIndex);
        if (match < 0)
            return;

        e.Handled = true;
        if (!_droppedDown)
        {
            this.SelectedIndex = match;
            return;
        }

        _hoverIndex = match;
        this.EnsurePopupVisible(match);
        _popup?.InvalidateAll();
    }

    /// <inheritdoc/>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _focused = true;
        this.Invalidate();
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _focused = false;
        this.CloseDropDown();
        this.Invalidate();
    }

    // --- The drop-down popup -----------------------------------------------------------------------

    /// <summary>
    /// Opens the drop-down below the field: field width, one row per item up to
    /// <see cref="MaxDropDownItems"/>, hover starting on the selected item. A no-op while already
    /// open or before the control is realized (only a live widget knows its screen position).
    /// </summary>
    public void OpenDropDown()
    {
        if (_droppedDown)
            return;

        var backend = this.Backend;
        if (backend is null)
            return;

        var popup = _popup ??= this.CreatePopup(backend);
        _popupVisibleRows = Math.Max(1, Math.Min(this.Items.Count, this.MaxDropDownItems));
        _popupSize = new Size(this.Width, _popupVisibleRows * this.Theme.RowHeight);
        _hoverIndex = _selectedIndex;
        _popupTopIndex = 0;
        this.EnsurePopupVisible(_hoverIndex);
        _droppedDown = true;
        popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), _popupSize);
        this.Invalidate();
        this.OnDropDown(EventArgs.Empty);
    }

    /// <summary>Closes the drop-down without changing the selection. A no-op while closed.</summary>
    public void CloseDropDown()
    {
        if (!_droppedDown)
            return;

        _droppedDown = false;
        _popup?.Hide();
        this.Invalidate();
        this.OnDropDownClosed(EventArgs.Empty);
    }

    private IPopupPeer CreatePopup(IPlatformBackend backend)
    {
        var popup = backend.CreatePopup();
        popup.Paint += (_, e) => this.OnPopupPaint(e);
        popup.MouseMove += (_, e) => this.OnPopupMouseMove(e);
        popup.MouseDown += (_, e) => this.OnPopupMouseDown(e);
        popup.MouseWheel += (_, e) => this.OnPopupMouseWheel(e);
        popup.KeyDown += (_, e) => this.OnKeyDown(e); // backends with a keyboard grab route keys here
        popup.KeyPress += (_, e) => this.OnKeyPress(e);
        popup.Dismissed += (_, _) => this.OnPopupDismissed();
        return popup;
    }

    /// <summary>Paints the popup's item list exactly like <see cref="ListBox"/> rows, with the hovered
    /// row in the theme selection colors.</summary>
    private void OnPopupPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var size = _popupSize;
        g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, size.Width, size.Height));

        var rowHeight = theme.RowHeight;
        var last = Math.Min(this.Items.Count, _popupTopIndex + _popupVisibleRows);
        for (var i = _popupTopIndex; i < last; ++i)
        {
            var rowRect = new Rectangle(0, (i - _popupTopIndex) * rowHeight, size.Width, rowHeight);
            var hovered = i == _hoverIndex;
            if (hovered)
                GlyphRenderer.FillSelection(g, theme, rowRect);

            var item = this.Items[i];
            ListBox.DrawRowContent(g, theme, rowRect, this.DisplaySelector(item), this.IconOf(item), hovered);
        }

        g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
    }

    private void OnPopupMouseMove(MouseEventArgs e)
    {
        if (e.Y < 0)
            return;

        var row = _popupTopIndex + (e.Y / this.Theme.RowHeight);
        if (row >= this.Items.Count || row == _hoverIndex)
            return;

        _hoverIndex = row;
        _popup?.InvalidateAll();
    }

    private void OnPopupMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Y < 0)
            return;

        var row = _popupTopIndex + (e.Y / this.Theme.RowHeight);
        if (row >= this.Items.Count)
            return;

        this.CommitAndClose(row);
    }

    private void OnPopupMouseWheel(MouseEventArgs e)
    {
        var maxTop = Math.Max(0, this.Items.Count - _popupVisibleRows);
        var top = Math.Clamp(_popupTopIndex - Math.Sign(e.Delta) * 3, 0, maxTop);
        if (top == _popupTopIndex)
            return;

        _popupTopIndex = top;
        _popup?.InvalidateAll();
    }

    /// <summary>Reacts to light dismissal (click outside, grab loss, Escape): the surface is already
    /// hidden, so only the open flag and the field's arrow state need resetting.</summary>
    private void OnPopupDismissed()
    {
        if (!_droppedDown)
            return;

        _droppedDown = false;
        this.Invalidate();
        this.OnDropDownClosed(EventArgs.Empty);
    }

    /// <summary>Commits the given row as the selection and closes the drop-down.</summary>
    private void CommitAndClose(int index)
    {
        this.CloseDropDown();
        this.SelectedIndex = index;
    }

    /// <summary>Moves the hover row by <paramref name="delta"/>, clamped, scrolling it into view.</summary>
    private void MoveHover(int delta)
    {
        var count = this.Items.Count;
        if (count == 0)
            return;

        var target = Math.Clamp(_hoverIndex + delta, 0, count - 1);
        if (target == _hoverIndex)
            return;

        _hoverIndex = target;
        this.EnsurePopupVisible(target);
        _popup?.InvalidateAll();
    }

    /// <summary>Scrolls the popup so the given row is visible.</summary>
    private void EnsurePopupVisible(int index)
    {
        if (index < 0)
            return;

        if (index < _popupTopIndex)
            _popupTopIndex = index;
        else if (index >= _popupTopIndex + _popupVisibleRows)
            _popupTopIndex = index - _popupVisibleRows + 1;

        _popupTopIndex = Math.Clamp(_popupTopIndex, 0, Math.Max(0, this.Items.Count - _popupVisibleRows));
    }

    /// <summary>Finds the next item after <paramref name="after"/> (wrapping) whose display text
    /// starts with <paramref name="prefix"/>, case-insensitively; -1 for no match.</summary>
    private int FindPrefixMatch(char prefix, int after)
    {
        var count = this.Items.Count;
        var upper = char.ToUpperInvariant(prefix);
        for (var step = 1; step <= count; ++step)
        {
            var i = (after + step + count) % count;
            var text = this.DisplaySelector(this.Items[i]);
            if (text.Length > 0 && char.ToUpperInvariant(text[0]) == upper)
                return i;
        }

        return -1;
    }

    // --- Items & editor plumbing -------------------------------------------------------------------

    /// <summary>Keeps the single selection pointing at the same item across item mutations: shifted by
    /// inserts/removes before it, cleared (with one event) when the selected item vanishes.</summary>
    private void OnItemsListChanged(object? sender, ListChangedEventArgs e)
    {
        var changed = false;
        switch (e.ChangeType)
        {
            case ListChangeType.Added:
                if (_selectedIndex >= e.Index)
                    ++_selectedIndex;
                break;

            case ListChangeType.Removed:
                if (_selectedIndex == e.Index)
                {
                    _selectedIndex = -1;
                    changed = true;
                }
                else if (_selectedIndex > e.Index)
                    --_selectedIndex;

                break;

            case ListChangeType.Reset:
                if (_selectedIndex >= this.Items.Count)
                {
                    _selectedIndex = -1;
                    changed = true;
                }

                break;
        }

        if (_droppedDown)
        {
            _hoverIndex = Math.Min(_hoverIndex, this.Items.Count - 1);
            _popupTopIndex = Math.Clamp(_popupTopIndex, 0, Math.Max(0, this.Items.Count - _popupVisibleRows));
            _popup?.InvalidateAll();
        }

        this.Invalidate();
        if (changed)
            this.OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>The icon for an item: <see cref="ImageSelector"/> first, then
    /// <see cref="ImageList"/> + <see cref="ImageIndexSelector"/> (materialized lazily).</summary>
    private IImage? IconOf(object? item)
    {
        var direct = this.ImageSelector?.Invoke(item);
        if (direct is not null)
            return direct;

        var images = this.ImageList;
        var selector = this.ImageIndexSelector;
        var backend = this.Backend;
        if (images is null || selector is null || backend is null)
            return null;

        var index = selector(item);
        return index >= 0 && index < images.Count ? images.GetImage(index, backend) : null;
    }

    /// <summary>Creates the hosted editor of the editable style and mirrors its text into
    /// <see cref="Text"/>; the nested-realization machinery realizes it onto the canvas.</summary>
    private void CreateEditor()
    {
        var editor = new TextBox { PlaceholderText = this.PlaceholderText, TabStop = false };
        this.SyncEditorBounds(editor);
        if (_selectedIndex >= 0)
            editor.Text = this.DisplaySelector(this.Items[_selectedIndex]);

        editor.TextChanged += this.OnEditorTextChanged;
        _editor = editor;
        this.Controls.Add(editor);
    }

    private void RemoveEditor()
    {
        var editor = _editor;
        if (editor is null)
            return;

        editor.TextChanged -= this.OnEditorTextChanged;
        _editor = null;
        this.Controls.Remove(editor);
    }

    /// <summary>Lays the editor over the field area, leaving the arrow-button zone free.</summary>
    private void SyncEditorBounds(TextBox editor)
        => editor.Bounds = new Rectangle(0, 0, Math.Max(0, this.Width - this.ButtonWidth), this.Height);

    private void OnEditorTextChanged(object? sender, EventArgs e) => this.OnTextChanged(EventArgs.Empty);

    /// <summary>Pushes the selected item's display text into the hosted editor, if any.</summary>
    private void PushSelectionIntoEditor()
    {
        if (_editor is { } editor && _selectedIndex >= 0)
            editor.Text = this.DisplaySelector(this.Items[_selectedIndex]);
    }
}
