using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Radial.Windows.Presentation;

namespace Radial.App.Controls;

/// <summary>
/// La lista de acciones de la biblioteca dentro del editor. Un doble clic coloca (o sustituye, según <see cref="ReplaceMode"/>) la
/// acción en el sector elegido; arrastrarla la suelta sobre cualquier sector de la vista previa.
/// </summary>
public partial class ActionPicker : UserControl
{
    private Point? _dragStart;

    public ActionPicker()
    {
        InitializeComponent();
    }

    /// <summary>Si es verdadero, elegir una acción sustituye la del sector elegido en lugar de colocarla en uno vacío.</summary>
    public bool ReplaceMode { get; set; }

    private EditorViewModel? Model => DataContext as EditorViewModel;

    private static LibraryRowViewModel? RowAt(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.DataContext as LibraryRowViewModel;
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = RowAt(e.OriginalSource as DependencyObject) is null ? null : e.GetPosition(null);

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var now = e.GetPosition(null);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        if (RowAt(e.OriginalSource as DependencyObject) is { } row)
        {
            var data = new DataObject();
            data.SetData(DragFormats.Definition, row.Id);
            DragDrop.DoDragDrop(List, data, DragDropEffects.Copy);
        }
    }

    private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Model is not { } model || RowAt(e.OriginalSource as DependencyObject) is not { } row)
        {
            return;
        }

        if (ReplaceMode)
        {
            await model.ReplaceSelectedAsync(row.Id);
        }
        else
        {
            model.PlaceDefinition(row.Id);
        }
    }
}
