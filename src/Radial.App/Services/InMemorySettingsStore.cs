using Radial.Core.Model;
using Radial.Core.Storage;

namespace Radial.App.Services;

/// <summary>
/// Almacén que no toca el disco: lo usa la autoprueba (<c>--selftest</c>, que no debe escribir nada) y la ruta de seguridad de un
/// fallo inesperado al cargar la configuración. Guardar en él valida igual que el real pero solo conserva el resultado en memoria.
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly RadialConfiguration _initial;

    public InMemorySettingsStore(RadialConfiguration initial) => _initial = initial;

    public SettingsLoadResult LoadOrCreate() => new(_initial, SettingsSource.DefaultsNotPersisted, [], CanSave: true);

    public SettingsSaveResult Save(RadialConfiguration configuration)
    {
        return Radial.Core.Validation.ConfigurationValidator.Validate(configuration).IsValid
            ? SettingsSaveResult.Saved
            : SettingsSaveResult.Failed("La configuración no es válida.");
    }
}
