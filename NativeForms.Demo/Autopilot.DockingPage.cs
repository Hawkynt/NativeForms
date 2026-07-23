using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class Autopilot
{
    /// <summary>
    /// Drives the Docking page: confirms the IDE arrangement, floats a pane and asserts a new window
    /// appears, flies an auto-hidden pane out on click, switches documents with Ctrl+Tab, drags a
    /// caption onto a docking guide (screenshotting the overlay mid-drag) and asserts the pane
    /// re-docked, and round-trips the layout through Save/Load. It leaves the page with no floating
    /// window so later focus-sensitive checks are not stranded.
    /// </summary>
    private void DriveDocking()
    {
        Section("Docking");
        this.SelectTab("Dock");

        var dock = _form.Part<DockPanel>("docking.dock");
        var solution = _form.Part<DockContent>("docking.solution");
        var output = _form.Part<DockContent>("docking.output");
        var toolbox = _form.Part<DockContent>("docking.toolbox");
        var editor1 = _form.Part<DockContent>("docking.editor1");

        // The interaction checks all run first, while nothing is floating: on this display the window
        // manager parks a new top-level at the screen origin, over the panel's own top-left, so a
        // floating pane would sit on the very captions and strips the gestures aim at.
        this.Screenshot("state-docking-layout");

        this.Check("DockPanel: the IDE layout docks a pane on every side", () =>
        {
            this.Expect("Solution docked", this.Read(() => solution.DockState), DockState.Docked);
            this.Expect("Output docked", this.Read(() => output.DockState), DockState.Docked);
            this.Expect("Toolbox auto-hidden", this.Read(() => toolbox.DockState), DockState.AutoHide);
        });

        this.Check("DockPanel: an auto-hidden pane flies out on hover and collapses on an outside click", () =>
        {
            this.Hover(dock, 8, 12); // the Toolbox strip tab, top-left edge
            this.Expect("Toolbox flew out", this.Read(() => dock.ActiveContent), toolbox);
            this.Screenshot("state-docking-autohide");
            this.Click(dock, this.Read(() => dock.Width) / 2, 6); // click a document caption, outside the fly-out
            this.ExpectChanged("Toolbox collapsed", toolbox, this.Read(() => dock.ActiveContent));
        });

        this.Check("DockPanel: Ctrl+Tab switches documents", () =>
        {
            var w = this.Read(() => dock.Width);
            this.Click(dock, w / 2, 6); // focus the panel via the document caption
            this.Do(editor1.Activate);
            var before = this.Read(() => dock.ActiveContent);
            this.Key(KeySym.Tab, Injection.ControlMask);
            this.ExpectChanged("active document", before, this.Read(() => dock.ActiveContent));
        });

        this.Check("DockPanel: dragging a caption onto a guide re-docks the pane", () =>
        {
            var w = this.Read(() => dock.Width);
            var h = this.Read(() => dock.Height);
            var beforeEdge = this.Read(() => solution.DockEdge);
            this.DragWithOverlayShot(dock, new Point(Math.Max(40, w / 8), 6), new Point(w / 2, h / 2), new Point(w - 8, h / 2));
            this.Expect("Solution still docked", this.Read(() => solution.DockState), DockState.Docked);
            this.ExpectChanged("Solution edge", beforeEdge, this.Read(() => solution.DockEdge));
        });

        this.Check("DockPanel: floating a pane opens a new window", () =>
        {
            var before = this.Read(() => output.DockState);
            this.Click(_form.Part<Button>("docking.float"), 60, 13); // float Output
            this.ExpectChanged("Output state", before, this.Read(() => output.DockState));
            this.Expect("Output floating", this.Read(() => output.DockState), DockState.Floating);
            this.ExpectTrue("a floating window exists", this.Popups().Count > 0);
            this.Screenshot("state-docking-floating");
            this.Do(() => output.DockState = DockState.Docked); // redock without a covered click
        });

        this.Check("DockPanel: the layout round-trips through save and load", () =>
        {
            this.Click(_form.Part<Button>("docking.save"), 55, 13);
            this.Do(() => solution.DockState = DockState.Document); // rearrange (no floating window in the way)
            this.Click(_form.Part<Button>("docking.load"), 55, 13);
            this.Expect("Solution restored to docked", this.Read(() => solution.DockState), DockState.Docked);
        });

        // Leave nothing floating; restore focus into the gallery.
        this.Do(() =>
        {
            if (output.DockState == DockState.Floating)
                output.DockState = DockState.Docked;
        });
        this.Click(dock, this.Read(() => dock.Width) / 2, 6);
    }

    /// <summary>Presses a caption, drags through a waypoint (where it screenshots the docking overlay
    /// guides and preview), then on to the drop point and releases.</summary>
    private void DragWithOverlayShot(Control control, Point from, Point waypoint, Point to)
    {
        var start = this.ScreenOf(control, from.X, from.Y);
        var mid = this.ScreenOf(control, waypoint.X, waypoint.Y);
        var end = this.ScreenOf(control, to.X, to.Y);
        var root = this.RootAt(start);

        this.Pump("a drag press", () =>
        {
            Injection.Move(root, start);
            Injection.Press(root, start, 1, 0);
        });
        this.Pump("a drag move", () => Injection.Move(root, mid, buttonHeld: true));
        this.Settle();
        this.Screenshot("state-docking-drag");
        this.Pump("a drag move", () => Injection.Move(root, end, buttonHeld: true));
        this.Settle();
        this.Pump("a drag release", () => Injection.Release(root, end, 1, 0));
        this.Settle();
    }
}
