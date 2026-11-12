using Radial.Core.Diagnostics;
using Radial.Core.Timing;
using Radial.Windows.Diagnostics;

namespace Radial.Windows.Tests.Diagnostics;

/// <summary>Reloj monotónico controlado por la prueba.</summary>
internal sealed class DiagClock : IClock
{
    private long _ticks;

    public TimeSpan Now => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public void Advance(TimeSpan amount) => Interlocked.Add(ref _ticks, amount.Ticks);
}

/// <summary>Un registro en memoria: guarda todo lo que recibe.</summary>
internal sealed class ListLog : IRadialLog
{
    private readonly List<Written> _entries = [];
    private readonly object _gate = new();

    public IReadOnlyList<Written> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Write(LogLevel level, string source, string message, Exception? exception = null, string? operation = null, string? context = null)
    {
        lock (_gate)
        {
            _entries.Add(new Written(level, source, message, exception, operation, context));
        }
    }

    internal sealed record Written(LogLevel Level, string Source, string Message, Exception? Exception, string? Operation, string? Context);
}

/// <summary>Un registro que siempre falla: comprueba que el sistema de errores no propaga el fallo.</summary>
internal sealed class ThrowingLog : IRadialLog
{
    public int Calls { get; private set; }

    public void Write(LogLevel level, string source, string message, Exception? exception = null, string? operation = null, string? context = null)
    {
        Calls++;
        throw new InvalidOperationException("el registro está roto (prueba)");
    }
}

internal sealed class RecordingNotifier : IUserNotifier
{
    private readonly List<UserNotice> _notices = [];
    private readonly object _gate = new();

    public IReadOnlyList<UserNotice> Notices
    {
        get
        {
            lock (_gate)
            {
                return [.. _notices];
            }
        }
    }

    public void Notify(UserNotice notice)
    {
        lock (_gate)
        {
            _notices.Add(notice);
        }
    }
}

internal sealed class ThrowingNotifier : IUserNotifier
{
    public int Calls { get; private set; }

    public void Notify(UserNotice notice)
    {
        Calls++;
        throw new InvalidOperationException("el aviso está roto (prueba)");
    }
}

internal sealed class RecordingMarker : IFatalMarker
{
    public List<(DateTimeOffset When, string Summary)> Records { get; } = [];

    public void Record(DateTimeOffset when, string summary) => Records.Add((when, summary));
}

internal sealed class ThrowingMarker : IFatalMarker
{
    public void Record(DateTimeOffset when, string summary) => throw new IOException("no se puede escribir el marcador (prueba)");
}

internal sealed class FakePresence : IUserPresence
{
    public bool Looking { get; set; }

    public bool Throws { get; set; }

    public bool IsLookingAtWindow => Throws ? throw new InvalidOperationException("presencia rota (prueba)") : Looking;
}

/// <summary>Un canal de aviso falso: disponible o no, y puede lanzar al mostrar.</summary>
internal sealed class FakeSink : INoticeSink
{
    public bool Available { get; set; } = true;

    public bool ThrowOnShow { get; set; }

    public List<UserNotice> Shown { get; } = [];

    public bool IsAvailable => Available;

    public void Show(UserNotice notice)
    {
        if (ThrowOnShow)
        {
            throw new InvalidOperationException("el canal está roto (prueba)");
        }

        Shown.Add(notice);
    }
}
