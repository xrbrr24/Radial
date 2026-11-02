using Radial.Windows.Input;

namespace Radial.App.Diagnostics;

/// <summary>
/// Registro de los eventos críticos que ya leyó la ventana de diagnóstico. El canal del servicio tiene un único lector;
/// la autoprueba consulta este registro en lugar de leer el canal.
/// </summary>
internal sealed class GestureLog
{
    private readonly List<GestureEvent> _items = [];
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public void Append(GestureEvent e)
    {
        lock (_gate)
        {
            _items.Add(e);
        }
    }

    public IReadOnlyList<GestureEvent> Since(int index)
    {
        lock (_gate)
        {
            return _items.Skip(index).ToArray();
        }
    }
}

/// <summary>Lo que la aplicación recibió de verdad (a nivel de ventana), para saber qué eventos se consumieron.</summary>
internal sealed class AppObservations
{
    public int MiddleDowns { get; set; }

    public int MiddleUps { get; set; }

    public int EscapeDowns { get; set; }

    public void Reset() => MiddleDowns = MiddleUps = EscapeDowns = 0;
}
