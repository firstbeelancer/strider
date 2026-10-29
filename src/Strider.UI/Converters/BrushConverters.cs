using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Strider.Core.Domain;

namespace Strider.UI.Converters;

/// <summary>
/// Turns a "#RRGGBB" string into a brush so palettes and colour chips can be
/// bound straight from the ViewModel (which stays UI-framework free).
/// Empty or unparsable values fall back to <see cref="Brushes.Transparent"/>.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ToBrush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static IBrush ToBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (Exception)
        {
            return Brushes.Transparent;
        }
    }
}

/// <summary>
/// <see cref="FlagColor"/> to brush — used by the coloured flag bar, the coloured
/// subject and the flag-rule list.
/// </summary>
public sealed class FlagColorToBrushConverter : IValueConverter
{
    public static readonly FlagColorToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FlagColor color && color != FlagColor.None
            ? HexToBrushConverter.ToBrush(FlagColors.ToHex(color))
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a boolean — for "show A when false, B when true" pairs.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
