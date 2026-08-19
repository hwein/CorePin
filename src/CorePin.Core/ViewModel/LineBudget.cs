namespace CorePin.Core.ViewModel;

/// The rule list is 220 px wide: longer line text is cut in the middle, never at the end.
public static class LineBudget
{
    public const int MaxChars = 30;

    public const string Ellipsis = "…";

    public static string Fit(string text, int maxChars = MaxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);

        if (text.Length <= maxChars) return text;
        if (maxChars == 1) return Ellipsis;

        int keep = maxChars - 1;
        int tail = keep / 2;
        return text[..(keep - tail)] + Ellipsis + text[^tail..];
    }
}
