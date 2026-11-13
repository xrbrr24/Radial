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

    /// <summary>
    /// Un atajo de teclado venció: tras esperar el tiempo configurado seguía pulsado algún modificador físico (Ctrl, Mayús, Alt o
    /// Win). No se envió ninguna tecla.
    /// </summary>
    public const string ModifiersNotReleased = "ModifiersNotReleased";

    /// <summary>
    /// La ventana en primer plano cambió mientras el atajo esperaba, o no hay ninguna ventana en primer plano: no se envió, para que
    /// el atajo no llegue a otra aplicación ni a ninguna.
    /// </summary>
    public const string ForegroundChanged = "ForegroundChanged";

    /// <summary>El sistema no aceptó la entrada de teclado que se intentó enviar (se liberó cualquier tecla que hubiera quedado pulsada).</summary>
    public const string InputInjectionFailed = "InputInjectionFailed";

    /// <summary>
    /// El sistema no aceptó la entrada de teclado Y tampoco se pudo confirmar la liberación de lo que hubiera quedado pulsado
    /// (incluso tras reintentarla): es posible que alguna tecla, normalmente un modificador, siga pulsada para Windows. Pulsar y
    /// soltar esa tecla en el teclado físico lo corrige.
    /// </summary>
    public const string KeyReleaseFailed = "KeyReleaseFailed";

    /// <summary>
    /// El destino de una aplicación, archivo o carpeta no se lanza por política (ruta de dispositivo, protocolo del sistema o ruta
    /// que no es ni absoluta ni un nombre de programa). Distinto de <see cref="TargetNotFound"/>: no se llegó a mirar si existe.
    /// </summary>
    public const string TargetNotAllowed = "TargetNotAllowed";

    /// <summary>No hay dispositivo de salida de audio predeterminado, o Windows no permite abrirlo ahora (desconectado, servicio de audio detenido).</summary>
    public const string AudioUnavailable = "AudioUnavailable";

    /// <summary>Windows rechazó leer o cambiar el volumen o el silencio del dispositivo de salida.</summary>
    public const string AudioControlFailed = "AudioControlFailed";

    /// <summary>
    /// No hay una ventana externa sobre la que actuar: ninguna en primer plano, es de Radial, es el escritorio o la barra de tareas,
    /// o dejó de existir. No se hizo nada.
    /// </summary>
    public const string WindowNotAvailable = "WindowNotAvailable";

    /// <summary>La ventana existe pero no admite la operación pedida (p. ej. no se puede maximizar, o ya está maximizada).</summary>
    public const string WindowOperationNotSupported = "WindowOperationNotSupported";

    /// <summary>Windows rechazó la operación sobre la ventana (p. ej. la ventana pertenece a un proceso elevado).</summary>
    public const string WindowOperationFailed = "WindowOperationFailed";

    /// <summary>Un ejecutor lanzó una excepción en lugar de devolver un resultado: es un defecto, pero no debe quedar en silencio.</summary>
    public const string UnexpectedError = "UnexpectedError";
}
