using System.Text.Json.Nodes;
using Radial.Core.Validation;

namespace Radial.Core.Storage;

/// <summary>Un paso de migración del JSON de la versión <see cref="FromVersion"/> a la siguiente. Trabaja sobre el árbol JSON, no sobre el modelo.</summary>
public interface IConfigurationMigration
{
    int FromVersion { get; }

    /// <summary>
    /// Transforma <paramref name="root"/> en su sitio a la forma de la versión siguiente. El migrador actualiza <c>schemaVersion</c>.
    /// Las incoherencias que se resuelven por el camino (sin perder datos) se comunican añadiendo avisos a <paramref name="notes"/>.
    /// </summary>
    void Migrate(JsonObject root, ICollection<ValidationIssue> notes);
}

/// <summary>
/// Cadena de migraciones: lleva un archivo de la versión en que se escribió hasta <see cref="TargetVersion"/> aplicando, en orden,
/// una migración por versión. Falta de un paso intermedio = error (no se salta nada). Una versión mayor que
/// <see cref="TargetVersion"/> no se migra: la rechaza quien lee (<see cref="ConfigurationFile.Parse(string, ConfigurationMigrator?)"/>).
/// </summary>
public sealed class ConfigurationMigrator
{
    private readonly Dictionary<int, IConfigurationMigration> _byFrom = [];

    /// <exception cref="ArgumentException">Versión de destino inválida, migración repetida o fuera de rango.</exception>
    public ConfigurationMigrator(int targetVersion, IEnumerable<IConfigurationMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        if (targetVersion < 1)
        {
            throw new ArgumentException("La versión de destino debe ser al menos 1.", nameof(targetVersion));
        }

        TargetVersion = targetVersion;
        foreach (var migration in migrations)
        {
            if (migration is null || migration.FromVersion < 1 || migration.FromVersion >= targetVersion)
            {
                throw new ArgumentException($"Una migración no es válida: debe partir de una versión entre 1 y {targetVersion - 1}.", nameof(migrations));
            }

            if (!_byFrom.TryAdd(migration.FromVersion, migration))
            {
                throw new ArgumentException($"Hay dos migraciones desde la versión {migration.FromVersion}.", nameof(migrations));
            }
        }
    }

    /// <summary>Las migraciones de producción: la versión actual del esquema y la migración de la 1 a la 2 (la biblioteca de acciones).</summary>
    public static ConfigurationMigrator Default { get; } = new(ConfigurationFile.CurrentSchemaVersion, [new SchemaV1ToV2Migration()]);

    public int TargetVersion { get; }

    /// <summary>Migra <paramref name="root"/> en su sitio desde <paramref name="fromVersion"/> hasta <see cref="TargetVersion"/>.</summary>
    /// <exception cref="InvalidOperationException">Falta la migración de algún paso intermedio.</exception>
    public void Migrate(JsonObject root, int fromVersion, ICollection<ValidationIssue>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        notes ??= new List<ValidationIssue>();

        for (var version = fromVersion; version < TargetVersion; version++)
        {
            if (!_byFrom.TryGetValue(version, out var migration))
            {
                throw new InvalidOperationException($"No hay migración desde la versión {version} de la configuración.");
            }

            migration.Migrate(root, notes);
            root["schemaVersion"] = version + 1;
        }
    }
}
