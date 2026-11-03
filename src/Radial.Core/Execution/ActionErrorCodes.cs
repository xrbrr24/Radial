namespace Radial.Core.Execution;

/// <summary>
/// Códigos de error estables de <see cref="ActionExecutionError.Code"/>. Los dos primeros los define el propio contrato; el
/// resto los añaden los ejecutores concretos solo cuando existe una situación real que hay que distinguir (cada ejecutor nuevo,
/// p. ej. el atajo con su vencimiento, añadirá los suyos).
/// </summary>
public static class ActionErrorCodes
{
    /// <summary>El ejecutor no sabe ejecutar este tipo de acción (<see cref="IActionExecutor.CanExecute"/> es falso).</summary>
    public const string UnsupportedAction = "UnsupportedAction";

    /// <summary>La acción tiene problemas según <see cref="Model.RadialAction.GetProblems"/>: no se ejecuta nada.</summary>
    public const string InvalidAction = "InvalidAction";

    /// <summary>El destino no existe: el archivo o la carpeta no están, o la aplicación no se encontró al lanzarla.</summary>
    public const string TargetNotFound = "TargetNotFound";

    /// <summary>El sistema rechazó abrir el destino (sin aplicación asociada, acceso denegado, elevación cancelada...).</summary>
    public const string LaunchFailed = "LaunchFailed";

    /// <summary>La dirección no usa un esquema que se pueda lanzar. En la fase 4 solo se lanzan <c>http</c> y <c>https</c>.</summary>
    public const string UrlNotAllowed = "UrlNotAllowed";
}
