using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>A top-level model the source generator turns into a <c>PopulateGrid</c> method.</summary>
[GridEditable]
internal partial class GeneratedSettings
{
    [GridCategory("General")]
    [GridDescription("The display name.")]
    public string Name { get; set; } = "Widget";

    [GridCategory("Behavior")]
    public bool Enabled { get; set; } = true;

    [GridCategory("Layout")]
    [GridRange(0, 400)]
    public int Width { get; set; } = 120;

    [GridCategory("Layout")]
    public GeneratedDock Dock { get; set; } = GeneratedDock.Top;

    [GridIgnore]
    public string Secret { get; set; } = "hidden";
}

internal enum GeneratedDock { None, Left, Top, Right, Bottom }

/// <summary>
/// The <c>NativeForms.Generators</c> source generator emits a reflection-free <c>PopulateGrid</c> that adds a
/// row per public settable property, honouring the <c>Grid*</c> attributes.
/// </summary>
[TestFixture]
internal sealed class PropertyGridGeneratorTests
{
    private static PropertyGrid Realize(PropertyGrid grid)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(grid);
        Application.Run(form, backend);
        return grid;
    }

    [Test]
    public void PopulateGrid_adds_a_row_per_editable_property_with_attributes()
    {
        var model = new GeneratedSettings();
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };

        model.PopulateGrid(grid); // generated

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Select(r => r.Name), Is.EquivalentTo(new[] { "Name", "Enabled", "Width", "Dock" }));
            Assert.That(grid.Rows.Any(r => r.Name == "Secret"), Is.False, "[GridIgnore] excludes the property");

            var name = grid.Rows.Single(r => r.Name == "Name");
            Assert.That(name.Category, Is.EqualTo("General"));
            Assert.That(name.Description, Is.EqualTo("The display name."));

            var width = grid.Rows.Single(r => r.Name == "Width");
            Assert.That(width.Editor, Is.EqualTo(PropertyGridEditor.Number));
            Assert.That(width.Minimum, Is.EqualTo(0));
            Assert.That(width.Maximum, Is.EqualTo(400));

            Assert.That(grid.Rows.Single(r => r.Name == "Enabled").Editor, Is.EqualTo(PropertyGridEditor.Boolean));
            Assert.That(grid.Rows.Single(r => r.Name == "Dock").Editor, Is.EqualTo(PropertyGridEditor.Choice));
        });
    }

    [Test]
    public void The_generated_rows_write_back_through_the_model()
    {
        var model = new GeneratedSettings();
        var grid = new PropertyGrid { Bounds = new(0, 0, 300, 300) };
        model.PopulateGrid(grid);
        Realize(grid);

        grid.Rows.Single(r => r.Name == "Name").Set!("Renamed");
        grid.Rows.Single(r => r.Name == "Dock").Set!("Bottom");

        Assert.Multiple(() =>
        {
            Assert.That(model.Name, Is.EqualTo("Renamed"));
            Assert.That(model.Dock, Is.EqualTo(GeneratedDock.Bottom));
        });
    }
}
