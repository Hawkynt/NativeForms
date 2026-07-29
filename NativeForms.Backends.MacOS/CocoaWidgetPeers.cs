using Hawkynt.NativeForms.Backends;

namespace Hawkynt.NativeForms.Backends.MacOS;

/// <summary>
/// A two-state box: an <c>NSButton</c> wearing the switch button type, which is what AppKit calls a
/// check box.
/// </summary>
/// <remarks>
/// <para>
/// The first of the promotions in PRD §12 to reach this backend. The control is the same
/// <see cref="CheckBox"/> either way — the core decides at realization whether its configured state
/// stays inside what a real widget can do, and drops back to the owner-drawn twin when it does not.
/// What is bought here is the part owner-draw cannot have: VoiceOver knows what this is without being
/// told, the press animation is the desktop's own, and it follows a high-contrast or accent-colour
/// setting the painter would have to be taught.
/// </para>
/// <para>
/// A programmatic <c>setState:</c> does not run the action, so nothing here has to suppress an echo the
/// way the GTK peer suppresses <c>toggled</c>: AppKit only sends the action when the user works the
/// control.
/// </para>
/// </remarks>
internal class CocoaCheckBoxPeer : CocoaControlPeer, ICheckBoxPeer
{
    /// <summary>NSButtonType: switch 3, radio 4.</summary>
    private protected const nint _Switch = 3;
    private protected const nint _Radio = 4;

    private readonly nint _target;

    private protected CocoaCheckBoxPeer(nint buttonType)
        : base(Create(buttonType))
    {
        if (this.Handle == 0)
            return;

        _target = CocoaAction.Create(this.OnToggled);
        if (_target == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTarget:"), _target);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setAction:"), CocoaAction.Selector);
    }

    public CocoaCheckBoxPeer()
        : this(_Switch)
    {
    }

    /// <inheritdoc/>
    public event EventHandler? CheckedChanged;

    private static nint Create(nint buttonType)
    {
        var allocated = CocoaRuntime.Allocate("NSButton");
        var button = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (button != 0)
            CocoaRuntime.SendVoid(button, CocoaRuntime.sel_registerName("setButtonType:"), buttonType);

        return button;
    }

    /// <summary>A button carries its caption as a title, not as a string value.</summary>
    public override void SetText(string text)
    {
        if (this.Handle == 0)
            return;

        var title = CocoaRuntime.NSString(text);
        if (title == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setTitle:"), title);
        CocoaNative.CFRelease(title);
    }

    /// <inheritdoc/>
    /// <remarks>NSControlStateValue: off 0, on 1.</remarks>
    public void SetChecked(bool value)
    {
        _checked = value;
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setState:"), value ? 1 : 0);
    }

    /// <inheritdoc/>
    public bool GetChecked()
        => this.Handle == 0 ? _checked : CocoaRuntime.SendInteger(this.Handle, CocoaRuntime.sel_registerName("state")) != 0;

    private bool _checked;

    /// <summary>The widget reporting that the user worked it.</summary>
    private void OnToggled()
    {
        _checked = this.GetChecked();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public override void Dispose() => CocoaAction.Forget(_target);
}

/// <summary>A radio button: the same <c>NSButton</c>, wearing the radio type.</summary>
/// <remarks>
/// Grouping stays in the core, which unchecks the siblings sharing a parent. AppKit applies the same
/// rule to radio buttons sharing a superview, and a peer's superview is its control's parent, so the
/// two cannot reach different answers — which is why this asks for the real radio type rather than
/// dressing a switch up as one and losing the platform's keyboard behaviour with it.
/// </remarks>
internal sealed class CocoaRadioButtonPeer : CocoaCheckBoxPeer, IRadioButtonPeer
{
    public CocoaRadioButtonPeer()
        : base(_Radio)
    {
    }
}

/// <summary>A progress indicator: a real <c>NSProgressIndicator</c> in its bar style.</summary>
/// <remarks>
/// It is an <c>NSView</c> and not an <c>NSControl</c>, so it answers neither <c>setStringValue:</c> nor
/// <c>setEnabled:</c> — and an unrecognized selector here does not fail quietly, it ends the process.
/// Both are therefore refused rather than inherited: a progress bar has no caption to set, and macOS
/// has no disabled look for one.
/// </remarks>
internal sealed class CocoaProgressBarPeer : CocoaControlPeer, IProgressBarPeer
{
    public CocoaProgressBarPeer()
        : base(Create())
    {
    }

    private static nint Create()
    {
        var allocated = CocoaRuntime.Allocate("NSProgressIndicator");
        var bar = allocated == 0
            ? 0
            : CocoaRuntime.SendRectInit(allocated, CocoaRuntime.sel_registerName("initWithFrame:"), new(0, 0, 1, 1));

        if (bar == 0)
            return 0;

        // NSProgressIndicatorStyleBar, and a fraction rather than the toolkit's own range: the core
        // has already reduced value/minimum/maximum to one number between nothing and everything.
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setStyle:"), 0);
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setIndeterminate:"), false);
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setMinValue:"), 0.0);
        CocoaRuntime.SendVoid(bar, CocoaRuntime.sel_registerName("setMaxValue:"), 1.0);
        return bar;
    }

    /// <inheritdoc/>
    public void SetFraction(double fraction)
    {
        if (this.Handle != 0)
            CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setDoubleValue:"), fraction);
    }

    /// <inheritdoc/>
    public void SetMarquee(bool marquee)
    {
        if (this.Handle == 0)
            return;

        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName("setIndeterminate:"), marquee);
        CocoaRuntime.SendVoid(this.Handle, CocoaRuntime.sel_registerName(marquee ? "startAnimation:" : "stopAnimation:"), 0);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing to do: an indeterminate <c>NSProgressIndicator</c> animates itself once started, so a
    /// caller stepping it by hand would only be competing with the platform's own timing.
    /// </remarks>
    public void Pulse() { }

    /// <inheritdoc cref="CocoaProgressBarPeer"/>
    public override void SetText(string text) { }

    /// <inheritdoc cref="CocoaProgressBarPeer"/>
    public override void SetEnabled(bool enabled) { }
}
