namespace Radial.Core.Model;

public enum IconSource
{
    /// <summary>Glifo de una fuente de iconos del sistema; <see cref="IconReference.Value"/> es su nombre.</summary>
    Glyph,

    /// <summary>Imagen o ejecutable del que extraer el icono; <see cref="IconReference.Value"/> es su ruta.</summary>
    File,
}

public sealed record IconReference(IconSource Source, string Value);
