using System.Drawing;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>Where a group is being shown: the ribbon itself or one of the popups that carry it.</summary>
internal enum RibbonSurface : byte {
  /// <summary>The expanded ribbon's own group area.</summary>
  Ribbon,

  /// <summary>The minimized ribbon's tab-click flyout.</summary>
  TabFlyout,

  /// <summary>The popup a collapsed group opens into.</summary>
  GroupFlyout,
}

/// <summary>
/// The ribbon's fields (<see cref="RibbonComboBox"/>, <see cref="RibbonSpinner"/>) and the collapsed
/// group's flyout they have to keep working in. A collapsed group opens its own layout in a popup — the
/// way Office does it — rather than a menu, because a field has no menu-row form; every surface then
/// paints and hit-tests the same group code, so a field behaves alike on all three.
/// </summary>
public partial class Ribbon {
  /// <summary>Width of the chevron column at the right of a combo box's field.</summary>
  private const int _ComboChevronWidth = 16;

  /// <summary>Width of the up/down arrow column at the right of a spinner's field.</summary>
  private const int _SpinButtonWidth = 14;

  /// <summary>Horizontal inset of a field's text inside its frame.</summary>
  private const int _FieldTextInset = 3;

  /// <summary>The most rows a combo box's list shows before it scrolls.</summary>
  private const int _MaxListRows = 8;

  /// <summary>
  /// Everything the fields and the collapsed group's flyout need while one is in use, created on first
  /// use so a ribbon that never shows either pays one null reference for all of it.
  /// </summary>
  private sealed class FieldState {
    public IPopupPeer? GroupPopup;
    public IPopupPeer? GroupPopupParent;
    public RibbonGroup? Group;
    public Point GroupLocation;
    public Size GroupSize;
    public int GroupHot = -1;

    public IPopupPeer? ListPopup;
    public IPopupPeer? ListPopupParent;
    public RibbonComboBox? ListCombo;
    public Point ListLocation;
    public Size ListSize;
    public int ListHover;
    public int ListTop;
    public int ListRows;

    /// <summary>The field the keyboard talks to, the surface it sits on and its box there.</summary>
    public RibbonFieldItem? Active;
    public RibbonSurface ActiveSurface;
    public Rectangle ActiveBox;

    /// <summary>The spinner entry being typed, or <see langword="null"/> while not editing.</summary>
    public string? Edit;

    /// <summary>Whether the next keystroke replaces the whole entry — true right after editing begins.</summary>
    public bool EditReplace;

    /// <summary>Set while a popup is being shown, so the grab moving to it is not read as a dismissal.</summary>
    public bool SuppressDismiss;
  }

  private FieldState? _fieldState;

  private FieldState Fields => _fieldState ??= new();

  // --- Geometry -----------------------------------------------------------------------------------

  /// <summary>A field item's width: icon, caption, the field box and the padding around them.</summary>
  private int FieldItemWidth(RibbonFieldItem field, Font font) {
    var width = _ItemPadding + field.FieldWidth + _ItemPadding;
    if (field.HasIcon)
      width += _SmallIconSize + _ItemPadding;

    var text = field.TextWidth(this.Backend, font);
    return text > 0 ? width + text + _ItemPadding : width;
  }

  /// <summary>The framed value box of a field laid out in <paramref name="item"/> — right-aligned, so the
  /// boxes of a column line up whatever their captions.</summary>
  private static Rectangle FieldBox(RibbonFieldItem field, Rectangle item)
      => new(item.Right - _ItemPadding - field.FieldWidth, item.Y + 1, field.FieldWidth, Math.Max(0, item.Height - 2));

  /// <summary>The up/down arrow column of a spinner's box.</summary>
  private static Rectangle SpinButtons(Rectangle box)
      => new(box.Right - _SpinButtonWidth, box.Y, _SpinButtonWidth, box.Height);

  /// <summary>The screen point a surface's client origin sits at.</summary>
  private Point SurfaceOrigin(RibbonSurface surface) => surface switch {
    RibbonSurface.TabFlyout => this.PointToScreen(new Point(0, this.TabStripHeight)),
    RibbonSurface.GroupFlyout => _fieldState?.GroupLocation ?? Point.Empty,
    _ => this.PointToScreen(Point.Empty),
  };

  /// <summary>The popup a surface is, shown or not, or <see langword="null"/> for the ribbon itself.</summary>
  private IPopupPeer? SurfacePopup(RibbonSurface surface) => surface switch {
    RibbonSurface.TabFlyout => _flyoutPopup,
    RibbonSurface.GroupFlyout => _fieldState?.GroupPopup,
    _ => null,
  };

  /// <summary>Whether <paramref name="popup"/> is one of the flyouts and currently up.</summary>
  private bool IsFlyoutShown(IPopupPeer? popup)
      => popup is not null
         && ((_flyoutShown && ReferenceEquals(popup, _flyoutPopup))
             || (_fieldState is { Group: not null } state && ReferenceEquals(popup, state.GroupPopup)));

  /// <summary>Repaints a surface.</summary>
  private void InvalidateSurface(RibbonSurface surface) {
    if (surface == RibbonSurface.Ribbon)
      this.Invalidate();
    else
      this.SurfacePopup(surface)?.InvalidateAll();
  }

  /// <summary>Repaints whichever flyouts are up, after an item changed underneath them.</summary>
  private void InvalidateOpenFlyouts() {
    if (_flyoutShown)
      _flyoutPopup?.InvalidateAll();

    if (_fieldState is { Group: not null } state)
      state.GroupPopup?.InvalidateAll();
  }

  /// <summary>Whether any of the ribbon's popups is up, which is what guards the grab's spurious focus
  /// loss — see <see cref="Control.OwnsOpenPopup"/>.</summary>
  private void SyncOwnsOpenPopup()
      => this.OwnsOpenPopup = _flyoutShown || _gridButton is not null || _fieldState is { Group: not null } or { ListCombo: not null };

  // --- Painting -----------------------------------------------------------------------------------

  /// <summary>Paints a field: icon and caption on the left, the framed value box on the right — a
  /// chevron for a combo box, an arrow pair for a spinner — and, while a spinner is being typed into,
  /// the entry with its caret or its replace-on-type highlight.</summary>
  private void PaintField(IGraphics g, ITheme theme, RibbonFieldItem field, Rectangle rect, bool hovered, RibbonSurface surface) {
    var enabled = field.Enabled;
    var textColor = enabled ? theme.ControlText : theme.DisabledText;
    var font = theme.DefaultFont;
    var box = FieldBox(field, rect);

    var x = rect.X + _ItemPadding;
    if (field.ResolveImage(this.Backend) is { } icon) {
      g.DrawImage(icon, new Rectangle(x, rect.Y + ((rect.Height - _SmallIconSize) / 2), _SmallIconSize, _SmallIconSize));
      x += _SmallIconSize + _ItemPadding;
    }

    if (field.DisplayText.Length > 0)
      g.DrawText(field.DisplayText, font, textColor, new Rectangle(x, rect.Y, Math.Max(0, box.X - _ItemPadding - x), rect.Height), ContentAlignment.MiddleLeft);

    if (box.Width <= 0 || box.Height <= 0)
      return;

    var state = _fieldState;
    var active = state is not null && ReferenceEquals(state.Active, field) && state.ActiveSurface == surface;
    g.FillRectangle(enabled ? theme.FieldBackground : theme.ControlBackground, box);
    g.DrawRectangle(enabled && (active || hovered) ? theme.Accent : theme.Border, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1));

    var buttonWidth = field is RibbonSpinner ? _SpinButtonWidth : _ComboChevronWidth;
    var textRect = new Rectangle(box.X + _FieldTextInset, box.Y, Math.Max(0, box.Width - buttonWidth - (2 * _FieldTextInset)), box.Height);
    var editing = active && state!.Edit is not null && field is RibbonSpinner;
    if (editing)
      this.PaintFieldEntry(g, theme, font, state!, textRect);
    else
      g.DrawText(field.FieldText, font, textColor, textRect, ContentAlignment.MiddleLeft);

    if (field is RibbonSpinner) {
      var buttons = SpinButtons(box);
      g.DrawLine(theme.Border, buttons.X, buttons.Y, buttons.X, buttons.Bottom - 1);
      var half = buttons.Height / 2;
      var arrowX = buttons.X + ((buttons.Width - 7) / 2);
      Glyphs.PaintTriangle(g, textColor, new Rectangle(arrowX, buttons.Y + ((half - 4) / 2) + 1, 7, 4), GlyphDirection.Up);
      Glyphs.PaintTriangle(g, textColor, new Rectangle(arrowX, buttons.Y + half + ((buttons.Height - half - 4) / 2), 7, 4), GlyphDirection.Down);
      return;
    }

    Glyphs.PaintTriangle(
        g,
        textColor,
        new Rectangle(box.Right - _ComboChevronWidth + ((_ComboChevronWidth - 8) / 2), box.Y + ((box.Height - 5) / 2), 8, 5),
        GlyphDirection.Down);
  }

  /// <summary>Paints the entry being typed into a spinner: highlighted while the next keystroke would
  /// replace it, otherwise followed by a caret.</summary>
  private void PaintFieldEntry(IGraphics g, ITheme theme, Font font, FieldState state, Rectangle textRect) {
    var entry = state.Edit!;
    var width = entry.Length == 0 || this.Backend is not { } backend ? 0 : Math.Min(textRect.Width, backend.MeasureText(entry, font).Width);
    if (state.EditReplace && width > 0) {
      GlyphRenderer.FillSelection(g, theme, new Rectangle(textRect.X, textRect.Y + 2, width, Math.Max(0, textRect.Height - 4)));
      g.DrawText(entry, font, theme.SelectionText, textRect, ContentAlignment.MiddleLeft);
      return;
    }

    g.DrawText(entry, font, theme.ControlText, textRect, ContentAlignment.MiddleLeft);
    var caret = textRect.X + width;
    g.DrawLine(theme.ControlText, caret, textRect.Y + 3, caret, textRect.Bottom - 4);
  }

  // --- Pressing a field ---------------------------------------------------------------------------

  /// <summary>Routes a press on the ribbon's own group area to the field under it, if there is one.</summary>
  private bool TryPressFieldOnRibbon(int x, int y) {
    if (y < this.TabStripHeight || this.Minimized || this.SelectedTab is not { } tab)
      return false;

    var groupIndex = this.HitTestGroup(x, y);
    if (groupIndex < 0)
      return false;

    var group = tab.Groups[groupIndex];
    var index = this.HitTestItem(group, group.Bounds, group.IsCollapsed, x, y, this.GroupAreaHeight, out var itemBounds);
    if (index < 0 || group.Items[index] is not RibbonFieldItem field)
      return false;

    this.PressField(field, itemBounds, x, y, RibbonSurface.Ribbon);
    return true;
  }

  /// <summary>
  /// A press on a field laid out at <paramref name="item"/> on a surface: a combo box opens its list, a
  /// spinner's arrows step it and its number starts an entry. The field becomes the keyboard's target
  /// either way; a press on a disabled field does nothing at all.
  /// </summary>
  private void PressField(RibbonFieldItem field, Rectangle item, int x, int y, RibbonSurface surface) {
    if (!field.Enabled)
      return;

    var state = this.Fields;
    var box = FieldBox(field, item);
    var sameEntry = state.Edit is not null && ReferenceEquals(state.Active, field) && state.ActiveSurface == surface;
    var onButtons = field is RibbonSpinner && SpinButtons(box).Contains(x, y);
    if (!sameEntry || onButtons || !box.Contains(x, y))
      this.CommitEdit();

    var previous = state.ActiveSurface;
    state.Active = field;
    state.ActiveSurface = surface;
    state.ActiveBox = box;
    if (previous != surface)
      this.InvalidateSurface(previous);

    switch (field) {
      case RibbonComboBox combo:
        this.OpenList(combo, box, surface);
        break;

      case RibbonSpinner spinner when onButtons:
        spinner.Step(y < box.Y + (box.Height / 2) ? 1 : -1);
        break;

      case RibbonSpinner spinner when box.Contains(x, y) && state.Edit is null:
        BeginEdit(state, spinner);
        break;
    }

    this.InvalidateSurface(surface);
  }

  /// <summary>Ends whatever the last field was doing — commits a pending entry, drops the keyboard
  /// target — because the user pressed something else.</summary>
  private void EndFieldInteraction() {
    if (_fieldState is not { } state || state.Active is null)
      return;

    this.CommitEdit();
    var surface = state.ActiveSurface;
    state.Active = null;
    this.InvalidateSurface(surface);
  }

  // --- Typing into a spinner ----------------------------------------------------------------------

  private static void BeginEdit(FieldState state, RibbonSpinner spinner) {
    state.Edit = spinner.FieldText;
    state.EditReplace = true;
  }

  /// <summary>Commits a pending spinner entry: parsed, rounded and clamped, or reverted when it does not
  /// parse. A no-op while nothing is being typed.</summary>
  private void CommitEdit() {
    if (_fieldState is not { Edit: { } entry } state)
      return;

    state.Edit = null;
    if (state.Active is RibbonSpinner spinner)
      spinner.TryCommit(entry);

    this.InvalidateSurface(state.ActiveSurface);
  }

  /// <summary>Throws a pending spinner entry away.</summary>
  private void CancelEdit() {
    if (_fieldState is not { Edit: not null } state)
      return;

    state.Edit = null;
    this.InvalidateSurface(state.ActiveSurface);
  }

  /// <summary>Whether a field has a use for a key that the form's dialog-key chain would otherwise take
  /// (Enter, Escape, the arrows, Backspace).</summary>
  private bool FieldWantsKey(Keys keyData) {
    if (_flyoutShown && keyData == Keys.Escape)
      return true;

    if (_fieldState is not { } state)
      return false;

    if (state.ListCombo is not null || state.Edit is not null)
      return keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Escape or Keys.Back or Keys.PageUp or Keys.PageDown;

    return state.Active switch {
      RibbonComboBox => keyData is Keys.Up or Keys.Down or (Keys.Down | Keys.Alt) or Keys.F4 || (keyData == Keys.Escape && state.Group is not null),
      RibbonSpinner => keyData is Keys.Up or Keys.Down || (keyData == Keys.Escape && state.Group is not null),
      _ => keyData == Keys.Escape && state.Group is not null,
    };
  }

  /// <summary>A key for the fields: the open list, a spinner entry, the keyboard's target field, and
  /// Escape closing the collapsed group's flyout. Returns whether it was used.</summary>
  private bool HandleFieldKeyDown(KeyEventArgs e) {
    if (_fieldState is not { } state)
      return false;

    if (state.ListCombo is not null)
      return this.HandleListKey(state, e.KeyCode);

    if (state.Active is RibbonSpinner spinner && state.Edit is { } entry) {
      switch (e.KeyCode) {
        case Keys.Enter:
          this.CommitEdit();
          return true;

        case Keys.Escape:
          this.CancelEdit();
          return true;

        case Keys.Back:
          state.Edit = state.EditReplace || entry.Length == 0 ? string.Empty : entry[..^1];
          state.EditReplace = false;
          this.InvalidateSurface(state.ActiveSurface);
          return true;

        case Keys.Up:
        case Keys.Down:
          this.CommitEdit();
          spinner.Step(e.KeyCode == Keys.Up ? 1 : -1);
          BeginEdit(state, spinner);
          this.InvalidateSurface(state.ActiveSurface);
          return true;
      }

      return false;
    }

    switch (state.Active) {
      case RibbonComboBox combo when e.KeyData is (Keys.Down | Keys.Alt) or Keys.F4:
        this.OpenList(combo, state.ActiveBox, state.ActiveSurface);
        return true;

      case RibbonComboBox combo when e.KeyCode is Keys.Up or Keys.Down && combo.ItemCount > 0:
        combo.SelectedIndex = Math.Clamp(combo.SelectedIndex + (e.KeyCode == Keys.Up ? -1 : 1), 0, combo.ItemCount - 1);
        return true;

      case RibbonSpinner active when e.KeyCode is Keys.Up or Keys.Down:
        active.Step(e.KeyCode == Keys.Up ? 1 : -1);
        return true;
    }

    if (e.KeyCode == Keys.Escape && state.Group is not null) {
      this.CloseGroupFlyout();
      return true;
    }

    return false;
  }

  /// <summary>A typed character for the spinner the keyboard talks to: accepted characters go into the
  /// entry (starting one when none is open), anything else is swallowed while an entry is open.</summary>
  private void HandleFieldKeyPress(KeyPressEventArgs e) {
    if (_fieldState is not { Active: RibbonSpinner spinner } state || char.IsControl(e.KeyChar))
      return;

    if (!spinner.Accepts(e.KeyChar)) {
      e.Handled = state.Edit is not null;
      return;
    }

    if (state.Edit is null)
      BeginEdit(state, spinner);

    state.Edit = state.EditReplace ? e.KeyChar.ToString() : state.Edit + e.KeyChar;
    state.EditReplace = false;
    e.Handled = true;
    this.InvalidateSurface(state.ActiveSurface);
  }

  /// <inheritdoc/>
  protected override void OnKeyPress(KeyPressEventArgs e) {
    this.HandleFieldKeyPress(e);
    base.OnKeyPress(e);
  }

  /// <inheritdoc/>
  protected override void OnLostFocus(EventArgs e) {
    this.CommitEdit();
    base.OnLostFocus(e);
  }

  // --- The combo box's list -----------------------------------------------------------------------

  /// <summary>Opens a combo box's entries under its box on a surface, chained to that surface's popup
  /// when it is one so the list stacks above it and the surface keeps its grab across the hand-off.</summary>
  private void OpenList(RibbonComboBox combo, Rectangle box, RibbonSurface surface) {
    if (this.Backend is not { } backend || combo.ItemCount == 0)
      return;

    var state = this.Fields;
    this.CloseList();
    var parent = this.SurfacePopup(surface);
    if (state.ListPopup is not null && !ReferenceEquals(state.ListPopupParent, parent)) {
      // A stacked-popup server fixes a popup's parent at its first show, so a list moving to another
      // surface gets a surface of its own.
      state.ListPopup.Dispose();
      state.ListPopup = null;
    }

    var popup = state.ListPopup ??= this.CreateListPopup(backend);
    state.ListPopupParent = parent;
    state.ListCombo = combo;
    state.ListRows = Math.Min(combo.ItemCount, _MaxListRows);
    state.ListSize = new Size(box.Width, state.ListRows * this.Theme.RowHeight);
    state.ListHover = Math.Max(0, combo.SelectedIndex);
    state.ListTop = Math.Clamp(state.ListHover - state.ListRows + 1, 0, combo.ItemCount - state.ListRows);
    var origin = this.SurfaceOrigin(surface);
    state.ListLocation = new Point(origin.X + box.X, origin.Y + box.Bottom);
    if (parent is not null) {
      parent.ExpectGrabHandoff();
      popup.SetParentPopup(parent);
    }

    this.SyncOwnsOpenPopup();
    state.SuppressDismiss = true;
    try {
      popup.ShowAt(state.ListLocation, state.ListSize);
    } finally {
      state.SuppressDismiss = false;
    }
  }

  /// <summary>Takes the list down, handing the grab back to the surface it was opened from.</summary>
  private void CloseList() {
    if (_fieldState is not { ListCombo: not null } state)
      return;

    state.ListCombo = null;
    state.SuppressDismiss = true;
    try {
      state.ListPopup?.Hide();
    } finally {
      state.SuppressDismiss = false;
    }

    if (this.IsFlyoutShown(state.ListPopupParent))
      state.ListPopupParent!.Regrab();

    this.SyncOwnsOpenPopup();
  }

  /// <summary>Commits the entry at <paramref name="index"/> and closes the list.</summary>
  private void CommitList(int index) {
    if (_fieldState is not { ListCombo: { } combo })
      return;

    this.CloseList();
    combo.SelectedIndex = index;
  }

  private IPopupPeer CreateListPopup(IPlatformBackend backend) {
    var popup = backend.CreatePopup(this.OwnerWindowPeer);
    popup.Paint += (_, e) => this.PaintList(e.Graphics);
    popup.MouseMove += (_, e) => this.OnListMouseMove(e);
    popup.MouseDown += (_, e) => this.OnListMouseDown(e);
    popup.MouseWheel += (_, e) => this.OnListMouseWheel(e);
    popup.KeyDown += (_, e) => e.Handled = this.HandleFieldKeyDown(e); // backends with a keyboard grab route keys here
    popup.OutsidePress = this.OnListOutsidePress;
    popup.Dismissed += (_, _) => {
      if (_fieldState is { SuppressDismiss: false, ListCombo: not null })
        this.CloseList();
    };
    return popup;
  }

  /// <summary>A press outside the list that landed on the flyout it was opened from is a press on that
  /// flyout, not a dismissal of everything: the list closes and the press goes to the flyout.</summary>
  private bool OnListOutsidePress(Point screen) {
    if (_fieldState is not { } state || state.ListPopupParent is not { } parent)
      return false;

    RibbonSurface surface;
    Point origin;
    if (ReferenceEquals(parent, state.GroupPopup) && state.Group is not null) {
      if (!new Rectangle(state.GroupLocation, state.GroupSize).Contains(screen))
        return false;

      surface = RibbonSurface.GroupFlyout;
      origin = state.GroupLocation;
    } else if (ReferenceEquals(parent, _flyoutPopup) && _flyoutShown) {
      origin = this.SurfaceOrigin(RibbonSurface.TabFlyout);
      if (!new Rectangle(origin, new Size(this.Width, this.ExpandedGroupAreaHeight)).Contains(screen))
        return false;

      surface = RibbonSurface.TabFlyout;
    } else {
      return false;
    }

    this.CloseList();
    var press = new MouseEventArgs(MouseButtons.Left, screen.X - origin.X, screen.Y - origin.Y, 1);
    if (surface == RibbonSurface.GroupFlyout)
      this.OnGroupFlyoutMouseDown(press);
    else
      this.OnFlyoutMouseDown(press);

    return true;
  }

  private void HandleListKeyScroll(FieldState state) {
    var count = state.ListCombo?.ItemCount ?? 0;
    if (state.ListHover < state.ListTop)
      state.ListTop = state.ListHover;
    else if (state.ListHover >= state.ListTop + state.ListRows)
      state.ListTop = state.ListHover - state.ListRows + 1;

    state.ListTop = Math.Clamp(state.ListTop, 0, Math.Max(0, count - state.ListRows));
    state.ListPopup?.InvalidateAll();
  }

  private bool HandleListKey(FieldState state, Keys key) {
    var count = state.ListCombo!.ItemCount;
    switch (key) {
      case Keys.Up:
      case Keys.Down:
      case Keys.PageUp:
      case Keys.PageDown:
        var step = key switch { Keys.Up => -1, Keys.Down => 1, Keys.PageUp => -state.ListRows, _ => state.ListRows };
        state.ListHover = Math.Clamp(state.ListHover + step, 0, Math.Max(0, count - 1));
        this.HandleListKeyScroll(state);
        return true;

      case Keys.Enter:
        this.CommitList(state.ListHover);
        return true;

      case Keys.Escape:
        this.CloseList();
        return true;

      default:
        return false;
    }
  }

  /// <summary>Paints the list exactly like a list box's rows, the hovered one in the selection colours.</summary>
  private void PaintList(IGraphics g) {
    if (_fieldState is not { ListCombo: { } combo } state)
      return;

    var theme = this.Theme;
    var size = state.ListSize;
    var rowHeight = theme.RowHeight;
    g.FillRectangle(theme.FieldBackground, new Rectangle(0, 0, size.Width, size.Height));
    var last = Math.Min(combo.ItemCount, state.ListTop + state.ListRows);
    for (var row = state.ListTop; row < last; ++row) {
      var rowRect = new Rectangle(0, (row - state.ListTop) * rowHeight, size.Width, rowHeight);
      var hovered = row == state.ListHover;
      if (hovered)
        GlyphRenderer.FillSelection(g, theme, rowRect);

      ListBox.DrawRowContent(g, theme, rowRect, combo.Items[row], null, hovered);
    }

    g.DrawRectangle(theme.Border, new Rectangle(0, 0, size.Width - 1, size.Height - 1));
  }

  /// <summary>The entry under a list y-coordinate, or -1.</summary>
  private int ListRowAt(FieldState state, int y) {
    if (y < 0 || state.ListCombo is not { } combo)
      return -1;

    var row = state.ListTop + (y / this.Theme.RowHeight);
    return row < combo.ItemCount ? row : -1;
  }

  private void OnListMouseMove(MouseEventArgs e) {
    if (_fieldState is not { } state)
      return;

    var row = this.ListRowAt(state, e.Y);
    if (row < 0 || row == state.ListHover)
      return;

    state.ListHover = row;
    state.ListPopup?.InvalidateAll();
  }

  private void OnListMouseDown(MouseEventArgs e) {
    if (e.Button != MouseButtons.Left || _fieldState is not { } state)
      return;

    var row = this.ListRowAt(state, e.Y);
    if (row >= 0)
      this.CommitList(row);
  }

  private void OnListMouseWheel(MouseEventArgs e) {
    if (_fieldState is not { ListCombo: { } combo } state)
      return;

    var top = Math.Clamp(state.ListTop - (Math.Sign(e.Delta) * 3), 0, Math.Max(0, combo.ItemCount - state.ListRows));
    if (top == state.ListTop)
      return;

    state.ListTop = top;
    state.ListPopup?.InvalidateAll();
  }

  // --- The collapsed group's flyout ---------------------------------------------------------------

  /// <summary>
  /// Opens a collapsed group's own layout in a popup under its button — at the group's natural width and
  /// the height of the group area it was clicked in — so every item, fields included, works there as it
  /// does on the expanded ribbon. Opened from the tab flyout, it chains to it.
  /// </summary>
  private void OpenGroupFlyout(RibbonGroup group, RibbonSurface from) {
    if (this.Backend is not { } backend)
      return;

    this.CloseGroupFlyout();
    var state = this.Fields;
    var parent = this.SurfacePopup(from);
    if (state.GroupPopup is not null && !ReferenceEquals(state.GroupPopupParent, parent)) {
      state.GroupPopup.Dispose();
      state.GroupPopup = null;
    }

    var popup = state.GroupPopup ??= this.CreateGroupPopup(backend);
    state.GroupPopupParent = parent;
    var origin = this.SurfaceOrigin(from);
    var areaHeight = from == RibbonSurface.TabFlyout ? this.ExpandedGroupAreaHeight : this.GroupAreaHeight;
    state.GroupLocation = new Point(origin.X + group.Bounds.X, origin.Y + group.Bounds.Bottom);
    state.GroupSize = new Size(this.GroupWidth(group), areaHeight);
    state.GroupHot = -1;
    state.Group = group;
    if (parent is not null) {
      parent.ExpectGrabHandoff();
      popup.SetParentPopup(parent);
    }

    this.SyncOwnsOpenPopup();
    state.SuppressDismiss = true;
    try {
      popup.ShowAt(state.GroupLocation, state.GroupSize);
    } finally {
      state.SuppressDismiss = false;
    }
  }

  /// <summary>Closes the collapsed group's flyout, committing an entry typed there and taking its list
  /// with it. A no-op while it is closed.</summary>
  private void CloseGroupFlyout() {
    if (_fieldState is not { Group: not null } state)
      return;

    // Marked closed first, so nothing the clean-up below does hands the grab back to it.
    state.Group = null;
    this.CloseFieldPopupsOn(RibbonSurface.GroupFlyout);
    state.SuppressDismiss = true;
    try {
      state.GroupPopup?.Hide();
    } finally {
      state.SuppressDismiss = false;
    }

    if (this.IsFlyoutShown(state.GroupPopupParent))
      state.GroupPopupParent!.Regrab();

    this.SyncOwnsOpenPopup();
    this.Invalidate();
  }

  /// <summary>Ends the field interaction living on a surface that is about to close: commits its entry,
  /// closes a list opened from it, and closes the group flyout chained to the tab flyout.</summary>
  private void CloseFieldPopupsOn(RibbonSurface surface) {
    if (_fieldState is not { } state)
      return;

    if (surface == RibbonSurface.TabFlyout && state.Group is not null && state.GroupPopupParent is not null && ReferenceEquals(state.GroupPopupParent, _flyoutPopup))
      this.CloseGroupFlyout();

    if (state.ListCombo is not null && surface != RibbonSurface.Ribbon && state.ListPopupParent is not null && ReferenceEquals(state.ListPopupParent, this.SurfacePopup(surface)))
      this.CloseList();

    if (state.Active is not null && state.ActiveSurface == surface) {
      this.CommitEdit();
      state.Active = null;
    }
  }

  /// <summary>Closes every field popup and ends any field interaction — the tab changed, or the ribbon
  /// minimized or restored.</summary>
  private void CloseFieldPopups() {
    if (_fieldState is not { } state)
      return;

    this.CloseList();
    this.CloseGroupFlyout();
    this.CommitEdit();
    state.Active = null;
  }

  /// <summary>Releases the field popups when the ribbon unrealizes.</summary>
  private void DisposeFieldPopups() {
    if (_fieldState is not { } state)
      return;

    state.Edit = null;
    state.Active = null;
    state.ListCombo = null;
    state.Group = null;
    state.ListPopup?.Dispose();
    state.ListPopup = null;
    state.GroupPopup?.Dispose();
    state.GroupPopup = null;
  }

  private IPopupPeer CreateGroupPopup(IPlatformBackend backend) {
    var popup = backend.CreatePopup(this.OwnerWindowPeer);
    popup.Paint += (_, e) => this.PaintGroupFlyout(e.Graphics);
    popup.MouseMove += (_, e) => this.OnGroupFlyoutMouseMove(e);
    popup.MouseDown += (_, e) => this.OnGroupFlyoutMouseDown(e);
    popup.KeyDown += (_, e) => e.Handled = this.HandleFieldKeyDown(e); // backends with a keyboard grab route keys here
    popup.KeyPress += (_, e) => this.HandleFieldKeyPress(e);
    popup.Dismissed += (_, _) => {
      if (_fieldState is { SuppressDismiss: false, Group: not null })
        this.CloseGroupFlyout();
    };
    return popup;
  }

  /// <summary>The group's rectangle inside its flyout: the whole popup.</summary>
  private static Rectangle GroupFlyoutBounds(FieldState state) => new(Point.Empty, state.GroupSize);

  private void PaintGroupFlyout(IGraphics g) {
    if (_fieldState is not { Group: { } group } state)
      return;

    var theme = this.Theme;
    var bounds = GroupFlyoutBounds(state);
    g.FillRectangle(theme.ControlBackground, bounds);
    this.PaintGroup(
        g, theme, group, bounds, false, state.GroupHot, -1,
        this.GroupContentHeight(bounds.Height), this.CaptionStripHeight(), RibbonSurface.GroupFlyout);
  }

  private void OnGroupFlyoutMouseMove(MouseEventArgs e) {
    if (_fieldState is not { Group: { } group } state)
      return;

    var bounds = GroupFlyoutBounds(state);
    var hot = this.HitTestItem(group, bounds, false, e.X, e.Y, bounds.Height, out _);
    if (hot == state.GroupHot)
      return;

    state.GroupHot = hot;
    state.GroupPopup?.InvalidateAll();
  }

  /// <summary>A press in the collapsed group's flyout: a field takes it and the flyout stays up for the
  /// next one; any other enabled item runs and closes the flyout — and the tab flyout it was opened
  /// from — the way a menu closes behind the command it ran. A grid button hands over to its picker,
  /// opened where the flyout was.</summary>
  private void OnGroupFlyoutMouseDown(MouseEventArgs e) {
    if (e.Button != MouseButtons.Left || _fieldState is not { Group: { } group } state)
      return;

    var bounds = GroupFlyoutBounds(state);
    var index = this.HitTestItem(group, bounds, false, e.X, e.Y, bounds.Height, out var itemBounds);
    if (index >= 0 && group.Items[index] is RibbonFieldItem field) {
      this.PressField(field, itemBounds, e.X, e.Y, RibbonSurface.GroupFlyout);
      return;
    }

    this.EndFieldInteraction();
    if (index < 0 || group.Items[index] is RibbonHostItem || !group.Items[index].Enabled)
      return;

    var item = group.Items[index];
    var fromRibbon = state.GroupPopupParent is null;
    this.CloseGroupFlyout();
    this.CloseFlyout();
    if (item is not RibbonGridButton grid)
      item.PerformClick();
    else if (fromRibbon)
      this.OpenGridPicker(grid, group); // under the collapsed group's button, where the flyout was
  }
}
