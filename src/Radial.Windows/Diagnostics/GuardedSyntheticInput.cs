using Radial.Windows.Input;

namespace Radial.Windows.Diagnostics;

/// <summary>Una comprobación de seguridad falló: no se inyectó el evento.</summary>
public sealed class SyntheticInputUnsafeException(string message) : InvalidOperationException(message);

/// <summary>Lo que necesita <see cref="GuardedSyntheticInput"/> de Windows. Interno: las pruebas lo sustituyen por un falso.</summary>
internal interface ISyntheticBackend
{
    int OwnProcessId { get; }

    ScreenPoint GetCursor();

    int GetProcessIdAt(ScreenPoint point);

    int GetForegroundProcessId();

    void MoveTo(ScreenPoint point);

    void KeyDown(InputKey key);

    void KeyUp(InputKey key);

    void MiddleDown();

    void MiddleUp();

    void Wheel();
}

internal sealed class Win32SyntheticBackend : ISyntheticBackend
{
    public int OwnProcessId => Environment.ProcessId;

    public ScreenPoint GetCursor() => SyntheticInput.GetCursor();

    public int GetProcessIdAt(ScreenPoint point) => SyntheticInput.GetProcessIdAt(point);

    public int GetForegroundProcessId() => SyntheticInput.GetForegroundProcessId();

    public void MoveTo(ScreenPoint point) => SyntheticInput.MoveTo(point.X, point.Y);

    public void KeyDown(InputKey key) => SyntheticInput.KeyDown(key);

    public void KeyUp(InputKey key) => SyntheticInput.KeyUp(key);

    public void MiddleDown() => SyntheticInput.MiddleDown();

    public void MiddleUp() => SyntheticInput.MiddleUp();

    public void Wheel() => SyntheticInput.Wheel();
}

/// <summary>
/// SOLO DIAGNÓSTICO. Inyecta entrada sintética (con la marca de Radial) para la autoprueba, con tres protecciones:
///
/// 1. Antes de CADA inyección (<see cref="EnsureSafe"/>) comprueba que el cursor sigue donde la autoprueba lo dejó, que la
///    ventana bajo el cursor es de este proceso y que la ventana en primer plano (la que recibe el teclado) también lo es.
///    Si algo falla lanza <see cref="SyntheticInputUnsafeException"/> y no envía nada.
/// 2. Recuerda qué teclas y botones ha pulsado ella misma. <see cref="ReleaseAll"/> libera solo esas, de forma síncrona,
///    y las demás liberaciones (KeyUp/MiddleUp de algo que no pulsó) no se envían.
/// 3. <see cref="ReleaseAll"/> no hace la comprobación de seguridad: dejar una tecla o botón pulsados es peor que enviar
///    su liberación a otra ventana.
///
/// LIMITACIÓN: <see cref="EnsureSafe"/> reduce el riesgo, no lo elimina. Entre la comprobación y el <c>SendInput</c> pasan
/// microsegundos en los que otra ventana podría aparecer o tomar el primer plano; no hay forma de hacer ambas cosas de
/// manera atómica. Tampoco se puede garantizar nada sobre otro proceso con un hook propio que reaccione a la entrada.
/// No es seguro entre hilos: la autoprueba lo usa desde un único flujo.
/// </summary>
public sealed class GuardedSyntheticInput
{
    // Tolerancia del cursor: el movimiento absoluto se normaliza a 0..65535 y redondea; en escritorios grandes el error es de píxeles.
    private const int CursorTolerance = 3;

    private readonly ISyntheticBackend _backend;
    private ScreenPoint? _expectedCursor;
    private Held _held;

    [Flags]
    private enum Held
    {
        None = 0,
        LeftControl = 1,
        RightControl = 2,
        Escape = 4,
        Middle = 8,
    }

    public GuardedSyntheticInput()
        : this(new Win32SyntheticBackend())
    {
    }

    internal GuardedSyntheticInput(ISyntheticBackend backend)
    {
        _backend = backend;
    }

    /// <summary>Verdadero si la autoprueba ha pulsado algo que aún no ha liberado.</summary>
    public bool AnyHeld => _held != Held.None;

    public ScreenPoint GetCursor() => _backend.GetCursor();

    /// <summary>
    /// Mueve el cursor y recuerda la posición esperada. Solo un movimiento: no pulsa nada, así que no exige comprobación
    /// previa (también se usa para devolver el cursor a su sitio al terminar).
    /// </summary>
    public void MoveCursor(ScreenPoint target)
    {
        _backend.MoveTo(target);
        _expectedCursor = target;
    }

    /// <summary>
    /// Devuelve el cursor a <paramref name="original"/> solo si sigue donde la autoprueba lo dejó. Si alguien lo movió
    /// mientras tanto (el usuario), no se toca. Devuelve si lo movió.
    /// </summary>
    public bool RestoreCursorIfUntouched(ScreenPoint original)
    {
        if (_expectedCursor is not { } expected)
        {
            return false;
        }

        var cursor = _backend.GetCursor();
        if (Math.Abs(cursor.X - expected.X) > CursorTolerance || Math.Abs(cursor.Y - expected.Y) > CursorTolerance)
        {
            return false;
        }

        MoveCursor(original);
        return true;
    }

    /// <summary>Lo que la autoprueba tiene pulsado ahora, para informar al abortar (vacío si nada).</summary>
    public string DescribeHeld()
    {
        var parts = new List<string>();
        if ((_held & Held.LeftControl) != 0)
        {
            parts.Add("Ctrl izquierdo");
        }

        if ((_held & Held.RightControl) != 0)
        {
            parts.Add("Ctrl derecho");
        }

        if ((_held & Held.Escape) != 0)
        {
            parts.Add("Escape");
        }

        if ((_held & Held.Middle) != 0)
        {
            parts.Add("botón central");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Comprueba que es seguro inyectar ahora. Lanza <see cref="SyntheticInputUnsafeException"/> con el motivo si no lo es,
    /// o <see cref="InvalidOperationException"/> si aún no se ha colocado el cursor con <see cref="MoveCursor"/>.
    /// </summary>
    public void EnsureSafe()
    {
        if (_expectedCursor is not { } expected)
        {
            throw new InvalidOperationException("Antes de inyectar hay que colocar el cursor con MoveCursor.");
        }

        var cursor = _backend.GetCursor();
        if (Math.Abs(cursor.X - expected.X) > CursorTolerance || Math.Abs(cursor.Y - expected.Y) > CursorTolerance)
        {
            throw new SyntheticInputUnsafeException(
                $"el cursor ya no está donde la autoprueba lo dejó: esperado ({expected.X}, {expected.Y}), actual ({cursor.X}, {cursor.Y})");
        }

        var own = _backend.OwnProcessId;
        var underCursor = _backend.GetProcessIdAt(cursor);
        if (underCursor != own)
        {
            throw new SyntheticInputUnsafeException(
                $"bajo el cursor ({cursor.X}, {cursor.Y}) hay una ventana del proceso {underCursor}, no de Radial ({own})");
        }

        var foreground = _backend.GetForegroundProcessId();
        if (foreground != own)
        {
            throw new SyntheticInputUnsafeException($"la ventana en primer plano es del proceso {foreground}, no de Radial ({own})");
        }
    }

    public void KeyDown(InputKey key)
    {
        EnsureSafe();
        _backend.KeyDown(key);
        _held |= HeldOf(key);
    }

    /// <summary>Libera una tecla que la propia autoprueba pulsó. Si no la pulsó, no hace nada.</summary>
    public void KeyUp(InputKey key)
    {
        var flag = HeldOf(key);
        if ((_held & flag) == 0)
        {
            return;
        }

        EnsureSafe();
        _backend.KeyUp(key);
        _held &= ~flag;
    }

    public void MiddleDown()
    {
        EnsureSafe();
        _backend.MiddleDown();
        _held |= Held.Middle;
    }

    /// <summary>Libera el botón central si la propia autoprueba lo pulsó. Si no, no hace nada.</summary>
    public void MiddleUp()
    {
        if ((_held & Held.Middle) == 0)
        {
            return;
        }

        EnsureSafe();
        _backend.MiddleUp();
        _held &= ~Held.Middle;
    }

    public void Wheel()
    {
        EnsureSafe();
        _backend.Wheel();
    }

    /// <summary>
    /// Libera, de forma SÍNCRONA y sin comprobación de seguridad, únicamente lo que la autoprueba pulsó y no soltó: primero el
    /// botón central (para que el servicio, si sigue en marcha, consuma su liberación), luego Escape y por último Ctrl.
    /// Devuelve si todo quedó liberado. Se puede llamar varias veces.
    /// </summary>
    public bool ReleaseAll()
    {
        var all = TryRelease(Held.Middle, _backend.MiddleUp);
        all &= TryRelease(Held.Escape, () => _backend.KeyUp(InputKey.Escape));
        all &= TryRelease(Held.RightControl, () => _backend.KeyUp(InputKey.RightControl));
        all &= TryRelease(Held.LeftControl, () => _backend.KeyUp(InputKey.LeftControl));
        return all;
    }

    private bool TryRelease(Held flag, Action send)
    {
        if ((_held & flag) == 0)
        {
            return true;
        }

        try
        {
            send();
            _held &= ~flag;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false; // SendInput falló: sigue marcado como pulsado y se informa al llamador
        }
    }

    private static Held HeldOf(InputKey key) => key switch
    {
        InputKey.LeftControl => Held.LeftControl,
        InputKey.RightControl => Held.RightControl,
        InputKey.Escape => Held.Escape,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Tecla no soportada por la autoprueba."),
    };
}
