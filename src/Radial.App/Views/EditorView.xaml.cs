using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Radial.App.Controls;
using Radial.Core.Model;
using Radial.Windows.Presentation;

namespace Radial.App.Views;

/// <summary>
/// El editor radial: árbol de la jerarquía, vista previa con el mismo dibujo del overlay e inspector. Aquí solo hay lo propio de la
/// interfaz (arrastrar y soltar, foco, anchos); las operaciones y sus reglas viven en <see cref="EditorViewModel"/>.
/// </summary>
public partial class EditorView : UserControl
{
    private Point? _nodeDragStart;
    private TreeNodeViewModel? _nodeDragSource;
    private EditorViewModel? _model;

    public EditorView()
    {
        InitializeComponent();
        Icons.IconChosen += name => _model?.SetSelectedIcon(name);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        _model = e.NewValue as EditorViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
        }

        Preview.DataContext = e.NewValue;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Al cambiar de sector, los paneles opcionales (icono, cambiar acción) vuelven a cerrarse.
        if (e.PropertyName == nameof(EditorViewModel.SelectedDirection))
        {
            IconToggle.IsChecked = false;
            ReplaceToggle.IsChecked = false;
        }
    }

    /// <summary>En ventanas estrechas se reducen la jerarquía y el inspector para dejar sitio al radial.</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 1040;
        TreeColumn.Width = new GridLength(narrow ? 190 : 264);
        InspectorColumn.Width = new GridLength(narrow ? 280 : 344);
    }

    private void OnPathClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PathSegment segment })
        {
            _model?.OpenMenu(segment.MenuId);
        }
    }

    private void OnSectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SectorViewModel sector })
        {
            _model?.SelectDirection(sector.Direction);
        }
    }

    private void OnLabelLostFocus(object sender, RoutedEventArgs e) => _model?.CommitLabelText();

    private void OnLabelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _model?.CommitLabelText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _model?.SelectedItem is { } item && sender is TextBox box)
        {
            box.Text = item.Name;
            e.Handled = true;
        }
    }

    // ───────────────────────── Árbol: arrastrar y soltar ─────────────────────────

    private static TreeNodeViewModel? NodeOf(object sender) => (sender as FrameworkElement)?.DataContext as TreeNodeViewModel;

    private void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _nodeDragSource = NodeOf(sender);
        _nodeDragStart = e.GetPosition(null);
    }

    private void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (_nodeDragStart is not { } start || _nodeDragSource is not { GroupItemId: { } groupItem } || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var now = e.GetPosition(null);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _nodeDragStart = null;
        _nodeDragSource = null;
        var data = new DataObject();
        data.SetData(DragFormats.Item, groupItem.Value);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
    }

    private static bool Accepts(DragEventArgs e) =>
        e.Data.GetDataPresent(DragFormats.Item) || e.Data.GetDataPresent(DragFormats.Definition) || e.Data.GetDataPresent(DragFormats.Sector);

    private void OnNodeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Accepts(e) ? (e.Data.GetDataPresent(DragFormats.Definition) ? DragDropEffects.Copy : DragDropEffects.Move) : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnNodeDrop(object sender, DragEventArgs e)
    {
        if (_model is null || NodeOf(sender) is not { } target)
        {
            return;
        }

        e.Handled = true;
        if (e.Data.GetData(DragFormats.Item) is string itemId)
        {
            _model.MoveToMenu(new ItemId(itemId), target.MenuId);
        }
        else if (e.Data.GetData(DragFormats.Definition) is string definitionId)
        {
            // Una acción soltada sobre un menú del árbol se coloca en su primer sector libre.
            _model.OpenMenu(target.MenuId);
            _model.PlaceDefinition(definitionId);
        }
    }
}
