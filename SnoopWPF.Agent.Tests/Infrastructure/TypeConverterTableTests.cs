namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Collections.Generic;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using SnoopWPF.Agent.Engine;

[TestFixture]
public class TypeConverterTableTests
{
    // =========================================================================
    // Category A: Whitelisted types — happy path
    // =========================================================================

    // --- string ---

    [TestCase("hello", "hello")]
    [TestCase("", "")]
    [TestCase("  spaces  ", "  spaces  ")]
    public void Convert_String_ReturnsInputUnchanged(string input, string expected)
    {
        var result = TypeConverterTable.Convert(typeof(string), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Convert_StringNull_ReturnsNull()
    {
        var result = TypeConverterTable.Convert(typeof(string), null!);
        Assert.That(result, Is.Null);
    }

    // --- bool ---

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("True", true)]
    [TestCase("False", false)]
    [TestCase("TRUE", true)]
    [TestCase("FALSE", false)]
    public void Convert_Bool_ReturnsParsedValue(string input, bool expected)
    {
        var result = TypeConverterTable.Convert(typeof(bool), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    // --- int ---

    [TestCase("0", 0)]
    [TestCase("42", 42)]
    [TestCase("-7", -7)]
    [TestCase("2147483647", int.MaxValue)]
    public void Convert_Int_ReturnsParsedValue(string input, int expected)
    {
        var result = TypeConverterTable.Convert(typeof(int), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    // --- double ---

    [TestCase("3.14", 3.14)]
    [TestCase("0", 0.0)]
    [TestCase("-1.5", -1.5)]
    [TestCase("1E10", 1E10)]
    public void Convert_Double_ReturnsParsedValue(string input, double expected)
    {
        var result = TypeConverterTable.Convert(typeof(double), input);
        Assert.That(result, Is.EqualTo(expected).Within(1e-10));
    }

    [Test]
    public void Convert_DoubleNaN_ReturnsParsedValue()
    {
        var result = TypeConverterTable.Convert(typeof(double), "NaN");
        Assert.That(result, Is.EqualTo(double.NaN));
    }

    [Test]
    public void Convert_DoubleInfinity_ReturnsParsedValue()
    {
        var result = TypeConverterTable.Convert(typeof(double), "Infinity");
        Assert.That(result, Is.EqualTo(double.PositiveInfinity));
    }

    // --- float ---

    [TestCase("2.5", 2.5f)]
    [TestCase("0", 0f)]
    [TestCase("-1.25", -1.25f)]
    public void Convert_Float_ReturnsParsedValue(string input, float expected)
    {
        var result = TypeConverterTable.Convert(typeof(float), input);
        Assert.That(result, Is.EqualTo(expected).Within(1e-6f));
    }

    // --- decimal ---

    [TestCase("1.5")]
    [TestCase("0")]
    [TestCase("-99.99")]
    public void Convert_Decimal_ReturnsParsedValue(string input)
    {
        var expected = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var result = TypeConverterTable.Convert(typeof(decimal), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    // --- long ---

    [TestCase("9999999999", 9999999999L)]
    [TestCase("0", 0L)]
    [TestCase("-1", -1L)]
    public void Convert_Long_ReturnsParsedValue(string input, long expected)
    {
        var result = TypeConverterTable.Convert(typeof(long), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    // --- Thickness ---

    [Test]
    public void Convert_ThicknessSingleValue_ReturnsUniformThickness()
    {
        var result = TypeConverterTable.Convert(typeof(Thickness), "10");
        Assert.That(result, Is.EqualTo(new Thickness(10)));
    }

    [Test]
    public void Convert_ThicknessFourValues_ReturnsFourSidedThickness()
    {
        var result = TypeConverterTable.Convert(typeof(Thickness), "10,20,30,40");
        Assert.That(result, Is.EqualTo(new Thickness(10, 20, 30, 40)));
    }

    [Test]
    public void Convert_ThicknessZero_ReturnsZeroThickness()
    {
        var result = TypeConverterTable.Convert(typeof(Thickness), "0");
        Assert.That(result, Is.EqualTo(new Thickness(0)));
    }

    // --- CornerRadius ---

    [Test]
    public void Convert_CornerRadiusSingleValue_ReturnsUniformCornerRadius()
    {
        var result = TypeConverterTable.Convert(typeof(CornerRadius), "5");
        Assert.That(result, Is.EqualTo(new CornerRadius(5)));
    }

    [Test]
    public void Convert_CornerRadiusFourValues_ReturnsFourCornerRadius()
    {
        var result = TypeConverterTable.Convert(typeof(CornerRadius), "5,10,15,20");
        Assert.That(result, Is.EqualTo(new CornerRadius(5, 10, 15, 20)));
    }

    // --- GridLength ---

    [Test]
    public void Convert_GridLengthAuto_ReturnsAutoGridLength()
    {
        var result = TypeConverterTable.Convert(typeof(GridLength), "Auto");
        Assert.That(result, Is.EqualTo(GridLength.Auto));
    }

    [Test]
    public void Convert_GridLengthAutoLowercase_ReturnsAutoGridLength()
    {
        var result = TypeConverterTable.Convert(typeof(GridLength), "auto");
        Assert.That(result, Is.EqualTo(GridLength.Auto));
    }

    [Test]
    public void Convert_GridLengthStar_ReturnsStarGridLength()
    {
        var result = TypeConverterTable.Convert(typeof(GridLength), "*");
        Assert.That(result, Is.EqualTo(new GridLength(1, GridUnitType.Star)));
    }

    [Test]
    public void Convert_GridLengthDoubleStar_ReturnsWeightedStarGridLength()
    {
        var result = TypeConverterTable.Convert(typeof(GridLength), "2*");
        Assert.That(result, Is.EqualTo(new GridLength(2, GridUnitType.Star)));
    }

    [Test]
    public void Convert_GridLengthAbsolutePixels_ReturnsPixelGridLength()
    {
        var result = TypeConverterTable.Convert(typeof(GridLength), "100");
        Assert.That(result, Is.EqualTo(new GridLength(100, GridUnitType.Pixel)));
    }

    // --- Point ---

    [Test]
    public void Convert_Point_ReturnsParsedPoint()
    {
        var result = TypeConverterTable.Convert(typeof(Point), "10,20");
        Assert.That(result, Is.EqualTo(new Point(10, 20)));
    }

    [Test]
    public void Convert_PointDecimal_ReturnsParsedPoint()
    {
        var result = TypeConverterTable.Convert(typeof(Point), "1.5,2.5");
        Assert.That(result, Is.EqualTo(new Point(1.5, 2.5)));
    }

    // --- Size ---

    [Test]
    public void Convert_Size_ReturnsParsedSize()
    {
        var result = TypeConverterTable.Convert(typeof(Size), "100,50");
        Assert.That(result, Is.EqualTo(new Size(100, 50)));
    }

    // --- Rect ---

    [Test]
    public void Convert_Rect_ReturnsParsedRect()
    {
        var result = TypeConverterTable.Convert(typeof(Rect), "0,0,100,50");
        Assert.That(result, Is.EqualTo(new Rect(0, 0, 100, 50)));
    }

    // --- Color ---

    [Test]
    public void Convert_ColorHexRGB_ReturnsParsedColor()
    {
        var result = TypeConverterTable.Convert(typeof(Color), "#FF0000");
        Assert.That(result, Is.EqualTo(Color.FromRgb(0xFF, 0x00, 0x00)));
    }

    [Test]
    public void Convert_ColorHexARGB_ReturnsParsedColor()
    {
        var result = TypeConverterTable.Convert(typeof(Color), "#FFFF0000");
        Assert.That(result, Is.EqualTo(Color.FromArgb(0xFF, 0xFF, 0x00, 0x00)));
    }

    [Test]
    public void Convert_ColorHexRGBShorthand_ReturnsParsedColor()
    {
        // #F00 expands to #FF0000
        var result = TypeConverterTable.Convert(typeof(Color), "#F00");
        Assert.That(result, Is.EqualTo(Color.FromRgb(0xFF, 0x00, 0x00)));
    }

    [Test]
    public void Convert_ColorNamedRed_ReturnsRedColor()
    {
        var result = TypeConverterTable.Convert(typeof(Color), "Red");
        Assert.That(result, Is.EqualTo(Colors.Red));
    }

    [Test]
    public void Convert_ColorNamedBlue_ReturnsBlueColor()
    {
        var result = TypeConverterTable.Convert(typeof(Color), "Blue");
        Assert.That(result, Is.EqualTo(Colors.Blue));
    }

    [Test]
    public void Convert_ColorTransparent_ReturnsTransparentColor()
    {
        var result = TypeConverterTable.Convert(typeof(Color), "Transparent");
        Assert.That(result, Is.EqualTo(Colors.Transparent));
    }

    // --- Brush ---

    [Test]
    public void Convert_BrushNamedRed_ReturnsSolidColorBrushWithRedColor()
    {
        var result = TypeConverterTable.Convert(typeof(Brush), "Red");
        Assert.That(result, Is.InstanceOf<SolidColorBrush>());
        var brush = (SolidColorBrush)result!;
        Assert.That(brush.Color, Is.EqualTo(Colors.Red));
    }

    [Test]
    public void Convert_SolidColorBrushNamedBlue_ReturnsSolidColorBrushWithBlueColor()
    {
        var result = TypeConverterTable.Convert(typeof(SolidColorBrush), "Blue");
        Assert.That(result, Is.InstanceOf<SolidColorBrush>());
        var brush = (SolidColorBrush)result!;
        Assert.That(brush.Color, Is.EqualTo(Colors.Blue));
    }

    [Test]
    public void Convert_BrushHexColor_ReturnsSolidColorBrush()
    {
        var result = TypeConverterTable.Convert(typeof(Brush), "#00FF00");
        Assert.That(result, Is.InstanceOf<SolidColorBrush>());
        var brush = (SolidColorBrush)result!;
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(0x00, 0xFF, 0x00)));
    }

    // --- FontWeight ---

    [Test]
    public void Convert_FontWeightBold_ReturnsBoldFontWeight()
    {
        var result = TypeConverterTable.Convert(typeof(FontWeight), "Bold");
        Assert.That(result, Is.EqualTo(FontWeights.Bold));
    }

    [Test]
    public void Convert_FontWeightNormal_ReturnsNormalFontWeight()
    {
        var result = TypeConverterTable.Convert(typeof(FontWeight), "Normal");
        Assert.That(result, Is.EqualTo(FontWeights.Normal));
    }

    [Test]
    public void Convert_FontWeightThin_ReturnsThinFontWeight()
    {
        var result = TypeConverterTable.Convert(typeof(FontWeight), "Thin");
        Assert.That(result, Is.EqualTo(FontWeights.Thin));
    }

    // --- FontStyle ---

    [TestCase("Italic")]
    [TestCase("italic")]
    [TestCase("ITALIC")]
    public void Convert_FontStyleItalic_ReturnsItalicFontStyle(string input)
    {
        var result = TypeConverterTable.Convert(typeof(FontStyle), input);
        Assert.That(result, Is.EqualTo(FontStyles.Italic));
    }

    [TestCase("Normal")]
    [TestCase("normal")]
    public void Convert_FontStyleNormal_ReturnsNormalFontStyle(string input)
    {
        var result = TypeConverterTable.Convert(typeof(FontStyle), input);
        Assert.That(result, Is.EqualTo(FontStyles.Normal));
    }

    [TestCase("Oblique")]
    [TestCase("oblique")]
    public void Convert_FontStyleOblique_ReturnsObliqueFontStyle(string input)
    {
        var result = TypeConverterTable.Convert(typeof(FontStyle), input);
        Assert.That(result, Is.EqualTo(FontStyles.Oblique));
    }

    // --- Enums ---

    [TestCase("Visible", Visibility.Visible)]
    [TestCase("Hidden", Visibility.Hidden)]
    [TestCase("Collapsed", Visibility.Collapsed)]
    [TestCase("visible", Visibility.Visible)]
    [TestCase("COLLAPSED", Visibility.Collapsed)]
    public void Convert_Visibility_ReturnsParsedEnum(string input, Visibility expected)
    {
        var result = TypeConverterTable.Convert(typeof(Visibility), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Left", HorizontalAlignment.Left)]
    [TestCase("Center", HorizontalAlignment.Center)]
    [TestCase("Right", HorizontalAlignment.Right)]
    [TestCase("Stretch", HorizontalAlignment.Stretch)]
    [TestCase("center", HorizontalAlignment.Center)]
    public void Convert_HorizontalAlignment_ReturnsParsedEnum(string input, HorizontalAlignment expected)
    {
        var result = TypeConverterTable.Convert(typeof(HorizontalAlignment), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Top", VerticalAlignment.Top)]
    [TestCase("Center", VerticalAlignment.Center)]
    [TestCase("Bottom", VerticalAlignment.Bottom)]
    [TestCase("Stretch", VerticalAlignment.Stretch)]
    public void Convert_VerticalAlignment_ReturnsParsedEnum(string input, VerticalAlignment expected)
    {
        var result = TypeConverterTable.Convert(typeof(VerticalAlignment), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Left", TextAlignment.Left)]
    [TestCase("Center", TextAlignment.Center)]
    [TestCase("Right", TextAlignment.Right)]
    [TestCase("Justify", TextAlignment.Justify)]
    public void Convert_TextAlignment_ReturnsParsedEnum(string input, TextAlignment expected)
    {
        var result = TypeConverterTable.Convert(typeof(TextAlignment), input);
        Assert.That(result, Is.EqualTo(expected));
    }

    // --- Nullable<T> ---

    [Test]
    public void Convert_NullableBoolTrue_ReturnsTrue()
    {
        var result = TypeConverterTable.Convert(typeof(bool?), "true");
        Assert.That(result, Is.EqualTo(true));
    }

    [Test]
    public void Convert_NullableIntValue_ReturnsInt()
    {
        var result = TypeConverterTable.Convert(typeof(int?), "42");
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void Convert_NullableDoubleValue_ReturnsDouble()
    {
        var result = TypeConverterTable.Convert(typeof(double?), "1.5");
        Assert.That(result, Is.EqualTo(1.5));
    }

    // =========================================================================
    // Category B: Non-whitelisted types — NotSupportedException
    // =========================================================================

    [Test]
    public void Convert_UriType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Uri), "http://example.com"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_DateTimeType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(DateTime), "2025-01-01"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_ObjectType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(object), "anything"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_UIElementType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(UIElement), "something"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_GenericListType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(List<int>), "1,2,3"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_GuidType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Guid), "00000000-0000-0000-0000-000000000000"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_TimeSpanType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(TimeSpan), "01:00:00"),
            Throws.TypeOf<NotSupportedException>());
    }

    // --- Security-focused: sensitive / privilege-escalation types blocked ---

    [Test]
    public void Convert_SecureStringType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(SecureString), "secret"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_TypeType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Type), "System.String"),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Convert_StyleType_ThrowsNotSupportedException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Style), "someStyle"),
            Throws.TypeOf<NotSupportedException>());
    }

    // =========================================================================
    // Category C: Malformed inputs — FormatException
    // =========================================================================

    [Test]
    public void Convert_IntMalformed_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(int), "not-a-number"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_DoubleMalformed_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(double), "abc"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_FloatMalformed_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(float), "xyz"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_DecimalMalformed_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(decimal), "not-decimal"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_LongMalformed_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(long), "1.5"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_BoolMaybe_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(bool), "maybe"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_BoolInteger_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(bool), "1"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_ThicknessThreeComponents_ThrowsFormatException()
    {
        // 2-component and 3-component forms are unsupported
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Thickness), "1,2,3"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_ThicknessTwoComponents_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Thickness), "1,2"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_ThicknessNonNumeric_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Thickness), "abc"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_CornerRadiusThreeComponents_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(CornerRadius), "1,2,3"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_VisibilityInvalidValue_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Visibility), "InvalidValue"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_ColorNotAColor_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Color), "not-a-color"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_ColorInvalidHex_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Color), "#GGGG"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_FontStyleUnrecognized_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(FontStyle), "Bold"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_FontWeightLowercase_ThrowsFormatException()
    {
        // ParseFontWeight uses case-sensitive reflection; "bold" won't match "Bold"
        Assert.That(
            () => TypeConverterTable.Convert(typeof(FontWeight), "bold"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_FontWeightUnrecognized_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(FontWeight), "SuperBold"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_GridLengthInvalidStar_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(GridLength), "abc*"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_PointSingleComponent_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Point), "10"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_SizeThreeComponents_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Size), "10,20,30"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_RectTwoComponents_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Rect), "0,0"),
            Throws.TypeOf<FormatException>());
    }

    // =========================================================================
    // Category D: Null / empty inputs
    // =========================================================================

    [Test]
    public void Convert_IntEmptyString_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(int), string.Empty),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_DoubleEmptyString_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(double), string.Empty),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_BoolEmptyString_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(bool), string.Empty),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_ColorEmptyString_ThrowsFormatException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(typeof(Color), string.Empty),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Convert_NullPropertyType_ThrowsArgumentNullException()
    {
        Assert.That(
            () => TypeConverterTable.Convert(null!, "value"),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Convert_NullableIntEmptyString_ThrowsFormatException()
    {
        // Nullable<int> unwraps to int; empty string hits int parser
        Assert.That(
            () => TypeConverterTable.Convert(typeof(int?), string.Empty),
            Throws.TypeOf<FormatException>());
    }
}
