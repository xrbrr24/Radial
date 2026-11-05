namespace Radial.Windows.Presentation;

/// <summary>
/// Diálogos y avisos DENTRO de la ventana de Radial (no ventanas del sistema ni cuadros de mensaje). La vista muestra el diálogo
/// actual (<see cref="CurrentDialog"/>) sobre el contenido y el aviso breve (<see cref="ToastMessage"/>), que desaparece solo.
/// Solo hay un diálogo a la vez: pedir otro mientras hay uno abierto espera a que se cierre.
/// </summary>
public sealed class InAppDialogs : ObservableObject, IDialogService, IToastSink
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4.5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DialogViewModel? _dialog;
    private string _toastMessage = string.Empty;
    private bool _toastIsError;
    private int _toastToken;

    public DialogViewModel? CurrentDialog
    {
        get => _dialog;
        private set
        {
            if (Set(ref _dialog, value))
            {
                Raise(nameof(HasDialog));
            }
        }
    }

    public bool HasDialog => _dialog is not null;

    public string ToastMessage
    {
        get => _toastMessage;
        private set
        {
            if (Set(ref _toastMessage, value))
            {
                Raise(nameof(HasToast));
            }
        }
    }

    public bool ToastIsError
    {
        get => _toastIsError;
        private set => Set(ref _toastIsError, value);
    }

    public bool HasToast => _toastMessage.Length > 0;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
    {
        var choice = await ChooseAsync(title, message, ["Cancelar", confirmText], danger ? 1 : -1);
        return choice == 1;
    }

    public async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options, int dangerIndex = -1)
    {
        await _gate.WaitAsync();
        try
        {
            var dialog = new DialogViewModel(title, message, options, dangerIndex);
            CurrentDialog = dialog;
            var result = await dialog.Result;
            CurrentDialog = null;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Show(string message, bool isError = false)
    {
        var token = ++_toastToken;
        ToastIsError = isError;
        ToastMessage = message;
        _ = HideLaterAsync(token);
    }

    public void DismissToast()
    {
        _toastToken++;
        ToastMessage = string.Empty;
    }

    private async Task HideLaterAsync(int token)
    {
        try
        {
            await Task.Delay(ToastDuration);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token == _toastToken)
        {
            ToastMessage = string.Empty;
        }
    }
}
