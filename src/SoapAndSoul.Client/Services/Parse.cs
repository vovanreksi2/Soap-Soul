using System.Globalization;

namespace SoapAndSoul.Client.Services;

public static class Parse
{
    /// <summary>Reads an &lt;input type=number&gt; value (always dot-separated); accepts a comma too.</summary>
    public static decimal? Decimal(object? value)
    {
        var s = value?.ToString()?.Trim().Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
