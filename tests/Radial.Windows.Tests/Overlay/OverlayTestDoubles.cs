using Radial.Core.Execution;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;

namespace Radial.Windows.Tests.Overlay;

/// <summary>Reloj manual: las pruebas del coordinador no esperan nunca; avanzan el tiempo a mano.</summary>
internal sealed class TestClock : IClock
{
    public TimeSpan Now { get; set; }

    public void Advance(double milliseconds) => Now += TimeSpan.FromMilliseconds(milliseconds);
}

/// <summary>Presentador que anota todo lo que se le pide, en orden, y se puede hacer fallar.</summary>
internal sealed class RecordingPresenter : IRadialPresenter
{
    /// <summary>Factor píxeles/DIP que devuelve <see cref="Show"/>.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Factor que devuelve <see cref="ShowMenu"/> (otro monitor); si no se indica, el mismo que <see cref="Scale"/>.</summary>
    public double? MenuScale { get; set; }

    public bool Visible { get; private set; }

    public int Shows { get; private set; }

    public int Hides { get; private set; }

    /// <summary>Cada menú mostrado (el raíz con <see cref="Show"/> y los siguientes con <see cref="ShowMenu"/>), su nivel y su centro.</summary>
    public List<(RadialMenu Menu, int Depth, ScreenPoint Center)> Menus { get; } = [];

    public List<RadialDirection?> Selections { get; } = [];

    public List<RadialProgress> Progresses { get; } = [];

    /// <summary>Todo en orden: <c>show</c>, <c>menu:nombre</c>, <c>sel:dirección</c>, <c>progress</c>, <c>hide</c>.</summary>
    public List<string> Log { get; } = [];

    public Exception? FailOnShowMenu { get; set; }

    public int MenuChanges => Menus.Count - Shows;

    public double Show(RadialMenu menu, GeometrySettings geometry, ScreenPoint center)
    {
        Visible = true;
        Shows++;
        Menus.Add((menu, 1, center));
        Log.Add("show");
        return Scale;
    }

    public double ShowMenu(RadialMenu menu, int depth, ScreenPoint center)
    {
        if (!Visible)
        {
            throw new InvalidOperationException("Se cambió de menú con el overlay oculto.");
        }

        if (FailOnShowMenu is not null)
        {
            throw FailOnShowMenu;
        }

        Menus.Add((menu, depth, center));
        Log.Add("menu:" + menu.Name);
        return MenuScale ?? Scale;
    }

    public void SetSelection(RadialDirection? direction)
    {
        Selections.Add(direction);
        Log.Add("sel:" + (direction?.ToString() ?? "ninguno"));
    }

    public void SetProgress(RadialProgress progress)
    {
        Progresses.Add(progress);
        Log.Add("progress");
    }

    public void Hide()
    {
        Visible = false;
        Hides++;
        Log.Add("hide");
    }
}

/// <summary>Ejecutor que anota lo que se le pide y termina enseguida con éxito.</summary>
internal sealed class RecordingExecutor : IActionExecutor
{
    public List<RadialAction> Executed { get; } = [];

    public bool CanExecute(RadialAction action) => true;

    public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        Executed.Add(action);
        return Task.FromResult(ActionExecutionResult.Success);
    }
}

/// <summary>
/// Un coordinador completo con presentador, ejecutor y reloj falsos, y ayudas para mover el cursor, dejar pasar el tiempo y
/// empezar, confirmar o cancelar un gesto. Todo se mide en píxeles con la escala del presentador (1 por defecto).
/// </summary>
internal sealed class CoordinatorHarness
{
    public static readonly ScreenPoint Start = new(1000, 500);
    private readonly System.Collections.Concurrent.ConcurrentQueue<(RadialItem Item, ActionExecutionResult Result)> _finished = new();
    private readonly SemaphoreSlim _finishedSignal = new(0);
    private long _sequence;
    private long _timestamp = 1_000;

    /// <param name="executor">Quien ejecuta las acciones; por defecto un ejecutor que solo las anota (<see cref="Executor"/>).</param>
    public CoordinatorHarness(RadialConfiguration configuration, RecordingPresenter? presenter = null, IActionExecutor? executor = null)
    {
        Presenter = presenter ?? new RecordingPresenter();
        Coordinator = new RadialOverlayCoordinator(configuration, Clock, Presenter, () => Pointer, () => _timestamp, executor ?? Executor);
        Coordinator.ActionFinished += (item, result) =>
        {
            _finished.Enqueue((item, result));
            _finishedSignal.Release();
        };
        Coordinator.Completed += Completed.Add;
        Coordinator.Aborted += Aborted.Add;
        Coordinator.UnsupportedNavigation += Blocked.Add;
        Coordinator.MenuChanged += (menu, depth) => MenuChanges.Add((menu.Id, depth));
    }

    public TestClock Clock { get; } = new();

    public RecordingPresenter Presenter { get; }

    public RecordingExecutor Executor { get; } = new();

    public RadialOverlayCoordinator Coordinator { get; }

    public ScreenPoint Pointer { get; private set; } = Start;

    public List<Radial.Core.Engine.RadialResult> Completed { get; } = [];

    public List<CancelCause> Aborted { get; } = [];

    public List<MenuId> Blocked { get; } = [];

    public List<(MenuId Menu, int Depth)> MenuChanges { get; } = [];

    /// <summary>Espera (con un límite, nunca indefinidamente) a que termine la ejecución de la siguiente acción confirmada.</summary>
    public async Task<(RadialItem Item, ActionExecutionResult Result)> NextFinishedAsync()
    {
        if (!await _finishedSignal.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("La acción no terminó en 10 s.");
        }

        _finished.TryDequeue(out var finished);
        return finished;
    }

    /// <summary>Empieza el gesto en <see cref="Start"/> (o donde se indique).</summary>
    public void Activate(ScreenPoint? at = null) =>
        Coordinator.Handle(Event(GestureEventKind.Activated, at ?? Start));

    /// <summary>Mueve el cursor y deja que el coordinador lo lea (lo que hace el temporizador de la interfaz).</summary>
    public void Move(ScreenPoint to)
    {
        Pointer = to;
        Coordinator.Poll();
    }

    /// <summary>Deja pasar el tiempo SIN leer el cursor.</summary>
    public void Wait(double milliseconds) => Clock.Advance(milliseconds);

    /// <summary>Deja pasar el tiempo y lee el cursor donde está (el motor mide las permanencias con este sondeo).</summary>
    public void Dwell(double milliseconds)
    {
        Clock.Advance(milliseconds);
        Coordinator.Poll();
    }

    public void Release(ScreenPoint at)
    {
        Pointer = at;
        Coordinator.Handle(Event(GestureEventKind.Confirmed, at));
    }

    public void Cancel(CancelCause cause) => Coordinator.Handle(Event(GestureEventKind.Cancelled, Pointer, cause));

    private GestureEvent Event(GestureEventKind kind, ScreenPoint point, CancelCause cause = CancelCause.None) =>
        new(++_sequence, _timestamp += 10, kind, point, cause);
}
