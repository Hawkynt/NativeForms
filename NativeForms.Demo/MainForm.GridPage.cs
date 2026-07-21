using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms.Demo;

internal sealed partial class MainForm
{
    /// <summary>A mutable work-item row for the data-grid demo; check and numeric cells write back.</summary>
    private sealed class WorkItem
    {
        /// <summary>The section the item belongs to, shown in the merged section rows.</summary>
        public required string Category { get; init; }

        /// <summary>The task name shown next to the category icon.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>The category icon painted into the first column.</summary>
        public IImage? Icon { get; init; }

        /// <summary>Whether the task is done; toggled by the check column.</summary>
        public bool Done { get; set; }

        /// <summary>The completion percentage painted by the progress column.</summary>
        public int Percent { get; set; }

        /// <summary>The booked hours; edited through the numeric column.</summary>
        public decimal Hours { get; set; }

        /// <summary>The documentation link shown by the link column.</summary>
        public string Docs { get; init; } = string.Empty;

        /// <summary>Whether this row is a section marker rendered as one merged full-width cell.</summary>
        public bool IsSection { get; init; }
    }

    /// <summary>
    /// The Grid page: one large <see cref="DataGridView"/> exercising the column kinds (icon+text,
    /// check with write-back, progress, numeric with write-back, button, link), alternating rows,
    /// row headers, automatic sorting on the frozen first column, multi-select, merged section rows
    /// and a few dozen rows in the grid's bound <see cref="DataGridView.Items"/> list. Selection and
    /// content clicks report into the status strip.
    /// </summary>
    private TabPage BuildGridPage()
    {
        var page = new TabPage("Grid") { ImageIndex = _IconRed };

        var grid = new DataGridView
        {
            Bounds = new(16, 36, 948, 500),
            RowHeight = 24,
            AlternatingRows = true,
            ShowRowHeaders = true,
            MultiSelect = true,
            FullRowTextSelector = static o => ((WorkItem)o!).IsSection ? $"— {((WorkItem)o!).Category} —" : null,
        };

        grid.Columns.Add(new DataGridViewColumn("Task", static o => ((WorkItem)o!).Name)
        {
            Width = 220,
            Frozen = true,
            SortMode = DataGridViewColumnSortMode.Automatic,
            ImageSelector = static o => ((WorkItem)o!).Icon,
        });
        grid.Columns.Add(new DataGridViewColumn("Done", static o => ((WorkItem)o!).Done)
        {
            Kind = DataGridViewColumnKind.Check,
            Width = 60,
            CheckedSelector = static o => ((WorkItem)o!).Done,
            CheckedSetter = static (o, value) => ((WorkItem)o!).Done = value,
        });
        grid.Columns.Add(new DataGridViewColumn("Progress", static o => ((WorkItem)o!).Percent)
        {
            Kind = DataGridViewColumnKind.Progress,
            Width = 140,
            ProgressSelector = static o => ((WorkItem)o!).Percent,
        });
        grid.Columns.Add(new DataGridViewColumn("Hours", static o => ((WorkItem)o!).Hours)
        {
            Kind = DataGridViewColumnKind.NumericUpDown,
            Width = 90,
            Alignment = ContentAlignment.MiddleRight,
            DecimalPlaces = 1,
            Maximum = 999m,
            NumberSelector = static o => ((WorkItem)o!).Hours,
            NumberSetter = static (o, value) => ((WorkItem)o!).Hours = value,
            CellStyleSelector = static o => ((WorkItem)o!).Hours > 24m
                ? new(foreColor: Color.Firebrick)
                : default,
        });
        grid.Columns.Add(new DataGridViewColumn("Open", static _ => "Open…")
        {
            Kind = DataGridViewColumnKind.Button,
            Width = 90,
        });
        grid.Columns.Add(new DataGridViewColumn("Docs", static o => ((WorkItem)o!).Docs)
        {
            Kind = DataGridViewColumnKind.Link,
            Width = 180,
        });

        grid.Items.AddRange(BuildWorkItems(this.DiscImage(Color.RoyalBlue), this.DiscImage(Color.MediumSeaGreen), this.DiscImage(Color.Orange)));

        grid.SelectionChanged += (_, _) =>
        {
            var count = 0;
            WorkItem? first = null;
            foreach (var item in grid.SelectedItems)
            {
                first ??= item as WorkItem;
                ++count;
            }

            this.SetStatus(count == 0
                ? "Grid: nothing selected."
                : $"Grid: {count} row(s) selected, first is \"{first?.Name}\".");
        };
        grid.CellContentClick += (_, e) =>
        {
            var row = grid.Items[e.RowIndex] as WorkItem;
            this.SetStatus($"Grid: \"{grid.Columns[e.ColumnIndex].HeaderText}\" clicked on \"{row?.Name}\".");
        };
        grid.SelectedRowIndex = 1;

        page.Controls.AddRange(
            Caption("DataGridView — click the Task header to sort; the column stays frozen.", 16, 12, 540),
            grid,
            Caption("Check and Hours cells write back into the bound items.", 16, 544, 420));

        // The walkthrough sorts the grid, widens a column, scrolls both ways and writes back into two
        // bound cells. The column widths and the rows themselves are the authored state; the offsets
        // are simply zero, which is where a grid nobody has touched sits.
        var columnWidths = new int[grid.Columns.Count];
        for (var i = 0; i < columnWidths.Length; ++i)
            columnWidths[i] = grid.Columns[i].Width;

        var authoredRows = new WorkItem[grid.Items.Count];
        grid.Items.CopyTo(authoredRows, 0);
        var doneFlags = new bool[authoredRows.Length];
        var hourValues = new decimal[authoredRows.Length];
        for (var i = 0; i < authoredRows.Length; ++i)
        {
            doneFlags[i] = authoredRows[i].Done;
            hourValues[i] = authoredRows[i].Hours;
        }

        var selectedRow = grid.SelectedRowIndex;
        this.OnReset(() =>
        {
            grid.Sort(null, SortOrder.None);
            for (var i = 0; i < columnWidths.Length; ++i)
                grid.Columns[i].Width = columnWidths[i];

            for (var i = 0; i < authoredRows.Length; ++i)
            {
                authoredRows[i].Done = doneFlags[i];
                authoredRows[i].Hours = hourValues[i];
            }

            grid.HorizontalOffset = 0;
            grid.TopRow = 0;
            grid.SelectedRowIndex = selectedRow;
            grid.Invalidate();
        });

        this.Publish("grid.page", page);
        this.Publish("grid.grid", grid);

        return page;
    }

    /// <summary>Builds three sections of ten deterministic rows each, plus one marker row per section.</summary>
    private static WorkItem[] BuildWorkItems(IImage coreIcon, IImage backendIcon, IImage demoIcon)
    {
        var sections = new (string Category, IImage Icon)[]
        {
            ("Core", coreIcon),
            ("Backends", backendIcon),
            ("Demo", demoIcon),
        };
        var verbs = new[] { "Design", "Implement", "Test", "Document", "Polish" };
        var nouns = new[] { "layout", "painting" };

        var rows = new WorkItem[sections.Length * ((verbs.Length * nouns.Length) + 1)];
        var index = 0;
        foreach (var (category, icon) in sections)
        {
            rows[index++] = new() { Category = category, IsSection = true };
            for (var v = 0; v < verbs.Length; ++v)
            for (var n = 0; n < nouns.Length; ++n)
            {
                var ordinal = (v * nouns.Length) + n;
                rows[index++] = new()
                {
                    Category = category,
                    Name = $"{verbs[v]} {category.ToLowerInvariant()} {nouns[n]}",
                    Icon = icon,
                    Done = ordinal % 3 == 0,
                    Percent = ordinal * 11 % 101,
                    Hours = 2.5m * (ordinal + 1),
                    Docs = $"docs/{category.ToLowerInvariant()}.md",
                };
            }
        }

        return rows;
    }
}
