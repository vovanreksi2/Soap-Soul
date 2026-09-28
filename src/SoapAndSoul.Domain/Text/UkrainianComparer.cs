namespace SoapAndSoul.Domain.Text;

/// <summary>
/// Case-insensitive ordering by the Ukrainian alphabet (і, ї, є, ґ in their places),
/// independent of ICU. Other characters keep their ordinal order after Cyrillic ones.
/// </summary>
public sealed class UkrainianComparer : IComparer<string>
{
    public static readonly UkrainianComparer Instance = new();

    private const string Alphabet = "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя";

    private static int Rank(char c)
    {
        c = char.ToLowerInvariant(c);
        var i = Alphabet.IndexOf(c);
        if (i >= 0) return 0x10000 + i;
        return c < 0x80 ? c : 0x20000 + c;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var n = Math.Min(x.Length, y.Length);
        for (var k = 0; k < n; k++)
        {
            var d = Rank(x[k]).CompareTo(Rank(y[k]));
            if (d != 0) return d;
        }
        return x.Length.CompareTo(y.Length);
    }
}
