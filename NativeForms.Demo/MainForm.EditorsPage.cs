using System.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>A model the property grid inspects through delegate get/set (never reflection).</summary>
    private sealed class WidgetModel
    {
        public string Name = "Save button";
        public bool Enabled = true;
        public int Width = 120;
        public string Align = "Center";
        public string Accent = "#FF0078D4";
    }

    /// <summary>The Editors page (§7.10): a reflection-free <see cref="PropertyGrid"/> inspecting a model.</summary>
    private TabPage BuildEditorsPage()
    {
        var page = new TabPage("Editors") { ImageIndex = _IconPurple };
        var model = new WidgetModel();

        var grid = new PropertyGrid { Bounds = new(16, 36, 380, 380) };
        grid.AddRow(new PropertyGridRow("Name", () => model.Name, v => model.Name = v)
        {
            Category = "Appearance",
            Description = "The caption shown on the widget.",
        });
        grid.AddRow(new PropertyGridRow("Accent", () => model.Accent, v => model.Accent = v)
        {
            Category = "Appearance",
            Editor = PropertyGridEditor.Color,
            Description = "The widget's accent colour (hex RRGGBBAA).",
        });
        grid.AddRow(new PropertyGridRow("Enabled", () => model.Enabled ? "True" : "False", v => model.Enabled = v == "True")
        {
            Category = "Behavior",
            Editor = PropertyGridEditor.Boolean,
            Description = "Whether the widget responds to input.",
        });
        grid.AddRow(new PropertyGridRow("Width", () => model.Width.ToString(), v => { if (int.TryParse(v, out var w)) model.Width = w; })
        {
            Category = "Layout",
            Editor = PropertyGridEditor.Number,
            Description = "The widget width in pixels.",
        });
        grid.AddRow(new PropertyGridRow("Align", () => model.Align, v => model.Align = v)
        {
            Category = "Layout",
            Editor = PropertyGridEditor.Choice,
            Choices = new[] { "Left", "Center", "Right" },
            Description = "Horizontal alignment of the caption.",
        });
        grid.PropertyValueChanged += (_, e) => this.SetStatus($"PropertyGrid: {e.Row.Name} = {e.NewValue}.");

        page.Controls.AddRange(
            Caption("PropertyGrid (categories · typed inline editors · reflection-free)", 16, 12, 480),
            grid);

        return page;
    }
}
