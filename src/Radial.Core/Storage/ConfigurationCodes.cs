namespace Radial.Core.Storage;

/// <summary>Códigos estables de los problemas al leer una configuración (los comprueban las pruebas, no los mensajes).</summary>
public static class ConfigurationCodes
{
    public const string TooLarge = "config.too-large";
    public const string JsonInvalid = "config.json-invalid";
    public const string RootInvalid = "config.root-invalid";
    public const string StructureInvalid = "config.structure-invalid";
    public const string SchemaVersionMissing = "config.schema-version-missing";
    public const string SchemaVersionInvalid = "config.schema-version-invalid";
    public const string SchemaVersionFuture = "config.schema-version-future";
    public const string MigrationFailed = "config.migration-failed";
    public const string FieldInvalid = "config.field-invalid";
    public const string ActionTypeMissing = "config.action-type-missing";
    public const string ActionTypeInvalid = "config.action-type-invalid";
    public const string ActionPropertyInvalid = "config.action-property-invalid";

    /// <summary>Aviso de la migración: una incoherencia del archivo antiguo que se resolvió (no se perdió nada).</summary>
    public const string MigrationNote = "config.migration-note";

    /// <summary>Un elemento tiene a la vez <c>actionId</c> y <c>action</c>: serían dos fuentes de verdad.</summary>
    public const string ItemActionConflict = "config.item-action-conflict";

    /// <summary>Un elemento referencia (<c>actionId</c>) una acción que no está en la biblioteca.</summary>
    public const string ActionRefMissing = "config.action-ref-missing";

    /// <summary>
    /// Aviso al cargar: una acción de un tipo conocido no es válida (dirección con un esquema no admitido, tecla fuera del vocabulario,
    /// ruta vacía...). Solo se neutraliza esa acción: se conserva su JSON íntegro, no se ejecuta y el resto de la configuración carga.
    /// </summary>
    public const string ActionNeutralized = "config.action-neutralized";
}
