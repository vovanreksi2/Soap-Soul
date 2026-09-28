namespace SoapAndSoul.Domain.Text;

/// <summary>Typo-tolerant search: substring match, or a word prefix within a small edit distance.</summary>
public static class FuzzyMatcher
{
    public static bool Matches(string? query, string text)
    {
        var q = (query ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0) return true;
        var t = text.ToLowerInvariant();
        if (t.Contains(q, StringComparison.Ordinal)) return true;

        var tolerance = q.Length >= 6 ? 2 : q.Length >= 3 ? 1 : 0;
        foreach (var word in SplitWords(t))
        {
            if (Levenshtein(q, Prefix(word, q.Length + 1)) <= tolerance) return true;
            if (Levenshtein(q, Prefix(word, q.Length)) <= tolerance) return true;
        }
        return false;
    }

    private static string Prefix(string s, int n) => s.Length <= n ? s : s[..n];

    private static IEnumerable<string> SplitWords(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '\'' or '’');
            if (isWordChar && start < 0) start = i;
            else if (!isWordChar && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }

    internal static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
