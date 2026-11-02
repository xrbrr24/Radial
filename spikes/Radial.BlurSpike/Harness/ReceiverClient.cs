using System.Diagnostics;
using Radial.BlurSpike.Native;

namespace Radial.BlurSpike.Harness;

/// <summary>Lanza y consulta el proceso receptor. Los contadores se leen del título de su ventana.</summary>
internal sealed class ReceiverClient : IDisposable
{
    private Process? _process;

    public IntPtr Hwnd { get; private set; }

    public uint Pid => (uint)(_process?.Id ?? 0);

    public bool IsRunning => _process is { HasExited: false } && Hwnd != IntPtr.Zero;

    public Win32.RECT Bounds => Win32.GetRect(Hwnd);

    public async Task StartAsync(int x, int y, int width, int height)
    {
        if (IsRunning)
        {
            return;
        }

        _process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--receiver {x} {y} {width} {height}") { UseShellExecute = false })
            ?? throw new InvalidOperationException("No se pudo lanzar el receptor.");

        for (var attempt = 0; attempt < 100 && Hwnd == IntPtr.Zero; attempt++)
        {
            await Task.Delay(100);
            Hwnd = Win32.TopLevelWindowsOf((uint)_process.Id)
                .FirstOrDefault(h => Win32.IsWindowVisible(h) && Win32.GetTitle(h).StartsWith(ReceiverWindow.TitlePrefix, StringComparison.Ordinal));
        }

        if (Hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("El receptor no mostró su ventana.");
        }

        await Task.Delay(400);
    }

    public (int Clicks, int Moves) ReadCounters()
    {
        var title = Win32.GetTitle(Hwnd);
        return (Extract(title, "clicks="), Extract(title, "moves="));
    }

    public (int CenterX, int CenterY) Center()
    {
        var rect = Bounds;
        return ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill();
        }

        _process?.Dispose();
        _process = null;
        Hwnd = IntPtr.Zero;
    }

    private static int Extract(string title, string key)
    {
        var start = title.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
        {
            return -1;
        }

        start += key.Length;
        var end = start;
        while (end < title.Length && char.IsDigit(title[end]))
        {
            end++;
        }

        return int.Parse(title.AsSpan(start, end - start));
    }
}
