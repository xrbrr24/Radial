using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Path = System.Windows.Shapes.Path;

namespace Radial.App.Overlay;

/// <summary>
/// El dibujo del radial: un disco oscuro translúcido, ocho sectores anulares dibujados con geometría (<see cref="StreamGeometry"/>,
/// no botones), un botón central y, en cada sector, un icono y una etiqueta. Solo sabe pintar: el sector resaltado se lo indica
/// quien lo usa (<see cref="SetSelection"/>); no lee el ratón, no navega y no ejecuta nada. Las formas se calculan una vez, con
/// <see cref="RadialLayout"/> de Core (los mismos ángulos que usa el motor para seleccionar), y al resaltar solo cambian pinceles.
/// </summary>
internal sealed class RadialView : UserControl
{
    private static readonly PointD Center = new(RadialTheme.ViewSize / 2, RadialTheme.ViewSize / 2);

    private const double LabelBlockWidth = 92;
    private const double LabelBlockHeight = 68;
    private const double PointerSize = 8;

    private readonly Canvas _canvas = new() { Width = RadialTheme.ViewSize, Height = RadialTheme.ViewSize };
    private readonly Dictionary<RadialDirection, SectorVisual> _sectors = [];
    private readonly Ellipse _pointer = new() { Width = PointerSize, Height = PointerSize, Fill = RadialTheme.Pointer, Visibility = Visibility.Hidden };
    private double _deadZoneRadius;
    private RadialDirection? _selected;

    public RadialView()
    {
        Width = RadialTheme.ViewSize;
        Height = RadialTheme.ViewSize;
        Content = _canvas;

        // El radial se maneja con los hooks, nunca con el ratón sobre la ventana.
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    public RadialDirection? Selection => _selected;

    /// <summary>Construye el dibujo de un menú. Quita lo anterior y deja el radial sin sector resaltado.</summary>
    public void SetMenu(RadialMenu menu, GeometrySettings geometry)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(geometry);

        _canvas.Children.Clear();
        _sectors.Clear();
        _selected = null;
        _deadZoneRadius = geometry.DeadZoneRadius;

        _canvas.Children.Add(CreateBackdrop());

        var innerRadius = _deadZoneRadius + RadialTheme.HubClearance;
        foreach (var direction in Enum.GetValues<RadialDirection>())
        {
            var visual = new SectorVisual(direction, menu.GetItem(direction), innerRadius);
            _sectors[direction] = visual;
            _canvas.Children.Add(visual.Path);
            if (visual.Label is not null)
            {
                _canvas.Children.Add(visual.Label);
            }
        }

        _canvas.Children.Add(CreateHub());
        _canvas.Children.Add(_pointer);
        ApplySelection();
    }

    /// <summary>Resalta un sector; <c>null</c> los deja todos en reposo (cursor en la zona central).</summary>
    public void SetSelection(RadialDirection? direction)
    {
        if (direction == _selected)
        {
            return;
        }

        _selected = direction;
        ApplySelection();
    }

    private void ApplySelection()
    {
        foreach (var (direction, visual) in _sectors)
        {
            visual.Apply(direction == _selected);
        }

        if (_selected is { } selected)
        {
            // Una marca en el botón central que apunta al sector elegido.
            var at = RadialLayout.LabelAnchor(Center, selected, Math.Max(_deadZoneRadius - 14, 6));
            Canvas.SetLeft(_pointer, at.X - PointerSize / 2);
            Canvas.SetTop(_pointer, at.Y - PointerSize / 2);
            _pointer.Visibility = Visibility.Visible;
        }
        else
        {
            _pointer.Visibility = Visibility.Hidden;
        }
    }

    private static Ellipse CreateBackdrop()
    {
        var disc = new Ellipse
        {
            Width = RadialTheme.BackdropRadius * 2,
            Height = RadialTheme.BackdropRadius * 2,
            Fill = RadialTheme.Backdrop,
            Stroke = RadialTheme.BackdropEdge,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(disc, Center.X - RadialTheme.BackdropRadius);
        Canvas.SetTop(disc, Center.Y - RadialTheme.BackdropRadius);
        return disc;
    }

    private Ellipse CreateHub()
    {
        var hub = new Ellipse
        {
            Width = _deadZoneRadius * 2,
            Height = _deadZoneRadius * 2,
            Fill = RadialTheme.Hub,
            Stroke = RadialTheme.HubEdge,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(hub, Center.X - _deadZoneRadius);
        Canvas.SetTop(hub, Center.Y - _deadZoneRadius);
        return hub;
    }

    private static StreamGeometry CreateGeometry(SectorShape shape)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Cada sector abarca menos de 180°, así que el arco nunca es «grande».
            context.BeginFigure(ToPoint(shape.OuterStart), isFilled: true, isClosed: true);
            context.ArcTo(ToPoint(shape.OuterEnd), new Size(shape.OuterRadius, shape.OuterRadius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(ToPoint(shape.InnerEnd), true, false);
            context.ArcTo(ToPoint(shape.InnerStart), new Size(shape.InnerRadius, shape.InnerRadius), 0, false, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point ToPoint(PointD point) => new(point.X, point.Y);

    /// <summary>Un sector: su forma en reposo y resaltada, su icono y su etiqueta. Resaltar solo cambia pinceles y la forma.</summary>
    private sealed class SectorVisual
    {
        private readonly StreamGeometry _rest;
        private readonly StreamGeometry _raised;
        private readonly TextBlock? _icon;
        private readonly TextBlock? _text;

        public SectorVisual(RadialDirection direction, RadialItem? item, double innerRadius)
        {
            _rest = CreateGeometry(RadialLayout.CreateSector(Center, direction, innerRadius, RadialTheme.OuterRadius, RadialTheme.SectorGapDegrees));
            _raised = CreateGeometry(RadialLayout.CreateSector(Center, direction, innerRadius, RadialTheme.OuterRadius + RadialTheme.SelectedGrowth, RadialTheme.SectorGapDegrees));
            HasItem = item is not null;
            Path = new Path { Data = _rest, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round };

            if (item is null)
            {
                return;
            }

            _icon = new TextBlock
            {
                Text = GlyphCatalog.Resolve(item.Icon),
                FontFamily = RadialTheme.IconFont,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _text = new TextBlock
            {
                Text = item.Name,
                FontFamily = RadialTheme.TextFont,
                FontSize = 12.5,
                MaxWidth = LabelBlockWidth - 8,
                MaxHeight = 34, // dos líneas: «Seleccionar todo» se parte en dos
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(_icon);
            stack.Children.Add(_text);

            Label = new Grid { Width = LabelBlockWidth, Height = LabelBlockHeight, IsHitTestVisible = false };
            Label.Children.Add(stack);

            var anchor = RadialLayout.LabelAnchor(Center, direction, RadialTheme.LabelRadius);
            Canvas.SetLeft(Label, anchor.X - LabelBlockWidth / 2);
            Canvas.SetTop(Label, anchor.Y - LabelBlockHeight / 2);
        }

        public Path Path { get; }

        public Grid? Label { get; }

        public bool HasItem { get; }

        public void Apply(bool selected)
        {
            if (!HasItem)
            {
                Path.Data = _rest;
                Path.Fill = RadialTheme.SectorFillEmpty;
                Path.Stroke = RadialTheme.SectorEdge;
                return;
            }

            Path.Data = selected ? _raised : _rest;
            Path.Fill = selected ? RadialTheme.SelectedFill : RadialTheme.SectorFill;
            Path.Stroke = selected ? RadialTheme.SelectedEdge : RadialTheme.SectorEdge;
            Path.StrokeThickness = selected ? 1.5 : 1;
            _icon!.Foreground = selected ? RadialTheme.IconSelected : RadialTheme.Icon;
            _text!.Foreground = selected ? RadialTheme.LabelSelected : RadialTheme.Label;
        }
    }
}
