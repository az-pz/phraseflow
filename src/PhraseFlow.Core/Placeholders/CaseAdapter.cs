using System.Globalization;

namespace PhraseFlow.Core.Placeholders;

public enum CaseStyle
{
    AsIs,
    Capitalize,
    Upper,
}

/// <summary>Propagates the case of a typed keyword to the expansion (btw / Btw / BTW).</summary>
public static class CaseAdapter
{
    public static CaseStyle Detect(string typed, string keyword)
    {
        if (string.Equals(typed, keyword, StringComparison.Ordinal))
        {
            return CaseStyle.AsIs;
        }

        var letters = typed.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return CaseStyle.AsIs;
        }

        if (letters.Length > 1 && letters.All(char.IsUpper))
        {
            return CaseStyle.Upper;
        }

        return char.IsUpper(letters[0]) ? CaseStyle.Capitalize : CaseStyle.AsIs;
    }

    public static ExpansionOutput Apply(ExpansionOutput output, CaseStyle style, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return style switch
        {
            CaseStyle.Upper => output.MapText(t => t.ToUpper(culture)),
            CaseStyle.Capitalize => output.CapitalizeFirstLetter(culture),
            _ => output,
        };
    }
}
