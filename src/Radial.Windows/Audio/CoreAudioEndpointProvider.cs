using System.Runtime.InteropServices;

namespace Radial.Windows.Audio;

/// <summary>
/// <see cref="IAudioEndpointProvider"/> real sobre Windows Core Audio (MMDevice + <c>IAudioEndpointVolume</c>): el mismo control que
/// el mezclador de volumen de Windows; no simula teclas multimedia. Las interfaces COM se declaran a mano (sin paquetes), con el
/// orden de métodos de <c>mmdeviceapi.h</c> y <c>endpointvolume.h</c>; solo se llaman los necesarios. Cada llamada abre el
/// dispositivo predeterminado de reproducción (rol multimedia) y lo libera al terminar. Pensado para llamarse desde un hilo del pool,
/// no desde el de interfaz.
/// </summary>
public sealed class CoreAudioEndpointProvider : IAudioEndpointProvider
{
    private const int DataFlowRender = 0;
    private const int RoleMultimedia = 1;
    private const int ClsctxAll = 0x17;
    private const int ErrorNotFound = unchecked((int)0x80070490); // E_NOTFOUND: no hay dispositivo
    private const int AudclntDeviceInvalidated = unchecked((int)0x88890004);
    private const int AudclntServiceNotRunning = unchecked((int)0x88890010);

    private static readonly Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public IAudioEndpoint OpenDefaultOutput()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Check(enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out device));

            var iid = AudioEndpointVolumeId;
            Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var volume));
            return new CoreAudioEndpoint((IAudioEndpointVolume)volume);
        }
        catch (AudioDeviceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            throw new AudioDeviceException(
                "Windows no permitió abrir el dispositivo de salida de audio.",
                deviceUnavailable: exception is COMException com && IsUnavailable(com.HResult),
                exception);
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    internal static bool IsUnavailable(int hresult) =>
        hresult is ErrorNotFound or AudclntDeviceInvalidated or AudclntServiceNotRunning;

    internal static void Check(int hresult)
    {
        if (hresult < 0)
        {
            throw new AudioDeviceException(
                IsUnavailable(hresult)
                    ? "No hay un dispositivo de salida de audio disponible."
                    : $"Windows rechazó la operación de audio (0x{hresult:X8}).",
                IsUnavailable(hresult));
        }
    }

    internal static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            _ = Marshal.ReleaseComObject(comObject);
        }
    }

    private sealed class CoreAudioEndpoint : IAudioEndpoint
    {
        private IAudioEndpointVolume? _volume;
        private Guid _eventContext = Guid.Empty;

        public CoreAudioEndpoint(IAudioEndpointVolume volume)
        {
            _volume = volume;
        }

        public float GetLevel()
        {
            Check(Volume.GetMasterVolumeLevelScalar(out var level));
            return level;
        }

        public void SetLevel(float level) => Check(Volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref _eventContext));

        public bool GetMuted()
        {
            Check(Volume.GetMute(out var muted));
            return muted;
        }

        public void SetMuted(bool muted) => Check(Volume.SetMute(muted, ref _eventContext));

        public void Dispose()
        {
            Release(_volume);
            _volume = null;
        }

        private IAudioEndpointVolume Volume => _volume ?? throw new ObjectDisposedException(nameof(CoreAudioEndpoint));
    }

    // ───────────────────────── COM ─────────────────────────

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

        [PreserveSig]
        int GetVolumeStepInfo(out uint step, out uint stepCount);

        [PreserveSig]
        int VolumeStepUp(ref Guid eventContext);

        [PreserveSig]
        int VolumeStepDown(ref Guid eventContext);

        [PreserveSig]
        int QueryHardwareSupport(out uint hardwareSupportMask);

        [PreserveSig]
        int GetVolumeRange(out float levelMinDb, out float levelMaxDb, out float volumeIncrementDb);
    }
}
