using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Radial.Windows.Presentation;

/// <summary>
/// Base de los modelos de vista: notifica los cambios de propiedades. Sin WPF (<c>INotifyPropertyChanged</c> es de la biblioteca
/// base), así que los modelos de vista se prueban sin interfaz. Se usan siempre desde el hilo de la interfaz.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Un comando síncrono. <see cref="CanExecuteChanged"/> se produce cuando el propietario llama a <see cref="RaiseCanExecuteChanged"/>.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute(parameter);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Un comando asíncrono (confirmaciones, selectores): no se puede lanzar dos veces a la vez y un fallo no se pierde en una tarea que
/// nadie espera, se entrega a <see cref="Failed"/>.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Se produce si la ejecución lanza una excepción.</summary>
    public static event Action<Exception>? Failed;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await RunAsync(parameter);

    /// <summary>Ejecuta y espera (las pruebas lo usan en lugar de <see cref="Execute"/>).</summary>
    public async Task RunAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
