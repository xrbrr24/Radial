namespace Radial.Core.Model;

/// <summary>
/// Una acción cuyo tipo (<see cref="Type"/>, el discriminador del JSON) esta versión no conoce, p. ej. de una versión posterior o
/// de un editor distinto. Conserva el objeto JSON original ÍNTEGRO (<see cref="RawJson"/>, con sus propiedades anidadas) para
/// reescribirlo sin pérdida al guardar. NUNCA se ejecuta: ningún ejecutor la admite, el motor no la devuelve como ejecutable y
/// la validación solo la señala con un aviso. Sección 7.1 del documento técnico.
/// </summary>
/// <param name="Type">El discriminador original.</param>
/// <param name="RawJson">El objeto JSON completo de la acción, incluido el discriminador, tal como estaba en la configuración.</param>
public sealed record UnknownAction(string Type, string RawJson) : RadialAction
{
    // No es un error: la configuración sigue siendo utilizable. El validador la señala como advertencia.
    public override IEnumerable<string> GetProblems() => [];
}
