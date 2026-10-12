using System.Globalization;
using System.Text;
using RaceIngenior.Models;

namespace RaceIngenior.Radio;

/// <summary>
/// Builds a minimal telemetry string for the LLM (short keys, rounded values, deltas).
/// </summary>
public sealed class TelemetryPromptBuilder
{
    private string _lastPacked = "";

    public string Build(TelemetrySnapshot s)
    {
        if (!s.Connected)
            return "tel=off";

        var sb = new StringBuilder(180);
        sb.Append("trk=").Append(Short(s.Track, 20));
        sb.Append(";car=").Append(Short(s.Vehicle, 24));
        sb.Append(";cls=").Append(Short(s.VehicleClass, 12));
        sb.Append(";P").Append(s.Place);
        sb.Append(";L").Append(s.Lap);
        sb.Append(";ph").Append(s.GamePhase);
        if (s.YellowFlagState > 0) sb.Append(";yel").Append(s.YellowFlagState);
        if (s.UnderYellow) sb.Append(";UY");
        if (s.IsBlueFlag) sb.Append(";BLUE");
        if (s.HasLocalYellow)
        {
            sb.Append(";locY");
            if (s.SectorFlags.Length > 1 && s.SectorFlags[1] > 0) sb.Append("S1");
            if (s.SectorFlags.Length > 2 && s.SectorFlags[2] > 0) sb.Append("S2");
            if (s.SectorFlags.Length > 0 && s.SectorFlags[0] > 0) sb.Append("S3");
        }
        sb.Append(";spd").Append(F0(s.SpeedKmh));
        sb.Append(";g").Append(s.Gear);
        sb.Append(";fuel").Append(F1(s.FuelL)).Append('/').Append(F0(s.FuelCapacityL));
        if (s.VirtualEnergy is double ve) sb.Append(";VE").Append(F1(ve));
        if (s.BatteryFraction > 0) sb.Append(";bat").Append(F2(s.BatteryFraction));
        sb.Append(";last").Append(F3(s.LastLap));
        sb.Append(";best").Append(F3(s.BestLap));
        if (s.TimeBehindNext > 0) sb.Append(";dN").Append(F2(s.TimeBehindNext));
        if (s.TimeBehindLeader > 0) sb.Append(";dL").Append(F2(s.TimeBehindLeader));
        sb.Append(";lap%").Append(F2(s.LapDistFraction));

        // Temps / wear only if noteworthy
        AppendTemp(sb, "H2O", s.WaterTempC, 105);
        AppendTemp(sb, "oil", s.OilTempC, 125);
        if (s.Overheating) sb.Append(";OVR");
        if (s.Detached) sb.Append(";DMG");

        var w = s.Wheels;
        // Send remaining tread % (LMU: mWear 1=new → 100%).
        sb.Append(";tire%")
            .Append(F0(w.FlWear * 100)).Append(',')
            .Append(F0(w.FrWear * 100)).Append(',')
            .Append(F0(w.RlWear * 100)).Append(',')
            .Append(F0(w.RrWear * 100));
        sb.Append(";carcassC")
            .Append(F0(w.FlCarcassC)).Append(',')
            .Append(F0(w.FrCarcassC)).Append(',')
            .Append(F0(w.RlCarcassC)).Append(',')
            .Append(F0(w.RrCarcassC));

        // Puncture flag (rF2 mFlat) — not lock-up flat spot.
        if (w.AnyFlat)
        {
            var flats = new List<string>(4);
            if (w.FlFlat) flats.Add("FL");
            if (w.FrFlat) flats.Add("FR");
            if (w.RlFlat) flats.Add("RL");
            if (w.RrFlat) flats.Add("RR");
            sb.Append(";flat=").Append(string.Join(',', flats));
        }

        if (w.FlBrakeC > 400 || w.FrBrakeC > 400 || w.RlBrakeC > 400 || w.RrBrakeC > 400)
        {
            sb.Append(";brk")
                .Append(F0(w.FlBrakeC)).Append(',')
                .Append(F0(w.FrBrakeC)).Append(',')
                .Append(F0(w.RlBrakeC)).Append(',')
                .Append(F0(w.RrBrakeC));
        }

        var near = s.Nearby.Take(3).ToList();
        if (near.Count > 0)
        {
            sb.Append(";near=");
            sb.Append(string.Join(',', near.Select(n =>
            {
                var side = string.IsNullOrEmpty(n.Side) ? '?' : n.Side[0];
                return $"{Short(n.Name, 10)}@{F0(n.RelativeLapDist)}{side}";
            })));
        }

        var packed = sb.ToString();
        if (packed == _lastPacked)
            return "tel=same";

        // Delta-ish: if only numeric noise changed a lot, still send full; keep last
        _lastPacked = packed;
        return packed;
    }

    private static void AppendTemp(StringBuilder sb, string key, double value, double threshold)
    {
        if (value >= threshold)
            sb.Append(';').Append(key).Append(F0(value));
    }

    private static string Short(string s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return "-";
        s = s.Trim();
        return s.Length <= n ? s : s[..n];
    }

    private static string F0(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
}
