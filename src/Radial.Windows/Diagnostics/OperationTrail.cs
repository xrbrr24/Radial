using System.Globalization;
using System.Text;

namespace Radial.Windows.Diagnostics;

/// <summary>
/// Para saber QUÉ estaba haciendo Radial cuando falló algo. Dos cosas: la operación actual del flujo de ejecución
/// (<see cref="Current"/>, que sigue a los <c>await</c>) y un rastro de las últimas operaciones de todos los hilos
/// (<see cref="Snapshot"/>), que el registro añade como contexto a un error. Solo guarda nombres cortos de operación escritos por
/// Radial («procesar gesto», «guardar configuración»), nunca datos del usuario. Seguro entre hilos y de tamaño fijo.
/// </summary>
public sealed class OperationTrail
{
    public const int DefaultCapacity = 8;

    private readonly AsyncLocal<string?> _current = new();
    private readonly Queue<(DateTimeOffset At, string Text)> _recent = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly int _capacity;
    private readonly object _gate = new();

    public OperationTrail(Func<DateTimeOffset>? now = null, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _now = now ?? (() => DateTimeOffset.Now);
        _capacity = capacity;
    }

    /// <summary>La operación que se está ejecutando en este flujo (o <c>null</c> si no hay ninguna declarada).</summary>
    public string? Current => _current.Value;

    /// <summary>Declara una operación hasta que se libere el resultado (<c>using</c>). Se anota en el rastro.</summary>
    public IDisposable Begin(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var previous = _current.Value;
        _current.Value = operation;
        Note(operation);
        return new Scope(this, previous);
    }

    /// <summary>Anota un suceso en el rastro sin abrir un ámbito.</summary>
    public void Note(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_gate)
        {
            _recent.Enqueue((_now(), text));
            while (_recent.Count > _capacity)
            {
                _recent.Dequeue();
            }
        }
    }

    /// <summary>Las últimas operaciones, una por línea con su hora, de la más antigua a la más reciente; <c>null</c> si no hay ninguna.</summary>
    public string? Snapshot()
    {
        lock (_gate)
        {
            if (_recent.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var (at, text) in _recent)
            {
                builder.Append(at.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ').AppendLine(text);
            }

            return builder.ToString().TrimEnd();
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly OperationTrail _owner;
        private readonly string? _previous;
        private int _disposed;

        public Scope(OperationTrail owner, string? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner._current.Value = _previous;
            }
        }
    }
}
