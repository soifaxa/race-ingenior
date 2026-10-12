using RaceIngenior.Models;

namespace RaceIngenior.Services;

public sealed record TireAdvice(
    string Recommended,
    string ConditionLabel,
    string Reason,
    string Fitted,
    string TrackTempText,
    string AmbientTempText,
    string RainText,
    string WetnessText,
    string CloudText);

/// <summary>
/// Recommends Soft / Medium / Hard / Wet from LMU path wetness + track temperature
/// (Michelin Hypercar-style windows; also used as guidance for other classes).
/// </summary>
public static class TireCompoundAdvisor
{
    public static TireAdvice Evaluate(TelemetrySnapshot snap)
    {
        if (!snap.Connected)
        {
            return new TireAdvice(
                Recommended: "—",
                ConditionLabel: "NO DATA",
                Reason: "Connect to LMU to read track weather and wetness.",
                Fitted: "—",
                TrackTempText: "—",
                AmbientTempText: "—",
                RainText: "—",
                WetnessText: "—",
                CloudText: "—");
        }

        var fitted = FormatFitted(snap.FrontTireCompound, snap.RearTireCompound);
        var trackT = snap.TrackTempC;
        var ambientT = snap.AmbientTempC;
        var rain = Clamp01(snap.Raining);
        var wetAvg = Clamp01(snap.AvgPathWetness);
        var wetMax = Clamp01(snap.MaxPathWetness);
        var cloud = Clamp01(snap.DarkCloud);

        string recommended;
        string condition;
        string reason;

        if (rain >= 0.20 || wetAvg >= 0.40 || wetMax >= 0.55)
        {
            recommended = "WET";
            condition = "WET TRACK";
            reason = rain >= 0.20
                ? $"Rain {Pct(rain)} and path wetness avg {Pct(wetAvg)} / max {Pct(wetMax)} — fit Wet."
                : $"Path wetness avg {Pct(wetAvg)} / max {Pct(wetMax)} — fit Wet (drying or standing water).";
        }
        else if (rain >= 0.08 || wetAvg >= 0.18 || wetMax >= 0.28)
        {
            recommended = "WET";
            condition = "DAMP / DRYING";
            reason =
                $"Damp path (avg {Pct(wetAvg)}, max {Pct(wetMax)}, rain {Pct(rain)}). " +
                "Prefer Wet until the racing line is clearly dry; Soft only if fully dry and cold.";
        }
        else if (trackT < 15)
        {
            recommended = "SOFT";
            condition = "COLD / DRY";
            reason = $"Track {trackT:0}°C (ambient {ambientT:0}°C) — Soft window (~0–15°C).";
        }
        else if (trackT <= 25)
        {
            recommended = "MEDIUM";
            condition = "MODERATE / DRY";
            reason = $"Track {trackT:0}°C (ambient {ambientT:0}°C) — Medium window (~15–25°C).";
        }
        else
        {
            recommended = "HARD";
            condition = "HOT / DRY";
            reason = $"Track {trackT:0}°C (ambient {ambientT:0}°C) — Hard window (above ~25°C).";
        }

        return new TireAdvice(
            Recommended: recommended,
            ConditionLabel: condition,
            Reason: reason,
            Fitted: fitted,
            TrackTempText: $"{trackT:0.0}°C",
            AmbientTempText: $"{ambientT:0.0}°C",
            RainText: Pct(rain),
            WetnessText: $"avg {Pct(wetAvg)} · max {Pct(wetMax)}",
            CloudText: Pct(cloud));
    }

    private static string FormatFitted(string front, string rear)
    {
        front = front?.Trim() ?? "";
        rear = rear?.Trim() ?? "";
        if (front.Length == 0 && rear.Length == 0)
            return "—";
        if (front.Length == 0)
            return rear;
        if (rear.Length == 0 || string.Equals(front, rear, StringComparison.OrdinalIgnoreCase))
            return front;
        return $"F {front} · R {rear}";
    }

    private static double Clamp01(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 1);

    private static string Pct(double v) => $"{v * 100:0}%";
}
