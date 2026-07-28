using System;
using System.Drawing;
using System.Linq;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// A model exercising every <see cref="DataGridViewColumnKind"/> the grid ships: the ones the generator
/// infers from the property's type, and the rest pinned with <c>[GridColumnKind]</c>.
/// </summary>
[GridEditable]
internal partial class EveryColumnKind
{
    // --- Inferred from the type ---------------------------------------------------------------
    public string Text { get; set; } = "text";

    public bool Check { get; set; }

    public int Number { get; set; }

    public KindDock Choice { get; set; }

    public KindPermissions Flags { get; set; }

    public DateTime Moment { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly Clock { get; set; }

    public Color Swatch { get; set; }

    // --- Pinned, because no property type implies them -----------------------------------------
    [GridColumnKind(DataGridViewColumnKind.Button)]
    public string Action { get; set; } = "Go";

    [GridColumnKind(DataGridViewColumnKind.Link)]
    public string Href { get; set; } = "https://example.invalid";

    [GridColumnKind(DataGridViewColumnKind.MultiImage)]
    public string Badges { get; set; } = string.Empty;

    [GridColumnKind(DataGridViewColumnKind.Progress)]
    public int Done { get; set; }

    [GridColumnKind(DataGridViewColumnKind.MaskedText)]
    public string Phone { get; set; } = string.Empty;

    [GridColumnKind(DataGridViewColumnKind.DomainUpDown)]
    public string Size { get; set; } = "M";

    [GridColumnKind(DataGridViewColumnKind.ListBox)]
    public string Pick { get; set; } = string.Empty;
}

internal enum KindDock { None, Left, Top }

[Flags]
internal enum KindPermissions { None = 0, Read = 1, Write = 2 }

/// <summary>
/// PRD §15: every column kind the grid ships has to be reachable from the model's attributes, or the
/// generator is a shortcut rather than a replacement for writing the columns by hand. Inference covers the
/// kinds a property type implies; <c>[GridColumnKind]</c> covers the rest, and this fixture is what proves
/// there is no kind you cannot ask for.
/// </summary>
[TestFixture]
internal sealed class GeneratedColumnKindTests
{
    private static DataGridView Populated()
    {
        var grid = new DataGridView();
        EveryColumnKind.PopulateColumns(grid);
        return grid;
    }

    private static DataGridViewColumnKind KindOf(DataGridView grid, string header)
        => grid.Columns.Single(column => column.HeaderText == header).Kind;

    [Test]
    public void Every_kind_the_grid_ships_is_produced_by_some_annotation()
    {
        var grid = Populated();
        var produced = grid.Columns.Select(column => column.Kind).Distinct().ToArray();

        var missing = Enum.GetValues<DataGridViewColumnKind>().Except(produced).ToArray();

        Assert.That(
            missing,
            Is.Empty,
            $"no annotation reaches: {string.Join(", ", missing)} — add it to EveryColumnKind, or to the generator");
    }

    [TestCase("Text", DataGridViewColumnKind.Text)]
    [TestCase("Check", DataGridViewColumnKind.Check)]
    [TestCase("Number", DataGridViewColumnKind.NumericUpDown)]
    [TestCase("Choice", DataGridViewColumnKind.ComboBox)]
    [TestCase("Flags", DataGridViewColumnKind.CheckedListBox)]
    [TestCase("Moment", DataGridViewColumnKind.DateTime)]
    [TestCase("Day", DataGridViewColumnKind.DateTime)]
    [TestCase("Clock", DataGridViewColumnKind.TimePicker)]
    [TestCase("Swatch", DataGridViewColumnKind.Color)]
    public void A_property_type_infers_its_kind(string header, DataGridViewColumnKind expected)
        => Assert.That(KindOf(Populated(), header), Is.EqualTo(expected));

    [TestCase("Action", DataGridViewColumnKind.Button)]
    [TestCase("Href", DataGridViewColumnKind.Link)]
    [TestCase("Badges", DataGridViewColumnKind.MultiImage)]
    [TestCase("Done", DataGridViewColumnKind.Progress)]
    [TestCase("Phone", DataGridViewColumnKind.MaskedText)]
    [TestCase("Size", DataGridViewColumnKind.DomainUpDown)]
    [TestCase("Pick", DataGridViewColumnKind.ListBox)]
    public void An_explicit_kind_overrides_what_the_type_would_infer(string header, DataGridViewColumnKind expected)
        => Assert.That(KindOf(Populated(), header), Is.EqualTo(expected));

    [Test]
    public void A_flags_enum_and_a_plain_enum_infer_different_kinds()
    {
        var grid = Populated();

        Assert.Multiple(() =>
        {
            Assert.That(KindOf(grid, "Choice"), Is.EqualTo(DataGridViewColumnKind.ComboBox), "one value at a time");
            Assert.That(KindOf(grid, "Flags"), Is.EqualTo(DataGridViewColumnKind.CheckedListBox), "several at once");
        });
    }

    [Test]
    public void The_same_model_also_drives_the_inspector_and_the_list()
    {
        var propertyGrid = new PropertyGrid();
        var listView = new ListView();
        var model = new EveryColumnKind();

        model.PopulateGrid(propertyGrid);
        EveryColumnKind.PopulateColumns(listView);

        Assert.Multiple(() =>
        {
            Assert.That(propertyGrid.Rows, Is.Not.Empty, "one annotated model, three populators");
            Assert.That(listView.Columns, Is.Not.Empty);
            Assert.That(model.ToListViewItem(), Is.Not.Null);
        });
    }
}
