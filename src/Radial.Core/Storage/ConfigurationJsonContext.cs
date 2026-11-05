using System.Text.Json.Serialization;

namespace Radial.Core.Storage;

/// <summary>
/// Contexto de <c>System.Text.Json</c> generado en compilación (sin reflexión): propiedades en camelCase, JSON con sangrías y sin
/// escribir los valores nulos. La lectura es sensible a mayúsculas: los nombres del archivo son los que escribe Radial.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConfigFileDto))]
internal partial class ConfigurationJsonContext : JsonSerializerContext
{
}
