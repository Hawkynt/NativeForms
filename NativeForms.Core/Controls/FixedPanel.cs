namespace Hawkynt.NativeForms;

/// <summary>Which <see cref="SplitContainer"/> panel keeps its size when the container resizes,
/// matching <c>System.Windows.Forms.FixedPanel</c>.</summary>
public enum FixedPanel
{
    /// <summary>Neither panel is pinned: the splitter distance scales proportionally.</summary>
    None = 0,

    /// <summary><see cref="SplitContainer.Panel1"/> keeps its size; Panel2 absorbs the resize.</summary>
    Panel1 = 1,

    /// <summary><see cref="SplitContainer.Panel2"/> keeps its size; Panel1 absorbs the resize.</summary>
    Panel2 = 2,
}
