using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.App.Controls;

/// <summary>
/// Grabador de atajos. SOLO recoge teclas mientras el modo de grabación está activo y el campo tiene el foco del teclado de esta
/// ventana: no hay ninguna captura global ni permanente (eso son los hooks del radial, que no intervienen aquí). Traduce la tecla de
/// WPF a su código de tecla virtual y se lo da al modelo, que decide si la combinación es utilizable.
/// </summary>
public partial class KeyCaptureBox : UserControl
{
    public KeyCaptureBox()
    {
        InitializeComponent();
    }

    private KeyCaptureViewModel? Model => DataContext as KeyCaptureViewModel;

    private void OnRecord(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        if (model.IsRecording)
        {
            model.StopRecording();
            return;
        }

        model.StartRecording();
        Field.Focus();
        Keyboard.Focus(Field);
    }

    private void OnClear(object sender, RoutedEventArgs e) => Model?.Clear();

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Al salir del campo la grabación termina: no se queda esperando teclas en segundo plano.
        if (Model is { IsRecording: true } model && !IsAncestorOf(e.NewFocus as DependencyObject))
        {
            model.StopRecording();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Model is not { IsRecording: true } model)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var modifiers = ToModifiers(Keyboard.Modifiers);
        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            model.StopRecording();
            e.Handled = true;
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        e.Handled = model.HandleKey(virtualKey, modifiers);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Model is { IsRecording: true } model)
        {
            model.SetHeldModifiers(ToModifiers(Keyboard.Modifiers));
            e.Handled = true;
        }
    }

    private static KeyModifiers ToModifiers(ModifierKeys keys)
    {
        var result = KeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control))
        {
            result |= KeyModifiers.Control;
        }

        if (keys.HasFlag(ModifierKeys.Shift))
        {
            result |= KeyModifiers.Shift;
        }

        if (keys.HasFlag(ModifierKeys.Alt))
        {
            result |= KeyModifiers.Alt;
        }

        if (keys.HasFlag(ModifierKeys.Windows))
        {
            result |= KeyModifiers.Windows;
        }

        return result;
    }
}
