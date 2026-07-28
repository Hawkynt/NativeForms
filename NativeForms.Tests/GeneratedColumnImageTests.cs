using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Hawkynt.NativeForms.Drawing;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>A model whose columns get their images from the annotations rather than from hand-written selectors.</summary>
[GridEditable]
internal partial class ImageAnnotatedRow
{
    /// <summary>The icon <see cref="Name"/>'s column draws beside its text.</summary>
    [GridIgnore]
    public IImage? Icon { get; set; }

    /// <summary>The strip <see cref="Rating"/>'s column draws.</summary>
    [GridIgnore]
    public IReadOnlyList<IImage> Stars { get; set; } = [];

    /// <summary>The badges stacked over <see cref="State"/>'s column.</summary>
    [GridIgnore]
    public IReadOnlyList<IImage> Badges { get; set; } = [];

    [GridColumnImage(nameof(Icon))]
    [GridColumnTextImageRelation(TextImageRelation.TextBeforeImage)]
    [GridColumnImageSize(24, 16, false)]
    public string Name { get; set; } = "row";

    [GridColumnKind(DataGridViewColumnKind.MultiImage)]
    [GridColumnImages(nameof(Stars))]
    public string Rating { get; set; } = string.Empty;

    [GridColumnOverlayImages(nameof(Badges))]
    public string State { get; set; } = "ok";
}

/// <summary>
/// PRD §15.2: the grid's image model is reachable from the model's attributes, not only from
/// hand-written selectors — one icon beside the text, a strip of them, a stack of overlay badges, the box
/// they are drawn into and which side of the text the icon sits on. Each named member is resolved at
/// compile time, which is the whole point of the generator over the reflection-based reference library.
/// </summary>
[TestFixture]
internal sealed class GeneratedColumnImageTests
{
    private static DataGridViewColumn Column(DataGridView grid, string header)
        => grid.Columns.Single(column => column.HeaderText == header);

    private static DataGridView Populated()
    {
        var grid = new DataGridView();
        ImageAnnotatedRow.PopulateColumns(grid);
        return grid;
    }

    [Test]
    public void A_named_image_property_becomes_the_columns_ImageSelector()
    {
        var icon = new HeadlessImage(8, 8);
        var row = new ImageAnnotatedRow { Icon = icon };

        var selector = Column(Populated(), "Name").ImageSelector;

        Assert.Multiple(() =>
        {
            Assert.That(selector, Is.Not.Null, "the annotation should have wired one");
            Assert.That(selector!(row), Is.SameAs(icon));
        });
    }

    [Test]
    public void A_row_without_an_image_simply_has_none()
    {
        var selector = Column(Populated(), "Name").ImageSelector;

        Assert.That(selector!(new ImageAnnotatedRow()), Is.Null);
    }

    [Test]
    public void A_named_list_property_becomes_the_columns_ImagesSelector()
    {
        var row = new ImageAnnotatedRow { Stars = [new HeadlessImage(8, 8), new HeadlessImage(8, 8)] };

        var selector = Column(Populated(), "Rating").ImagesSelector;

        Assert.Multiple(() =>
        {
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector!(row), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void A_named_list_property_becomes_the_columns_OverlayImagesSelector()
    {
        var row = new ImageAnnotatedRow { Badges = [new HeadlessImage(8, 8)] };

        var selector = Column(Populated(), "State").OverlayImagesSelector;

        Assert.Multiple(() =>
        {
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector!(row), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void The_image_box_and_its_aspect_rule_come_from_the_annotation()
    {
        var column = Column(Populated(), "Name");

        Assert.Multiple(() =>
        {
            Assert.That(column.ImageSize, Is.EqualTo(new Size(24, 16)));
            Assert.That(column.KeepImageAspectRatio, Is.False, "the annotation asked for a stretch");
        });
    }

    [Test]
    public void The_image_relation_comes_from_the_annotation()
        => Assert.That(Column(Populated(), "Name").TextImageRelation, Is.EqualTo(TextImageRelation.TextBeforeImage));

    [Test]
    public void An_unannotated_column_keeps_the_defaults()
    {
        var column = Column(Populated(), "State");

        Assert.Multiple(() =>
        {
            Assert.That(column.ImageSelector, Is.Null);
            Assert.That(column.ImagesSelector, Is.Null);
            Assert.That(
                column.TextImageRelation,
                Is.EqualTo(TextImageRelation.ImageBeforeText),
                "the column's own default, untouched");
        });
    }
}
