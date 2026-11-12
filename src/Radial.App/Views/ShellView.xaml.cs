using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.App.Views;

/// <summary>
/// El centro de control: navegación lateral, barra superior y la sección abierta, más los diálogos y avisos de la propia ventana.
/// Solo contiene lo propio de la interfaz (modo compacto, atajos de teclado, transición entre secciones, botones de los diálogos);
/// la navegación, el guardado y la búsqueda de comandos son de <see cref="ShellViewModel"/>.
/// </summary>
public partial class ShellView : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(ShellView), new PropertyMetadata(false));

    private ShellViewModel? _model;
    private DialogViewModel? _shownDialog;

    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>La navegación lateral se reduce a iconos cuando la ventana es estrecha.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        private set => SetValue(IsCompactProperty, value);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
            _model.Dialogs.PropertyChanged -= OnDialogsChanged;
        }

        _model = e.NewValue as ShellViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
            _model.Dialogs.PropertyChanged += OnDialogsChanged;
            ShowDialog(_model.Dialogs.CurrentDialog);
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 1060;
        IsCompact = compact;
        NavColumn.Width = new GridLength(compact ? 74 : 240);
        CommandHost.Width = compact ? 230 : 360;
    }

    // ───────────────────────── Navegación ─────────────────────────

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AppSection section })
        {
            _model?.Go(section);
        }
    }

    private void OnPreferencesClick(object sender, RoutedEventArgs e) => _model?.Go(AppSection.Settings);

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            // Transición discreta entre secciones: aparece con un fundido corto.
            Page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    // ───────────────────────── Teclado ─────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        // Con un diálogo abierto, Esc lo cancela y nada más recibe teclas.
        if (_model.Dialogs.CurrentDialog is { } dialog)
        {
            if (e.Key == Key.Escape)
            {
                dialog.Cancel.Execute(null);
                e.Handled = true;
            }

            return;
        }

        // La decisión de qué atajo global corresponde (y de que mientras se graba un atajo no hay ninguno, para que Ctrl+S y Ctrl+K lleguen
        // al grabador) es del modelo de vista. Esta vista solo traduce la tecla de WPF a su código de tecla virtual.
        var modifiers = Keyboard.Modifiers == ModifierKeys.Control ? KeyModifiers.Control : KeyModifiers.None;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (_model.ResolveGlobalShortcut(modifiers, KeyInterop.VirtualKeyFromKey(key)))
        {
            case ShellViewModel.GlobalShortcut.Save:
                if (_model.Save.CanExecute(null))
                {
                    _model.Save.Execute(null);
                }

                e.Handled = true;
                break;

            case ShellViewModel.GlobalShortcut.FocusCommands:
                CommandBox.Focus();
                CommandBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    // ───────────────────────── Búsqueda de comandos ─────────────────────────

    private void OnCommandGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_model is not null && !_model.IsCommandOpen)
        {
            _model.IsCommandOpen = true;
        }
    }

    private void OnCommandLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Pulsar un resultado de la lista quita el foco del cuadro: eso no debe cerrar la lista antes de que el clic llegue.
        if (_model is null || IsInsidePopup(e.NewFocus as DependencyObject))
        {
            return;
        }

        _model.CloseCommands();
    }

    private bool IsInsidePopup(DependencyObject? element)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, CommandPopupBody))
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnCommandKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _model.CloseCommands();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _model.CommandResults.Count > 0)
        {
            _model.RunCommand.Execute(_model.CommandResults[0]);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    // ───────────────────────── Diálogos ─────────────────────────

    private void OnDialogsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InAppDialogs.CurrentDialog) && _model is not null)
        {
            ShowDialog(_model.Dialogs.CurrentDialog);
        }
    }

    /// <summary>Construye los botones del diálogo (uno por opción; la última es la principal, la peligrosa va en rojo) y le da el foco.</summary>
    private void ShowDialog(DialogViewModel? dialog)
    {
        if (ReferenceEquals(dialog, _shownDialog))
        {
            return;
        }

        _shownDialog = dialog;
        DialogButtons.Children.Clear();
        if (dialog is null)
        {
            return;
        }

        Button? primary = null;
        for (var index = 0; index < dialog.Options.Count; index++)
        {
            var isLast = index == dialog.Options.Count - 1;
            var key = isLast ? "Button.Primary" : index == dialog.DangerIndex ? "Button.Danger" : "Button.Ghost";
            if (isLast && dialog.DangerIndex == index)
            {
                key = "Button.Danger";
            }

            var button = new Button
            {
                Content = dialog.Options[index],
                Style = (Style)FindResource(key),
                Margin = new Thickness(index == 0 ? 0 : 8, 0, 0, 0),
                Padding = new Thickness(16, 8, 16, 8),
                Command = dialog.Choose,
                CommandParameter = index,
            };
            DialogButtons.Children.Add(button);
            primary = button;
        }

        // El foco va al último botón (la acción principal) para que Enter la confirme; con un botón peligroso Enter no lo activa por defecto.
        if (primary is not null)
        {
            var target = dialog.DangerIndex == dialog.Options.Count - 1 ? DialogButtons.Children.OfType<Button>().FirstOrDefault() : primary;
            Dispatcher.BeginInvoke(new Action(() => target?.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
