using System.IO;
using System.Windows;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Input;
using Radial.Windows.Overlay;
using Radial.Windows.Storage;

namespace Radial.App.Overlay;

/// <summary>
/// Herramienta de desarrollo: <c>Radial.App.exe --overlay-check &lt;archivo&gt;</c> muestra el overlay real sobre la posición actual
/// del cursor durante un instante y escribe qué comprobó Windows: estilos de la ventana, DPI, centrado y si cambió la ventana en
/// primer plano. No instala hooks, no inyecta entrada de ningún tipo y no hace clic (el click-through no se prueba con clics
/// sintéticos): solo mira el estado de la ventana. No sustituye la comprobación visual y con el ratón.
/// </summary>
internal static class OverlayCheck
{
    public static async Task RunAsync(string path)
    {
        var lines = new List<string> { $"Comprobación del overlay {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };
        var exitCode = 1;
        OverlayWindow? overlay = null;
        try
        {
            var configuration = DefaultConfiguration.Create();
            configuration.TryGetMenu(configuration.RootMenuId, out var menu);

            overlay = new OverlayWindow();
            overlay.Diagnostic += line => lines.Add(line);
            overlay.Prepare();

            var cursor = OverlayWindowInterop.GetCursorPosition();
            if (cursor is null)
            {
                lines.Add("FALLO: Windows no devolvió la posición del cursor.");
                return;
            }

            var styles = overlay.Styles;
            var scale = overlay.Show(menu!, configuration.Geometry, cursor.Value);
            overlay.SetSelection(RadialDirection.East);
            await Task.Delay(500);

            var placement = overlay.LastPlacement!.Value;
            var centerX = placement.Actual.X + placement.Actual.Size / 2;
            var centerY = placement.Actual.Y + placement.Actual.Size / 2;
            var after = OverlayWindowInterop.ReadStyles(overlay.Hwnd);

            var checks = new (string Name, bool Passed, string Detail)[]
            {
                ("WS_EX_LAYERED (transparencia)", after.Layered, after.Layered ? "sí" : "no"),
                ("WS_EX_TRANSPARENT (click-through)", after.Transparent, after.Transparent ? "sí" : "no"),
                ("WS_EX_NOACTIVATE (no se activa)", after.NoActivate, after.NoActivate ? "sí" : "no"),
                ("WS_EX_TOOLWINDOW (fuera de Alt+Tab)", after.ToolWindow, after.ToolWindow ? "sí" : "no"),
                ("WS_EX_TOPMOST (siempre encima)", after.Topmost, after.Topmost ? "sí" : "no"),
                ("Mostrar no quitó ningún estilo", (after.Raw & styles.Raw) == styles.Raw, $"antes 0x{styles.Raw:X8}, después 0x{after.Raw:X8}"),
                ("Centrado en el cursor (±1 px)", Math.Abs(centerX - cursor.Value.X) <= 1 && Math.Abs(centerY - cursor.Value.Y) <= 1, $"cursor ({cursor.Value.X}, {cursor.Value.Y}), centro de la ventana ({centerX}, {centerY})"),
                ("Tamaño físico = tamaño en DIP × escala", placement.Actual.Size == OverlayPlacement.PhysicalSize(RadialTheme.ViewSize, placement.Dpi), $"{placement.Actual.Size} px, DPI {placement.Dpi}, escala {scale:F2}"),
                ("El foco no cambió al mostrar", overlay.LastShowKeptForeground == true, overlay.LastShowKeptForeground?.ToString() ?? "sin dato"),
                ("La ventana no está activa", !overlay.IsActive, overlay.IsActive ? "IsActive = true" : "IsActive = false"),
            };

            foreach (var (name, passed, detail) in checks)
            {
                lines.Add($"[{(passed ? "OK" : "FALLO")}] {name}: {detail}");
            }

            // Cambio de menú con la ventana ya visible (entrar en un grupo): se recoloca en otro punto sin ocultarla ni cambiar el foco.
            configuration.TryGetMenu(new MenuId(DefaultConfiguration.OpenMenuName), out var submenu);
            var moved = new ScreenPoint(cursor.Value.X + 60, cursor.Value.Y + 40);
            var foregroundBeforeMenu = OverlayWindowInterop.GetForegroundWindow();
            overlay.ShowMenu(submenu!, 2, moved);
            overlay.SetProgress(new RadialProgress(0.5, 0, true));
            await Task.Delay(300);
            var movedPlacement = overlay.LastPlacement!.Value;
            var movedX = movedPlacement.Actual.X + movedPlacement.Actual.Size / 2;
            var movedY = movedPlacement.Actual.Y + movedPlacement.Actual.Size / 2;
            var menuChecks = new (string Name, bool Passed, string Detail)[]
            {
                ("Cambio de menú: sigue visible (no se ocultó)", overlay.IsVisible, overlay.IsVisible.ToString()),
                ("Cambio de menú: recentrado en el punto nuevo (±1 px)", Math.Abs(movedX - moved.X) <= 1 && Math.Abs(movedY - moved.Y) <= 1, $"punto ({moved.X}, {moved.Y}), centro de la ventana ({movedX}, {movedY})"),
                ("Cambio de menú: el foco no cambió", OverlayWindowInterop.GetForegroundWindow() == foregroundBeforeMenu, "comparado antes y después"),
                ("Cambio de menú: la ventana no está activa", !overlay.IsActive, overlay.IsActive.ToString()),
                ("Cambio de menú: estilos intactos", (OverlayWindowInterop.ReadStyles(overlay.Hwnd).Raw & styles.Raw) == styles.Raw, $"0x{OverlayWindowInterop.ReadStyles(overlay.Hwnd).Raw:X8}"),
            };

            foreach (var (name, passed, detail) in menuChecks)
            {
                lines.Add($"[{(passed ? "OK" : "FALLO")}] {name}: {detail}");
            }

            overlay.Dispatcher.Invoke(() => ((IRadialPresenter)overlay).Hide());
            lines.Add($"Tras ocultar: visible = {overlay.IsVisible}");
            exitCode = checks.All(c => c.Passed) && menuChecks.All(c => c.Passed) ? 0 : 2;
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
}
