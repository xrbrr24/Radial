using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Radial.App.Overlay;

namespace Radial.App.Theme;

/// <summary>Convierte un booleano en cualquier objeto (visibilidad, pincel, texto...): se declara en XAML con los dos valores.</summary>
[ValueConversion(typeof(bool), typeof(object))]
internal sealed class BoolConverter : IValueConverter
{
    public object? TrueValue { get; set; }

    public object? FalseValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Convierte según sea <c>null</c> o no el valor enlazado.</summary>
[ValueConversion(typeof(object), typeof(object))]
internal sealed class NullConverter : IValueConverter
{
    public object? NullValue { get; set; }

    public object? NotNullValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? NullValue : NotNullValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Convierte según un número sea cero o no (listas vacías, contadores).</summary>
[ValueConversion(typeof(int), typeof(object))]
internal sealed class ZeroConverter : IValueConverter
{
    public object? ZeroValue { get; set; }

    public object? OtherValue { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int number && number == 0 ? ZeroValue : OtherValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Nombre de icono del catálogo → carácter de la fuente de iconos.</summary>
[ValueConversion(typeof(string), typeof(string))]
internal sealed class GlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        GlyphCatalog.Resolve(value as string) ?? GlyphCatalog.Fallback;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Sangría de un nivel del árbol: el nivel × el ancho del parámetro, como margen izquierdo.</summary>
[ValueConversion(typeof(int), typeof(Thickness))]
internal sealed class DepthIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var step = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 14;
        return new Thickness(Math.Max(0, ((value as int?) ?? 1) - 1) * step, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible solo si no hay formulario abierto y no hay ninguna acción elegida (el panel lateral está vacío).</summary>
internal sealed class ShowWhenEmpty : IMultiValueConverter
{
    public static ShowWhenEmpty Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [bool formOpen, var selected] && !formOpen && (selected is null || selected == DependencyProperty.UnsetValue) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible solo si no hay formulario abierto y hay una acción elegida (el panel lateral muestra su detalle).</summary>
internal sealed class ShowWhenDetail : IMultiValueConverter
{
    public static ShowWhenDetail Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [bool formOpen, var selected] && !formOpen && selected is not null && selected != DependencyProperty.UnsetValue ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Verdadero si todos los valores enlazados son iguales entre sí (marca el elemento de navegación de la sección abierta).</summary>
internal sealed class AreEqualConverter : IMultiValueConverter
{
    public static AreEqualConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] != DependencyProperty.UnsetValue && Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
