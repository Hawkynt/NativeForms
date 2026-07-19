namespace Hawkynt.NativeForms;

/// <summary>
/// One page of a <see cref="TabControl"/>: a <see cref="Panel"/> whose <see cref="Control.Text"/> is
/// the tab caption and whose children fill the tab control's content area. Pages are managed through
/// <see cref="TabControl.TabPages"/> — the tab control owns their bounds and visibility.
/// </summary>
public class TabPage : Panel
{
    /// <summary>Creates an untitled page.</summary>
    public TabPage() { }

    /// <summary>Creates a page with the given tab caption.</summary>
    public TabPage(string text) => this.Text = text;

    /// <summary>
    /// The index of this page's icon in the owning <see cref="TabControl.ImageList"/>, or -1 for no
    /// icon. The icon is painted before the caption in the tab header.
    /// </summary>
    public int ImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            (this.Parent as TabControl)?.Invalidate();
        }
    } = -1;

    /// <inheritdoc/>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        (this.Parent as TabControl)?.Invalidate();
    }
}
