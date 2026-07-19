using System.Windows.Input;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Hawkynt.NativeForms;

/// <summary>
/// Base class for everything hosted by a strip — menu items, toolbar buttons, separators, status
/// labels. An item is not a <see cref="Control"/>: it owns no peer and no bounds of its own; the
/// hosting strip (or the drop-down engine) lays it out and paints it. State changes bubble to the
/// owning collection so the strip repaints without the item ever knowing who displays it.
/// </summary>
public abstract class ToolStripItem
{
    /// <summary>The collection currently holding this item, or <see langword="null"/> while detached.</summary>
    internal ToolStripItemCollection? Owner { get; set; }

    /// <summary>The caption. <c>&amp;</c> marks the following character as the mnemonic
    /// (<c>&amp;&amp;</c> escapes a literal ampersand).</summary>
    public string Text
    {
        get => field;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;

            field = value;
            this.ParseMnemonic(value);
            this.NotifyOwner();
        }
    } = string.Empty;

    /// <summary>The caption with the mnemonic markers removed — what actually gets painted.</summary>
    internal string DisplayText { get; private set; } = string.Empty;

    /// <summary>The index of the mnemonic character within <see cref="DisplayText"/>, or -1.</summary>
    internal int MnemonicIndex { get; private set; } = -1;

    /// <summary>The part of <see cref="DisplayText"/> before the mnemonic character. Cached at
    /// assignment time so underline painting allocates nothing per frame.</summary>
    internal string MnemonicPrefix { get; private set; } = string.Empty;

    /// <summary>The mnemonic character as a one-char string, for width measurement while painting.</summary>
    internal string MnemonicCharText { get; private set; } = string.Empty;

    /// <summary>A direct icon for the item; wins over <see cref="ImageList"/> + <see cref="ImageIndex"/>.</summary>
    public IImage? Image
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            this.NotifyOwner();
        }
    }

    /// <summary>The icon store <see cref="ImageIndex"/> indexes into, or <see langword="null"/> for none.</summary>
    public ImageList? ImageList
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            this.NotifyOwner();
        }
    }

    /// <summary>The index of this item's icon within <see cref="ImageList"/>; negative for none.</summary>
    public int ImageIndex
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    } = -1;

    /// <summary>
    /// Whether the item reacts to the user. With a <see cref="Command"/> attached the command's
    /// <see cref="ICommand.CanExecute"/> gates the effective value on top of the assigned one, so a
    /// view-model guard greys the item out automatically.
    /// </summary>
    public bool Enabled
    {
        get => field && (this.Command?.CanExecute(null) ?? true);
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    } = true;

    /// <summary>Whether the item occupies space and paints.</summary>
    public bool Visible
    {
        get => field;
        set
        {
            if (field == value)
                return;

            field = value;
            this.NotifyOwner();
        }
    } = true;

    /// <summary>Arbitrary user data riding along with the item.</summary>
    public object? Tag { get; set; }

    /// <summary>
    /// The MVVM command this item invokes. <see cref="PerformClick"/> executes it, its
    /// <see cref="ICommand.CanExecuteChanged"/> re-evaluates <see cref="Enabled"/>, and the hosting
    /// strip repaints — the same wiring a bound button gets.
    /// </summary>
    public ICommand? Command
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            if (field is not null)
                field.CanExecuteChanged -= this.OnCanExecuteChanged;

            field = value;
            if (value is not null)
                value.CanExecuteChanged += this.OnCanExecuteChanged;

            this.NotifyOwner();
        }
    }

    /// <summary>Raised when the item is activated (click, Enter, shortcut).</summary>
    public event EventHandler? Click;

    /// <summary>Activates the item as a user click would: raises <see cref="Click"/> and executes
    /// <see cref="Command"/>. A no-op while the item is not <see cref="Enabled"/>.</summary>
    public void PerformClick()
    {
        if (!this.Enabled)
            return;

        this.OnClick(EventArgs.Empty);
        var command = this.Command;
        if (command is not null && command.CanExecute(null))
            command.Execute(null);
    }

    /// <summary>Raises <see cref="Click"/>. Subclasses toggle their check state before delegating here.</summary>
    protected virtual void OnClick(EventArgs e) => this.Click?.Invoke(this, e);

    /// <summary>The effective icon: <see cref="Image"/> first, then <see cref="ImageList"/> +
    /// <see cref="ImageIndex"/> materialized against <paramref name="backend"/>.</summary>
    internal IImage? ResolveImage(IPlatformBackend? backend)
    {
        if (this.Image is { } direct)
            return direct;

        var images = this.ImageList;
        var index = this.ImageIndex;
        return images is not null && backend is not null && index >= 0 && index < images.Count
            ? images.GetImage(index, backend)
            : null;
    }

    /// <summary>Tells the owning collection (and thus the hosting strip) that this item changed.</summary>
    private protected void NotifyOwner() => this.Owner?.NotifyItemChanged();

    /// <summary>
    /// Splits the caption into its painted text and mnemonic metadata: the first single <c>&amp;</c>
    /// marks the following character, <c>&amp;&amp;</c> collapses to a literal ampersand. Runs once
    /// per assignment so the paint path never re-parses or allocates.
    /// </summary>
    private void ParseMnemonic(string text)
    {
        if (!text.Contains('&'))
        {
            this.DisplayText = text;
            this.MnemonicIndex = -1;
            this.MnemonicPrefix = string.Empty;
            this.MnemonicCharText = string.Empty;
            return;
        }

        var display = new System.Text.StringBuilder(text.Length);
        var mnemonic = -1;
        for (var i = 0; i < text.Length; ++i)
        {
            var c = text[i];
            if (c == '&' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next != '&' && mnemonic < 0)
                    mnemonic = display.Length;

                display.Append(next);
                ++i;
                continue;
            }

            if (c != '&')
                display.Append(c);
        }

        var result = display.ToString();
        this.DisplayText = result;
        this.MnemonicIndex = mnemonic;
        this.MnemonicPrefix = mnemonic > 0 ? result[..mnemonic] : string.Empty;
        this.MnemonicCharText = mnemonic >= 0 ? result[mnemonic].ToString() : string.Empty;
    }

    /// <summary>The command's guard changed; the effective <see cref="Enabled"/> may differ now.</summary>
    private void OnCanExecuteChanged(object? sender, EventArgs e) => this.NotifyOwner();
}
