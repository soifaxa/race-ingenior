using RaceIngenior.Models;
using RaceIngenior.Services;

namespace RaceIngenior.ViewModels;

/// <summary>Display model for the in-app WEC session report board.</summary>
public sealed class SessionReportViewModel
{
    public string HeaderTrack { get; }
    public string HeaderCar { get; }
    public string HeaderMeta { get; }
    public string ReportId { get; }

    public string Track { get; }
    public string Vehicle { get; }
    public string VehicleClass { get; }
    public string Driver { get; }
    public string SessionType { get; }
    public string Duration { get; }
    public string Laps { get; }
    public string BestLap { get; }
    public string LastLap { get; }
    public string Place { get; }
    public string Ended { get; }

    public string Compound { get; }
    public string Warmup { get; }
    public string WarmupNotes { get; }
    public string WearStart { get; }
    public string WearEnd { get; }
    public string CarcassMin { get; }
    public string CarcassMax { get; }
    public string Puncture { get; }

    public string Fuel { get; }
    public string VirtualEnergy { get; }

    public string TrackTemps { get; }
    public string AirTemps { get; }
    public string Weather { get; }
    public string SuggestedCompound { get; }

    public string FlagFcy { get; }
    public string FlagLocal { get; }
    public string FlagBlue { get; }

    public SessionReportViewModel(SessionReport r)
    {
        HeaderTrack = string.IsNullOrWhiteSpace(r.Track) ? "—" : r.Track.ToUpperInvariant();
        HeaderCar = BrandResolver.FormatClassAndBrand(
            r.VehFilename, vehicleName: r.Vehicle, vehicleClass: r.VehicleClass);
        HeaderMeta = $"{r.SessionType.ToUpperInvariant()}  ·  {r.LapsCompleted} LAPS  ·  {FormatDuration(r.DurationSec)}";
        ReportId = r.ReportId;

        Track = Dash(r.Track);
        Vehicle = HeaderCar;
        VehicleClass = Dash(r.VehicleClass);
        Driver = Dash(r.Driver);
        SessionType = $"{r.SessionType}  ({r.SessionCode})";
        Duration = FormatDuration(r.DurationSec);
        Laps = r.LapsCompleted.ToString();
        BestLap = FormatLap(r.BestLapSec);
        LastLap = FormatLap(r.LastLapSec);
        Place = $"BEST P{(r.BestPlace > 0 ? r.BestPlace : "—")}   FINAL P{(r.FinalPlace > 0 ? r.FinalPlace : "—")}";
        Ended = Dash(r.EndReason);

        Compound = $"F {Dash(r.FrontCompound)}   ·   R {Dash(r.RearCompound)}";
        Warmup = r.WarmupSeconds is double ws
            ? $"{FormatDuration(ws)}  /  {r.WarmupLaps ?? 0} laps  (ready {r.WarmupReadyCarcassC:0}°C)"
            : "—";
        WarmupNotes = Dash(r.WarmupNotes);
        WearStart = WearLine(r.WearStart);
        WearEnd = WearLine(r.WearEnd);
        CarcassMin = TempLine(r.CarcassMinC);
        CarcassMax = TempLine(r.CarcassMaxC);
        Puncture = r.HadPuncture ? "YES" : "NO";

        Fuel = $"{r.FuelStartL:0.0} L  →  {r.FuelEndL:0.0} L    used {r.FuelUsedL:0.0} L  /  {r.FuelCapacityL:0.0} L";
        if (r.VirtualEnergyStart is double || r.VirtualEnergyEnd is double)
        {
            VirtualEnergy =
                $"{FmtVe(r.VirtualEnergyStart)}  →  {FmtVe(r.VirtualEnergyEnd)}    used {FmtVe(r.VirtualEnergyUsed)}";
        }
        else if (IsHyper(r.VehicleClass))
            VirtualEnergy = "UNAVAILABLE (LMU REST)";
        else
            VirtualEnergy = "N/A (NON-HYPERCAR)";

        TrackTemps =
            $"START {r.TrackTempStartC:0.0}°  END {r.TrackTempEndC:0.0}°  MIN {r.TrackTempMinC:0.0}°  MAX {r.TrackTempMaxC:0.0}°  AVG {r.TrackTempAvgC:0.0}°";
        AirTemps =
            $"START {r.AmbientTempStartC:0.0}°  END {r.AmbientTempEndC:0.0}°  MIN {r.AmbientTempMinC:0.0}°  MAX {r.AmbientTempMaxC:0.0}°  AVG {r.AmbientTempAvgC:0.0}°";
        Weather = $"RAIN {r.RainEnd * 100:0}%   ·   PATH WETNESS {r.AvgWetnessEnd * 100:0}%";
        SuggestedCompound = string.IsNullOrWhiteSpace(r.RecommendedCompoundEnd)
            ? "—"
            : r.RecommendedCompoundEnd.ToUpperInvariant();

        FlagFcy = r.FullCourseYellowCount.ToString();
        FlagLocal = r.LocalYellowCount.ToString();
        FlagBlue = r.BlueFlagCount.ToString();
    }

    private static bool IsHyper(string? c) =>
        !string.IsNullOrWhiteSpace(c) && c.Contains("Hyper", StringComparison.OrdinalIgnoreCase);

    private static string Dash(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    private static string FmtVe(double? v) =>
        v is double d ? $"{d:0.0} %" : "—";

    private static string FormatDuration(double sec)
    {
        if (sec < 0) sec = 0;
        var ts = TimeSpan.FromSeconds(sec);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string FormatLap(double sec) => LapTimeFormat.Format(sec);

    private static string WearLine(WheelWearSnapshot w) =>
        $"FL {w.Fl * 100:0}%  FR {w.Fr * 100:0}%  RL {w.Rl * 100:0}%  RR {w.Rr * 100:0}%";

    private static string TempLine(WheelTempSnapshot t) =>
        $"FL {t.Fl:0}°  FR {t.Fr:0}°  RL {t.Rl:0}°  RR {t.Rr:0}°";
}
