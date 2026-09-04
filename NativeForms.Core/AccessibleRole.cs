namespace Hawkynt.NativeForms;

/// <summary>
/// What a control <em>is</em>, as far as assistive technology is concerned — the word a screen reader
/// says after the name.
/// </summary>
/// <remarks>
/// A deliberately small set: the roles every platform's accessibility layer agrees on, named as Windows
/// Forms names them so a port reads the same. Each backend maps these onto its own vocabulary (ATK roles
/// on GTK, control types on Windows); a role a platform has no word for degrades to its generic one
/// rather than being dropped.
/// </remarks>
public enum AccessibleRole {
  /// <summary>Let the control decide, which is what every control does unless told otherwise.</summary>
  Default,

  /// <summary>Static text that labels something else.</summary>
  StaticText,

  /// <summary>A push button.</summary>
  PushButton,

  /// <summary>A two-state check box.</summary>
  CheckButton,

  /// <summary>One of a set of mutually exclusive options.</summary>
  RadioButton,

  /// <summary>An editable text field.</summary>
  Text,

  /// <summary>A drop-down list.</summary>
  ComboBox,

  /// <summary>A list of items.</summary>
  List,

  /// <summary>One item of a list.</summary>
  ListItem,

  /// <summary>A hierarchical list.</summary>
  Tree,

  /// <summary>A tabular grid of cells.</summary>
  Table,

  /// <summary>A slider the user drags along a range.</summary>
  Slider,

  /// <summary>A progress indicator.</summary>
  ProgressBar,

  /// <summary>A scroll bar.</summary>
  ScrollBar,

  /// <summary>A hyperlink.</summary>
  Link,

  /// <summary>A group of related controls, usually with a caption.</summary>
  Grouping,

  /// <summary>A set of tab pages.</summary>
  PageTabList,

  /// <summary>One tab page.</summary>
  PageTab,

  /// <summary>A menu bar or menu.</summary>
  MenuBar,

  /// <summary>One menu entry.</summary>
  MenuItem,

  /// <summary>A tool bar.</summary>
  ToolBar,

  /// <summary>A window.</summary>
  Window,

  /// <summary>A container with no meaning of its own.</summary>
  Pane,

  /// <summary>An image.</summary>
  Graphic,
}
