using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    private DockPanel? _dock;
    private string? _savedDockLayout;
    private Action? _resetDockLayout;
    private readonly Dictionary<string, DockContent> _dockPanes = new(StringComparer.Ordinal);

    /// <summary>
    /// The Docking page: a Visual-Studio-style <see cref="DockPanel"/> hosting a central document tab
    /// well (two "open files"), a Solution pane docked left, a Properties pane docked right, an Output
    /// pane docked bottom, a Toolbox pane auto-hidden to the left edge, and a Find-results pane that
    /// tears off into a floating window once the gallery is shown. Buttons save, load and reset the
    /// arrangement; dragging any pane caption onto a docking guide re-docks it live.
    /// </summary>
    private TabPage BuildDockingPage()
    {
        var page = new TabPage("Docking") { ImageIndex = _IconGear };

        var caption = Caption(
            "DockPanel — drag a pane caption onto a guide to re-dock; splitters resize; pin auto-hides.",
            16, 10, 640);

        var save = new Button { Bounds = new(16, 34, 120, 26), Text = "Save layout" };
        var load = new Button { Bounds = new(144, 34, 120, 26), Text = "Load layout" };
        var reset = new Button { Bounds = new(272, 34, 130, 26), Text = "Reset layout" };
        var floatBtn = new Button { Bounds = new(410, 34, 150, 26), Text = "Float Output" };

        var dock = new DockPanel
        {
            ImageList = _icons,
            Bounds = new(16, 68, 952, 520),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        _dock = dock;

        var editor1 = MakeDockPane("doc.program", "Program.cs", _IconFile,
            "// Program.cs\n\nusing Hawkynt.NativeForms;\n\nvar app = new Form();\nApplication.Run(app);");
        var editor2 = MakeDockPane("doc.readme", "Readme.md", _IconFile,
            "# NativeForms\n\nA tiny, native, trim-friendly UI toolkit.\nThis pane lives in the document tab well.");
        var solution = MakeDockPane("tool.solution", "Solution Explorer", _IconFolder,
            "Solution 'NativeForms'\n  NativeForms.Core\n  NativeForms.Backends.Gtk\n  NativeForms.Demo");
        var properties = MakeDockPane("tool.properties", "Properties", _IconGear,
            "(Name)      dockPanel1\nDock        Fill\nBackColor   Control\nTabIndex    0");
        var output = MakeDockPane("tool.output", "Output", _IconRun,
            "Build started...\n  NativeForms.Core -> ok\n  NativeForms.Demo -> ok\nBuild succeeded.");
        var toolbox = MakeDockPane("tool.toolbox", "Toolbox", _IconBlue,
            "Pointer\nButton\nLabel\nTextBox\nDockPanel");
        var find = MakeDockPane("tool.find", "Find Results", _IconGreen,
            "Find all \"DockPanel\":\n  DockPanel.cs (12)\n  DockContent.cs (3)\n  MainForm.DockingPage.cs (7)");

        foreach (var pane in new[] { editor1, editor2, solution, properties, output, toolbox, find })
            _dockPanes[pane.Name] = pane;

        // The default arrangement, reused by the Reset button and the autopilot restore step.
        _resetDockLayout = () =>
        {
            dock.Add(editor1, DockState.Document);
            dock.Add(editor2, DockState.Document);
            dock.Add(solution, DockState.Docked, DockEdge.Left);
            dock.Add(properties, DockState.Docked, DockEdge.Right);
            dock.Add(output, DockState.Docked, DockEdge.Bottom);
            dock.Add(toolbox, DockState.AutoHide, DockEdge.Left);
            dock.Add(find, DockState.Document);
            editor1.Activate();
        };

        // Build the arrangement now (Find stays parked until the window is shown so its float window
        // has a running loop to live on).
        dock.Add(editor1, DockState.Document);
        dock.Add(editor2, DockState.Document);
        dock.Add(solution, DockState.Docked, DockEdge.Left);
        dock.Add(properties, DockState.Docked, DockEdge.Right);
        dock.Add(output, DockState.Docked, DockEdge.Bottom);
        dock.Add(toolbox, DockState.AutoHide, DockEdge.Left);
        dock.Add(find, DockState.Document);
        editor1.Activate();

        save.Click += (_, _) =>
        {
            _savedDockLayout = dock.SaveLayout();
            this.SetStatus("Docking: layout saved.");
        };
        load.Click += (_, _) =>
        {
            if (_savedDockLayout is not { } layout)
            {
                this.SetStatus("Docking: nothing saved yet.");
                return;
            }

            dock.LoadLayout(layout, key => _dockPanes.GetValueOrDefault(key));
            this.SetStatus("Docking: layout restored.");
        };
        reset.Click += (_, _) =>
        {
            _resetDockLayout?.Invoke();
            this.SetStatus("Docking: layout reset.");
        };
        floatBtn.Click += (_, _) =>
        {
            output.DockState = output.DockState == DockState.Floating ? DockState.Docked : DockState.Floating;
            this.SetStatus($"Docking: Output is {output.DockState}.");
        };

        page.Controls.AddRange(caption, save, load, reset, floatBtn, dock);

        this.Publish("docking.page", page);
        this.Publish("docking.dock", dock);
        this.Publish("docking.solution", solution);
        this.Publish("docking.properties", properties);
        this.Publish("docking.output", output);
        this.Publish("docking.toolbox", toolbox);
        this.Publish("docking.find", find);
        this.Publish("docking.editor1", editor1);
        this.Publish("docking.editor2", editor2);
        this.Publish("docking.save", save);
        this.Publish("docking.load", load);
        this.Publish("docking.reset", reset);
        this.Publish("docking.float", floatBtn);
        this.OnReset(() => _resetDockLayout?.Invoke());
        return page;
    }

    private static DockContent MakeDockPane(string name, string title, int imageIndex, string body)
    {
        var pane = new DockContent(title) { Name = name, ImageIndex = imageIndex };
        pane.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = body,
            TextAlign = ContentAlignment.TopLeft,
        });
        return pane;
    }
}
