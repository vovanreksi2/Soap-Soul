using System.Globalization;
using SoapAndSoul.Domain.Catalog;

namespace SoapAndSoul.Domain.Text;

/// <summary>uk-UA number formatting that does not depend on ICU (the client runs with invariant globalization).</summary>
public static class Formatting
{
    private static readonly NumberFormatInfo Uk = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NegativeSign = "−",
    };

    /// <summary>Two decimals below 10, whole numbers above: "0,18", "4,5", "156".</summary>
    public static string Number(decimal n)
    {
        var rounded = Math.Round(n, Math.Abs(n) < 10 ? 2 : 0, MidpointRounding.AwayFromZero);
        return rounded.ToString("#,0.##", Uk);
    }

    public static string Money(decimal n) => Number(n) + " грн";

    public static string Amount(decimal n, MeasureUnit unit) => Number(n) + " " + unit.Label();

    /// <summary>Plain value for an &lt;input type=number&gt; (dot separator, no grouping).</summary>
    public static string Input(decimal n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Batch weight: switches to кг / л from 1000.</summary>
    public static string Weight(decimal value, CosmeticLine line)
    {
        var unit = Categories.CapacityUnit(line);
        if (value < 1000) return Number(value) + " " + unit.Label();
        return Number(value / 1000) + (unit == MeasureUnit.Gram ? " кг" : " л");
    }
}
