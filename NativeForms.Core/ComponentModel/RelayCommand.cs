using System.Windows.Input;

namespace Hawkynt.NativeForms.ComponentModel;

/// <summary>
/// An <see cref="ICommand"/> that forwards to delegates — the standard way to expose a view-model
/// action to a button in MVVM. Reflection-free and allocation-light.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>Creates a command from an execute delegate and an optional guard.</summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute();

    /// <summary>Signals that <see cref="CanExecute"/> may have changed so bound controls re-query it.</summary>
    public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A strongly-typed <see cref="ICommand"/> whose delegates receive the command parameter.</summary>
/// <typeparam name="T">The command parameter type.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    /// <summary>Creates a command from an execute delegate and an optional guard.</summary>
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute((T?)parameter);

    /// <summary>Signals that <see cref="CanExecute"/> may have changed so bound controls re-query it.</summary>
    public void RaiseCanExecuteChanged() => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
