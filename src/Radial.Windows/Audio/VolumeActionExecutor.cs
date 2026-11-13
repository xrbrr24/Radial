using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Audio;

/// <summary>
/// Controla el volumen del dispositivo de salida predeterminado (<see cref="VolumeAction"/>) con Windows Core Audio. Orden fijo:
/// 1. Otro tipo de acción: <see cref="ActionErrorCodes.UnsupportedAction"/>. Token ya cancelado: <see cref="ActionExecutionResult.Cancelled"/>.
/// 2. <c>GetProblems()</c> de Core no vacío (nivel o diferencia fuera de 0–100 / 1–100): <see cref="ActionErrorCodes.InvalidAction"/>.
/// 3. Se abre el dispositivo predeterminado EN EL POOL (Core Audio es COM y puede tardar con el servicio de audio ocupado; quien
///    llama es el hilo de interfaz). Sin dispositivo o servicio de audio parado: <see cref="ActionErrorCodes.AudioUnavailable"/>.
/// 4. Se calcula y se aplica:
///    - Subir / bajar: nivel actual (en puntos enteros, 0–100) ± diferencia, acotado a 0–100. Subir también quita el silencio.
///    - Fijar: el nivel indicado; si es mayor que 0 y estaba en silencio, se quita el silencio.
///    - Alternar silencio: invierte el silencio sin tocar el nivel.
///    Un rechazo de Windows: <see cref="ActionErrorCodes.AudioControlFailed"/>.
/// El éxito significa «Windows aceptó el cambio». No simula teclas multimedia, no abre mezclador ni diálogos y no deja el
/// dispositivo abierto. Los mensajes no incluyen nombres de dispositivo.
/// </summary>
public sealed class VolumeActionExecutor : IActionExecutor
{
    private readonly IAudioEndpointProvider _provider;

    public VolumeActionExecutor(IAudioEndpointProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>Ejecutor de producción sobre Windows Core Audio.</summary>
    public static VolumeActionExecutor CreateDefault() => new(new CoreAudioEndpointProvider());

    public bool CanExecute(RadialAction action) => action is VolumeAction;

    public async Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not VolumeAction volume)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.UnsupportedAction, $"Este ejecutor no admite acciones de tipo {action.GetType().Name}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        var problems = volume.GetProblems().ToList();
        if (problems.Count > 0)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.InvalidAction, "La acción no es válida: " + string.Join(" ", problems));
        }

        try
        {
            // Con el token cancelado antes de empezar, Task.Run no ejecuta nada. Una vez dentro, el cambio se completa y se informa.
            return await Task.Run(() => Apply(volume), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }
    }

    private ActionExecutionResult Apply(VolumeAction action)
    {
        try
        {
            using var endpoint = _provider.OpenDefaultOutput();
            switch (action.Operation)
            {
                case VolumeOperation.ToggleMute:
                    endpoint.SetMuted(!endpoint.GetMuted());
                    break;

                case VolumeOperation.Increase:
                    SetPercent(endpoint, Math.Min(100, CurrentPercent(endpoint) + action.Percent), unmute: true);
                    break;

                case VolumeOperation.Decrease:
                    SetPercent(endpoint, Math.Max(0, CurrentPercent(endpoint) - action.Percent), unmute: false);
                    break;

                case VolumeOperation.SetLevel:
                    SetPercent(endpoint, action.Percent, unmute: action.Percent > 0);
                    break;
            }

            return ActionExecutionResult.Success;
        }
        catch (AudioDeviceException exception) when (exception.DeviceUnavailable)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.AudioUnavailable, "No hay un dispositivo de salida de audio disponible.");
        }
        catch (AudioDeviceException)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.AudioControlFailed, "Windows no permitió cambiar el volumen.");
        }
    }

    private static int CurrentPercent(IAudioEndpoint endpoint) => (int)Math.Round(endpoint.GetLevel() * 100f, MidpointRounding.AwayFromZero);

    private static void SetPercent(IAudioEndpoint endpoint, int percent, bool unmute)
    {
        endpoint.SetLevel(percent / 100f);
        if (unmute && endpoint.GetMuted())
        {
            endpoint.SetMuted(false);
        }
    }
}
