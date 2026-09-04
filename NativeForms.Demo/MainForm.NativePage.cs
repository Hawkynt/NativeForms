using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm {
  /// <summary>
  /// The Native page: every control that can be promoted to a real platform widget (PRD §12), built
  /// twice with identical properties — once pinned to the widget, once pinned to the owner-drawn
  /// painter — in two columns that line up row for row, so the two renderings can be compared
  /// directly. The third column holds the controls whose state puts them outside the gate, which is
  /// what the comparison is <em>for</em>: it shows what the painter still has to draw itself.
  /// </summary>
  private TabPage BuildNativePage() {
    var page = new TabPage("Native") { ImageIndex = _IconGear };

    page.Controls.AddRange(
        Caption("Promoted to the platform widget", _NativeLeft, 12, _NativeColumn),
        Caption("Same properties, owner-drawn", _NativeRight, 12, _NativeColumn));

    // Both columns are built by the same code with only the pin flipped, so any difference on screen
    // is the rendering and nothing else.
    var native = this.BuildPromotionColumn(_NativeLeft, useNative: true);
    var drawn = this.BuildPromotionColumn(_NativeRight, useNative: false);
    page.Controls.AddRange(native);
    page.Controls.AddRange(drawn);

    // --- Column 3: outside the gate ---------------------------------------------------------

    var gatedTop = _NativeRowTop;
    var iconCheck = new CheckBox {
      Bounds = new(_NativeGated, gatedTop, _NativeColumn, 20),
      Text = "CheckBox with an Image",
      Image = this.SquareImage(Color.MediumSeaGreen),
    };

    var iconRadio = new RadioButton {
      Bounds = new(_NativeGated, gatedTop + 26, _NativeColumn, 20),
      Text = "RadioButton with an Image",
      Image = this.SquareImage(Color.Goldenrod),
      Checked = true,
    };

    var verticalBar = new ProgressBar {
      Bounds = new(_NativeGated, gatedTop + 56, 18, 90),
      Orientation = Orientation.Vertical,
      Value = 60,
    };

    var iconCombo = new ComboBox {
      Bounds = new(_NativeGated + 28, gatedTop + 56, _NativeColumn - 28, 26),
      DropDownStyle = ComboBoxStyle.DropDownList,
      ImageList = _icons,
      DisplaySelector = static o => (string)o!,
      ImageIndexSelector = static _ => _IconFolder,
    };
    iconCombo.Items.AddRange(["With a per-item icon", "Second row"]);
    iconCombo.SelectedIndex = 0;

    var editableCombo = new ComboBox {
      Bounds = new(_NativeGated + 28, gatedTop + 88, _NativeColumn - 28, 26),
      DropDownStyle = ComboBoxStyle.DropDown,
      PlaceholderText = "Editable style",
    };
    editableCombo.Items.AddRange(["alpha", "beta"]);

    var checkedList = new CheckedListBox {
      Bounds = new(_NativeGated, gatedTop + 152, _NativeColumn, 76),
      CheckOnClick = true,
    };
    checkedList.Items.AddRange(["CheckedListBox", "never promotes", "— the row check"]);
    checkedList.SetItemChecked(0, true);

    var iconGroup = new GroupBox {
      Bounds = new(_NativeGated, gatedTop + 240, _NativeColumn, 62),
      Text = "GroupBox with a caption icon",
      Image = this.DiscImage(Color.SteelBlue),
    };
    iconGroup.Controls.Add(new Label { Bounds = new(12, 26, 220, 18), Text = "boxes stay owner-drawn" });

    // The gate is per control, so a group can be half widget and half painter — and grouping still
    // has to reach across the split, which is the case worth showing rather than only describing.
    var mixedGroup = new GroupBox {
      Bounds = new(_NativeGated, gatedTop + 312, _NativeColumn, 88),
      Text = "One group, both renderings",
    };
    var mixedNative = new RadioButton { Bounds = new(14, 26, _NativeColumn - 28, 20), Text = "widget", Checked = true };
    var mixedDrawn = new RadioButton {
      Bounds = new(14, 52, _NativeColumn - 28, 20),
      Text = "painted (has an image)",
      Image = this.SquareImage(Color.MediumPurple),
    };
    mixedGroup.Controls.AddRange(mixedNative, mixedDrawn);

    page.Controls.AddRange(
        Caption("Outside the gate — still painted", _NativeGated, 12, _NativeColumn),
        iconCheck, iconRadio, verticalBar, iconCombo, editableCombo, checkedList, iconGroup, mixedGroup);

    this.Publish("native.page", page);
    this.Publish("native.gatedCheck", iconCheck);
    this.Publish("native.gatedRadio", iconRadio);
    this.Publish("native.gatedProgress", verticalBar);
    this.Publish("native.gatedCombo", iconCombo);
    this.Publish("native.gatedEditableCombo", editableCombo);
    this.Publish("native.gatedCheckedList", checkedList);
    this.Publish("native.gatedGroup", iconGroup);
    this.Publish("native.mixedNative", mixedNative);
    this.Publish("native.mixedDrawn", mixedDrawn);
    return page;
  }

  /// <summary>The x of the promoted column.</summary>
  private const int _NativeLeft = 16;

  /// <summary>The x of the owner-drawn column.</summary>
  private const int _NativeRight = 340;

  /// <summary>The x of the column holding the controls the gate rejects.</summary>
  private const int _NativeGated = 664;

  /// <summary>The width every column's controls span.</summary>
  private const int _NativeColumn = 300;

  /// <summary>The y the first comparison row sits at.</summary>
  private const int _NativeRowTop = 36;

  /// <summary>
  /// Builds one column of the comparison: the same nine controls with the same properties, pinned to
  /// the given rendering path. Each is published under a name carrying the path, so the autopilot can
  /// assert that the pin held.
  /// </summary>
  /// <param name="x">The column's left edge.</param>
  /// <param name="useNative">Whether to pin the column to the platform widgets.</param>
  private Control[] BuildPromotionColumn(int x, bool useNative) {
    var suffix = useNative ? "Native" : "Drawn";
    var y = _NativeRowTop;

    var check = new CheckBox {
      Bounds = new(x, y, _NativeColumn, 20),
      Text = "CheckBox",
      Checked = true,
      UseNativeWidget = useNative,
    };

    var link = new LinkLabel {
      Bounds = new(x, y + 26, _NativeColumn, 20),
      Text = "LinkLabel",
      UseNativeWidget = useNative,
    };

    var group = new GroupBox {
      Bounds = new(x, y + 56, _NativeColumn, 86),
      Text = "GroupBox",
      UseNativeWidget = useNative,
    };
    var first = new RadioButton {
      Bounds = new(14, 26, _NativeColumn - 28, 20),
      Text = "RadioButton",
      Checked = true,
      UseNativeWidget = useNative,
    };
    var second = new RadioButton {
      Bounds = new(14, 52, _NativeColumn - 28, 20),
      Text = "… and its sibling",
      UseNativeWidget = useNative,
    };
    group.Controls.AddRange(first, second);

    var progress = new ProgressBar {
      Bounds = new(x, y + 152, _NativeColumn, 18),
      Value = 60,
      UseNativeWidget = useNative,
    };

    var track = new TrackBar {
      Bounds = new(x, y + 178, _NativeColumn, 28),
      Minimum = 0,
      Maximum = 10,
      Value = 6,
      UseNativeWidget = useNative,
    };

    var horizontal = new HScrollBar {
      Bounds = new(x, y + 212, _NativeColumn, 16),
      Maximum = 100,
      LargeChange = 10,
      Value = 30,
      UseNativeWidget = useNative,
    };

    var combo = new ComboBox {
      Bounds = new(x, y + 236, _NativeColumn - 28, 26),
      DropDownStyle = ComboBoxStyle.DropDownList,
      UseNativeWidget = useNative,
    };
    combo.Items.AddRange(["ComboBox", "second row", "third row"]);
    combo.SelectedIndex = 0;

    var vertical = new VScrollBar {
      Bounds = new(x + _NativeColumn - 16, y + 236, 16, 108),
      Maximum = 100,
      LargeChange = 10,
      Value = 40,
      UseNativeWidget = useNative,
    };

    var list = new ListBox {
      Bounds = new(x, y + 268, _NativeColumn - 28, 76),
      UseNativeWidget = useNative,
    };
    list.Items.AddRange(["ListBox", "single selection", "no per-item icon"]);
    list.SelectedIndex = 0;

    this.Publish($"native.check{suffix}", check);
    this.Publish($"native.link{suffix}", link);
    this.Publish($"native.group{suffix}", group);
    this.Publish($"native.radio{suffix}", first);
    this.Publish($"native.progress{suffix}", progress);
    this.Publish($"native.track{suffix}", track);
    this.Publish($"native.hscroll{suffix}", horizontal);
    this.Publish($"native.vscroll{suffix}", vertical);
    this.Publish($"native.combo{suffix}", combo);
    this.Publish($"native.list{suffix}", list);

    return [check, link, group, progress, track, horizontal, combo, vertical, list];
  }
}
