using System.ComponentModel;
using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Tests.Fakes;
using NUnit.Framework;

namespace Hawkynt.NativeForms.Tests;

[TestFixture]
internal sealed class DockPanelTests
{
    private static DockContent Pane(string name) => new(name) { Name = name };

    private static (DockPanel dock, Form form, HeadlessCanvasPeer canvas) Realize(DockPanel dock)
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 600, 400) };
        dock.Bounds = new(0, 0, 600, 400);
        form.Controls.Add(dock);
        Application.Run(form, backend);
        return (dock, form, (HeadlessCanvasPeer)dock.Peer!);
    }

    // --- Model --------------------------------------------------------------------------------

    [Test]
    public void Adding_a_document_makes_it_active()
    {
        var dock = new DockPanel();
        var doc = Pane("doc");
        dock.AddDocument(doc);

        Assert.That(doc.DockState, Is.EqualTo(DockState.Document));
        Assert.That(dock.ActiveContent, Is.SameAs(doc));
        Assert.That(dock.Contents, Has.Count.EqualTo(1));
    }

    [Test]
    public void Docking_to_an_edge_records_state_and_edge()
    {
        var dock = new DockPanel();
        var pane = Pane("tools");
        dock.Add(pane, DockState.Docked, DockEdge.Right);

        Assert.That(pane.DockState, Is.EqualTo(DockState.Docked));
        Assert.That(pane.DockEdge, Is.EqualTo(DockEdge.Right));
        Assert.That(dock.GroupOf(pane), Is.Not.Null);
    }

    [Test]
    public void Two_panes_on_the_same_edge_share_one_group()
    {
        var dock = new DockPanel();
        var a = Pane("a");
        var b = Pane("b");
        dock.Add(a, DockState.Docked, DockEdge.Left);
        dock.Add(b, DockState.Docked, DockEdge.Left);

        Assert.That(dock.GroupOf(a), Is.SameAs(dock.GroupOf(b)));
        Assert.That(dock.GroupOf(a)!.Contents, Has.Count.EqualTo(2));
    }

    [Test]
    public void Only_the_active_pane_of_a_group_is_shown()
    {
        var dock = new DockPanel();
        var a = Pane("a");
        var b = Pane("b");
        Realize(dock);
        dock.Add(a, DockState.Docked, DockEdge.Left);
        dock.Add(b, DockState.Docked, DockEdge.Left); // b becomes active tab

        Assert.That(dock.IsContentShown(b), Is.True);
        Assert.That(dock.IsContentShown(a), Is.False);

        a.Activate();
        Assert.That(dock.IsContentShown(a), Is.True);
        Assert.That(dock.IsContentShown(b), Is.False);
    }

    [Test]
    public void Floating_a_pane_opens_a_window_and_reparents_it()
    {
        var dock = new DockPanel();
        var pane = Pane("p");
        Realize(dock);
        dock.AddDocument(pane);

        pane.Float();

        Assert.That(pane.DockState, Is.EqualTo(DockState.Floating));
        Assert.That(dock.FloatingWindowCount, Is.EqualTo(1));
        Assert.That(pane.Parent, Is.Not.SameAs(dock));
        Assert.That(dock.Contents, Does.Contain(pane)); // still owned
    }

    [Test]
    public void Redocking_a_floating_pane_closes_its_window()
    {
        var dock = new DockPanel();
        var pane = Pane("p");
        Realize(dock);
        dock.AddDocument(pane);
        pane.Float();

        pane.DockState = DockState.Document;

        Assert.That(dock.FloatingWindowCount, Is.EqualTo(0));
        Assert.That(pane.DockState, Is.EqualTo(DockState.Document));
        Assert.That(pane.Parent, Is.SameAs(dock));
    }

    [Test]
    public void Auto_hiding_collapses_to_the_strip_and_flies_out_on_activate()
    {
        var dock = new DockPanel();
        var pane = Pane("props");
        Realize(dock);
        dock.Add(pane, DockState.Docked, DockEdge.Left);

        pane.ToggleAutoHide();
        Assert.That(pane.DockState, Is.EqualTo(DockState.AutoHide));
        Assert.That(dock.AutoHideContents, Does.Contain(pane));
        Assert.That(dock.IsContentShown(pane), Is.False); // collapsed

        pane.Activate(); // fly out
        Assert.That(dock.FlyoutContent, Is.SameAs(pane));
        Assert.That(dock.IsContentShown(pane), Is.True);
    }

    [Test]
    public void Closing_a_pane_honours_the_veto()
    {
        var dock = new DockPanel();
        var pane = Pane("p");
        dock.AddDocument(pane);
        pane.CloseRequested += (_, e) => e.Cancel = true;

        pane.Close();
        Assert.That(dock.Contents, Does.Contain(pane));
    }

    [Test]
    public void Closing_a_pane_removes_it()
    {
        var dock = new DockPanel();
        var a = Pane("a");
        var b = Pane("b");
        dock.AddDocument(a);
        dock.AddDocument(b);

        a.Close();
        Assert.That(dock.Contents, Does.Not.Contain(a));
        Assert.That(a.DockPanel, Is.Null);
        Assert.That(dock.ActiveContent, Is.SameAs(b));
    }

    // --- Drag docking (real input path) -------------------------------------------------------

    [Test]
    public void Dragging_a_document_tab_to_the_panel_edge_docks_it()
    {
        var dock = new DockPanel();
        var a = Pane("a");
        var b = Pane("b");
        var (_, _, canvas) = Realize(dock);
        dock.AddDocument(a);
        dock.AddDocument(b); // same document well, two tabs

        // Tab b is the second tab in the bottom strip; press it, then drag to the right panel edge.
        canvas.RaiseMouseDown(200, 400 - 5);
        canvas.RaiseMouseMove(600 - 5, 200);
        canvas.RaiseMouseUp(600 - 5, 200);

        Assert.That(b.DockState, Is.EqualTo(DockState.Docked));
        Assert.That(a.DockState, Is.EqualTo(DockState.Document));
        Assert.That(dock.GroupOf(a), Is.Not.SameAs(dock.GroupOf(b)));
    }

    [Test]
    public void Dragging_below_the_threshold_only_activates_the_tab()
    {
        var dock = new DockPanel();
        var a = Pane("a");
        var b = Pane("b");
        var (_, _, canvas) = Realize(dock);
        dock.AddDocument(a);
        dock.AddDocument(b);
        a.Activate();

        canvas.RaiseMouseDown(200, 400 - 5); // tab b
        canvas.RaiseMouseUp(201, 400 - 5);   // no real move

        Assert.That(dock.ActiveContent, Is.SameAs(b));
        Assert.That(b.DockState, Is.EqualTo(DockState.Document)); // still a document
    }

    // --- Keyboard -----------------------------------------------------------------------------

    [Test]
    public void Ctrl_Tab_cycles_documents()
    {
        var dock = new DockPanel();
        var a = Pane("a");
        var b = Pane("b");
        var (_, _, canvas) = Realize(dock);
        dock.AddDocument(a);
        dock.AddDocument(b);
        a.Activate();
        dock.Focus();

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control);
        Assert.That(dock.ActiveContent, Is.SameAs(b));

        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Control);
        Assert.That(dock.ActiveContent, Is.SameAs(a));
    }

    // --- Persistence --------------------------------------------------------------------------

    [Test]
    public void Layout_round_trips_through_save_and_load()
    {
        var dock = new DockPanel();
        var doc = Pane("doc");
        var left = Pane("left");
        var bottom = Pane("bottom");
        var hidden = Pane("props");
        var floated = Pane("float");
        Realize(dock);
        dock.AddDocument(doc);
        dock.Add(left, DockState.Docked, DockEdge.Left);
        dock.Add(bottom, DockState.Docked, DockEdge.Bottom);
        dock.Add(hidden, DockState.AutoHide, DockEdge.Right);
        dock.Add(floated, DockState.Floating);

        var saved = dock.SaveLayout();

        // Rearrange, then restore.
        left.DockState = DockState.Document;
        floated.DockState = DockState.Document;

        var map = new Dictionary<string, DockContent>
        {
            ["doc"] = doc, ["left"] = left, ["bottom"] = bottom, ["props"] = hidden, ["float"] = floated,
        };
        dock.LoadLayout(saved, key => map.GetValueOrDefault(key));

        Assert.That(doc.DockState, Is.EqualTo(DockState.Document));
        Assert.That(left.DockState, Is.EqualTo(DockState.Docked));
        Assert.That(bottom.DockState, Is.EqualTo(DockState.Docked));
        Assert.That(hidden.DockState, Is.EqualTo(DockState.AutoHide));
        Assert.That(hidden.DockEdge, Is.EqualTo(DockEdge.Right));
        Assert.That(floated.DockState, Is.EqualTo(DockState.Floating));
        Assert.That(dock.FloatingWindowCount, Is.EqualTo(1));
    }

    [Test]
    public void Load_skips_unresolvable_keys()
    {
        var dock = new DockPanel();
        var doc = Pane("doc");
        Realize(dock);
        dock.AddDocument(doc);
        var saved = dock.SaveLayout();

        // A resolver that knows nothing leaves an empty, valid panel.
        dock.LoadLayout(saved, _ => null);
        Assert.That(dock.RootNode, Is.Null);
    }

    [Test]
    public void Saved_layout_carries_the_header()
    {
        var dock = new DockPanel();
        dock.AddDocument(Pane("doc"));
        Assert.That(dock.SaveLayout(), Does.StartWith("NFDOCK1|"));
    }
}
