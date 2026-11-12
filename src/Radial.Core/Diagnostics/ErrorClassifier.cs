using System.Runtime.InteropServices;

namespace Radial.Core.Diagnostics;

/// <summary>Cuánto compromete un error el funcionamiento de Radial.</summary>
public enum ErrorImpact
{
    /// <summary>Una operación falló pero Radial sigue funcionando entero: se registra y, si el usuario lo necesita, se le avisa.</summary>
    Recoverable,

    /// <summary>Una función de Radial quedó comprometida (no se puede guardar, la captura global está detenida) pero la aplicación puede seguir abierta.</summary>
    Degraded,

    /// <summary>No se puede garantizar un estado sano: Radial debe soltar la captura, registrar y terminar.</summary>
    Fatal,
}

/// <summary>En qué contexto de ejecución se produjo el error. Decide, junto con el tipo de excepción, el <see cref="ErrorImpact"/>.</summary>
public enum ErrorOrigin
{
    /// <summary>Excepción no controlada en el hilo de interfaz (un manejador de evento, un comando, un enlace).</summary>
    UiThread,

    /// <summary>Excepción no controlada en otro hilo (hilo del pool, temporizador, hilo propio). El runtime termina el proceso.</summary>
    BackgroundThread,

    /// <summary>Una tarea falló y nadie esperó su resultado. El runtime NO termina el proceso por esto.</summary>
    UnobservedTask,

    /// <summary>Durante el arranque, antes de que Radial esté listo.</summary>
    Startup,

    /// <summary>Durante el cierre ordenado.</summary>
    Shutdown,

    /// <summary>Al ejecutar una acción del radial o un comando del usuario.</summary>
    Action,

    /// <summary>Al leer o escribir la configuración o el registro en disco.</summary>
    Storage,

    /// <summary>En la captura global de teclado y ratón o en su lector de eventos.</summary>
    Capture,

    /// <summary>En el propio mecanismo de aviso al usuario. Nunca escala y nunca vuelve a avisar (no hay bucle de avisos).</summary>
    Notification,
}

/// <summary>
/// La política de recuperación, en un solo sitio y sin dependencias de Windows. Dos entradas deciden el resultado:
///
/// 1. El TIPO de la excepción (o el de cualquiera de sus internas). OutOfMemory, AccessViolation, StackOverflow,
///    InvalidProgram, BadImageFormat, SEH, la caída de un dominio y un fallo de inicializador estático son <see cref="ErrorImpact.Fatal"/>
///    en cualquier contexto: el proceso puede tener la memoria o el estado corrompidos, y seguir «como si nada» ocultaría un estado
///    potencialmente dañado.
/// 2. El ORIGEN. Interfaz, tarea sin observar, acción y cierre: recuperable (la operación falla, Radial sigue; en la interfaz, con un
///    presupuesto de errores, ver <see cref="ErrorBudget"/>, para no seguir indefinidamente en un estado inestable). Almacenamiento y
///    captura: degradado. Arranque: fatal (no hay un Radial funcionando al que volver). Otro hilo: fatal (el runtime ya termina el
///    proceso; solo queda dejar constancia). Aviso al usuario: recuperable siempre.
/// </summary>
public static class ErrorClassifier
{
    private const int MaxChainDepth = 8;

    public static ErrorImpact Classify(Exception exception, ErrorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Un fallo del propio aviso nunca escala: si lo hiciera, un icono de bandeja roto podría tumbar la aplicación.
        if (origin == ErrorOrigin.Notification)
        {
            return ErrorImpact.Recoverable;
        }

        if (IsFatalType(exception))
        {
            return ErrorImpact.Fatal;
        }

        return origin switch
        {
            ErrorOrigin.Startup => ErrorImpact.Fatal,
            ErrorOrigin.BackgroundThread => ErrorImpact.Fatal,
            ErrorOrigin.Storage => ErrorImpact.Degraded,
            ErrorOrigin.Capture => ErrorImpact.Degraded,
            _ => ErrorImpact.Recoverable,
        };
    }

    /// <summary>La excepción (o alguna de sus internas) es de un tipo tras el que no se puede confiar en el estado del proceso.</summary>
    public static bool IsFatalType(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var pending = new Stack<(Exception Exception, int Depth)>();
        pending.Push((exception, 0));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (current is OutOfMemoryException
                or AccessViolationException
                or StackOverflowException
                or InvalidProgramException
                or BadImageFormatException
                or SEHException
                or AppDomainUnloadedException
                or CannotUnloadAppDomainException
                or TypeInitializationException)
            {
                return true;
            }

            if (depth >= MaxChainDepth)
            {
                continue;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push((inner, depth + 1));
                }
            }
            else if (current.InnerException is { } next)
            {
                pending.Push((next, depth + 1));
            }
        }

        return false;
    }
}
