namespace Radial.Core.Diagnostics;

/// <summary>Gravedad de una entrada del registro.</summary>
public enum LogLevel
{
    /// <summary>Información útil para seguir lo que hace Radial (arranque, cierre, configuración cargada).</summary>
    Information = 0,

    /// <summary>Algo no fue como se esperaba pero Radial sigue funcionando con normalidad (una acción falló, un aviso de configuración).</summary>
    Warning = 1,

    /// <summary>Un fallo: una operación no se pudo completar o una función quedó comprometida.</summary>
    Error = 2,

    /// <summary>Un fallo que obliga a detener Radial, o que lo habría hecho. Se escribe en disco antes de volver.</summary>
    Critical = 3,
}
