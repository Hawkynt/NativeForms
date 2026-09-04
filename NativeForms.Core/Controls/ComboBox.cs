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
public class ComboBox : OwnerDrawnControl {
  /// <inheritdoc/>
  private protected override AccessibleRole DefaultAccessibleRole => AccessibleRole.ComboBox;

  private int _selectedIndex = -1;
  private bool _droppedDown;
  private bool _focused;

  private IPopupPeer? _popup;

  /// <summary>The hovered <em>row</em> of the open drop-down, which is the item index only while the
  /// list is unfiltered; <see cref="ItemIndexAt"/> is what turns one into the other.</summary>
  private int _hoverRow = -1;
  private int _popupTopIndex;
  private int _popupVisibleRows;
  private Size _popupSize;

  /// <summary>
  /// The item indices a suggestion filter has left showing, or <see langword="null"/> while the list
  /// is showing everything. Never empty: no match closes the drop-down rather than opening an empty
  /// box under the field.
  /// </summary>
  private List<int>? _matches;

  private TextBox? _editor;

  /// <summary>Guards the editor write that inline completion makes, so it is not read back as typing.</summary>
  private bool _autoCompleting;

  /// <summary>
  /// What the user has actually typed, without anything completion filled in. A deletion is told from
  /// an insertion by comparing against <em>this</em> rather than against what the field shows:
  /// typing over a selected completion makes the field's text shorter, so a field-length comparison
  /// would read every second keystroke as a backspace and stop completing.
  /// </summary>
  private string _typedText = string.Empty;

  /// <summary>Creates a combo box in the closed, non-editable <see cref="ComboBoxStyle.DropDownList"/> style.</summary>
  public ComboBox() {
    this.Items = new();
    this.Items.ListChanged += this.OnItemsListChanged;
  }

  /// <summary>An unset <see cref="Control.BackColor"/> resolves to the theme's field background.</summary>
  private protected override Color FallbackBackColor => this.Theme.FieldBackground;

  /// <summary>The items offered by the drop-down. Mutating this collection repaints the control.</summary>
  public ObservableList<object?> Items { get; }

  /// <summary>Produces the display text for an item. Defaults to <c>ToString()</c>.</summary>
  public Func<object?, string> DisplaySelector {
    get => field;
    set {
      field = value ?? (static item => item?.ToString() ?? string.Empty);
      this.Invalidate();
    }
  } = static item => item?.ToString() ?? string.Empty;

  /// <summary>Optional selector producing an icon for an item; <see langword="null"/> for none.</summary>
  public Func<object?, IImage?>? ImageSelector {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      this.ReconsiderPromotion(); // per-item icons are what a stock combo cannot show
      this.Invalidate();
    }
  }

  /// <summary>The icon store <see cref="ImageIndexSelector"/> indexes into, or <see langword="null"/> for none.</summary>
  public ImageList? ImageList {
    get => field;
    set {
      if (ReferenceEquals(field, value))
        return;

      this.BindImageListAnimation(field, value);
      field = value;
      this.ReconsiderPromotion();
      this.Invalidate();
    }
  }

  /// <summary>Optional selector mapping an item to its <see cref="ImageList"/> index; a negative
  /// index means no icon. <see cref="ImageSelector"/> wins when both are set.</summary>
  public Func<object?, int>? ImageIndexSelector {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      this.ReconsiderPromotion();
      this.Invalidate();
    }
  }

  /// <summary>Optional selector producing the binding value of an item, the reflection-free stand-in
  /// for <c>ValueMember</c>; <see langword="null"/> makes the item its own value.</summary>
  public Func<object?, object?>? ValueSelector { get; set; }

  /// <summary>
  /// How the field presents itself: closed and owner-painted (<see cref="ComboBoxStyle.DropDownList"/>,
  /// the default) or editable through a hosted native <see cref="TextBox"/>
  /// (<see cref="ComboBoxStyle.DropDown"/>).
  /// </summary>
  /// <exception cref="NotSupportedException"><see cref="ComboBoxStyle.Simple"/> is not implemented yet.</exception>
  public ComboBoxStyle DropDownStyle {
    get => field;
    set {
      if (field == value)
        return;

      if (value == ComboBoxStyle.Simple)
        throw new NotSupportedException("ComboBoxStyle.Simple is not implemented yet.");

      field = value;
      if (value == ComboBoxStyle.DropDown)
        this.CreateEditor();
      else
        this.RemoveEditor();

      this.ReconsiderPromotion();
      this.Invalidate();
    }
  } = ComboBoxStyle.DropDownList;

  /// <summary>The greyed hint shown while nothing is selected (closed style) or the editor is empty.</summary>
  public string PlaceholderText {
    get => field;
    set {
      value ??= string.Empty;
      if (field == value)
        return;

      field = value;
      if (_editor is not null)
        _editor.PlaceholderText = value;

      this.ReconsiderPromotion();
      this.Invalidate();
    }
  } = string.Empty;

  /// <summary>
  /// How the editable style completes what is typed against the items: not at all (the default),
  /// by filling the rest of the first match into the field (<see cref="AutoCompleteMode.Append"/>),
  /// by narrowing the drop-down to the matches (<see cref="AutoCompleteMode.Suggest"/>), or both.
  /// </summary>
  /// <remarks>
  /// The candidates are the combo's own items — Windows Forms' <c>AutoCompleteSource.ListItems</c> —
  /// because committing a suggestion sets <see cref="SelectedIndex"/>, and a candidate from anywhere
  /// else has no index to commit. A <see cref="ComboBoxStyle.DropDownList"/> combo ignores this: with
  /// no editor there is nothing to complete, and typing already cycles through the matching items.
  /// </remarks>
  public AutoCompleteMode AutoCompleteMode {
    get => field;
    set {
      if (field == value)
        return;

      field = value;
      if (value is AutoCompleteMode.None or AutoCompleteMode.Append)
        this.ClearSuggestions(); // whatever a previous Suggest left showing is no longer meant
    }
  }

  /// <summary>The maximum number of rows the drop-down shows before it scrolls. Defaults to 8.</summary>
  public int MaxDropDownItems {
    get => field;
    set => field = Math.Max(1, value);
  } = 8;

  /// <summary>The selected item's index, or -1 for none. Setting it repaints and raises
  /// <see cref="SelectedIndexChanged"/> when the value actually changes.</summary>
  public int SelectedIndex {
    get => _selectedIndex;
    set {
      var clamped = value < -1 || value >= this.Items.Count ? -1 : value;
      if (_selectedIndex == clamped)
        return;

      _selectedIndex = clamped;
      _native?.SetSelectedIndex(clamped);
      this.PushSelectionIntoEditor();
      this.Invalidate();
      this.OnSelectedIndexChanged(EventArgs.Empty);
    }
  }

  /// <summary>The selected item, or <see langword="null"/> for none.</summary>
  public object? SelectedItem {
    get => _selectedIndex >= 0 ? this.Items[_selectedIndex] : null;
    set => this.SelectedIndex = value is null ? -1 : this.Items.IndexOf(value);
  }

  /// <summary>
  /// The selected item's binding value — <see cref="ValueSelector"/> applied to
  /// <see cref="SelectedItem"/>, or the item itself without a selector. Assigning selects the first
  /// item whose value <see cref="object.Equals(object?, object?)"/> the given one (none clears the
  /// selection), closing the classic <c>ValueMember</c>/<c>SelectedValue</c> loop without reflection.
  /// </summary>
  public object? SelectedValue {
    get {
      var item = this.SelectedItem;
      return _selectedIndex < 0 ? null : this.ValueSelector is { } selector ? selector(item) : item;
    }
    set {
      var selector = this.ValueSelector;
      for (var i = 0; i < this.Items.Count; ++i) {
        var item = this.Items[i];
        if (Equals(selector is null ? item : selector(item), value)) {
          this.SelectedIndex = i;
          return;
        }
      }

      this.SelectedIndex = -1;
    }
  }


  /// <summary>
  /// Replaces the items from a sequence and resolves <paramref name="displayMember"/> and
  /// <paramref name="valueMember"/> to accessors at compile time, so the Windows Forms shape —
  /// a data source plus member <em>names</em> — works without reflection.
  /// </summary>
  /// <remarks>
  /// The names go through the lookup the <c>[Bindable]</c> generator emitted on <typeparamref name="T"/>,
  /// so they are ordinary property reads by the time anything runs and survive trimming and NativeAOT.
  /// A name the type does not have throws here, at the call, rather than yielding blank rows later.
  /// Passing <see langword="null"/> for a member leaves the corresponding selector alone, so this
  /// composes with <see cref="DisplaySelector"/> and <see cref="ValueSelector"/> rather than replacing
  /// them.
  /// </remarks>
  /// <typeparam name="T">The item type, which must carry <c>[Bindable]</c>.</typeparam>
  /// <param name="items">The items to show.</param>
  /// <param name="displayMember">The property whose value is displayed, or <see langword="null"/>.</param>
  /// <param name="valueMember">The property behind <see cref="SelectedValue"/>, or <see langword="null"/>.</param>
  /// <exception cref="ArgumentException">A named member is not a public readable property of <typeparamref name="T"/>.</exception>
  public void SetDataSource<T>(IEnumerable<T> items, string? displayMember = null, string? valueMember = null)
      where T : IBindableMembers {
    ArgumentNullException.ThrowIfNull(items);

    if (displayMember is not null) {
      var accessor = BindableMembers.Require<T>(displayMember, nameof(displayMember));
      this.DisplaySelector = item => accessor(item)?.ToString() ?? string.Empty;
    }

    if (valueMember is not null) {
      var accessor = BindableMembers.Require<T>(valueMember, nameof(valueMember));
      this.ValueSelector = item => accessor(item);
    }

    this.Items.Clear();
    foreach (var item in items)
      this.Items.Add(item);
  }

  /// <summary>Replaces the items from a snapshot of any sequence (one-way binding convenience).</summary>
  public IEnumerable? DataSource {
    set {
      this.Items.Clear();
      if (value is null)
        return;

      foreach (var item in value)
        this.Items.Add(item);
    }
  }

  /// <summary>Whether the drop-down list is currently open. Settable, like its WinForms namesake.</summary>
  public bool DroppedDown {
    get => _droppedDown;
    set {
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
  public override string Text {
    get => _editor?.Text ?? (_selectedIndex >= 0 ? this.DisplaySelector(this.Items[_selectedIndex]) : string.Empty);
    set {
      value ??= string.Empty;
      var editor = _editor;
      if (editor is not null) {
        editor.Text = value;
        return;
      }

      for (var i = 0; i < this.Items.Count; ++i)
        if (this.DisplaySelector(this.Items[i]) == value) {
          this.SelectedIndex = i;
          return;
        }
    }
  }

  private IComboBoxPeer? _native;
  private bool? _nativeOffered;


  /// <summary>Whether this combo is currently rendered by a real platform widget.</summary>
  public override bool IsNativeWidget => _native is not null;

  /// <summary>
  /// Whether the current property values are all expressible by a platform drop-down list. A stock
  /// combo shows a flat list of strings and nothing else, so per-item images and a placeholder rule it
  /// out — and the editable style hosts a real <see cref="TextBox"/> child, which a native combo has
  /// nowhere to put.
  /// </summary>
  private bool IsNativeEligible
      => this.DropDownStyle == ComboBoxStyle.DropDownList
      && this.PlaceholderText.Length == 0
      && this.ImageSelector is null
      && this.ImageIndexSelector is null
      && this.ImageList is null;

  /// <summary>What <see cref="IsNativeWidget"/> would be if the peer were built right now.</summary>
  private bool WouldBeNative
      => (this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible && (_nativeOffered ?? true);

  /// <inheritdoc/>
  private protected override IControlPeer CreatePeer(IPlatformBackend backend) {
    if ((this.UseNativeWidget ?? Application.PreferNativeWidgets) && this.IsNativeEligible) {
      var offered = backend.CreateComboBox();
      _nativeOffered = offered is not null;
      if (offered is { } peer) {
        _native = peer;
        this.PushNativeItems(peer);
        peer.SelectionChanged += this.OnNativeSelectionChanged;
        peer.DropDownOpened += this.OnNativeDropDownOpened;
        peer.DropDownClosed += this.OnNativeDropDownClosed;
        return peer;
      }
    }

    return base.CreatePeer(backend);
  }

  /// <summary>Re-realizes the control when a property change crossed the eligibility line.</summary>
  private void ReconsiderPromotion() {
    if (this.IsNativeWidget != this.WouldBeNative)
      this.RerealizePeer();
  }

  /// <summary>Renders every item through <see cref="DisplaySelector"/> and hands the list over whole.</summary>
  private void PushNativeItems(IComboBoxPeer peer) {
    var count = this.Items.Count;
    var texts = count == 0 ? [] : new string[count];
    for (var i = 0; i < count; ++i)
      texts[i] = this.DisplaySelector(this.Items[i]) ?? string.Empty;

    peer.SetItems(texts, _selectedIndex);
  }

  /// <summary>The widget's selection moved; mirror it, which raises the public event exactly once.</summary>
  private void OnNativeSelectionChanged(object? sender, EventArgs e) {
    if (_native is { } peer)
      this.SelectedIndex = peer.GetSelectedIndex();
  }

  /// <summary>The widget opened its own list; flip the flag and raise the event the popup path raises.</summary>
  private void OnNativeDropDownOpened(object? sender, EventArgs e) {
    if (_droppedDown)
      return;

    _droppedDown = true;
    this.OnDropDown(EventArgs.Empty);
  }

  /// <summary>The widget closed its own list.</summary>
  private void OnNativeDropDownClosed(object? sender, EventArgs e) {
    if (!_droppedDown)
      return;

    _droppedDown = false;
    this.OnDropDownClosed(EventArgs.Empty);
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

  private protected override void OnRealized(IControlPeer peer) {
    base.OnRealized(peer);
    if (_editor is { } editor)
      this.SyncEditorBounds(editor);
  }

  private protected override void OnUnrealized() {
    if (_native is { } peer) {
      peer.SelectionChanged -= this.OnNativeSelectionChanged;
      peer.DropDownOpened -= this.OnNativeDropDownOpened;
      peer.DropDownClosed -= this.OnNativeDropDownClosed;
      _native = null;
    }

    base.OnUnrealized();
    _droppedDown = false;
    _popup?.Dispose();
    _popup = null;
  }

  // --- The closed field --------------------------------------------------------------------------

  /// <inheritdoc/>
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics;
    var theme = this.Theme;
    var width = this.Width;
    var height = this.Height;
    g.FillRectangle(this.BackColor, new Rectangle(0, 0, width, height));

    var buttonWidth = this.ButtonWidth;
    if (_editor is null) // the editable style shows its content through the hosted editor instead
    {
      var fieldRect = new Rectangle(0, 0, width - buttonWidth, height);
      if (_selectedIndex >= 0) {
        var item = this.Items[_selectedIndex];
        ListBox.DrawRowContent(g, theme, fieldRect, this.DisplaySelector(item), this.IconOf(item), false);
      } else if (this.PlaceholderText.Length > 0)
        g.DrawText(this.PlaceholderText, this.Font, theme.DisabledText, new Rectangle(fieldRect.X + 2, fieldRect.Y, fieldRect.Width - 2, fieldRect.Height), ContentAlignment.MiddleLeft);
    }

    // The drop-down arrow, centered in the button zone.
    var arrowColor = this.Enabled ? this.ForeColor : theme.DisabledText;
    GlyphRenderer.DrawComboArrow(g, arrowColor, new Rectangle(width - buttonWidth, 0, buttonWidth, height));

    g.DrawRectangle(_focused ? theme.Accent : theme.Border, new Rectangle(0, 0, width - 1, height - 1));
  }

  /// <inheritdoc/>
  protected override void OnMouseDown(MouseEventArgs e) {
    this.Focus();
    if (e.Button != MouseButtons.Left)
      return;

    if (_droppedDown)
      this.CloseDropDown();
    else
      this.OpenDropDown();
  }

  /// <inheritdoc/>
  protected override void OnKeyDown(KeyEventArgs e) {
    if (e.KeyCode == Keys.F4 || (e.KeyCode == Keys.Down && e.Alt)) {
      if (_droppedDown && e.KeyCode == Keys.F4)
        this.CloseDropDown();
      else
        this.OpenDropDown();

      e.Handled = true;
      return;
    }

    if (_droppedDown) {
      switch (e.KeyCode) {
        case Keys.Escape:
          this.CloseDropDown();
          e.Handled = true;
          break;

        case Keys.Enter:
          if (_hoverRow >= 0 && _hoverRow < this.RowCount)
            this.CommitAndClose(this.ItemIndexAt(_hoverRow));
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

    switch (e.KeyCode) {
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
  protected override void OnKeyPress(KeyPressEventArgs e) {
    if (char.IsControl(e.KeyChar) || this.Items.Count == 0)
      return;

    var match = this.FindPrefixMatch(e.KeyChar, _droppedDown ? this.ItemIndexAt(_hoverRow) : _selectedIndex);
    if (match < 0)
      return;

    e.Handled = true;
    if (!_droppedDown) {
      this.SelectedIndex = match;
      return;
    }

    _hoverRow = this.RowOf(match);
    this.EnsurePopupVisible(_hoverRow);
    _popup?.InvalidateAll();
  }

  /// <inheritdoc/>
  protected override void OnGotFocus(EventArgs e) {
    base.OnGotFocus(e);
    _focused = true;
    this.Invalidate();
  }

  /// <inheritdoc/>
  protected override void OnLostFocus(EventArgs e) {
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
  public void OpenDropDown() {
    if (_droppedDown)
      return;

    // A promoted combo has no popup of its own: the widget owns the list, and reports back through
    // DropDownOpened, which is what flips the flag and raises the event.
    if (_native is { } native) {
      native.SetDroppedDown(true);
      return;
    }

    var backend = this.Backend;
    if (backend is null)
      return;

    var popup = _popup ??= this.CreatePopup(backend);
    _popupVisibleRows = Math.Max(1, Math.Min(this.RowCount, this.MaxDropDownItems));
    _popupSize = new Size(this.Width, _popupVisibleRows * this.Theme.RowHeight);
    _hoverRow = this.RowOf(_selectedIndex);
    _popupTopIndex = 0;
    this.EnsurePopupVisible(_hoverRow);
    _droppedDown = true;
    this.OwnsOpenPopup = true;
    popup.ShowAt(this.PointToScreen(new Point(0, this.Height)), _popupSize);
    this.Invalidate();
    this.OnDropDown(EventArgs.Empty);
  }

  /// <summary>Closes the drop-down without changing the selection. A no-op while closed.</summary>
  public void CloseDropDown() {
    if (!_droppedDown)
      return;

    if (_native is { } native) {
      native.SetDroppedDown(false);
      return;
    }

    _droppedDown = false;
    this.OwnsOpenPopup = false;
    _popup?.Hide();
    this.Invalidate();
    this.OnDropDownClosed(EventArgs.Empty);
  }

  private IPopupPeer CreatePopup(IPlatformBackend backend) {
    var popup = backend.CreatePopup(this.OwnerWindowPeer);
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
  private void OnPopupPaint(PaintEventArgs e) {
    var g = e.Graphics;
    var theme = this.Theme;
    var size = _popupSize;
    g.FillRectangle(this.BackColor, new Rectangle(0, 0, size.Width, size.Height));

    var rowHeight = theme.RowHeight;
    var last = Math.Min(this.RowCount, _popupTopIndex + _popupVisibleRows);
    for (var row = _popupTopIndex; row < last; ++row) {
      var rowRect = new Rectangle(0, (row - _popupTopIndex) * rowHeight, size.Width, rowHeight);
      var hovered = row == _hoverRow;
      if (hovered)
        GlyphRenderer.FillSelection(g, theme, rowRect);

      var item = this.Items[this.ItemIndexAt(row)];
      ListBox.DrawRowContent(g, theme, rowRect, this.DisplaySelector(item), this.IconOf(item), hovered);
    }

    g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
  }

  private void OnPopupMouseMove(MouseEventArgs e) {
    if (e.Y < 0)
      return;

    var row = _popupTopIndex + (e.Y / this.Theme.RowHeight);
    if (row >= this.RowCount || row == _hoverRow)
      return;

    _hoverRow = row;
    _popup?.InvalidateAll();
  }

  private void OnPopupMouseDown(MouseEventArgs e) {
    if (e.Button != MouseButtons.Left || e.Y < 0)
      return;

    var row = _popupTopIndex + (e.Y / this.Theme.RowHeight);
    if (row >= this.RowCount)
      return;

    this.CommitAndClose(this.ItemIndexAt(row));
  }

  private void OnPopupMouseWheel(MouseEventArgs e) {
    var maxTop = Math.Max(0, this.RowCount - _popupVisibleRows);
    var top = Math.Clamp(_popupTopIndex - Math.Sign(e.Delta) * 3, 0, maxTop);
    if (top == _popupTopIndex)
      return;

    _popupTopIndex = top;
    _popup?.InvalidateAll();
  }

  /// <summary>Reacts to light dismissal (click outside, grab loss, Escape): the surface is already
  /// hidden, so only the open flag and the field's arrow state need resetting.</summary>
  private void OnPopupDismissed() {
    if (!_droppedDown)
      return;

    _droppedDown = false;
    this.OwnsOpenPopup = false;
    this.Invalidate();
    this.OnDropDownClosed(EventArgs.Empty);
  }

  /// <summary>Commits the given item as the selection and closes the drop-down.</summary>
  private void CommitAndClose(int index) {
    this.CloseDropDown();
    _matches = null; // the filter belongs to the edit that is now finished
    this.SelectedIndex = index;
  }

  /// <summary>Moves the hover row by <paramref name="delta"/>, clamped, scrolling it into view.</summary>
  private void MoveHover(int delta) {
    var count = this.RowCount;
    if (count == 0)
      return;

    var target = Math.Clamp(_hoverRow + delta, 0, count - 1);
    if (target == _hoverRow)
      return;

    _hoverRow = target;
    this.EnsurePopupVisible(target);
    _popup?.InvalidateAll();
  }

  /// <summary>Scrolls the popup so the given row is visible.</summary>
  private void EnsurePopupVisible(int index) {
    if (index < 0)
      return;

    if (index < _popupTopIndex)
      _popupTopIndex = index;
    else if (index >= _popupTopIndex + _popupVisibleRows)
      _popupTopIndex = index - _popupVisibleRows + 1;

    _popupTopIndex = Math.Clamp(_popupTopIndex, 0, Math.Max(0, this.RowCount - _popupVisibleRows));
  }

  // --- The suggestion filter ---------------------------------------------------------------------

  /// <summary>How many rows the drop-down has: every item, or only the ones a filter left.</summary>
  private int RowCount => _matches?.Count ?? this.Items.Count;

  /// <summary>The item a drop-down row stands for; the two are the same only unfiltered.</summary>
  private int ItemIndexAt(int row)
      => _matches is { } matches
          ? row >= 0 && row < matches.Count ? matches[row] : -1
          : row;

  /// <summary>The row an item occupies, or -1 while a filter is hiding it.</summary>
  private int RowOf(int itemIndex)
      => _matches is { } matches ? matches.IndexOf(itemIndex) : itemIndex;

  /// <summary>Drops any suggestion filter, so the next open shows the whole list again.</summary>
  private void ClearSuggestions() {
    if (_matches is null)
      return;

    _matches = null;
    _hoverRow = this.RowOf(_selectedIndex);
    _popupTopIndex = 0;
    if (_droppedDown)
      this.ResizeDropDown();
  }

  /// <summary>
  /// Re-fits an open drop-down to the rows it now has, in place. Resized rather than re-shown, which
  /// on a backend with a pointer grab would hand the grab round and dismiss the popup mid-edit —
  /// the same reason the filterable menu resizes.
  /// </summary>
  private void ResizeDropDown() {
    if (_popup is not { } popup)
      return;

    _popupVisibleRows = Math.Max(1, Math.Min(this.RowCount, this.MaxDropDownItems));
    _popupSize = new Size(this.Width, _popupVisibleRows * this.Theme.RowHeight);
    this.EnsurePopupVisible(_hoverRow);
    popup.Resize(_popupSize);
    popup.InvalidateAll();
  }

  /// <summary>
  /// Completes what has just been typed into the hosted editor: fills in the rest of the first match
  /// (<see cref="AutoCompleteMode.Append"/>) and narrows the drop-down to the matches
  /// (<see cref="AutoCompleteMode.Suggest"/>).
  /// </summary>
  /// <param name="typed">What the editor now holds.</param>
  /// <param name="deleting">Whether the edit shortened the text, which never completes.</param>
  private void ApplyAutoComplete(string typed, bool deleting) {
    var mode = this.AutoCompleteMode;
    if (mode is AutoCompleteMode.None || _editor is not { } editor)
      return;

    if (typed.Length == 0) {
      // An empty field matches everything, which is no filter at all rather than every row.
      this.ClearSuggestions();
      if (_droppedDown && mode.HasFlag(AutoCompleteMode.Suggest))
        this.CloseDropDown();

      return;
    }

    if (mode.HasFlag(AutoCompleteMode.Suggest)) {
      var matches = this.CollectMatches(typed);
      if (matches.Count == 0) {
        _matches = null;
        if (_droppedDown)
          this.CloseDropDown();
      } else {
        _matches = matches;
        _hoverRow = -1; // nothing is chosen yet: the first arrow key picks the first match
        _popupTopIndex = 0;
        if (_droppedDown)
          this.ResizeDropDown();
        else
          this.OpenDropDown();
      }
    }

    if (deleting || !mode.HasFlag(AutoCompleteMode.Append))
      return;

    var match = this.FindCompletion(typed);
    if (match < 0)
      return;

    var completion = this.DisplaySelector(this.Items[match]);
    if (completion.Length <= typed.Length)
      return; // an exact match has nothing left to fill in

    _autoCompleting = true;
    try {
      editor.Text = completion;
      editor.Select(typed.Length, completion.Length - typed.Length);
    } finally {
      _autoCompleting = false;
    }

  }

  /// <summary>The indices of the items whose display text starts with <paramref name="prefix"/>.</summary>
  private List<int> CollectMatches(string prefix) {
    var matches = new List<int>();
    for (var i = 0; i < this.Items.Count; ++i)
      if (this.DisplaySelector(this.Items[i]).StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
        matches.Add(i);

    return matches;
  }

  /// <summary>The first item whose display text starts with <paramref name="prefix"/>; -1 for none.</summary>
  private int FindCompletion(string prefix) {
    for (var i = 0; i < this.Items.Count; ++i)
      if (this.DisplaySelector(this.Items[i]).StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
        return i;

    return -1;
  }

  /// <summary>Finds the next item after <paramref name="after"/> (wrapping) whose display text
  /// starts with <paramref name="prefix"/>, case-insensitively; -1 for no match.</summary>
  private int FindPrefixMatch(char prefix, int after) {
    var count = this.Items.Count;
    var upper = char.ToUpperInvariant(prefix);
    for (var step = 1; step <= count; ++step) {
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
  private void OnItemsListChanged(object? sender, ListChangedEventArgs e) {
    var changed = false;
    switch (e.ChangeType) {
      case ListChangeType.Added:
        if (_selectedIndex >= e.Index)
          ++_selectedIndex;
        break;

      case ListChangeType.Removed:
        if (_selectedIndex == e.Index) {
          _selectedIndex = -1;
          changed = true;
        } else if (_selectedIndex > e.Index)
          --_selectedIndex;

        break;

      case ListChangeType.Reset:
        if (_selectedIndex >= this.Items.Count) {
          _selectedIndex = -1;
          changed = true;
        }

        break;
    }

    if (_droppedDown) {
      _hoverRow = Math.Min(_hoverRow, this.RowCount - 1);
      _popupTopIndex = Math.Clamp(_popupTopIndex, 0, Math.Max(0, this.RowCount - _popupVisibleRows));
      _popup?.InvalidateAll();
    }

    // The widget holds its own copy of the list, so any structural change re-sends it whole; the item
    // counts are small and this keeps one code path instead of an incremental mirror.
    if (_native is { } peer)
      this.PushNativeItems(peer);

    this.Invalidate();
    if (changed)
      this.OnSelectedIndexChanged(EventArgs.Empty);
  }

  /// <summary>The icon for an item: <see cref="ImageSelector"/> first, then
  /// <see cref="ImageList"/> + <see cref="ImageIndexSelector"/> (materialized lazily).</summary>
  private IImage? IconOf(object? item) {
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
  private void CreateEditor() {
    var editor = new TextBox { PlaceholderText = this.PlaceholderText, TabStop = false };
    this.SyncEditorBounds(editor);
    if (_selectedIndex >= 0)
      editor.Text = this.DisplaySelector(this.Items[_selectedIndex]);

    editor.TextChanged += this.OnEditorTextChanged;
    _editor = editor;
    this.Controls.Add(editor);
  }

  private void RemoveEditor() {
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

  /// <summary>
  /// The hosted editor's text moved. Completion runs first so the public
  /// <see cref="Control.TextChanged"/> reports the finished text once, rather than once for what was
  /// typed and again for what was filled in.
  /// </summary>
  private void OnEditorTextChanged(object? sender, EventArgs e) {
    if (_autoCompleting)
      return; // completion's own write; the call that started it reports the result

    var typed = _editor?.Text ?? string.Empty;
    var deleting = typed.Length <= _typedText.Length;
    _typedText = typed;

    this.ApplyAutoComplete(typed, deleting);
    this.OnTextChanged(EventArgs.Empty);
  }

  /// <summary>Pushes the selected item's display text into the hosted editor, if any.</summary>
  private void PushSelectionIntoEditor() {
    if (_editor is not { } editor || _selectedIndex < 0)
      return;

    // Not an edit: a selection pushed into the field must not be completed against the items or
    // re-open the drop-down that committing it just closed.
    _autoCompleting = true;
    try {
      editor.Text = this.DisplaySelector(this.Items[_selectedIndex]);
    } finally {
      _autoCompleting = false;
    }

    _typedText = editor.Text;
  }
}
