namespace ObtainiumPC.Services.Versioning;

/// <summary>
/// Port of lib/utils/string_compare.dart: alphanumeric run comparison where
/// digit runs compare numerically, letter runs ordinally, and digits sort
/// after letters.
/// </summary>
public static class StringCompare
{
    public static int CompareAlphaNumeric(string a, string b)
    {
        var aParts = SplitAlphaNumeric(a);
        var bParts = SplitAlphaNumeric(b);
        for (var i = 0; i < aParts.Count && i < bParts.Count; i++)
        {
            var aPart = aParts[i];
            var bPart = bParts[i];
            var aIsNumber = IsAllDigits(aPart);
            var bIsNumber = IsAllDigits(bPart);
            if (aIsNumber && bIsNumber)
            {
                if (long.TryParse(aPart, out var an) && long.TryParse(bPart, out var bn))
                {
                    var cmp = an.CompareTo(bn);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    var cmp = string.CompareOrdinal(aPart, bPart);
                    if (cmp != 0) return cmp;
                }
            }
            else if (!aIsNumber && !bIsNumber)
            {
                var cmp = string.CompareOrdinal(aPart, bPart);
                if (cmp != 0) return cmp;
            }
            else
            {
                return aIsNumber ? 1 : -1;
            }
        }
        return aParts.Count.CompareTo(bParts.Count);
    }

    private static List<string> SplitAlphaNumeric(string s)
    {
        var parts = new List<string>();
        if (s.Length == 0) return parts;
        var sb = new System.Text.StringBuilder();
        var isNumeric = char.IsDigit(s[0]);
        sb.Append(s[0]);
        for (var i = 1; i < s.Length; i++)
        {
            var currentIsNumeric = char.IsDigit(s[i]);
            if (currentIsNumeric == isNumeric) { sb.Append(s[i]); }
            else { parts.Add(sb.ToString()); sb.Clear(); sb.Append(s[i]); isNumeric = currentIsNumeric; }
        }
        parts.Add(sb.ToString());
        return parts;
    }

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s) if (!char.IsDigit(c)) return false;
        return true;
    }
}
