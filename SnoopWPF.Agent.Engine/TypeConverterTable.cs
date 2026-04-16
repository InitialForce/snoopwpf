namespace SnoopWPF.Agent.Engine;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Hardcoded whitelist of supported types for property mutation via SetPropertyAsync.
/// NEVER use TypeDescriptor.GetConverter() — all conversion is done via explicit,
/// handcrafted parsers in this class.
/// </summary>
/// <remarks>
/// Supported types: string, bool, int, double, float, decimal, long,
/// Thickness, CornerRadius, Color, Brush (named only), Visibility,
/// HorizontalAlignment, VerticalAlignment, FontWeight, FontStyle,
/// TextAlignment, GridLength, Point, Size, Rect, and all enum types.
///
/// Never settable: Uri, ImageSource, BitmapSource, FontFamily, Style,
/// ControlTemplate, DataTemplate, Binding, Type, any UIElement subtype.
/// </remarks>
public static class TypeConverterTable
{
    /// <summary>
    /// Attempts to convert <paramref name="rawValue"/> to the target <paramref name="propertyType"/>.
    /// Returns the converted value, or throws <see cref="NotSupportedException"/> for unsupported types
    /// or <see cref="FormatException"/> for malformed values.
    /// </summary>
    /// <param name="propertyType">The type to convert to.</param>
    /// <param name="rawValue">The string value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="NotSupportedException">Thrown when the type is not on the whitelist.</exception>
    /// <exception cref="FormatException">Thrown when the value cannot be parsed to the target type.</exception>
    public static object Convert(Type propertyType, string rawValue)
    {
        if (propertyType is null)
        {
            throw new ArgumentNullException(nameof(propertyType));
        }

        // Unwrap Nullable<T>.
        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // string — pass through.
        if (effectiveType == typeof(string))
        {
            return rawValue;
        }

        // bool
        if (effectiveType == typeof(bool))
        {
            if (bool.TryParse(rawValue, out var b))
            {
                return b;
            }

            throw new FormatException($"Cannot parse '{rawValue}' as bool. Use 'true' or 'false'.");
        }

        // int
        if (effectiveType == typeof(int))
        {
            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                return i;
            }

            throw new FormatException($"Cannot parse '{rawValue}' as int.");
        }

        // double
        if (effectiveType == typeof(double))
        {
            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }

            throw new FormatException($"Cannot parse '{rawValue}' as double.");
        }

        // float
        if (effectiveType == typeof(float))
        {
            if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                return f;
            }

            throw new FormatException($"Cannot parse '{rawValue}' as float.");
        }

        // decimal
        if (effectiveType == typeof(decimal))
        {
            if (decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
            {
                return dec;
            }

            throw new FormatException($"Cannot parse '{rawValue}' as decimal.");
        }

        // long
        if (effectiveType == typeof(long))
        {
            if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            throw new FormatException($"Cannot parse '{rawValue}' as long.");
        }

        // Thickness — format: "uniform" or "left,top,right,bottom" (InvariantCulture)
        if (effectiveType == typeof(Thickness))
        {
            return ParseThickness(rawValue);
        }

        // CornerRadius — format: "uniform" or "tl,tr,br,bl" (InvariantCulture)
        if (effectiveType == typeof(CornerRadius))
        {
            return ParseCornerRadius(rawValue);
        }

        // Color — format: "#AARRGGBB", "#RRGGBB", "#RGB", or named color (e.g. "Red")
        if (effectiveType == typeof(Color))
        {
            return ParseColor(rawValue);
        }

        // Brush — named colors only (SolidColorBrush from named color)
        if (effectiveType == typeof(Brush) || effectiveType == typeof(SolidColorBrush))
        {
            var color = ParseColor(rawValue);
            return new SolidColorBrush(color);
        }

        // GridLength — "Auto", "*", "2*", or pixel value
        if (effectiveType == typeof(GridLength))
        {
            return ParseGridLength(rawValue);
        }

        // Point — "x,y" (InvariantCulture)
        if (effectiveType == typeof(Point))
        {
            return ParsePoint(rawValue);
        }

        // Size — "w,h" (InvariantCulture)
        if (effectiveType == typeof(Size))
        {
            return ParseSize(rawValue);
        }

        // Rect — "x,y,w,h" (InvariantCulture)
        if (effectiveType == typeof(Rect))
        {
            return ParseRect(rawValue);
        }

        // FontWeight — named: "Bold", "Normal", "Thin", etc.
        if (effectiveType == typeof(FontWeight))
        {
            return ParseFontWeight(rawValue);
        }

        // FontStyle — "Normal", "Italic", "Oblique"
        if (effectiveType == typeof(FontStyle))
        {
            return ParseFontStyle(rawValue);
        }

        // All enum types (Visibility, HorizontalAlignment, VerticalAlignment, TextAlignment, etc.)
        if (effectiveType.IsEnum)
        {
            try
            {
                // Culture-neutral: Enum.Parse does not use culture.
                return Enum.Parse(effectiveType, rawValue, ignoreCase: true);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                var validValues = string.Join(", ", Enum.GetNames(effectiveType));
                throw new FormatException(
                    $"Cannot parse '{rawValue}' as {effectiveType.Name}. Valid values: {validValues}.");
            }
        }

        // Explicitly blocked types (UIElement subtypes, complex WPF types).
        throw new NotSupportedException(
            $"Type '{effectiveType.FullName}' is not on the settable type whitelist. " +
            "Settable types: string, bool, int, double, float, decimal, long, " +
            "Thickness, CornerRadius, Color, Brush, GridLength, Point, Size, Rect, " +
            "FontWeight, FontStyle, and enum types (Visibility, HorizontalAlignment, etc.).");
    }

    // -------------------------------------------------------------------------
    // Private parsers
    // -------------------------------------------------------------------------

    private static Thickness ParseThickness(string value)
    {
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
        {
            if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var uniform))
            {
                return new Thickness(uniform);
            }
        }
        else if (parts.Length == 4)
        {
            if (TryParseDouble(parts[0], out var left) &&
                TryParseDouble(parts[1], out var top) &&
                TryParseDouble(parts[2], out var right) &&
                TryParseDouble(parts[3], out var bottom))
            {
                return new Thickness(left, top, right, bottom);
            }
        }

        throw new FormatException(
            $"Cannot parse '{value}' as Thickness. Use 'uniform' or 'left,top,right,bottom' with InvariantCulture decimals.");
    }

    private static CornerRadius ParseCornerRadius(string value)
    {
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
        {
            if (TryParseDouble(parts[0], out var uniform))
            {
                return new CornerRadius(uniform);
            }
        }
        else if (parts.Length == 4)
        {
            if (TryParseDouble(parts[0], out var tl) &&
                TryParseDouble(parts[1], out var tr) &&
                TryParseDouble(parts[2], out var br) &&
                TryParseDouble(parts[3], out var bl))
            {
                return new CornerRadius(tl, tr, br, bl);
            }
        }

        throw new FormatException(
            $"Cannot parse '{value}' as CornerRadius. Use 'uniform' or 'topLeft,topRight,bottomRight,bottomLeft'.");
    }

    private static Color ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Color value must not be empty.");
        }

        var trimmed = value.Trim();

        // Hex color: #RGB, #RRGGBB, #AARRGGBB
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = trimmed.Substring(1);

            if (hex.Length == 3)
            {
                // #RGB → #RRGGBB
                hex = string.Concat(
                    hex[0], hex[0],
                    hex[1], hex[1],
                    hex[2], hex[2]);
            }

            if (hex.Length == 6)
            {
                if (TryParseHexByte(hex, 0, out var r) &&
                    TryParseHexByte(hex, 2, out var g) &&
                    TryParseHexByte(hex, 4, out var b))
                {
                    return Color.FromRgb(r, g, b);
                }
            }
            else if (hex.Length == 8)
            {
                if (TryParseHexByte(hex, 0, out var a) &&
                    TryParseHexByte(hex, 2, out var r) &&
                    TryParseHexByte(hex, 4, out var g) &&
                    TryParseHexByte(hex, 6, out var b))
                {
                    return Color.FromArgb(a, r, g, b);
                }
            }

            throw new FormatException(
                $"Cannot parse '{value}' as Color. Hex format: #RGB, #RRGGBB, or #AARRGGBB.");
        }

        // Named color: look up via Colors type.
        var colorProp = typeof(Colors).GetProperty(
            trimmed,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (colorProp is not null && colorProp.GetValue(null) is Color namedColor)
        {
            return namedColor;
        }

        throw new FormatException(
            $"Cannot parse '{value}' as Color. Use a named color (e.g. 'Red', 'Blue') or hex (#RRGGBB, #AARRGGBB).");
    }

    private static GridLength ParseGridLength(string value)
    {
        var trimmed = value.Trim();

        if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        if (trimmed.EndsWith("*", StringComparison.Ordinal))
        {
            var starPart = trimmed.Substring(0, trimmed.Length - 1).Trim();
            if (starPart.Length == 0)
            {
                return new GridLength(1, GridUnitType.Star);
            }

            if (TryParseDouble(starPart, out var starVal))
            {
                return new GridLength(starVal, GridUnitType.Star);
            }
        }
        else if (TryParseDouble(trimmed, out var pixels))
        {
            return new GridLength(pixels, GridUnitType.Pixel);
        }

        throw new FormatException(
            $"Cannot parse '{value}' as GridLength. Use 'Auto', '*', '2*', or a pixel value.");
    }

    private static Point ParsePoint(string value)
    {
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            TryParseDouble(parts[0], out var x) &&
            TryParseDouble(parts[1], out var y))
        {
            return new Point(x, y);
        }

        throw new FormatException($"Cannot parse '{value}' as Point. Use 'x,y'.");
    }

    private static Size ParseSize(string value)
    {
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            TryParseDouble(parts[0], out var w) &&
            TryParseDouble(parts[1], out var h))
        {
            return new Size(w, h);
        }

        throw new FormatException($"Cannot parse '{value}' as Size. Use 'width,height'.");
    }

    private static Rect ParseRect(string value)
    {
        var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4 &&
            TryParseDouble(parts[0], out var x) &&
            TryParseDouble(parts[1], out var y) &&
            TryParseDouble(parts[2], out var w) &&
            TryParseDouble(parts[3], out var h))
        {
            return new Rect(x, y, w, h);
        }

        throw new FormatException($"Cannot parse '{value}' as Rect. Use 'x,y,width,height'.");
    }

    private static FontWeight ParseFontWeight(string value)
    {
        // Map well-known FontWeight names to static properties on FontWeights.
        var prop = typeof(FontWeights).GetProperty(
            value.Trim(),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (prop is not null && prop.GetValue(null) is FontWeight fw)
        {
            return fw;
        }

        throw new FormatException(
            $"Cannot parse '{value}' as FontWeight. Use names like 'Bold', 'Normal', 'Thin', 'Medium', etc.");
    }

    private static FontStyle ParseFontStyle(string value)
    {
        var trimmed = value.Trim();

        if (string.Equals(trimmed, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            return FontStyles.Normal;
        }

        if (string.Equals(trimmed, "Italic", StringComparison.OrdinalIgnoreCase))
        {
            return FontStyles.Italic;
        }

        if (string.Equals(trimmed, "Oblique", StringComparison.OrdinalIgnoreCase))
        {
            return FontStyles.Oblique;
        }

        throw new FormatException(
            $"Cannot parse '{value}' as FontStyle. Use 'Normal', 'Italic', or 'Oblique'.");
    }

    private static bool TryParseDouble(string s, out double value)
    {
        return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseHexByte(string hex, int offset, out byte result)
    {
        try
        {
            result = System.Convert.ToByte(hex.Substring(offset, 2), 16);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}
