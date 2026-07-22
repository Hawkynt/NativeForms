using System.ComponentModel;

namespace Hawkynt.NativeForms;

/// <summary>
/// The top-level window that hosts a <see cref="DockContent"/> torn off into a
/// <see cref="DockState.Floating"/> state. The pane is re-parented into this form and fills it; closing
/// the window re-docks the pane back into the owning <see cref="DockPanel"/> rather than destroying it.
/// </summary>
internal sealed class DockFloatWindow : Form
{
    private readonly DockPanel _owner;
    private bool _reclaimed;

    internal DockFloatWindow(DockPanel owner, DockContent content)
    {
        _owner = owner;
        this.Content = content;
        this.Text = content.Title;
        this.StartPosition = FormStartPosition.Manual;

        // Pull the pane out of the panel and let it fill the window. Controls.Remove clears the parent
        // and tears down the peer tree; the pane re-realizes as a child of this form.
        if (ReferenceEquals(content.Parent, owner))
            owner.Controls.Remove(content);

        content.Dock = DockStyle.Fill;
        this.Controls.Add(content);

        this.FormClosing += this.OnFloatClosing;
    }

    /// <summary>The pane this window hosts.</summary>
    internal DockContent Content { get; }

    /// <summary>A floating pane is a secondary window: closing it re-docks the pane, it never ends the
    /// application's message loop.</summary>
    private protected override bool QuitsOnLoopClose => false;

    /// <summary>Shows the window on the running loop; a no-op when there is no loop yet (tests).</summary>
    internal void ShowFloating()
    {
        try
        {
            this.Show();
        }
        catch (InvalidOperationException)
        {
            // No message loop is running (headless tests): the model is still correct, the window just
            // has no native presence.
        }
    }

    /// <summary>Removes the pane so it survives the window's closure, returning it parentless.</summary>
    internal void ReclaimContent()
    {
        _reclaimed = true;
        if (ReferenceEquals(this.Content.Parent, this))
            this.Controls.Remove(this.Content);
        this.Content.Dock = DockStyle.None;
    }

    /// <summary>Closes the window; a no-op when it was never shown.</summary>
    internal void CloseFloating()
    {
        try
        {
            this.Close();
        }
        catch (InvalidOperationException)
        {
            // Never realized — nothing to close.
        }
    }

    private void OnFloatClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reclaimed)
            return; // a programmatic un-float already pulled the pane out.

        this.ReclaimContent();
        _owner.OnFloatWindowClosed(this);
    }
}
