using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Radial.App.Controls;
using Radial.Windows.Presentation;

namespace Radial.App.Views;

/// <summary>
/// La biblioteca de acciones: búsqueda, filtros, lista en secciones y formulario. Solo contiene lo propio de la interfaz (agrupar la
/// lista, arrastrar una acción hacia el editor, cerrar el menú de «Nueva acción»); lo demás lo hace <see cref="LibraryViewModel"/>.
/// </summary>
public partial class LibraryView : UserControl
{
    private LibraryViewModel? _model;
    private Point? _dragStart;
    private bool _syncing;

    public LibraryView()
    {
        InitializeComponent();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        _model = e.NewValue as LibraryViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
            BindRows();
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.Rows))
        {
            BindRows();
        }
        else if (e.PropertyName == nameof(LibraryViewModel.Selected) && !_syncing)
        {
            ShowSelected();
        }
    }

    /// <summary>Refleja en la lista la acción elegida en el modelo (p. ej. la que se acaba de crear) y la deja a la vista.</summary>
    private void ShowSelected()
    {
        _syncing = true;
        try
        {
            List.SelectedItem = _model?.Selected;
            if (List.SelectedItem is not null)
            {
                List.ScrollIntoView(List.SelectedItem);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && _model is not null)
        {
            _model.Selected = List.SelectedItem as LibraryRowViewModel;
        }
    }

    /// <summary>
    /// Enlaza la lista agrupada por sección («Mis acciones» y «De fábrica»). Cada búsqueda produce una lista nueva; la agrupación se
    /// monta aquí porque un <c>CollectionViewSource</c> en recursos no hereda el contexto de datos.
    /// </summary>
    private void BindRows()
    {
        if (_model is null)
        {
            return;
        }

        var view = new ListCollectionView(_model.Rows.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryRowViewModel.SectionTitle)));
        _syncing = true;
        try
        {
            List.ItemsSource = view;
        }
        finally
        {
            _syncing = false;
        }

        ShowSelected();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => PanelColumn.Width = new GridLength(e.NewSize.Width < 980 ? 340 : 420);

    private void OnNewKind(object sender, RoutedEventArgs e) => NewToggle.IsChecked = false;

    private void OnPlaceClick(object sender, RoutedEventArgs e)
    {
        if (_model?.Selected is { } row)
        {
            _model.RequestPlace(row.Id);
        }
    }

    private void OnFormIconsLoaded(object sender, RoutedEventArgs e) =>
        FormIcons.IconChosen += name =>
        {
            if (_model?.Form is { } form)
            {
                form.IconName = name;
            }
        };

    // ───────────────────────── Arrastrar una acción hacia el editor ─────────────────────────

    private static LibraryRowViewModel? RowAt(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            source = VisualTreeHelper.GetParent(source);
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
        if (RowAt(e.OriginalSource as DependencyObject) is { IsUnsupported: false } row)
        {
            var data = new DataObject();
            data.SetData(DragFormats.Definition, row.Id);
            DragDrop.DoDragDrop(List, data, DragDropEffects.Copy);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_model is not null && RowAt(e.OriginalSource as DependencyObject) is { IsUnsupported: false })
        {
            _model.Edit.Execute(null);
        }
    }
}
