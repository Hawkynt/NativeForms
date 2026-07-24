using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class Autopilot
{
    /// <summary>
    /// Drives the Menus page: right-clicks the native button carrying a <see cref="ContextMenuStrip"/>
    /// and asserts the menu opens at the pointer. This is the native-widget path — a right-click on an
    /// owner-drawn control always opened its menu, but native ones (button, label) had no such menu
    /// until their peers began forwarding the request.
    /// </summary>
    private void DriveMenus()
    {
        Section("Menus");
        this.SelectTab("Menus");

        var button = _form.Part<Button>("menus.button");

        this.Check("ContextMenuStrip: a right-click on a native button opens its menu at the pointer", () =>
        {
            var cursor = this.ScreenOf(button, 110, 20);
            this.ClickAt(cursor, button: 3);
            this.ExpectTrue("the button's context menu did not open", this.Read(() => button.ContextMenuStrip is { IsOpen: true }));

            var popups = this.Popups();
            if (popups.Count == 0)
            {
                this.Fail("no popup toplevel appeared for the button's context menu");
                return;
            }

            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            this.ExpectNear("the menu's left edge against the cursor", bounds.X, cursor.X, 4);
            this.ExpectNear("the menu's top edge against the cursor", bounds.Y, cursor.Y, 4);
        });

        this.Screenshot("state-menus-button-context");

        this.Check("ContextMenuStrip: hovering a submenu parent opens the child cascade and keeps it open", () =>
        {
            var popups = this.Popups();
            if (popups.Count == 0)
            {
                this.Fail("the button's context menu is not open to scan for a submenu parent");
                return;
            }

            // Scan the menu column top to bottom: hovering the submenu-parent row opens a second popup.
            // A pure hover (no press) must leave both levels standing — the grab handoff from parent to
            // child is expected, not a dismissal, so the cascade may not collapse under it.
            var bounds = this.Read(() => Injection.WindowBounds(popups[0]));
            var opened = false;
            for (var y = 8; y <= bounds.Height - 4 && !opened; y += 6)
            {
                var point = new Point(bounds.X + (bounds.Width / 2), bounds.Y + y);
                this.Pump("a submenu hover", () => Injection.Move(this.RootAt(point), point));
                this.Settle(60);
                opened = this.Popups().Count >= 2;
            }

            this.ExpectTrue("no submenu opened while hovering down the menu's rows", opened);
            this.Settle(200);
            this.ExpectTrue("the submenu collapsed under the parent's grab handoff instead of staying open",
                this.Popups().Count >= 2);
        });

        // Dismiss the menu so later checks start from a clean surface.
        this.Key(KeySym.Escape);
        this.Settle(80);
    }
}
