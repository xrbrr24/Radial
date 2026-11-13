namespace Radial.Windows.Audio;

/// <summary>
/// El dispositivo de salida de audio predeterminado, ya abierto. Volumen en escala 0–1 (la «escalar» de Core Audio, la misma del
/// control de volumen de Windows) y silencio. Se libera al terminar: no se conserva entre ejecuciones (el dispositivo
/// predeterminado puede cambiar en cualquier momento).
/// </summary>
public interface IAudioEndpoint : IDisposable
{
    float GetLevel();

    void SetLevel(float level);

    bool GetMuted();

    void SetMuted(bool muted);
}

/// <summary>Abre el dispositivo de salida predeterminado. Lo sustituyen las pruebas por un doble que no toca el audio real.</summary>
public interface IAudioEndpointProvider
{
    /// <exception cref="AudioDeviceException">No hay dispositivo de salida o Windows no permite abrirlo.</exception>
    IAudioEndpoint OpenDefaultOutput();
}

/// <summary>
/// Un fallo previsible de Core Audio. <see cref="DeviceUnavailable"/> distingue «no hay dispositivo / se desconectó / el servicio de
/// audio no responde» (nada que hacer) de «Windows rechazó la operación». El mensaje no incluye nombres de dispositivo.
/// </summary>
public sealed class AudioDeviceException(string message, bool deviceUnavailable, Exception? inner = null) : Exception(message, inner)
{
    public bool DeviceUnavailable { get; } = deviceUnavailable;
}
