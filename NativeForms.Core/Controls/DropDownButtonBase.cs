using System.Drawing;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// The shared engine behind the standalone menu buttons <see cref="SplitButton"/> and
/// <see cref="DropDownButton"/>: a themed button face with icon+text content, a trailing arrow zone,
/// and a drop-down of <see cref="DropDownItems"/> opened below the control through the shared
/// <see cref="MenuDropDown"/> engine — the same popup a <see cref="MenuStrip"/> or
/// <see cref="ContextMenuStrip"/> shows. Subclasses decide which gestures run an action and which
/// open the menu.
/// </summary>
public abstract class DropDownButtonBase : OwnerDrawnControl
{
    /// <summary>The width of the trailing arrow zone.</summary>
    internal const int ArrowZoneWidth = 12;

    private ToolStripItemCollection? _dropDownItems;
    private MenuDropDown? _dropDown;

    /// <summary>The items shown when the drop-down opens, sharing the strip/menu item model. Lazily
    /// created, so a button that never opens pays nothing for the collection.</summary>
    public ToolStripItemCollection DropDownItems
    {
        get
        {
            var items = _dropDownItems;
            if (items is null)
            {
                _dropDownItems = items = new();
                items.Changed += (_, _) => this.Invalidate();
            }

            return items;
        }
    }

    /// <summary>Whether a drop-down would show anything, without materializing an empty collection.</summary>
    public bool HasDropDownItems => _dropDownItems is { Count: > 0 };

    /// <summary>Whether the drop-down cascade is currently open.</summary>
    public bool IsDropDownOpen => _dropDown is { IsOpen: true };

    /// <summary>An optional icon rendered before the caption through the shared content layout.</summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.Invalidate();
        }
    }

    /// <inheritdoc/>
    protected override bool Focusable => true;

    /// <summary>The lazily created drop-down engine; only valid while the control is realized.</summary>
    private MenuDropDown Engine
    {
        get
        {
            var engine = _dropDown;
            if (engine is null)
            {
                _dropDown = engine = new(this.Backend!, this.Theme);
                engine.Closed += (_, _) => this.Invalidate();
            }

            return engine;
        }
    }

    /// <summary>Opens the drop-down below the control, left-aligned with it. A no-op before
    /// realization or while <see cref="DropDownItems"/> is empty.</summary>
    public void ShowDropDown()
    {
        if (this.Backend is null || !this.HasDropDownItems)
            return;

        this.Engine.Open(this.DropDownItems, this.PointToScreen(new(0, this.Height)));
    }

    /// <summary>Closes the drop-down cascade, if open.</summary>
    public void CloseDropDown() => _dropDown?.CloseAll();

    /// <inheritdoc/>
    private protected override void OnUnrealized()
    {
        base.OnUnrealized();
        _dropDown?.CloseAll();
        _dropDown = null;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!this.Enabled || e.KeyCode is not Keys.Down)
            return;

        this.ShowDropDown();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        GlyphRenderer.DrawButtonFace(g, theme, new Rectangle(0, 0, this.Width - 1, this.Height - 1), string.Empty, this.Enabled);

        var textColor = this.Enabled ? theme.ControlText : theme.DisabledText;
        var content = new Rectangle(0, 0, this.Width - ArrowZoneWidth, this.Height);
        var image = this.Image;
        ContentLayout.Arrange(
            content,
            image is null ? Size.Empty : new(image.Width, image.Height),
            g.MeasureText(this.Text, theme.DefaultFont),
            TextImageRelation.ImageBeforeText,
            ContentAlignment.MiddleCenter,
            out var imageRect,
            out var textRect);
        if (image is not null)
            g.DrawImage(image, imageRect);

        if (this.Text.Length > 0)
            g.DrawText(this.Text, theme.DefaultFont, textColor, textRect, ContentAlignment.MiddleCenter);

        this.PaintArrowZone(g, theme, textColor);
    }

    /// <summary>Paints the trailing arrow zone: the down triangle, plus whatever chrome the concrete
    /// button adds (the split separator).</summary>
    private protected virtual void PaintArrowZone(IGraphics g, ITheme theme, Color color)
        => Glyphs.PaintTriangle(g, color, new(this.Width - ArrowZoneWidth + 3, (this.Height / 2) - 1, 6, 4), GlyphDirection.Down);
}
