using System.Drawing;
using System.Runtime.InteropServices;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// PRD §8: the accessibility information reaches ATK, asserted against real GTK widgets.
///
/// The headless fake records whatever the core pushed into it, so it can only prove the core says the
/// right thing — never that GTK heard it. That matters here more than usual: an owner-drawn control is a
/// bare drawing surface to a screen reader, and the whole point of this feature is the object GTK keeps
/// beside the widget. So this reads the name and role back out of ATK itself.
///
/// Without a display the whole fixture reports itself as ignored rather than passing vacuously.
/// </summary>
[TestFixture]
public sealed class GtkAccessibilityTests
{
    private static Observations? _observed;
    private static string? _skipReason;

    private sealed class Observations
    {
        /// <summary>The ATK name and role of an owner-drawn control's canvas.</summary>
        public string? OwnerDrawnName;
        public int OwnerDrawnRole;

        /// <summary>The same for one given an explicit accessible name.</summary>
        public string? RenamedName;

        /// <summary>And for a real platform widget, which GTK also fills in itself.</summary>
        public string? NativeName;

        /// <summary>The ATK name after the control's caption changed.</summary>
        public string? AfterCaptionChange;

        public string? Failure;
    }

    [OneTimeSetUp]
    public void RunTheFormOnce()
    {
        if (!OperatingSystem.IsLinux())
        {
            _skipReason = "GTK is only exercised on Linux.";
            return;
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            _skipReason = "No DISPLAY: these assertions need real GTK widgets.";
            return;
        }

        BackendRegistry.Register(new GtkBackend());
        var observed = new Observations();

        var form = new Form { Text = "atk", Width = 400, Height = 300 };

        // Owner-drawn: pinned, so this is the canvas case rather than a promoted widget.
        var drawn = new CheckBox { Bounds = new Rectangle(10, 10, 200, 22), Text = "Enable logging", UseNativeWidget = false };
        var renamed = new CheckBox { Bounds = new Rectangle(10, 40, 200, 22), Text = "×", UseNativeWidget = false, AccessibleName = "Close" };
        var native = new CheckBox { Bounds = new Rectangle(10, 70, 200, 22), Text = "Native box", UseNativeWidget = true };
        var renaming = new CheckBox { Bounds = new Rectangle(10, 100, 200, 22), Text = "before", UseNativeWidget = false };
        form.Controls.Add(drawn);
        form.Controls.Add(renamed);
        form.Controls.Add(native);
        form.Controls.Add(renaming);

        var timer = new Timer { Interval = 300 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                observed.OwnerDrawnName = AtkName(drawn);
                observed.OwnerDrawnRole = AtkRole(drawn);
                observed.RenamedName = AtkName(renamed);
                observed.NativeName = AtkName(native);

                renaming.Text = "after";
                observed.AfterCaptionChange = AtkName(renaming);
            }
            catch (Exception exception)
            {
                observed.Failure = exception.ToString();
            }
            finally
            {
                form.Close();
            }
        };

        form.Load += (_, _) => timer.Start();
        Application.Run(form);
        _observed = observed;
    }

    /// <summary>The name ATK reports for a control's widget.</summary>
    private static string? AtkName(Control control)
    {
        var accessible = AccessibleOf(control);
        if (accessible == 0)
            return null;

        var text = NativeMethods.atk_object_get_name(accessible);
        return text == 0 ? null : Marshal.PtrToStringUTF8(text);
    }

    /// <summary>The role ATK reports for a control's widget.</summary>
    private static int AtkRole(Control control)
    {
        var accessible = AccessibleOf(control);
        return accessible == 0 ? 0 : NativeMethods.atk_object_get_role(accessible);
    }

    private static nint AccessibleOf(Control control)
        => control.Peer is GtkControlPeer peer && peer.WidgetHandle != 0
            ? NativeMethods.gtk_widget_get_accessible(peer.WidgetHandle)
            : 0;

    private static Observations Result()
    {
        if (_skipReason is { } reason)
            Assert.Ignore(reason);

        Assert.That(_observed, Is.Not.Null, "the GTK loop never reached the observation tick.");
        Assert.That(_observed!.Failure, Is.Null, _observed.Failure);
        return _observed;
    }

    [Test]
    public void An_owner_drawn_controls_caption_reaches_ATK()
        => Assert.That(
            Result().OwnerDrawnName,
            Is.EqualTo("Enable logging"),
            "a screen reader would otherwise meet an unnamed drawing area");

    [Test]
    public void An_owner_drawn_control_tells_ATK_what_it_is()
        => Assert.That(Result().OwnerDrawnRole, Is.EqualTo(8), "ATK_ROLE_CHECK_BOX");

    [Test]
    public void An_explicit_accessible_name_reaches_ATK_instead_of_the_glyph()
        => Assert.That(Result().RenamedName, Is.EqualTo("Close"));

    [Test]
    public void A_promoted_control_carries_the_name_too()
        => Assert.That(Result().NativeName, Is.EqualTo("Native box"));

    [Test]
    public void Changing_the_caption_changes_what_ATK_reports()
        => Assert.That(Result().AfterCaptionChange, Is.EqualTo("after"));
}
