using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Radial.App.Overlay;
using Radial.Core.Geometry;
using Radial.Windows.Presentation;

namespace Radial.App.Controls;

/// <summary>Formatos de arrastre entre la biblioteca, la vista previa y el árbol.</summary>
internal static class DragFormats
{
    /// <summary>Identificador de una definición de la biblioteca (texto).</summary>
    public const string Definition = "radial.definition";

    /// <summary>Sector (<see cref="RadialDirection"/> como entero) que se arrastra dentro de la vista previa.</summary>
    public const string Sector = "radial.sector";

    /// <summary>Identificador de un elemento colocado (texto), arrastrado desde la vista previa hacia el árbol.</summary>
    public const string Item = "radial.item";
}

/// <summary>
/// La vista previa del editor: el MISMO <see cref="RadialView"/> que dibuja el overlay real (no hay una segunda implementación del
/// dibujo), escalado para caber, con sectores seleccionables y zonas de soltar. No activa hooks ni ejecuta nada: solo traduce el
/// ratón a un sector con la geometría de Core (<see cref="RadialGeometry.Select"/>, la misma que usa el motor) y se lo da al modelo.
/// Los sectores vacíos y el destino del arrastre se marcan con una capa propia encima, sin tocar el dibujo del radial.
/// </summary>
internal sealed class RadialPreview : UserControl
{
    private const double DragThreshold = 6;
    private static readonly PointD Center = new(RadialTheme.ViewSize / 2, RadialTheme.ViewSize / 2);

    private readonly RadialView _view = new();
    private readonly Canvas _marks = new() { Width = RadialTheme.ViewSize, Height = RadialTheme.ViewSize, IsHitTestVisible = false };
    private readonly Grid _surface = new() { Width = RadialTheme.ViewSize, Height = RadialTheme.ViewSize, Background = Brushes.Transparent };
    private EditorViewModel? _model;
    private Point? _dragStart;
    private RadialDirection? _dragFrom;
    private RadialDirection? _dropTarget;

    public RadialPreview()
    {
        _surface.Children.Add(_view);
        _surface.Children.Add(_marks);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _surface };
        Background = Brushes.Transparent;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        DataContextChanged += (_, e) => Attach(e.NewValue as EditorViewModel);
    }

    private void Attach(EditorViewModel? model)
    {
        if (_model is not null)
        {
            _model.PreviewInvalidated -= Render;
        }

        _model = model;
        if (_model is not null)
        {
            _model.PreviewInvalidated += Render;
            Render();
        }
    }

    /// <summary>Redibuja el menú abierto, la selección y las marcas. Se llama cada vez que el modelo avisa de un cambio.</summary>
    private void Render()
    {
        if (_model is null)
        {
            return;
        }

        _view.SetMenu(_model.CurrentMenu, _model.Geometry, _model.Depth);
        _view.SetSelection(_model.SelectedDirection);
        DrawMarks();
    }

    private void DrawMarks()
    {
        _marks.Children.Clear();
        if (_model is null)
        {
            return;
        }

        var innerRadius = _model.Geometry.DeadZoneRadius + RadialTheme.HubClearance;

        // Un sector vacío elegido no lo resalta el radial (no tiene nada que resaltar): se marca con un contorno discontinuo.
        if (_model.SelectedDirection is { } selected && _model.CurrentMenu.GetItem(selected) is null)
        {
            _marks.Children.Add(Mark(selected, innerRadius, filled: false, dashed: true));
        }

        if (_dropTarget is { } target)
        {
            _marks.Children.Add(Mark(target, innerRadius, filled: true, dashed: false));
        }
    }

    private static Path Mark(RadialDirection direction, double innerRadius, bool filled, bool dashed)
    {
        var shape = RadialLayout.CreateSector(Center, direction, innerRadius, RadialTheme.OuterRadius, RadialTheme.SectorGapDegrees);
        return new Path
        {
            Data = RadialView.CreateGeometry(shape),
            Fill = filled ? new SolidColorBrush(Color.FromArgb(0x4D, 0x5B, 0xB8, 0xF0)) : Brushes.Transparent,
            Stroke = RadialTheme.SelectedEdge,
            StrokeThickness = dashed ? 1.5 : 2,
            StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null,
            IsHitTestVisible = false,
        };
    }

    // ───────────────────────── Ratón ─────────────────────────

    /// <summary>El sector bajo un punto de la superficie, o <c>null</c> si está en el centro o fuera del radial.</summary>
    private RadialDirection? DirectionAt(Point point)
    {
        if (_model is null)
        {
            return null;
        }

        var pointer = new PointD(point.X, point.Y);
        var dx = pointer.X - Center.X;
        var dy = pointer.Y - Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < _model.Geometry.DeadZoneRadius || distance > RadialTheme.BackdropRadius)
        {
            return null;
        }

        return RadialGeometry.Select(Center, pointer, _model.Geometry.DeadZoneRadius);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_model is null)
        {
            return;
        }

        var position = e.GetPosition(_surface);
        var direction = DirectionAt(position);
        _model.SelectDirection(direction);

        if (e.ClickCount == 2 && direction is not null)
        {
            _model.EnterSelectedGroup();
            _dragStart = null;
            return;
        }

        if (direction is { } d && _model.CurrentMenu.GetItem(d) is not null)
        {
            _dragStart = position;
            _dragFrom = d;
        }
        else
        {
            _dragStart = null;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start || _dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed || _model is null)
        {
            return;
        }

        var position = e.GetPosition(_surface);
        if (Math.Abs(position.X - start.X) < DragThreshold && Math.Abs(position.Y - start.Y) < DragThreshold)
        {
            return;
        }

        _dragStart = null;
        var data = new DataObject();
        data.SetData(DragFormats.Sector, (int)from);
        if (_model.CurrentMenu.GetItem(from) is { } item)
        {
            data.SetData(DragFormats.Item, item.Id.Value);
        }

        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        SetDropTarget(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragStart = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is null)
        {
            return;
        }

        var directions = Enum.GetValues<RadialDirection>();
        var current = _model.SelectedDirection is { } s ? Array.IndexOf(directions, s) : -1;
        switch (e.Key)
        {
            case Key.Right or Key.Down:
                _model.SelectDirection(directions[(current + 1 + directions.Length) % directions.Length]);
                break;
            case Key.Left or Key.Up:
                _model.SelectDirection(directions[(current <= 0 ? directions.Length : current) - 1]);
                break;
            case Key.Enter:
                _model.EnterSelectedGroup();
                break;
            case Key.Delete when _model.Remove.CanExecute(null):
                _model.Remove.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ───────────────────────── Soltar ─────────────────────────

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var accepts = e.Data.GetDataPresent(DragFormats.Definition) || e.Data.GetDataPresent(DragFormats.Sector);
        var direction = accepts ? DirectionAt(e.GetPosition(_surface)) : null;
        SetDropTarget(direction);
        e.Effects = direction is null ? DragDropEffects.None : e.Data.GetDataPresent(DragFormats.Sector) ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        SetDropTarget(null);
    }

    protected override async void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        SetDropTarget(null);
        if (_model is null || DirectionAt(e.GetPosition(_surface)) is not { } target)
        {
            return;
        }

        e.Handled = true;
        if (e.Data.GetDataPresent(DragFormats.Sector) && e.Data.GetData(DragFormats.Sector) is int from)
        {
            _model.MoveSector((RadialDirection)from, target);
        }
        else if (e.Data.GetData(DragFormats.Definition) is string definitionId)
        {
            if (_model.CurrentMenu.GetItem(target) is null)
            {
                _model.PlaceDefinition(definitionId, target);
            }
            else
            {
                // Soltar sobre un sector ocupado lo sustituye (un grupo con contenido pide confirmación antes).
                _model.SelectDirection(target);
                await _model.ReplaceSelectedAsync(definitionId);
            }
        }
    }

    private void SetDropTarget(RadialDirection? target)
    {
        if (_dropTarget == target)
        {
            return;
        }

        _dropTarget = target;
        DrawMarks();
    }
}
