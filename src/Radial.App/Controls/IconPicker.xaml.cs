using System.Windows;
using System.Windows.Controls;
using Radial.App.Overlay;

namespace Radial.App.Controls;

/// <summary>Cuadrícula con los iconos del catálogo. Elegir uno produce <see cref="IconChosen"/> con su nombre.</summary>
public partial class IconPicker : UserControl
{
    public IconPicker()
    {
        InitializeComponent();
        Items.ItemsSource = GlyphCatalog.Names;
    }

    public event Action<string>? IconChosen;

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
        {
            IconChosen?.Invoke(name);
        }
    }
}
