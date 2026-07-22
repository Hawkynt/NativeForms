using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// <see cref="GridPicker"/> must map a hovered point to the top-left cell block, report the block as
/// <see cref="GridPicker.Rows"/>/<see cref="GridPicker.Columns"/>, commit it through
/// <see cref="GridPicker.RangeSelected"/> on a click or Enter, walk the block with the arrow keys and
/// cancel on Escape — all headlessly.
/// </summary>
[TestFixture]
internal sealed class GridPickerTests
{
    // GridPickerCore: 18px cells, 6px padding. Cell (col, row) 1-based centre sits at
    // 6 + (col-1)*18 + 9 and 6 + (row-1)*18 + 9.
    private static int CellCentre(int oneBased) => 6 + ((oneBased - 1) * 18) + 9;

    private static HeadlessCanvasPeer Realize(GridPicker picker)
    {
        var backend = new HeadlessBackend();
        var form = new Form { Bounds = new(0, 0, 400, 400) };
        form.Controls.Add(picker);
        Application.Run(form, backend);
        return backend.Created.OfType<HeadlessCanvasPeer>().First();
    }

    [Test]
    public void Hovering_a_cell_reports_its_block()
    {
        var picker = new GridPicker { Bounds = new(0, 0, 200, 200), MaxColumns = 6, MaxRows = 5 };
        var canvas = Realize(picker);

        canvas.RaiseMouseMove(CellCentre(3), CellCentre(2));

        Assert.Multiple(() =>
        {
            Assert.That(picker.Columns, Is.EqualTo(3));
            Assert.That(picker.Rows, Is.EqualTo(2));
        });
    }

    [Test]
    public void Leaving_the_control_clears_the_block()
    {
        var picker = new GridPicker { Bounds = new(0, 0, 200, 200) };
        var canvas = Realize(picker);
        canvas.RaiseMouseMove(CellCentre(2), CellCentre(2));
        Assert.That(picker.Columns, Is.EqualTo(2));

        canvas.RaiseMouseLeave();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Columns, Is.Zero);
            Assert.That(picker.Rows, Is.Zero);
        });
    }

    [Test]
    public void Clicking_a_cell_commits_its_dimensions()
    {
        var picker = new GridPicker { Bounds = new(0, 0, 200, 200), MaxColumns = 8, MaxRows = 6 };
        var canvas = Realize(picker);
        var committed = default((int Rows, int Columns)?);
        picker.RangeSelected += (_, e) => committed = (e.Rows, e.Columns);

        canvas.RaiseMouseDown(CellCentre(4), CellCentre(3));

        Assert.That(committed, Is.EqualTo((3, 4)));
    }

    [Test]
    public void The_arrow_keys_walk_the_block_and_enter_commits_it()
    {
        var picker = new GridPicker { Bounds = new(0, 0, 200, 200) };
        var canvas = Realize(picker);
        var committed = default((int Rows, int Columns)?);
        picker.RangeSelected += (_, e) => committed = (e.Rows, e.Columns);

        canvas.RaiseKeyDown(Keys.Right); // the first press lands on the first cell (1×1)
        Assert.Multiple(() =>
        {
            Assert.That(picker.Columns, Is.EqualTo(1));
            Assert.That(picker.Rows, Is.EqualTo(1));
        });

        canvas.RaiseKeyDown(Keys.Right);
        canvas.RaiseKeyDown(Keys.Down);
        Assert.Multiple(() =>
        {
            Assert.That(picker.Columns, Is.EqualTo(2));
            Assert.That(picker.Rows, Is.EqualTo(2));
        });

        canvas.RaiseKeyDown(Keys.Enter);
        Assert.That(committed, Is.EqualTo((2, 2)));
    }

    [Test]
    public void Escape_raises_cancel_without_committing()
    {
        var picker = new GridPicker { Bounds = new(0, 0, 200, 200) };
        var canvas = Realize(picker);
        var committed = false;
        var cancelled = false;
        picker.RangeSelected += (_, _) => committed = true;
        picker.Canceled += (_, _) => cancelled = true;

        canvas.RaiseMouseMove(CellCentre(2), CellCentre(2));
        canvas.RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(cancelled, Is.True);
            Assert.That(committed, Is.False);
        });
    }

    [Test]
    public void The_block_never_leaves_the_grid()
    {
        var picker = new GridPicker { Bounds = new(0, 0, 200, 200), MaxColumns = 3, MaxRows = 3 };
        var canvas = Realize(picker);

        // A point well past the last column/row must not select a phantom cell.
        canvas.RaiseMouseMove(CellCentre(3) + 400, CellCentre(3) + 400);

        Assert.Multiple(() =>
        {
            Assert.That(picker.Columns, Is.Zero);
            Assert.That(picker.Rows, Is.Zero);
        });
    }
}
