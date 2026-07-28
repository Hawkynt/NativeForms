using System.Linq;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>A top-level model the source generator turns into a <c>PopulateGrid</c> method.</summary>
[GridEditable]
[GridRowHiddenWhen(nameof(IsArchived))]
[GridRowSelectableWhen(nameof(IsSelectable))]
[GridRowHeightFrom(nameof(PreferredHeight))]
internal partial class GeneratedSettings
{
    // Gating members: hidden from the UI, but still resolvable by name from the attributes above.
    [GridIgnore] public bool IsArchived { get; set; }
    [GridIgnore] public bool IsSelectable { get; set; } = true;
    [GridIgnore] public int PreferredHeight { get; set; } = 24;

    [GridCategory("General")]
    [GridDescription("The display name.")]
    public string Name { get; set; } = "Widget";

    [GridCategory("Behavior")]
    public bool Enabled { get; set; } = true;

    [GridCategory("Layout")]
    [GridRange(0, 400)]
    [GridColumnWidth(90)]
    [GridColumnSortMode(DataGridViewColumnSortMode.Automatic)]
    [GridColumnReadOnlyWhen(nameof(IsArchived))]
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

    [Test]
    public void PopulateColumns_builds_a_grid_from_the_same_model()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 400, 200) };

        GeneratedSettings.PopulateColumns(grid); // generated

        var names = grid.Columns.Select(c => c.HeaderText).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Name"));
            Assert.That(names, Does.Contain("Width"));
            Assert.That(names, Does.Not.Contain("Secret"), "[GridIgnore] excludes the column too");

            var width = grid.Columns.Single(c => c.HeaderText == "Width");
            Assert.That(width.Kind, Is.EqualTo(DataGridViewColumnKind.NumericUpDown), "int infers a numeric column");
            Assert.That(width.Width, Is.EqualTo(90), "[GridColumnWidth]");
            Assert.That(width.SortMode, Is.EqualTo(DataGridViewColumnSortMode.Automatic), "[GridColumnSortMode]");

            Assert.That(grid.Columns.Single(c => c.HeaderText == "Enabled").Kind, Is.EqualTo(DataGridViewColumnKind.Check));
            Assert.That(grid.Columns.Single(c => c.HeaderText == "Dock").Kind, Is.EqualTo(DataGridViewColumnKind.ComboBox));
            Assert.That(grid.Columns.Single(c => c.HeaderText == "Name").Kind, Is.EqualTo(DataGridViewColumnKind.Text));
        });
    }

    [Test]
    public void Generated_columns_read_and_write_through_the_model()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 400, 200) };
        GeneratedSettings.PopulateColumns(grid);
        var model = new GeneratedSettings { Name = "Row" };

        var name = grid.Columns.Single(c => c.HeaderText == "Name");
        Assert.That(name.ValueSelector(model), Is.EqualTo("Row"), "the generated selector reads the model");

        name.ValueSetter!(model, "Renamed");
        Assert.That(model.Name, Is.EqualTo("Renamed"), "the generated setter writes back");
    }

    [Test]
    public void Class_level_rules_wire_the_grids_row_selectors()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 400, 200) };
        GeneratedSettings.PopulateColumns(grid);
        var archived = new GeneratedSettings { IsArchived = true, IsSelectable = false, PreferredHeight = 40 };

        Assert.Multiple(() =>
        {
            Assert.That(grid.RowHiddenSelector!(archived), Is.True, "[GridRowHiddenWhen]");
            Assert.That(grid.RowSelectableSelector!(archived), Is.False, "[GridRowSelectableWhen]");
            Assert.That(grid.RowHeightSelector!(archived), Is.EqualTo(40), "[GridRowHeightFrom]");
        });
    }

    [Test]
    public void A_conditional_read_only_column_resolves_its_named_property()
    {
        var grid = new DataGridView { Bounds = new(0, 0, 400, 200) };
        GeneratedSettings.PopulateColumns(grid);
        var width = grid.Columns.Single(c => c.HeaderText == "Width");

        Assert.Multiple(() =>
        {
            Assert.That(width.ReadOnlyCellSelector!(new GeneratedSettings { IsArchived = true }), Is.True);
            Assert.That(width.ReadOnlyCellSelector!(new GeneratedSettings { IsArchived = false }), Is.False);
        });
    }
}
