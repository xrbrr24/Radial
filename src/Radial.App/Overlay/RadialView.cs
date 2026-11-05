using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Radial.Core.Geometry;
using Radial.Core.Model;
using Radial.Windows.Overlay;
using Path = System.Windows.Shapes.Path;

namespace Radial.App.Overlay;

/// <summary>
/// El dibujo del radial: un disco oscuro translúcido, ocho sectores anulares dibujados con geometría (<see cref="StreamGeometry"/>,
/// no botones), un botón central y, en cada sector, un icono y una etiqueta. Solo sabe pintar: el menú, el sector resaltado y el
/// progreso se lo indica quien lo usa (<see cref="SetMenu"/>, <see cref="SetSelection"/>, <see cref="SetProgress"/>); no lee el
/// ratón, no navega y no ejecuta nada. Las formas se calculan una vez por menú, con <see cref="RadialLayout"/> de Core (los mismos
/// ángulos que usa el motor para seleccionar), y al resaltar solo cambian pinceles.
///
/// Un grupo (abre otro menú) se distingue por una banda de acento en el borde exterior de su sector y un chevrón; mientras se
/// permanece sobre él, un arco por fuera del sector se va completando. Dentro de un submenú el centro muestra una flecha de
/// volver; al salir del centro el retroceso queda «armado» (la flecha y el borde se iluminan) y al volver a él un anillo alrededor
/// del centro se completa. Una acción de tipo desconocido (no ejecutable) se dibuja apagada y no se resalta.
/// </summary>
internal sealed class RadialView : UserControl
{
    private static readonly PointD Center = new(RadialTheme.ViewSize / 2, RadialTheme.ViewSize / 2);

    private const double LabelBlockWidth = 92;
    private const double LabelBlockHeight = 68;
    private const double PointerSize = 8;
    private const double BackGlyphBox = 40;
    private const double MaxSweep = 359.9;

    private readonly Canvas _canvas = new() { Width = RadialTheme.ViewSize, Height = RadialTheme.ViewSize };
    private readonly Dictionary<RadialDirection, SectorVisual> _sectors = [];
    private readonly Ellipse _pointer = new() { Width = PointerSize, Height = PointerSize, Fill = RadialTheme.Pointer, Visibility = Visibility.Hidden };
    private readonly Path _submenuArc = CreateProgressPath(RadialTheme.ProgressThickness);
    private readonly Path _backArc = CreateProgressPath(RadialTheme.ProgressThickness - 1);
    private readonly TextBlock _backGlyph = new()
    {
        Text = "", // Back
        FontFamily = RadialTheme.IconFont,
        FontSize = 18,
        Width = BackGlyphBox,
        TextAlignment = TextAlignment.Center,
        IsHitTestVisible = false,
        Visibility = Visibility.Hidden,
    };

    private Ellipse? _hub;
    private double _deadZoneRadius;
    private int _depth = 1;
    private RadialDirection? _selected;
    private RadialProgress _progress;

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

    /// <summary>
    /// Construye el dibujo de un menú. Quita lo anterior y deja el radial sin sector resaltado ni progreso.
    /// <paramref name="depth"/> es el número de menús apilados (1 = menú raíz): con más de uno el centro dibuja la flecha de volver.
    /// </summary>
    public void SetMenu(RadialMenu menu, GeometrySettings geometry, int depth = 1)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(geometry);

        _canvas.Children.Clear();
        _sectors.Clear();
        _selected = null;
        _progress = default;
        _depth = Math.Max(1, depth);
        _deadZoneRadius = geometry.DeadZoneRadius;

        _canvas.Children.Add(CreateBackdrop());

        var innerRadius = _deadZoneRadius + RadialTheme.HubClearance;
        foreach (var direction in Enum.GetValues<RadialDirection>())
        {
            var visual = new SectorVisual(direction, menu.GetItem(direction), innerRadius);
            _sectors[direction] = visual;
            _canvas.Children.Add(visual.Path);
            if (visual.Band is not null)
            {
                _canvas.Children.Add(visual.Band);
            }

            if (visual.Label is not null)
            {
                _canvas.Children.Add(visual.Label);
            }
        }

        _hub = CreateHub();
        _canvas.Children.Add(_hub);

        Canvas.SetLeft(_backGlyph, Center.X - BackGlyphBox / 2);
        Canvas.SetTop(_backGlyph, Center.Y - 12);
        _canvas.Children.Add(_backGlyph);
        _canvas.Children.Add(_submenuArc);
        _canvas.Children.Add(_backArc);
        _canvas.Children.Add(_pointer);

        ApplySelection();
        ApplyProgress();
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
        ApplyProgress();
    }

    /// <summary>Dibuja el progreso de entrar en el grupo resaltado y el de volver atrás.</summary>
    public void SetProgress(RadialProgress progress)
    {
        if (progress == _progress)
        {
            return;
        }

        _progress = progress;
        ApplyProgress();
    }

    private void ApplySelection()
    {
        foreach (var (direction, visual) in _sectors)
        {
            visual.Apply(direction == _selected);
        }

        if (_selected is { } selected && _sectors.TryGetValue(selected, out var current) && current.IsHighlightable)
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

    private void ApplyProgress()
    {
        // Entrar en un grupo: un arco por fuera del sector resaltado que se completa con la permanencia.
        if (_selected is { } selected && _sectors.TryGetValue(selected, out var visual) && visual.IsGroup && _progress.Submenu > 0)
        {
            var sector = RadialLayout.CreateSector(Center, selected, 10, 20, RadialTheme.SectorGapDegrees);
            var radius = RadialTheme.OuterRadius + RadialTheme.SelectedGrowth + RadialTheme.ProgressThickness;
            _submenuArc.Data = ArcGeometry(radius, sector.StartAngle, Math.Min(MaxSweep, sector.SweepAngle * Math.Clamp(_progress.Submenu, 0, 1)));
            _submenuArc.Visibility = Visibility.Visible;
        }
        else
        {
            _submenuArc.Data = null;
            _submenuArc.Visibility = Visibility.Hidden;
        }

        // Volver atrás: la flecha del centro (solo dentro de un submenú), iluminada cuando está armado, y un anillo alrededor.
        var canGoBack = _depth > 1;
        _backGlyph.Visibility = canGoBack ? Visibility.Visible : Visibility.Hidden;
        _backGlyph.Foreground = canGoBack && _progress.BackArmed ? RadialTheme.IconSelected : RadialTheme.IconDim;
        if (_hub is not null)
        {
            _hub.Stroke = canGoBack && _progress.BackArmed ? RadialTheme.HubEdgeArmed : RadialTheme.HubEdge;
        }

        if (canGoBack && _progress.Back > 0)
        {
            _backArc.Data = ArcGeometry(_deadZoneRadius + 6, 0, Math.Min(MaxSweep, 360 * Math.Clamp(_progress.Back, 0, 1)));
            _backArc.Visibility = Visibility.Visible;
        }
        else
        {
            _backArc.Data = null;
            _backArc.Visibility = Visibility.Hidden;
        }
    }

    private static Path CreateProgressPath(double thickness) => new()
    {
        Stroke = RadialTheme.Progress,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
        Visibility = Visibility.Hidden,
    };

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

    /// <summary>Un arco abierto (sin relleno) de <paramref name="sweepAngle"/> grados a <paramref name="radius"/> del centro.</summary>
    private static StreamGeometry ArcGeometry(double radius, double startAngle, double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var start = RadialLayout.PointAt(Center, radius, startAngle);
            var end = RadialLayout.PointAt(Center, radius, startAngle + sweepAngle);
            context.BeginFigure(ToPoint(start), isFilled: false, isClosed: false);
            context.ArcTo(ToPoint(end), new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
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
        private readonly StreamGeometry? _bandRest;
        private readonly StreamGeometry? _bandRaised;
        private readonly TextBlock? _icon;
        private readonly TextBlock? _text;

        public SectorVisual(RadialDirection direction, RadialItem? item, double innerRadius)
        {
            _rest = CreateGeometry(RadialLayout.CreateSector(Center, direction, innerRadius, RadialTheme.OuterRadius, RadialTheme.SectorGapDegrees));
            _raised = CreateGeometry(RadialLayout.CreateSector(Center, direction, innerRadius, RadialTheme.OuterRadius + RadialTheme.SelectedGrowth, RadialTheme.SectorGapDegrees));
            HasItem = item is not null;
            IsGroup = item is { IsGroup: true };
            IsUnsupported = item is { IsUnsupported: true };
            Path = new Path { Data = _rest, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round };

            if (item is null)
            {
                return;
            }

            _icon = new TextBlock
            {
                Text = IsUnsupported ? RadialTheme.UnsupportedGlyph : GlyphCatalog.Resolve(item.Icon),
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

            if (IsGroup)
            {
                // La banda de acento del borde exterior (más corta que el sector) es lo que distingue un grupo de una acción.
                _bandRest = CreateGeometry(RadialLayout.CreateSector(Center, direction, RadialTheme.OuterRadius - RadialTheme.GroupBandOffset - RadialTheme.GroupBandThickness, RadialTheme.OuterRadius - RadialTheme.GroupBandOffset, RadialTheme.GroupBandGapDegrees));
                _bandRaised = CreateGeometry(RadialLayout.CreateSector(Center, direction, RadialTheme.OuterRadius + RadialTheme.SelectedGrowth - RadialTheme.GroupBandOffset - RadialTheme.GroupBandThickness, RadialTheme.OuterRadius + RadialTheme.SelectedGrowth - RadialTheme.GroupBandOffset, RadialTheme.GroupBandGapDegrees));
                Band = new Path { Data = _bandRest, IsHitTestVisible = false };

                // El chevrón junto al icono: «esto abre más».
                var chevron = new TextBlock
                {
                    Text = "", // ChevronRight
                    FontFamily = RadialTheme.IconFont,
                    FontSize = 11,
                    Foreground = RadialTheme.GroupBandSelected,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsHitTestVisible = false,
                };
                stack.Children.Add(chevron);
            }
        }

        public Path Path { get; }

        public Path? Band { get; private set; }

        public Grid? Label { get; }

        public bool HasItem { get; }

        public bool IsGroup { get; }

        public bool IsUnsupported { get; }

        /// <summary>Se resalta al apuntarlo: tiene elemento y se puede usar (una acción desconocida no).</summary>
        public bool IsHighlightable => HasItem && !IsUnsupported;

        public void Apply(bool selected)
        {
            if (!HasItem)
            {
                Path.Data = _rest;
                Path.Fill = RadialTheme.SectorFillEmpty;
                Path.Stroke = RadialTheme.SectorEdge;
                return;
            }

            if (IsUnsupported)
            {
                // Se conserva en la configuración pero no se puede usar: apagada y sin resalte.
                Path.Data = _rest;
                Path.Fill = RadialTheme.SectorFillEmpty;
                Path.Stroke = RadialTheme.SectorEdge;
                Path.StrokeThickness = 1;
                _icon!.Foreground = RadialTheme.IconDim;
                _text!.Foreground = RadialTheme.IconDim;
                return;
            }

            Path.Data = selected ? _raised : _rest;
            Path.Fill = selected ? RadialTheme.SelectedFill : RadialTheme.SectorFill;
            Path.Stroke = selected ? RadialTheme.SelectedEdge : RadialTheme.SectorEdge;
            Path.StrokeThickness = selected ? 1.5 : 1;
            _icon!.Foreground = selected ? RadialTheme.IconSelected : RadialTheme.Icon;
            _text!.Foreground = selected ? RadialTheme.LabelSelected : RadialTheme.Label;

            if (Band is not null)
            {
                Band.Data = selected ? _bandRaised : _bandRest;
                Band.Fill = selected ? RadialTheme.GroupBandSelected : RadialTheme.GroupBand;
            }
        }
    }
}
