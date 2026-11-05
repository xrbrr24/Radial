namespace Radial.Windows.Presentation;

/// <summary>Diálogos de confirmación y de elección dentro de la aplicación. La implementación de la interfaz los dibuja; las pruebas usan un doble.</summary>
public interface IDialogService
{
    /// <returns><c>true</c> si el usuario confirma.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false);

    /// <returns>El índice de la opción elegida, o -1 si cancela.</returns>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options, int dangerIndex = -1);
}

/// <summary>Selectores de archivos y carpetas del sistema. Devuelven <c>null</c> si el usuario cancela.</summary>
public interface IFilePicker
{
    string? PickApplication();

    string? PickFile();

    string? PickFolder();
}

/// <summary>Avisos breves al usuario (resultado de una operación). Los muestra la interfaz.</summary>
public interface IToastSink
{
    void Show(string message, bool isError = false);
}

/// <summary>Un diálogo que la interfaz muestra y que se resuelve con <see cref="Complete"/>.</summary>
public sealed class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<int> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogViewModel(string title, string message, IReadOnlyList<string> options, int dangerIndex)
    {
        Title = title;
        Message = message;
        Options = options;
        DangerIndex = dangerIndex;
        Choose = new RelayCommand(parameter => Complete(parameter is int index ? index : -1));
        Cancel = new RelayCommand(() => Complete(-1));
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Los botones, de izquierda a derecha; el último es el principal.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>La opción destructiva (se dibuja en rojo), o -1.</summary>
    public int DangerIndex { get; }

    public RelayCommand Choose { get; }

    public RelayCommand Cancel { get; }

    public Task<int> Result => _result.Task;

    public void Complete(int index) => _result.TrySetResult(index);
}
