using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Tests.Fakes;

namespace Hawkynt.NativeForms.Tests;

/// <summary>
/// In-place cell editing: the hosted <see cref="TextBox"/>/<see cref="NumericUpDown"/> editors and
/// the combo/calendar popups, the begin/commit/cancel paths with their cancelable events
/// (<see cref="DataGridView.CellBeginEdit"/>, <see cref="DataGridView.CellValidating"/>,
/// <see cref="DataGridView.CellEndEdit"/>), the read-only gating, every entry gesture (double-click,
/// F2, typing) and the editor geometry following the cell under scroll — including the scroll-out
/// commit.
/// </summary>
[TestFixture]
internal sealed class DataGridViewEditingTests
{
    private sealed class Row
    {
        public string Name = string.Empty;
        public int Age;
        public string Status = string.Empty;
        public DateTime When;
    }

    private static HeadlessBackend RealizeBackend(OwnerDrawnControl control)
    {
        var backend = new HeadlessBackend();
        var form = new Form();
        form.Controls.Add(control);
        Application.Run(form, backend);
        return backend;
    }

    private static HeadlessCanvasPeer CanvasOf(HeadlessBackend backend)
        => backend.Created.OfType<HeadlessCanvasPeer>().First(static peer => peer is not HeadlessPopupPeer);

    private static DataGridView MakeGrid(out List<Row> rows)
    {
        // 22px header + 4 rows at 22px; columns at x 0..100 and 100..160.
        rows = [new() { Name = "Alice", Age = 30 }, new() { Name = "Bob", Age = 25 }, new() { Name = "Carol", Age = 40 }];
        var grid = new DataGridView { Bounds = new(0, 0, 200, 110) };
        grid.Columns.Add(new DataGridViewColumn("Name", static o => ((Row)o!).Name)
        {
            TextSetter = static (o, value) => ((Row)o!).Name = value,
        });
        grid.Columns.Add(new DataGridViewColumn("Age", static o => ((Row)o!).Age) { Width = 60 });
        grid.DataSource = rows;
        return grid;
    }

    [Test]
    public void BeginEdit_hosts_a_text_editor_over_the_cell()
    {
        var grid = MakeGrid(out _);
        var backend = RealizeBackend(grid);

        var began = grid.BeginEdit(1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(began, Is.True);
            Assert.That(grid.IsEditing, Is.True);
            Assert.That(grid.EditingControl, Is.InstanceOf<TextBox>());
            Assert.That(grid.EditingControl!.Text, Is.EqualTo("Bob"), "editor seeds from the cell text");
            Assert.That(grid.EditingControl.Bounds, Is.EqualTo(new Rectangle(0, 44, 100, 22)), "editor covers the cell");
            Assert.That(backend.Created.OfType<HeadlessTextBoxPeer>().Count(), Is.EqualTo(1), "editor realized as a native child");
        });
    }

    [Test]
    public void BeginEdit_raises_CellBeginEdit_and_honors_the_veto()
    {
        var grid = MakeGrid(out _);
        RealizeBackend(grid);
        DataGridViewCellCancelEventArgs? begin = null;
        grid.CellBeginEdit += (_, e) =>
        {
            begin = e;
            e.Cancel = true;
        };

        var began = grid.BeginEdit(0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(begin, Is.Not.Null);
            Assert.That(begin!.RowIndex, Is.Zero);
            Assert.That(begin.ColumnIndex, Is.Zero);
            Assert.That(began, Is.False);
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(grid.EditingControl, Is.Null);
        });
    }

    [Test]
    public void Read_only_gating_refuses_every_level()
    {
        var grid = MakeGrid(out _);
        RealizeBackend(grid);
        var column = grid.Columns[0];

        grid.ReadOnly = true;
        Assert.That(grid.BeginEdit(0, 0), Is.False, "grid level");
        grid.ReadOnly = false;

        column.ReadOnly = true;
        Assert.That(grid.BeginEdit(0, 0), Is.False, "column level");
        column.ReadOnly = false;

        column.ReadOnlyCellSelector = static o => ((Row)o!).Name == "Alice";
        Assert.Multiple(() =>
        {
            Assert.That(grid.BeginEdit(0, 0), Is.False, "cell level");
            Assert.That(grid.BeginEdit(1, 0), Is.True, "other cells stay editable");
        });
    }

    [Test]
    public void Text_column_without_a_setter_is_display_only()
    {
        var grid = MakeGrid(out _);
        RealizeBackend(grid);

        Assert.That(grid.BeginEdit(0, 1), Is.False, "the Age column has no TextSetter");
    }

    [Test]
    public void CommitEdit_writes_through_the_text_setter_and_raises_CellEndEdit()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        DataGridViewCellEventArgs? ended = null;
        grid.CellEndEdit += (_, e) => ended = e;
        grid.BeginEdit(0, 0);

        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Alicia");
        var committed = grid.CommitEdit();

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.True);
            Assert.That(rows[0].Name, Is.EqualTo("Alicia"), "written through the setter");
            Assert.That(ended, Is.Not.Null);
            Assert.That(ended!.RowIndex, Is.Zero);
            Assert.That(ended.ColumnIndex, Is.Zero);
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(grid.EditingControl, Is.Null, "editor torn down");
            Assert.That(grid.Controls, Is.Empty, "no hosted child left behind");
        });
    }

    [Test]
    public void CellValidating_veto_keeps_the_cell_editing_and_writes_nothing()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        object? proposed = null;
        var veto = true;
        grid.CellValidating += (_, e) =>
        {
            proposed = e.ProposedValue;
            e.Cancel = veto;
        };
        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("bad");

        Assert.Multiple(() =>
        {
            Assert.That(grid.CommitEdit(), Is.False);
            Assert.That(proposed, Is.EqualTo("bad"), "the proposed text travels with the event");
            Assert.That(grid.IsEditing, Is.True, "the veto keeps the cell in edit mode");
            Assert.That(rows[0].Name, Is.EqualTo("Alice"), "nothing was written");
        });

        veto = false;
        Assert.Multiple(() =>
        {
            Assert.That(grid.CommitEdit(), Is.True);
            Assert.That(rows[0].Name, Is.EqualTo("bad"), "the released commit writes");
        });
    }

    [Test]
    public void CancelEdit_discards_the_edit_without_writing()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        var ended = 0;
        grid.CellEndEdit += (_, _) => ++ended;
        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Alicia");

        grid.CancelEdit();

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Alice"));
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(ended, Is.EqualTo(1), "cancel still ends the edit");
        });
    }

    [Test]
    public void Enter_commits_and_Escape_cancels_on_the_grid_surface()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);

        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Ann");
        canvas.RaiseKeyDown(Keys.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Ann"), "Enter commits");
            Assert.That(grid.IsEditing, Is.False);
        });

        grid.BeginEdit(1, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Last().SimulateUserInput("Bobby");
        canvas.RaiseKeyDown(Keys.Escape);
        Assert.Multiple(() =>
        {
            Assert.That(rows[1].Name, Is.EqualTo("Bob"), "Escape cancels");
            Assert.That(grid.IsEditing, Is.False);
        });
    }

    [Test]
    public void F2_begins_editing_the_current_cell()
    {
        var grid = MakeGrid(out _);
        var backend = RealizeBackend(grid);
        grid.SelectedRowIndex = 1;
        grid.CurrentColumnIndex = 0;

        CanvasOf(backend).RaiseKeyDown(Keys.F2);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.True);
            Assert.That(grid.EditingControl!.Text, Is.EqualTo("Bob"));
        });
    }

    [Test]
    public void Double_click_begins_editing_the_cell()
    {
        var grid = MakeGrid(out _);
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);

        canvas.RaiseMouseDown(10, 30); // row 0, Name
        canvas.RaiseMouseDown(10, 30);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.True);
            Assert.That(grid.EditingControl!.Text, Is.EqualTo("Alice"));
        });
    }

    [Test]
    public void Typing_begins_editing_and_seeds_the_editor()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);
        grid.SelectedRowIndex = 0;

        canvas.RaiseKeyPress('x');

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.True);
            Assert.That(grid.EditingControl!.Text, Is.EqualTo("x"), "the typed character replaces the cell text");
        });

        canvas.RaiseKeyDown(Keys.Enter);
        Assert.That(rows[0].Name, Is.EqualTo("x"));
    }

    [Test]
    public void Typing_on_a_read_only_cell_does_not_begin_editing()
    {
        var grid = MakeGrid(out _);
        grid.ReadOnly = true;
        var backend = RealizeBackend(grid);
        grid.SelectedRowIndex = 0;

        CanvasOf(backend).RaiseKeyPress('x');

        Assert.That(grid.IsEditing, Is.False);
    }

    [Test]
    public void Scrolling_the_edited_row_out_of_view_commits()
    {
        var grid = MakeGrid(out var rows);
        for (var i = 0; i < 7; ++i)
            grid.Items.Add(new Row { Name = $"P{i}" });
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);
        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Zed");

        canvas.RaiseMouseWheel(-120); // 3 rows down — row 0 leaves the visible window

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False, "the scroll-out is a commit point");
            Assert.That(rows[0].Name, Is.EqualTo("Zed"), "committed, not cancelled — WinForms behavior");
        });
    }

    [Test]
    public void Editor_bounds_follow_the_cell_while_it_stays_visible()
    {
        var grid = MakeGrid(out _);
        for (var i = 0; i < 7; ++i)
            grid.Items.Add(new Row { Name = $"P{i}" });
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);
        grid.BeginEdit(3, 0); // display row 3 at y 88..110
        Assert.That(grid.EditingControl!.Bounds, Is.EqualTo(new Rectangle(0, 88, 100, 22)));

        canvas.RaiseMouseWheel(-120); // 3 rows down — the edited row moves to the top

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.True, "still visible, still editing");
            Assert.That(grid.EditingControl!.Bounds, Is.EqualTo(new Rectangle(0, 22, 100, 22)), "editor follows the cell");
        });
    }

    [Test]
    public void GetCellBounds_honors_scroll_and_reports_empty_off_screen()
    {
        var grid = MakeGrid(out _);
        for (var i = 0; i < 7; ++i)
            grid.Items.Add(new Row { Name = $"P{i}" });
        var backend = RealizeBackend(grid);

        Assert.Multiple(() =>
        {
            Assert.That(grid.GetCellBounds(1, 0), Is.EqualTo(new Rectangle(0, 44, 100, 22)));
            Assert.That(grid.GetCellBounds(1, 1), Is.EqualTo(new Rectangle(100, 44, 60, 22)));
            Assert.That(grid.GetCellBounds(9, 0).IsEmpty, Is.True, "below the visible window");
        });

        CanvasOf(backend).RaiseMouseWheel(-120); // top row 3
        Assert.Multiple(() =>
        {
            Assert.That(grid.GetCellBounds(1, 0).IsEmpty, Is.True, "scrolled above the window");
            Assert.That(grid.GetCellBounds(3, 0), Is.EqualTo(new Rectangle(0, 22, 100, 22)));
        });
    }

    [Test]
    public void Clicking_another_cell_commits_the_active_edit()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);
        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Ann");

        canvas.RaiseMouseDown(10, 50); // row 1

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Ann"), "the click-away committed first");
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1), "then the click selected normally");
        });
    }

    [Test]
    public void Numeric_editing_round_trips_through_the_number_setter()
    {
        var grid = MakeGrid(out var rows);
        grid.Columns[1].Kind = DataGridViewColumnKind.NumericUpDown;
        grid.Columns[1].NumberSelector = static o => ((Row)o!).Age;
        grid.Columns[1].NumberSetter = static (o, value) => ((Row)o!).Age = (int)value;
        grid.Columns[1].Minimum = 0;
        grid.Columns[1].Maximum = 200;
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);

        grid.BeginEdit(0, 1);
        Assert.Multiple(() =>
        {
            Assert.That(grid.EditingControl, Is.InstanceOf<NumericUpDown>());
            Assert.That(((NumericUpDown)grid.EditingControl!).Value, Is.EqualTo(30m), "editor seeds from the selector");
            Assert.That(grid.EditingControl.Bounds, Is.EqualTo(new Rectangle(100, 22, 60, 22)));
        });

        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("55");
        canvas.RaiseKeyDown(Keys.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Age, Is.EqualTo(55));
            Assert.That(grid.IsEditing, Is.False);
        });
    }

    [Test]
    public void Numeric_editing_clamps_into_the_column_range()
    {
        var grid = MakeGrid(out var rows);
        grid.Columns[1].Kind = DataGridViewColumnKind.NumericUpDown;
        grid.Columns[1].NumberSelector = static o => ((Row)o!).Age;
        grid.Columns[1].NumberSetter = static (o, value) => ((Row)o!).Age = (int)value;
        grid.Columns[1].Maximum = 200;
        var backend = RealizeBackend(grid);

        grid.BeginEdit(0, 1);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("500");
        grid.CommitEdit();

        Assert.That(rows[0].Age, Is.EqualTo(200), "the editor clamps like NumericUpDown");
    }

    [Test]
    public void Numeric_editor_takes_the_column_increment_and_decimal_places()
    {
        var grid = MakeGrid(out _);
        grid.Columns[1].Kind = DataGridViewColumnKind.NumericUpDown;
        grid.Columns[1].NumberSelector = static o => ((Row)o!).Age;
        grid.Columns[1].NumberSetter = static (o, value) => ((Row)o!).Age = (int)value;
        grid.Columns[1].Increment = 5m;
        grid.Columns[1].DecimalPlaces = 1;
        RealizeBackend(grid);

        grid.BeginEdit(0, 1);
        var editor = (NumericUpDown)grid.EditingControl!;
        editor.UpButton();

        Assert.Multiple(() =>
        {
            Assert.That(editor.Value, Is.EqualTo(35m), "steps by the column increment");
            Assert.That(editor.DecimalPlaces, Is.EqualTo(1));
        });
    }

    [Test]
    public void Combo_cell_paints_the_value_and_a_drop_arrow()
    {
        var grid = MakeGrid(out var rows);
        rows[0].Status = "Active";
        grid.Columns[1].Kind = DataGridViewColumnKind.ComboBox;
        grid.Columns[1].ValueSelector = static o => ((Row)o!).Status;
        var backend = RealizeBackend(grid);

        var g = CanvasOf(backend).RaisePaint();

        // The 5-row arrow triangle centers in the 16px zone at the cell's right edge (x 144..160),
        // row 0 (y 22..44): the widest row at y=30 spans 148..156, in the control text color.
        Assert.Multiple(() =>
        {
            Assert.That(g.Operations.Exists(o => o.StartsWith("text \"Active\"") && o.Contains("@104,22")), Is.True, "cell value");
            Assert.That(g.Operations, Does.Contain("line #FF1A1A1A 148,30-156,30"), "drop arrow");
        });
    }

    [Test]
    public void Combo_editing_opens_the_choice_popup_and_commits_the_pick()
    {
        var choices = new object?[] { "New", "Active", "Done" };
        var grid = MakeGrid(out var rows);
        grid.Columns[1].Kind = DataGridViewColumnKind.ComboBox;
        grid.Columns[1].ValueSelector = static o => ((Row)o!).Status;
        grid.Columns[1].ItemsSelector = _ => choices;
        grid.Columns[1].ValueSetter = static (o, value) => ((Row)o!).Status = (string)value!;
        var backend = RealizeBackend(grid);

        grid.BeginEdit(1, 1);

        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsShown, Is.True);
            Assert.That(popup.ShowCalls[0].Location, Is.EqualTo(new Point(100, 66)), "below the cell");
            Assert.That(popup.ShowCalls[0].Size, Is.EqualTo(new Size(60, 66)), "cell width, one row per choice");
        });

        var g = popup.RaisePaint();
        Assert.That(g.DrewText("Active"), Is.True, "choices painted like combo rows");

        popup.RaiseMouseDown(10, 27); // choice row 1 = "Active"
        Assert.Multiple(() =>
        {
            Assert.That(rows[1].Status, Is.EqualTo("Active"), "committed through the value setter");
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    [Test]
    public void Combo_popup_dismissal_cancels_the_edit()
    {
        var choices = new object?[] { "New", "Done" };
        var grid = MakeGrid(out var rows);
        grid.Columns[1].Kind = DataGridViewColumnKind.ComboBox;
        grid.Columns[1].ItemsSelector = _ => choices;
        grid.Columns[1].ValueSetter = static (o, value) => ((Row)o!).Status = (string)value!;
        var backend = RealizeBackend(grid);
        var ended = 0;
        grid.CellEndEdit += (_, _) => ++ended;
        grid.BeginEdit(0, 1);

        backend.Created.OfType<HeadlessPopupPeer>().Single().FireDismiss();

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(rows[0].Status, Is.Empty, "dismissal writes nothing");
            Assert.That(ended, Is.EqualTo(1));
        });
    }

    [Test]
    public void Date_editing_commits_through_the_date_setter_preserving_the_time()
    {
        var grid = MakeGrid(out var rows);
        rows[0].When = new(2026, 7, 15, 10, 30, 0);
        grid.Columns[1].Kind = DataGridViewColumnKind.DateTime;
        grid.Columns[1].DateSelector = static o => ((Row)o!).When;
        grid.Columns[1].DateSetter = static (o, value) => ((Row)o!).When = value;
        var backend = RealizeBackend(grid);

        grid.BeginEdit(0, 1);

        var popup = backend.Created.OfType<HeadlessPopupPeer>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(popup.IsShown, Is.True);
            Assert.That(popup.ShowCalls[0].Size, Is.EqualTo(new Size(7 * 26, 8 * 22)), "the DateTimePicker calendar geometry");
        });

        popup.RaiseKeyDown(Keys.Left);  // 15th -> 14th
        popup.RaiseKeyDown(Keys.Enter); // select the focus day

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].When, Is.EqualTo(new DateTime(2026, 7, 14, 10, 30, 0)), "day picked, time preserved");
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(popup.IsShown, Is.False);
        });
    }

    /// <summary>Adds a third, writable Status column so Tab has an editable cell to walk to past the
    /// display-only Age column.</summary>
    private static void AddStatusColumn(DataGridView grid)
        => grid.Columns.Add(new DataGridViewColumn("Status", static o => ((Row)o!).Status)
        {
            TextSetter = static (o, value) => ((Row)o!).Status = value,
        });

    [Test]
    public void EditProgrammatically_ignores_every_edit_gesture()
    {
        var grid = MakeGrid(out _);
        grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);
        grid.SelectedRowIndex = 0;
        grid.CurrentColumnIndex = 0;

        canvas.RaiseKeyDown(Keys.F2);
        Assert.That(grid.IsEditing, Is.False, "F2 is ignored");

        canvas.RaiseKeyPress('x');
        Assert.That(grid.IsEditing, Is.False, "typing is ignored");

        canvas.RaiseMouseDown(10, 30);
        canvas.RaiseMouseDown(10, 30);
        Assert.That(grid.IsEditing, Is.False, "double-click is ignored");

        Assert.That(grid.BeginEdit(0, 0), Is.True, "the explicit call still edits");
    }

    [Test]
    public void EditOnEnter_begins_editing_on_a_cell_click()
    {
        var grid = MakeGrid(out _);
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        var backend = RealizeBackend(grid);

        CanvasOf(backend).RaiseMouseDown(10, 30); // row 0, Name

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.True);
            Assert.That(grid.EditingControl!.Text, Is.EqualTo("Alice"));
        });
    }

    [Test]
    public void Enter_commits_and_moves_the_selection_down()
    {
        var grid = MakeGrid(out var rows);
        var backend = RealizeBackend(grid);
        grid.SelectedRowIndex = 0;
        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Ann");

        CanvasOf(backend).RaiseKeyDown(Keys.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Ann"), "Enter committed first");
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1), "then the selection moved down");
        });
    }

    [Test]
    public void Tab_commits_and_moves_to_the_next_editable_cell()
    {
        var grid = MakeGrid(out var rows);
        AddStatusColumn(grid);
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 0);
        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Ann");

        CanvasOf(backend).RaiseKeyDown(Keys.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Name, Is.EqualTo("Ann"), "Tab committed first");
            Assert.That(grid.IsEditing, Is.False, "the next cell waits for a gesture under the default mode");
            Assert.That(grid.SelectedRowIndex, Is.Zero);
            Assert.That(grid.CurrentColumnIndex, Is.EqualTo(2), "the display-only Age column was skipped");
        });
    }

    [Test]
    public void Tab_wraps_to_the_next_row_and_Shift_Tab_walks_backwards()
    {
        var grid = MakeGrid(out _);
        AddStatusColumn(grid);
        var backend = RealizeBackend(grid);
        var canvas = CanvasOf(backend);

        grid.BeginEdit(0, 2); // the last editable cell of row 0
        canvas.RaiseKeyDown(Keys.Tab);
        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1), "wrapped to the next row");
            Assert.That(grid.CurrentColumnIndex, Is.Zero);
        });

        grid.BeginEdit(1, 0);
        canvas.RaiseKeyDown(Keys.Tab, KeyModifiers.Shift);
        Assert.Multiple(() =>
        {
            Assert.That(grid.SelectedRowIndex, Is.Zero, "wrapped back to the previous row");
            Assert.That(grid.CurrentColumnIndex, Is.EqualTo(2), "arriving at its last editable cell");
        });
    }

    [Test]
    public void Tab_under_EditOnEnter_reopens_the_editor_on_the_next_cell()
    {
        var grid = MakeGrid(out var rows);
        AddStatusColumn(grid);
        rows[0].Status = "New";
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 0);

        CanvasOf(backend).RaiseKeyDown(Keys.Tab);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.True);
            Assert.That(grid.EditingControl!.Text, Is.EqualTo("New"), "the Status cell edits now");
        });
    }

    [Test]
    public void IsCurrentCellDirty_flips_on_the_first_editor_change()
    {
        var grid = MakeGrid(out _);
        var backend = RealizeBackend(grid);
        var flips = 0;
        grid.CurrentCellDirtyStateChanged += (_, _) => ++flips;

        grid.BeginEdit(0, 0);
        Assert.That(grid.IsCurrentCellDirty, Is.False, "seeding the editor is not an edit");

        backend.Created.OfType<HeadlessTextBoxPeer>().Single().SimulateUserInput("Ann");
        Assert.Multiple(() =>
        {
            Assert.That(grid.IsCurrentCellDirty, Is.True);
            Assert.That(flips, Is.EqualTo(1));
        });

        grid.CommitEdit();
        Assert.Multiple(() =>
        {
            Assert.That(grid.IsCurrentCellDirty, Is.False, "ending the edit clears the flag");
            Assert.That(flips, Is.EqualTo(2));
        });
    }

    [Test]
    public void RowValidating_veto_keeps_the_selection_on_the_row()
    {
        var grid = MakeGrid(out _);
        RealizeBackend(grid);
        grid.SelectedRowIndex = 0;
        var veto = true;
        DataGridViewCellCancelEventArgs? validating = null;
        grid.RowValidating += (_, e) =>
        {
            validating = e;
            e.Cancel = veto;
        };

        grid.SelectedRowIndex = 1;
        Assert.Multiple(() =>
        {
            Assert.That(validating, Is.Not.Null);
            Assert.That(validating!.RowIndex, Is.Zero, "the row being left");
            Assert.That(grid.SelectedRowIndex, Is.Zero, "the veto kept it");
        });

        veto = false;
        grid.SelectedRowIndex = 1;
        Assert.That(grid.SelectedRowIndex, Is.EqualTo(1), "the released change goes through");
    }

    [Test]
    public void RowValidated_fires_after_the_row_is_left()
    {
        var grid = MakeGrid(out _);
        var backend = RealizeBackend(grid);
        grid.SelectedRowIndex = 0;
        DataGridViewCellEventArgs? validated = null;
        grid.RowValidated += (_, e) => validated = e;

        CanvasOf(backend).RaiseMouseDown(10, 50); // click row 1

        Assert.Multiple(() =>
        {
            Assert.That(validated, Is.Not.Null);
            Assert.That(validated!.RowIndex, Is.Zero, "the row that was left");
            Assert.That(grid.SelectedRowIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void Date_editing_escape_cancels_without_writing()
    {
        var grid = MakeGrid(out var rows);
        rows[0].When = new(2026, 7, 15);
        grid.Columns[1].Kind = DataGridViewColumnKind.DateTime;
        grid.Columns[1].DateSelector = static o => ((Row)o!).When;
        grid.Columns[1].DateSetter = static (o, value) => ((Row)o!).When = value;
        var backend = RealizeBackend(grid);
        grid.BeginEdit(0, 1);

        backend.Created.OfType<HeadlessPopupPeer>().Single().RaiseKeyDown(Keys.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsEditing, Is.False);
            Assert.That(rows[0].When, Is.EqualTo(new DateTime(2026, 7, 15)), "nothing written");
        });
    }
}
