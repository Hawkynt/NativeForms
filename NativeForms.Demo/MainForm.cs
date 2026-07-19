using System.Drawing;
using Hawkynt.NativeForms.ComponentModel;

namespace Hawkynt.NativeForms.Demo;

/// <summary>
/// The demo window: a label and a button wired to <see cref="CounterViewModel"/> through the same
/// MVVM primitives an application would use — an <c>ICommand</c> for the action and a one-way
/// <see cref="PropertyBinding{T}"/> pushing the view-model's text onto the label.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly CounterViewModel _viewModel = new();
    private readonly Label _label;
    private readonly Button _button;

    // Held so the binding is not garbage-collected for the window's lifetime.
    private readonly PropertyBinding<string> _labelBinding;

    public MainForm()
    {
        this.Text = "NativeForms Demo";
        this.Bounds = new(Point.Empty, new Size(320, 160));

        _label = new()
        {
            Bounds = new(20, 20, 280, 24),
            Text = _viewModel.Display,
        };

        _button = new()
        {
            Bounds = new(20, 64, 140, 36),
            Text = "Click me",
        };
        _button.Click += (_, _) => _viewModel.Increment.Execute(null);

        this.Controls.AddRange(_label, _button);

        _labelBinding = new(
            _viewModel,
            nameof(CounterViewModel.Count),
            () => _viewModel.Display,
            text => _label.Text = text);
    }
}
