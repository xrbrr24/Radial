using Microsoft.Win32;
using Radial.Core.Diagnostics;

namespace Radial.Windows.Runtime;

/// <summary>Resultado de cambiar el arranque automático de Radial.</summary>
public sealed record StartupRegistrationResult(bool Success, bool Enabled, string Message)
{
    public static StartupRegistrationResult Changed(bool enabled) => new(true, enabled, string.Empty);

    public static StartupRegistrationResult Failed(bool enabled, string message) => new(false, enabled, message);
}

/// <summary>
/// Registro del arranque de Radial para el usuario actual. No usa tareas programadas, servicios ni permisos elevados: añade o quita
/// exclusivamente el valor <c>Radial</c> de <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Al iniciar con
/// <c>--background</c>, la aplicación crea su bandeja y captura sin dejar el centro de control abierto.
/// </summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    StartupRegistrationResult SetEnabled(bool enabled);
}

/// <summary>Implementación vacía para herramientas de desarrollo y pruebas que no deben tocar el Registro.</summary>
public sealed class NullStartupRegistration : IStartupRegistration
{
    public static NullStartupRegistration Instance { get; } = new();

    public bool IsEnabled => false;

    public StartupRegistrationResult SetEnabled(bool enabled) =>
        StartupRegistrationResult.Failed(false, "El arranque con Windows no está disponible en esta ejecución.");
}

/// <summary>Implementación de producción sobre la clave Run del usuario actual.</summary>
public sealed class StartupRegistration : IStartupRegistration
{
    public const string ValueName = "Radial";

    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly Func<string?> _processPath;
    private readonly IRegistryValues _registry;
    private readonly IRadialLog _log;

    public StartupRegistration(IRadialLog? log = null)
        : this(() => Environment.ProcessPath, new WindowsRegistryValues(), log ?? NullRadialLog.Instance)
    {
    }

    internal StartupRegistration(Func<string?> processPath, IRegistryValues registry, IRadialLog log)
    {
        ArgumentNullException.ThrowIfNull(processPath);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(log);
        _processPath = processPath;
        _registry = registry;
        _log = log;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                return _registry.Get(ValueName) is string { Length: > 0 };
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                _log.Warn(nameof(StartupRegistration), "No se pudo leer el estado del arranque con Windows.", exception, "leer arranque automático");
                return false;
            }
        }
    }

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                _registry.Remove(ValueName);
                _log.Info(nameof(StartupRegistration), "El arranque con Windows se desactivó.", "cambiar arranque automático");
                return StartupRegistrationResult.Changed(false);
            }

            var path = _processPath();
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return StartupRegistrationResult.Failed(false, "No se pudo determinar el ejecutable de Radial para iniciar con Windows.");
            }

            _registry.Set(ValueName, Quote(path) + " --background");
            _log.Info(nameof(StartupRegistration), "El arranque con Windows se activó.", "cambiar arranque automático");
            return StartupRegistrationResult.Changed(true);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            _log.Error(nameof(StartupRegistration), "No se pudo cambiar el arranque con Windows.", exception, "cambiar arranque automático");
            return StartupRegistrationResult.Failed(IsEnabled, "Windows no permitió cambiar el arranque automático. Vuelve a intentarlo.");
        }
    }

    internal static string Quote(string executablePath) => '"' + executablePath.Replace("\"", string.Empty, StringComparison.Ordinal) + '"';

    private static bool IsRegistryFailure(Exception exception) => exception is UnauthorizedAccessException or System.Security.SecurityException or IOException;
}

internal interface IRegistryValues
{
    object? Get(string name);

    void Set(string name, string value);

    void Remove(string name);
}

internal sealed class WindowsRegistryValues : IRegistryValues
{
    public object? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistration.RunKeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
