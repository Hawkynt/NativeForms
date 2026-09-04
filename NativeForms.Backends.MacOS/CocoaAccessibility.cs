namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// Publishes what a view is called and what it is, so VoiceOver can announce it.
/// </summary>
/// <remarks>
/// <para>
/// A real AppKit control answers for itself: an <c>NSButton</c> tells the screen reader it is a button
/// and reads out its own title without being asked. This matters for the surfaces that cannot — the
/// canvas an owner-drawn control paints on is one unlabelled rectangle to an accessibility client
/// however carefully the pixels are arranged, so the toolkit has to say out loud what they mean.
/// </para>
/// <para>
/// The roles are <c>NSString</c> constants exported by AppKit, read from its symbols rather than
/// written out as the <c>AX…</c> literals they happen to hold. A role the platform has no word for is
/// left alone rather than guessed at, which leaves the view's own answer in place — the same choice
/// the GTK peer makes with <c>ATK_ROLE_UNKNOWN</c>.
/// </para>
/// <para>
/// Every message is offered with <c>respondsToSelector:</c> first. These are declared on
/// <c>NSObject</c> and implemented since 10.10, so the check should never fail; it costs one message
/// per control at realization, and an unrecognized selector on this platform is not ignored — it ends
/// the process.
/// </para>
/// </remarks>
internal static class CocoaAccessibility {
  /// <summary>Describes a view, ignoring the parts the caller left unset.</summary>
  internal static void Describe(nint view, string? name, string? description, AccessibleRole role) {
    if (view == 0)
      return;

    if (name is not null)
      SetString(view, "setAccessibilityLabel:", name);

    if (description is not null)
      SetString(view, "setAccessibilityHelp:", description);

    if (RoleOf(role) is var axRole && axRole != 0) {
      Send(view, "setAccessibilityRole:", axRole);

      // A view AppKit does not already treat as an element is skipped over entirely, whatever
      // role it claims — which is exactly the case an owner-drawn canvas is in.
      Send(view, "setAccessibilityElement:", 1);
    }
  }

  private static void SetString(nint view, string selector, string value) {
    var text = CocoaRuntime.NSString(value);
    if (text == 0)
      return;

    Send(view, selector, text);
    CocoaNative.CFRelease(text);
  }

  private static void Send(nint view, string selector, nint argument) {
    var message = CocoaRuntime.sel_registerName(selector);
    if (CocoaRuntime.SendBool(view, CocoaRuntime.sel_registerName("respondsToSelector:"), message))
      CocoaRuntime.SendVoid(view, message, argument);
  }

  /// <summary>The AppKit role constant matching one of ours, or zero when it has no word for it.</summary>
  private static nint RoleOf(AccessibleRole role)
      => role switch {
        AccessibleRole.StaticText => Constant("NSAccessibilityStaticTextRole"),
        AccessibleRole.PushButton => Constant("NSAccessibilityButtonRole"),
        AccessibleRole.CheckButton => Constant("NSAccessibilityCheckBoxRole"),
        AccessibleRole.RadioButton => Constant("NSAccessibilityRadioButtonRole"),
        AccessibleRole.Text => Constant("NSAccessibilityTextFieldRole"),
        AccessibleRole.ComboBox => Constant("NSAccessibilityPopUpButtonRole"),
        AccessibleRole.List => Constant("NSAccessibilityListRole"),
        AccessibleRole.ListItem => Constant("NSAccessibilityRowRole"),
        AccessibleRole.Tree => Constant("NSAccessibilityOutlineRole"),
        AccessibleRole.Table => Constant("NSAccessibilityTableRole"),
        AccessibleRole.Slider => Constant("NSAccessibilitySliderRole"),
        AccessibleRole.ProgressBar => Constant("NSAccessibilityProgressIndicatorRole"),
        AccessibleRole.ScrollBar => Constant("NSAccessibilityScrollBarRole"),
        AccessibleRole.Link => Constant("NSAccessibilityLinkRole"),
        AccessibleRole.Grouping or AccessibleRole.Pane => Constant("NSAccessibilityGroupRole"),
        AccessibleRole.PageTabList => Constant("NSAccessibilityTabGroupRole"),
        AccessibleRole.PageTab => Constant("NSAccessibilityRadioButtonRole"),
        AccessibleRole.MenuBar => Constant("NSAccessibilityMenuBarRole"),
        AccessibleRole.MenuItem => Constant("NSAccessibilityMenuItemRole"),
        AccessibleRole.ToolBar => Constant("NSAccessibilityToolbarRole"),
        AccessibleRole.Window => Constant("NSAccessibilityWindowRole"),
        AccessibleRole.Graphic => Constant("NSAccessibilityImageRole"),
        _ => 0,
      };

  /// <summary>
  /// One role constant, resolved once. Resolving it means opening the framework and looking a symbol
  /// up in its export table, which is far too much to repeat per control for an answer that cannot
  /// change between two of them.
  /// </summary>
  private static nint Constant(string name) {
    if (_roles.TryGetValue(name, out var resolved))
      return resolved;

    resolved = CocoaRuntime.Constant(name);
    _roles[name] = resolved;
    return resolved;
  }

  private static readonly Dictionary<string, nint> _roles = [];
}
