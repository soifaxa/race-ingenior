using System.Text.RegularExpressions;

namespace RaceIngenior.Radio;

internal static class TtsTextPrep
{
    private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);

    public static string Prepare(string text)
    {
        var t = text.Trim();
        t = t.Replace("°C", " degrés", StringComparison.Ordinal);
        t = t.Replace("ºC", " degrés", StringComparison.Ordinal);
        t = Regex.Replace(t, @"(\d)\s*%", "$1 pour cent");
        t = Regex.Replace(t, @"\bFCY\b", "full course yellow", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bVSC\b", "virtual safety car", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bSC\b", "safety car", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bDRS\b", "D R S", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bLMGT3\b", "L M G T 3", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bLMP2\b", "L M P 2", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bLMP3\b", "L M P 3", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bGT3\b", "G T 3", RegexOptions.IgnoreCase);
        return MultiSpace.Replace(t, " ").Trim();
    }

    public static string RateStyleHint(int rate) => rate switch
    {
        <= -4 => "speak slowly and very clearly",
        <= -1 => "speak a little slower than normal",
        >= 4 => "speak quickly, still clear",
        >= 1 => "speak a little faster than normal",
        _ => "speak at a natural radio pace"
    };
}
