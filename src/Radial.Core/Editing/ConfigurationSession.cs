using Radial.Core.Model;
using Radial.Core.Storage;

namespace Radial.Core.Editing;

/// <summary>
/// Una sesión de edición de la configuración sobre el almacén: lleva la configuración que se edita (<see cref="Editor"/>), sabe si
/// tiene cambios sin guardar y guarda con <see cref="ISettingsStore"/> (que valida, escribe de forma atómica y no toca el archivo si
/// no ha cambiado nada). Un guardado fallido NO pierde los cambios: siguen en memoria y se puede reintentar. Sin interfaz.
/// «Cambios sin guardar» es una comparación del contenido que se escribiría, no una marca: deshacer a mano un cambio lo deja limpio
/// y no hay escrituras innecesarias.
/// </summary>
public sealed class ConfigurationSession
{
    private readonly ISettingsStore _store;
    private string _savedText;
    private RadialConfiguration _savedConfiguration;

    public ConfigurationSession(ISettingsStore store, SettingsLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(loaded);

        _store = store;
        Loaded = loaded;
        _savedConfiguration = loaded.Configuration;
        _savedText = ConfigurationFile.Serialize(loaded.Configuration);
        Editor = new ConfigurationEditor(loaded.Configuration);
        Editor.Changed += OnEditorChanged;
    }

    public ConfigurationEditor Editor { get; private set; }

    public RadialConfiguration Current => Editor.Current;

    /// <summary>Cómo se cargó la configuración y los avisos de la carga.</summary>
    public SettingsLoadResult Loaded { get; }

    /// <summary>Hay cambios que no están en el disco.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Se puede guardar (falso si el archivo del disco es de una versión futura o no se pudo leer: no se sobrescribe).</summary>
    public bool CanSave => Loaded.CanSave;

    /// <summary>El motivo del último guardado fallido; <c>null</c> si el último guardado fue bien o no hubo ninguno.</summary>
    public string? LastSaveError { get; private set; }

    /// <summary>Cambió el estado: una edición, un guardado o un descarte.</summary>
    public event Action? Changed;

    /// <summary>Se guardó correctamente (la configuración guardada, para aplicarla al radial en marcha).</summary>
    public event Action<RadialConfiguration>? Saved;

    public SettingsSaveResult Save()
    {
        if (!IsDirty)
        {
            return SettingsSaveResult.Unchanged;
        }

        var result = _store.Save(Current);
        if (result.Success)
        {
            _savedConfiguration = Current;
            _savedText = ConfigurationFile.Serialize(Current);
            LastSaveError = null;
            Refresh();
            Saved?.Invoke(Current);
        }
        else
        {
            LastSaveError = result.Message;
            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>Descarta los cambios y vuelve a la última configuración guardada.</summary>
    public void Discard()
    {
        Editor.Changed -= OnEditorChanged;
        Editor = new ConfigurationEditor(_savedConfiguration);
        Editor.Changed += OnEditorChanged;
        LastSaveError = null;
        Refresh();
    }

    private void OnEditorChanged() => Refresh();

    private void Refresh()
    {
        IsDirty = ConfigurationFile.Serialize(Current) != _savedText;
        Changed?.Invoke();
    }
}
