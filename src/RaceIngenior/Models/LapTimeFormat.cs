using System.Globalization;

namespace RaceIngenior.Models;

/// <summary>Motorsport lap-time display: <c>1:40.737</c> (m:ss.mmm).</summary>
public static class LapTimeFormat
{
    public static string Format(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "—";

        var totalMs = (int)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);
        var minutes = totalMs / 60_000;
        var rem = totalMs % 60_000;
        var secs = rem / 1000;
        var ms = rem % 1000;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{minutes}:{secs:00}.{ms:000}");
    }
}
