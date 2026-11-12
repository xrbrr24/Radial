using Radial.Core.Diagnostics;

namespace Radial.Windows.Diagnostics;

/// <summary>Qué tipo de aviso es: lo que el usuario puede ignorar o lo que compromete el funcionamiento de Radial.</summary>
public enum UserNoticeKind
{
    /// <summary>Una operación falló y Radial sigue entero (p. ej. una acción no se pudo ejecutar). Un aviso breve que desaparece.</summary>
    Notice,

    /// <summary>Una función de Radial quedó comprometida o Radial va a cerrarse. Se avisa por todos los medios y queda visible (banner).</summary>
    Problem,
}

/// <summary>
/// Un aviso al usuario, ya redactado para personas: nunca lleva el texto de una excepción, rutas ni direcciones.
/// <paramref name="Key"/> identifica el tipo de aviso (para agrupar los repetidos).
/// </summary>
public sealed record UserNotice(UserNoticeKind Kind, string Title, string Message, string Key);

/// <summary>
/// Cómo se le dice algo al usuario, sea cual sea la forma. Lo implementa <see cref="NotificationRouter"/>. <see cref="Notify"/> NO
/// lanza, NO bloquea (puede llamarse desde un hilo de trabajo) y no abre diálogos modales.
/// </summary>
public interface IUserNotifier
{
    void Notify(UserNotice notice);
}

/// <summary>Si el usuario está mirando la ventana de Radial ahora mismo (visible, no minimizada y activa). Se puede leer desde cualquier hilo.</summary>
public interface IUserPresence
{
    bool IsLookingAtWindow { get; }
}

/// <summary>
/// Un canal concreto de aviso (el banner/aviso dentro de la ventana, o el globo de la bandeja). Quien lo implementa se encarga de
/// llevar el aviso al hilo que corresponda SIN esperar. <see cref="IsAvailable"/> es falso mientras el canal no existe todavía
/// (p. ej. la bandeja durante el arranque) o ya no existe (cierre).
/// </summary>
public interface INoticeSink
{
    bool IsAvailable { get; }

    void Show(UserNotice notice);
}

/// <summary>
/// El enrutado de los avisos, sin WPF ni bandeja real (se prueba con canales falsos). Reutiliza los dos canales que ya tiene Radial:
/// los avisos dentro de la ventana y la bandeja del sistema.
/// - <see cref="UserNoticeKind.Notice"/>: dentro de la ventana si el usuario la está mirando; si no, globo de la bandeja. Si el canal
///   elegido no está disponible o falla, el otro.
/// - <see cref="UserNoticeKind.Problem"/>: SIEMPRE los dos (el globo para enterarse con la ventana oculta y el banner dentro de la
///   ventana para que se vea al abrirla).
/// - Si no se pudo entregar por ningún canal (p. ej. aún no hay bandeja), se guarda (hasta <see cref="MaxPending"/>) y se entrega con
///   <see cref="DeliverPending"/> cuando haya un canal.
/// Un canal que lanza se registra en el log y no impide el otro; ninguna excepción sale de aquí.
/// </summary>
public sealed class NotificationRouter : IUserNotifier
{
    public const int MaxPending = 8;

    private readonly IRadialLog _log;
    private readonly IUserPresence _presence;
    private readonly INoticeSink _inApp;
    private readonly INoticeSink _tray;
    private readonly List<UserNotice> _pending = [];
    private readonly object _gate = new();

    public NotificationRouter(IRadialLog log, IUserPresence presence, INoticeSink inApp, INoticeSink tray)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(inApp);
        ArgumentNullException.ThrowIfNull(tray);

        _log = log;
        _presence = presence;
        _inApp = inApp;
        _tray = tray;
    }

    /// <summary>Avisos que esperan a que haya un canal.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Notify(UserNotice notice)
    {
        if (notice is null)
        {
            return;
        }

        try
        {
            if (!Deliver(notice))
            {
                Enqueue(notice);
            }
        }
        catch (Exception exception)
        {
            // Nada de lo anterior debería lanzar (cada canal está protegido); si lo hace, el aviso se pierde pero el fallo queda anotado.
            SafeLog("El enrutado de un aviso falló.", exception);
        }
    }

    /// <summary>Reintenta los avisos que no se pudieron entregar. Se llama cuando aparece un canal (la bandeja, la ventana).</summary>
    public void DeliverPending()
    {
        List<UserNotice> batch;
        lock (_gate)
        {
            batch = [.. _pending];
            _pending.Clear();
        }

        foreach (var notice in batch)
        {
            Notify(notice);
        }
    }

    private bool Deliver(UserNotice notice)
    {
        if (notice.Kind == UserNoticeKind.Problem)
        {
            var viaTray = TryShow(_tray, notice, "la bandeja");
            var viaWindow = TryShow(_inApp, notice, "la ventana");
            return viaTray || viaWindow;
        }

        var looking = false;
        try
        {
            looking = _presence.IsLookingAtWindow;
        }
        catch (Exception exception)
        {
            SafeLog("No se pudo saber si el usuario ve la ventana.", exception);
        }

        var (first, firstName, second, secondName) = looking
            ? (_inApp, "la ventana", _tray, "la bandeja")
            : (_tray, "la bandeja", _inApp, "la ventana");
        return TryShow(first, notice, firstName) || TryShow(second, notice, secondName);
    }

    private bool TryShow(INoticeSink sink, UserNotice notice, string name)
    {
        try
        {
            if (!sink.IsAvailable)
            {
                return false;
            }

            sink.Show(notice);
            return true;
        }
        catch (Exception exception)
        {
            SafeLog($"No se pudo mostrar un aviso por {name}.", exception);
            return false;
        }
    }

    private void Enqueue(UserNotice notice)
    {
        lock (_gate)
        {
            _pending.Add(notice);
            while (_pending.Count > MaxPending)
            {
                _pending.RemoveAt(0);
            }
        }
    }

    private void SafeLog(string message, Exception exception)
    {
        try
        {
            _log.Write(LogLevel.Warning, nameof(NotificationRouter), message, exception, "avisar al usuario");
        }
        catch
        {
            // El registro es lo último: si también falla no hay dónde decirlo, y no se propaga.
        }
    }
}
