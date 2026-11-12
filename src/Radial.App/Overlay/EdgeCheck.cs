using System.IO;
using System.Windows;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Core.Timing;
using Radial.Windows.Input;
using Radial.Windows.Overlay;
using Radial.Windows.Storage;

namespace Radial.App.Overlay;

/// <summary>
/// Herramienta de desarrollo: <c>Radial.App.exe --edge-check &lt;archivo&gt;</c> recorre la RUTA REAL del radial (coordinador →
/// <see cref="OverlayWindow"/> → Windows) en los cuatro bordes, las cuatro esquinas y el centro de cada monitor, y escribe lo que mide
/// Windows, no lo que calcula el código: el rectángulo exterior real de la ventana (<c>GetWindowRect</c>), su marco visible según DWM,
/// el centro presentado, y qué sector dibuja la vista para un cursor puesto en cada una de las ocho direcciones. También entra en un
/// submenú junto a una esquina (cambio de menú con la ventana visible). No instala hooks, no inyecta entrada de ningún tipo y no mueve
/// el cursor: la posición del cursor es un valor que se le da al coordinador. Se ve parpadear el radial en cada punto. No sustituye la
/// prueba con el ratón real (el cursor físico, la barra de tareas oculta y un juego en pantalla completa quedan fuera).
/// </summary>
internal static class EdgeCheck
{
    private const double ReachDip = 120;
    private const double ArmDip = 14;
    private const int SettleMilliseconds = 90;

    public static async Task RunAsync(string path)
    {
        var lines = new List<string> { $"Comprobación de bordes (ruta real) {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
        var failures = 0;
        var exitCode = 1;
        OverlayWindow? overlay = null;
        try
        {
            var configuration = DefaultConfiguration.Create();
            var pointer = new ScreenPoint(0, 0);
            overlay = new OverlayWindow();
            overlay.Prepare();
            // La MISMA fábrica que usa la aplicación: lo que se comprueba aquí es la composición de producción.
            var coordinator = RadialOverlayCoordinatorFactory.Create(configuration, overlay, () => pointer);
            long sequence = 0;
            var menuChanged = false;
            coordinator.MenuChanged += (_, _) => menuChanged = true;

            void Report(string name, bool ok, string detail)
            {
                if (!ok)
                {
                    failures++;
                }

                lines.Add($"  [{(ok ? "OK" : "FALLO")}] {name}: {detail}");
            }
            Report("el coordinador lleva el localizador de monitores (seguimiento activo)", coordinator.FollowsMonitors, coordinator.FollowsMonitors.ToString());
            coordinator.FollowTrace += line => lines.Add("    [traza] " + line);


            void Send(GestureEventKind kind, ScreenPoint at, CancelCause cause = CancelCause.None) =>
                coordinator.Handle(new GestureEvent(++sequence, 1_000 + sequence, kind, at, cause));

            var monitors = FindMonitors();
            lines.Add($"Monitores encontrados: {monitors.Count}");
            foreach (var (bounds, work) in monitors)
            {
                lines.Add($"Monitor {Rect(bounds)}; área de trabajo {Rect(work)}");
                foreach (var (label, point) in Positions(bounds))
                {
                    lines.Add($"- {label} en ({point.X}, {point.Y})");
                    pointer = point;
                    menuChanged = false;
                    Send(GestureEventKind.Activated, point);
                    await Task.Delay(SettleMilliseconds);

                    CheckWindow(coordinator, overlay, work, point, Report);
                    var presented = overlay.LastPlacement!.Value.Requested.Center;
                    var scale = OverlayPlacement.Scale(overlay.LastPlacement.Value.Dpi);
                    var displaced = presented != point;

                    coordinator.Poll();
                    if (displaced)
                    {
                        Report("sin selección hasta mover el cursor", overlay.CurrentSelection is null, $"selección {overlay.CurrentSelection?.ToString() ?? "ninguna"}");
                    }

                    // Mover un poco el cursor despierta la selección; después, cada dirección debe seleccionar su sector.
                    pointer = Toward(point, bounds, (int)Math.Ceiling(ArmDip * scale));
                    coordinator.Poll();
                    CheckDirections(coordinator, overlay, presented, scale, bounds, p => pointer = p, Report);

                    Send(GestureEventKind.Cancelled, pointer, CancelCause.Manual);
                    Report("se oculta al cancelar", !overlay.IsVisible, $"visible = {overlay.IsVisible}");
                }

                // Cambio de submenú con la ventana ya visible, junto a dos esquinas opuestas.
                foreach (var (label, point) in new[] { ("esquina superior izquierda", new ScreenPoint(bounds.Left, bounds.Top)), ("esquina inferior derecha", new ScreenPoint(bounds.Right - 1, bounds.Bottom - 1)) })
                {
                    lines.Add($"- submenú desde la {label} en ({point.X}, {point.Y})");
                    pointer = point;
                    menuChanged = false;
                    Send(GestureEventKind.Activated, point);
                    await Task.Delay(SettleMilliseconds);
                    var presented = overlay.LastPlacement!.Value.Requested.Center;
                    var scale = OverlayPlacement.Scale(overlay.LastPlacement.Value.Dpi);

                    pointer = Toward(point, bounds, (int)Math.Ceiling(ArmDip * scale));
                    coordinator.Poll();
                    pointer = Target(presented, RadialDirection.West, scale, bounds); // «Abrir» es un grupo: hay que permanecer encima
                    var waited = 0;
                    while (!menuChanged && waited < 2_000)
                    {
                        coordinator.Poll();
                        await Task.Delay(15);
                        waited += 15;
                    }

                    Report("entra en el submenú por permanencia", menuChanged, $"{waited} ms");
                    if (menuChanged)
                    {
                        await Task.Delay(SettleMilliseconds);
                        CheckWindow(coordinator, overlay, work, pointer, Report);
                        var center = overlay.LastPlacement!.Value.Requested.Center;
                        scale = OverlayPlacement.Scale(overlay.LastPlacement.Value.Dpi);
                        pointer = Toward(pointer, bounds, (int)Math.Ceiling(ArmDip * scale));
                        coordinator.Poll();
                        pointer = Target(center, RadialDirection.East, scale, bounds);
                        coordinator.Poll();
                        Report("en el submenú, el cursor al este del centro dibujado selecciona East", overlay.CurrentSelection == RadialDirection.East, $"selección {overlay.CurrentSelection?.ToString() ?? "ninguna"}");
                    }

                    Send(GestureEventKind.Cancelled, pointer, CancelCause.Manual);
                }
            }

            // El cursor cruza de un monitor a otro con el radial abierto, a través de cada borde compartido y en los dos sentidos: ruta real
            // (coordinador → localizador de Windows → ventana real). El cursor lo mueve esta herramienta paso a paso (no hay entrada).
            foreach (var (from, to, dx, dy) in AdjacentPairs(monitors))
            {
                lines.Add($"- cruce de {Rect(from.Bounds)} a {Rect(to.Bounds)} (dirección {dx},{dy})");
                var along = dx != 0
                    ? Middle(Math.Max(from.Bounds.Top, to.Bounds.Top), Math.Min(from.Bounds.Bottom, to.Bounds.Bottom))
                    : Middle(Math.Max(from.Bounds.Left, to.Bounds.Left), Math.Min(from.Bounds.Right, to.Bounds.Right));

                // t = 0: el último píxel del monitor de origen junto al borde; t > 0: dentro del monitor de destino; t < 0: dentro del de origen.
                ScreenPoint At(int t) => new(
                    dx > 0 ? from.Bounds.Right - 1 + t : dx < 0 ? from.Bounds.Left - t : along,
                    dy > 0 ? from.Bounds.Bottom - 1 + t : dy < 0 ? from.Bounds.Top - t : along);

                menuChanged = false;
                pointer = At(0);
                Send(GestureEventKind.Activated, pointer);
                await Task.Delay(SettleMilliseconds);
                var scaleFrom = OverlayPlacement.Scale(overlay.LastPlacement!.Value.Dpi);
                var armPixels = (int)Math.Ceiling(ArmDip * scaleFrom);
                pointer = At(-armPixels);
                coordinator.Poll();

                var followedAt = int.MinValue;
                for (var t = -armPixels; t <= 160 && followedAt == int.MinValue; t += 2)
                {
                    var centerBefore = overlay.LastPlacement!.Value.Requested.Center;
                    var selectionBefore = overlay.CurrentSelection;
                    pointer = At(t);
                    coordinator.Poll();
                    await Task.Delay(5);
                    if (overlay.LastPlacement!.Value.Requested.Center != centerBefore)
                    {
                        followedAt = t;
                        var selectionAfter = overlay.CurrentSelection;
                        Report("al cruzar, la selección no salta de un sector a otro", selectionAfter == selectionBefore || selectionAfter is null, $"antes {selectionBefore?.ToString() ?? "ninguna"}, después {selectionAfter?.ToString() ?? "ninguna"}");
                    }
                }

                var marginPixels = (int)Math.Ceiling(MonitorFollow.MarginDip * scaleFrom);
                Report("el radial sigue al cursor al otro monitor", followedAt != int.MinValue, followedAt == int.MinValue ? "no se movió" : $"se movió con el cursor a {followedAt} px del borde");
                Report("solo cruza pasado el margen (histéresis)", followedAt >= marginPixels, $"cruzó en t = {followedAt}, margen {marginPixels} px");
                if (followedAt == int.MinValue)
                {
                    Send(GestureEventKind.Cancelled, pointer, CancelCause.Manual);
                    continue;
                }

                await Task.Delay(SettleMilliseconds); // deja pasar el aviso de DPI que sigue al traslado
                CheckWindow(coordinator, overlay, to.Work, pointer, Report);

                // Moverse un poco más dentro del monitor nuevo despierta la selección, medida desde el centro dibujado NUEVO.
                var scaleTo = OverlayPlacement.Scale(overlay.LastPlacement!.Value.Dpi);
                pointer = At(followedAt + (int)Math.Ceiling(2 * ArmDip * scaleTo));
                coordinator.Poll();
                var center = overlay.LastPlacement.Value.Requested.Center;
                var expected = RadialGeometry.Select(new PointD(center.X, center.Y), new PointD(pointer.X, pointer.Y), configuration.Geometry.DeadZoneRadius * scaleTo);
                Report("tras moverse, la selección sale del centro dibujado nuevo", overlay.CurrentSelection == expected, $"selección {overlay.CurrentSelection?.ToString() ?? "ninguna"}, esperada {expected?.ToString() ?? "ninguna"}; cursor {Pt(pointer)}, centro {Pt(center)}, escala {scaleTo:F2}");

                // Un temblor junto al borde compartido no hace saltar el radial de un lado a otro.
                var settled = overlay.LastPlacement.Value.Requested.Center;
                foreach (var t in new[] { 1, -2, 2, -3 })
                {
                    pointer = At(t);
                    coordinator.Poll();
                }

                Report("un temblor junto al borde no lo mueve", overlay.LastPlacement.Value.Requested.Center == settled, $"centro {Pt(settled)} → {Pt(overlay.LastPlacement.Value.Requested.Center)}");

                // Volver: hay que alejarse el margen del monitor donde está el radial ahora.
                var backPixels = (int)Math.Ceiling((MonitorFollow.MarginDip * scaleTo) + 4);
                pointer = At(-backPixels);
                coordinator.Poll();
                await Task.Delay(SettleMilliseconds);
                Report("al volver al monitor de origen, el radial lo sigue", overlay.LastPlacement.Value.Requested.Center != settled, $"centro {Pt(settled)} → {Pt(overlay.LastPlacement.Value.Requested.Center)}");
                CheckWindow(coordinator, overlay, from.Work, pointer, Report);

                Send(GestureEventKind.Cancelled, pointer, CancelCause.Manual);
                Report("se oculta al cancelar", !overlay.IsVisible, $"visible = {overlay.IsVisible}");
            }

            lines.Add(failures == 0 ? "RESULTADO: todo correcto." : $"RESULTADO: {failures} FALLO(S).");
            exitCode = failures == 0 ? 0 : 2;
        }
        catch (Exception exception)
        {
            lines.Add("La comprobación se interrumpió: " + exception);
        }
        finally
        {
            overlay?.Close();
            File.WriteAllLines(path, lines);
            Application.Current.Shutdown(exitCode);
        }
    }

    /// <summary>Lo que se mide de la ventana REAL tras mostrarla (y esperar a que WPF termine su diseño).</summary>
    private static void CheckWindow(RadialOverlayCoordinator coordinator, OverlayWindow overlay, ScreenRect work, ScreenPoint cursor, Action<string, bool, string> report)
    {
        var placement = overlay.LastPlacement!.Value;
        var expectedSize = OverlayPlacement.PhysicalSize(RadialTheme.ViewSize, placement.Dpi);
        var real = OverlayWindowInterop.GetWindowBounds(overlay.Hwnd);
        var frame = OverlayWindowInterop.GetVisibleFrameBounds(overlay.Hwnd);
        var requested = placement.Requested;

        report("la ventana está visible", overlay.IsVisible && coordinator.IsActive, $"visible = {overlay.IsVisible}, activo = {coordinator.IsActive}");
        if (real is not { } rect)
        {
            report("rectángulo real de la ventana", false, "Windows no lo devolvió");
            return;
        }

        report("tamaño real = tamaño esperado (DIP × escala)", rect.Width == expectedSize && rect.Height == expectedSize, $"real {rect.Width}x{rect.Height}, esperado {expectedSize}, DPI {placement.Dpi}");
        report("rectángulo real = rectángulo pedido", rect.Left == requested.X && rect.Top == requested.Y, $"real ({rect.Left}, {rect.Top}), pedido ({requested.X}, {requested.Y})");
        var fits = work.Width < expectedSize || work.Height < expectedSize
            || (rect.Left >= work.Left && rect.Top >= work.Top && rect.Right <= work.Right && rect.Bottom <= work.Bottom);
        report("la ventana real cabe entera en el área de trabajo", fits, $"ventana {Rect(rect)}, área {Rect(work)}");
        if (frame is { } visible)
        {
            report("marco visible (DWM) = rectángulo real", visible == rect, $"DWM {Rect(visible)}, real {Rect(rect)}");
        }

        var centerX = rect.Left + (rect.Width / 2);
        var centerY = rect.Top + (rect.Height / 2);
        var presented = requested.Center;
        report("centro presentado = centro real de la ventana", Math.Abs(centerX - presented.X) <= 1 && Math.Abs(centerY - presented.Y) <= 1, $"presentado ({presented.X}, {presented.Y}), real ({centerX}, {centerY}), cursor ({cursor.X}, {cursor.Y})");
    }

    private static void CheckDirections(
        RadialOverlayCoordinator coordinator,
        OverlayWindow overlay,
        ScreenPoint center,
        double scale,
        ScreenRect bounds,
        Action<ScreenPoint> setPointer,
        Action<string, bool, string> report)
    {
        var wrong = new List<string>();
        foreach (var direction in Enum.GetValues<RadialDirection>())
        {
            var target = Target(center, direction, scale, bounds);
            setPointer(target);
            coordinator.Poll();
            var inside = target.X >= bounds.Left && target.X < bounds.Right && target.Y >= bounds.Top && target.Y < bounds.Bottom;
            if (!inside || overlay.CurrentSelection != direction)
            {
                wrong.Add($"{direction}: cursor ({target.X}, {target.Y}) {(inside ? "dentro" : "FUERA del monitor")}, selección {overlay.CurrentSelection?.ToString() ?? "ninguna"}");
            }
        }

        report("los ocho sectores son alcanzables y seleccionan su dirección", wrong.Count == 0, wrong.Count == 0 ? "8/8" : string.Join("; ", wrong));
    }

    /// <summary>Punto a <see cref="ReachDip"/> DIP del centro en la dirección dada (casi el borde exterior del dibujo).</summary>
    private static ScreenPoint Target(ScreenPoint center, RadialDirection direction, double scale, ScreenRect bounds)
    {
        var point = RadialLayout.PointAt(new PointD(center.X, center.Y), ReachDip * scale, RadialGeometry.CenterAngle(direction));
        return new ScreenPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    /// <summary>Un punto a <paramref name="pixels"/> de <paramref name="from"/> hacia el centro del monitor (nunca sale de él).</summary>
    private static ScreenPoint Toward(ScreenPoint from, ScreenRect bounds, int pixels)
    {
        var middleX = (bounds.Left + bounds.Right) / 2;
        var middleY = (bounds.Top + bounds.Bottom) / 2;
        var dx = Math.Sign(middleX - from.X);
        var dy = Math.Sign(middleY - from.Y);
        if (dx == 0 && dy == 0)
        {
            dx = 1;
        }

        return new ScreenPoint(
            Math.Clamp(from.X + (dx * pixels), bounds.Left, bounds.Right - 1),
            Math.Clamp(from.Y + (dy * pixels), bounds.Top, bounds.Bottom - 1));
    }

    private static IEnumerable<(string Label, ScreenPoint Point)> Positions(ScreenRect b)
    {
        var left = b.Left;
        var right = b.Right - 1;
        var top = b.Top;
        var bottom = b.Bottom - 1;
        var midX = (b.Left + b.Right) / 2;
        var midY = (b.Top + b.Bottom) / 2;
        yield return ("esquina superior izquierda", new ScreenPoint(left, top));
        yield return ("borde superior", new ScreenPoint(midX, top));
        yield return ("esquina superior derecha", new ScreenPoint(right, top));
        yield return ("borde izquierdo", new ScreenPoint(left, midY));
        yield return ("centro", new ScreenPoint(midX, midY));
        yield return ("borde derecho", new ScreenPoint(right, midY));
        yield return ("esquina inferior izquierda", new ScreenPoint(left, bottom));
        yield return ("borde inferior", new ScreenPoint(midX, bottom));
        yield return ("esquina inferior derecha", new ScreenPoint(right, bottom));
    }

    /// <summary>Los monitores, descubiertos muestreando el escritorio virtual (sin Windows Forms ni enumeración nativa).</summary>
    private static List<(ScreenRect Bounds, ScreenRect Work)> FindMonitors()
    {
        var virtualScreen = OverlayWindowInterop.GetVirtualScreen();
        var found = new List<(ScreenRect Bounds, ScreenRect Work)>();
        for (var y = virtualScreen.Top; y < virtualScreen.Bottom; y += 100)
        {
            for (var x = virtualScreen.Left; x < virtualScreen.Right; x += 100)
            {
                if (OverlayWindowInterop.GetMonitorAreas(new ScreenPoint(x, y)) is { } areas && !found.Any(f => f.Bounds == areas.Bounds))
                {
                    found.Add(areas);
                }
            }
        }

        return found;
    }

    private static string Rect(ScreenRect r) => $"({r.Left}, {r.Top})–({r.Right}, {r.Bottom})";

    /// <summary>Pares ordenados de monitores que comparten un borde, con la dirección del primero al segundo.</summary>
    private static IEnumerable<((ScreenRect Bounds, ScreenRect Work) From, (ScreenRect Bounds, ScreenRect Work) To, int Dx, int Dy)> AdjacentPairs(List<(ScreenRect Bounds, ScreenRect Work)> monitors)
    {
        foreach (var from in monitors)
        {
            foreach (var to in monitors)
            {
                if (from.Bounds == to.Bounds)
                {
                    continue;
                }

                var overlapsVertically = Math.Min(from.Bounds.Bottom, to.Bounds.Bottom) > Math.Max(from.Bounds.Top, to.Bounds.Top);
                var overlapsHorizontally = Math.Min(from.Bounds.Right, to.Bounds.Right) > Math.Max(from.Bounds.Left, to.Bounds.Left);
                if (from.Bounds.Right == to.Bounds.Left && overlapsVertically)
                {
                    yield return (from, to, 1, 0);
                }
                else if (from.Bounds.Left == to.Bounds.Right && overlapsVertically)
                {
                    yield return (from, to, -1, 0);
                }
                else if (from.Bounds.Bottom == to.Bounds.Top && overlapsHorizontally)
                {
                    yield return (from, to, 0, 1);
                }
                else if (from.Bounds.Top == to.Bounds.Bottom && overlapsHorizontally)
                {
                    yield return (from, to, 0, -1);
                }
            }
        }
    }

    private static int Middle(int low, int high) => low + ((high - low) / 2);

    private static string Pt(ScreenPoint p) => $"({p.X}, {p.Y})";
}
