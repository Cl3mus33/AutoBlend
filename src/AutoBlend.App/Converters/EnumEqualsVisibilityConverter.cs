using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoBlend.App.Converters;

/// <summary>Visible when the bound enum value's name matches the converter parameter string.</summary>
public sealed class EnumEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var match = value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase);
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
