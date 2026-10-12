using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Telemetry;

namespace RaceIngenior.Services;

/// <summary>
/// Accumulates live-session stats and writes a Markdown + JSON report when the session ends.
/// </summary>
public sealed class SessionReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettings _settings;
    private readonly TrackRecordsStore _trackRecords;
    private readonly ILogger<SessionReportService> _logger;
    private readonly object _sync = new();

    private bool _active;
    private bool _waitingForFinishLine;
    private byte _lastPhase = 255;
    private int _lapAtSessionOver;
    private double _lapDistAtSessionOver;
    private double _prevLapDistFraction = -1;
    private SessionAccumulator? _acc;

    public event Action<string, SessionReport>? ReportSaved;

    public SessionReportService(
        TelemetryHub hub,
        TrackRecordsStore trackRecords,
        IOptions<AppSettings> settings,
        ILogger<SessionReportService> logger)
    {
        _settings = settings.Value;
        _trackRecords = trackRecords;
        _logger = logger;
        hub.Updated += OnTelemetry;
    }

    private void OnTelemetry(TelemetrySnapshot snap)
    {
        lock (_sync)
        {
            // Start only when entering a live on-track phase (not merely driving into pits).
            if (!_active && snap.IsInLiveSession)
            {
                StartSession(snap);
                _lastPhase = snap.GamePhase;
                return;
            }

            if (!_active)
            {
                _lastPhase = snap.GamePhase;
                return;
            }

            // End ONLY on GamePhase 8 (Session over) — not pit entry, pause, or phase 7 stop.
            // If still on track, defer the report until the finish line (or pits / DNF / disconnect).
            if (!_waitingForFinishLine && snap.GamePhase == 8 && _lastPhase != 8)
            {
                if (ShouldWaitForFinishLine(snap))
                {
                    _waitingForFinishLine = true;
                    _lapAtSessionOver = snap.Lap;
                    _lapDistAtSessionOver = snap.LapDistFraction;
                    _prevLapDistFraction = snap.LapDistFraction;
                    _logger.LogInformation(
                        "Session over while on track — waiting for finish line before report");
                    if (snap.Connected)
                        Sample(snap);
                    _lastPhase = snap.GamePhase;
                    return;
                }

                EndSession(snap, "Session over");
                _lastPhase = snap.GamePhase;
                return;
            }

            if (_waitingForFinishLine)
            {
                if (snap.Connected)
                    Sample(snap);

                if (HasCrossedFinishOrAborted(snap))
                {
                    EndSession(snap, "Session over");
                    _lastPhase = snap.GamePhase;
                    return;
                }

                _prevLapDistFraction = snap.LapDistFraction;
                _lastPhase = snap.GamePhase;
                return;
            }

            // Keep sampling through pits, garage, pause (9), and session stopped (7).
            if (snap.Connected)
                Sample(snap);
            _lastPhase = snap.GamePhase;
        }
    }

    /// <summary>On track, not yet finished — keep driving to the chequered / start-finish.</summary>
    private static bool ShouldWaitForFinishLine(TelemetrySnapshot snap) =>
        snap.Connected
        && snap.InRealtime
        && !snap.InPitsOrGarage
        && snap.FinishStatus == 0;

    private bool HasCrossedFinishOrAborted(TelemetrySnapshot snap)
    {
        // Official finish / DNF / DQ from scoring.
        if (snap.FinishStatus != 0)
            return true;

        // Left the track (pits, garage) or left the sim / realtime.
        if (!snap.Connected || !snap.InRealtime || snap.InPitsOrGarage)
            return true;

        // Dropped out of session-over into garage / menus.
        if (snap.GamePhase is 0 or 9)
            return true;

        // Lap completed after session over (finish status sometimes lags).
        if (snap.Lap > _lapAtSessionOver)
            return true;

        // Lap-distance wrap past the start/finish line.
        if (_prevLapDistFraction >= 0
            && _prevLapDistFraction > 0.80
            && snap.LapDistFraction < 0.20)
            return true;

        // Progressed past the start from wherever we were when phase 8 hit.
        if (_lapDistAtSessionOver > 0.05
            && snap.LapDistFraction + 0.02 < _lapDistAtSessionOver
            && snap.LapDistFraction < 0.15)
            return true;

        return false;
    }

    private void StartSession(TelemetrySnapshot snap)
    {
        _active = true;
        _acc = new SessionAccumulator
        {
            StartedUtc = DateTimeOffset.UtcNow,
            Track = snap.Track,
            Vehicle = snap.Vehicle,
            VehicleClass = snap.VehicleClass,
            Driver = snap.Driver,
            SessionCode = snap.Session,
            SessionType = DescribeSession(snap.Session),
            StartLap = snap.Lap,
            BestPlace = snap.Place > 0 ? snap.Place : int.MaxValue,
            FuelStartL = snap.FuelL,
            FuelCapacityL = snap.FuelCapacityL,
            VirtualEnergyStart = snap.VirtualEnergy,
            FrontCompound = snap.FrontTireCompound,
            RearCompound = snap.RearTireCompound,
            WearStart = WearOf(snap.Wheels),
            CarcassMax = TempsOf(snap.Wheels),
            CarcassMin = TempsOf(snap.Wheels),
            WarmReadyC = _settings.TireWarmReadyC,
            TrackTempStartC = snap.TrackTempC,
            TrackTempEndC = snap.TrackTempC,
            TrackTempMinC = snap.TrackTempC,
            TrackTempMaxC = snap.TrackTempC,
            TrackTempSum = snap.TrackTempC,
            AmbientTempStartC = snap.AmbientTempC,
            AmbientTempEndC = snap.AmbientTempC,
            AmbientTempMinC = snap.AmbientTempC,
            AmbientTempMaxC = snap.AmbientTempC,
            AmbientTempSum = snap.AmbientTempC,
            TempSampleCount = 1
        };

        if (!snap.InPitBox && !snap.InGarage)
            MaybeStartWarmup(_acc, snap);

        _logger.LogInformation("Session report started · {Track} · {Car}", snap.Track, snap.Vehicle);
    }

    private void Sample(TelemetrySnapshot snap)
    {
        var a = _acc;
        if (a is null)
            return;

        if (!string.IsNullOrWhiteSpace(snap.Track))
            a.Track = snap.Track;
        if (!string.IsNullOrWhiteSpace(snap.Vehicle))
            a.Vehicle = snap.Vehicle;
        if (!string.IsNullOrWhiteSpace(snap.VehicleClass))
            a.VehicleClass = snap.VehicleClass;
        if (!string.IsNullOrWhiteSpace(snap.Driver))
            a.Driver = snap.Driver;
        if (!string.IsNullOrWhiteSpace(snap.FrontTireCompound))
            a.FrontCompound = snap.FrontTireCompound;
        if (!string.IsNullOrWhiteSpace(snap.RearTireCompound))
            a.RearCompound = snap.RearTireCompound;

        a.FinalPlace = snap.Place;
        if (snap.Place > 0 && snap.Place < a.BestPlace)
            a.BestPlace = snap.Place;

        a.LastLap = snap.Lap;
        if (snap.BestLap > 0 && (a.BestLapSec <= 0 || snap.BestLap < a.BestLapSec))
            a.BestLapSec = snap.BestLap;
        if (snap.LastLap > 0)
            a.LastLapSec = snap.LastLap;

        a.FuelEndL = snap.FuelL;
        a.FuelCapacityL = snap.FuelCapacityL > 0 ? snap.FuelCapacityL : a.FuelCapacityL;
        // VE often arrives a few polls after session start (REST). Capture first seen as start.
        if (snap.VirtualEnergy is double ve)
        {
            a.VirtualEnergyStart ??= ve;
            a.VirtualEnergyEnd = ve;
        }

        UpdateWearEnd(a, snap.Wheels);
        UpdateCarcassExtremes(a, snap.Wheels);

        if (snap.Wheels.AnyFlat)
            a.HadPuncture = true;

        // Flag edge counts.
        if (snap.GamePhase == 6 && _lastPhase != 6)
            a.FullCourseYellowCount++;
        if (snap.IsBlueFlag && !a.PrevBlue)
            a.BlueFlagCount++;
        a.PrevBlue = snap.IsBlueFlag;
        var local = snap.HasLocalYellow;
        if (local && !a.PrevLocalYellow)
            a.LocalYellowCount++;
        a.PrevLocalYellow = local;

        a.TrackTempEndC = snap.TrackTempC;
        a.AmbientTempEndC = snap.AmbientTempC;
        a.TrackTempMinC = Math.Min(a.TrackTempMinC, snap.TrackTempC);
        a.TrackTempMaxC = Math.Max(a.TrackTempMaxC, snap.TrackTempC);
        a.AmbientTempMinC = Math.Min(a.AmbientTempMinC, snap.AmbientTempC);
        a.AmbientTempMaxC = Math.Max(a.AmbientTempMaxC, snap.AmbientTempC);
        a.TrackTempSum += snap.TrackTempC;
        a.AmbientTempSum += snap.AmbientTempC;
        a.TempSampleCount++;
        a.RainEnd = snap.Raining;
        a.AvgWetnessEnd = snap.AvgPathWetness;

        // Warmup: start when leaving pits cold; complete when ready.
        if ((snap.InPitBox || snap.InGarage) && a.WarmupCompletedAt is null)
        {
            a.WarmupStartedAt = null;
            a.WarmupStartLap = null;
        }
        else if (!snap.InPitBox && !snap.InGarage)
        {
            MaybeStartWarmup(a, snap);
            MaybeCompleteWarmup(a, snap);
        }
    }

    private void MaybeStartWarmup(SessionAccumulator a, TelemetrySnapshot snap)
    {
        if (a.WarmupCompletedAt is not null || a.WarmupStartedAt is not null)
            return;

        if (!TryMinCarcass(snap.Wheels, out var min) || min >= a.WarmReadyC)
            return;

        a.WarmupStartedAt = DateTimeOffset.UtcNow;
        a.WarmupStartLap = snap.Lap;
    }

    private void MaybeCompleteWarmup(SessionAccumulator a, TelemetrySnapshot snap)
    {
        if (a.WarmupCompletedAt is not null || a.WarmupStartedAt is null)
            return;

        if (!TryMinCarcass(snap.Wheels, out var min) || min < a.WarmReadyC)
            return;

        a.WarmupCompletedAt = DateTimeOffset.UtcNow;
        a.WarmupReadyCarcassC = min;
        a.WarmupEndLap = snap.Lap;
    }

    private void EndSession(TelemetrySnapshot snap, string reason)
    {
        if (_acc is null)
        {
            _active = false;
            return;
        }

        // One last sample while accumulator is still attached.
        if (snap.Connected)
            Sample(snap);

        var a = _acc;
        _active = false;
        _waitingForFinishLine = false;
        _prevLapDistFraction = -1;
        _acc = null;

        var report = BuildReport(a, reason);
        _trackRecords.ConsiderReport(report);
        var paths = Save(report);
        if (paths.Count > 0)
        {
            _logger.LogInformation("Session report saved · {Id} → {Path}", report.ReportId, paths[0]);
            ReportSaved?.Invoke(paths[0], report);
        }
    }

    private SessionReport BuildReport(SessionAccumulator a, string reason)
    {
        var ended = DateTimeOffset.UtcNow;
        var duration = Math.Max(0, (ended - a.StartedUtc).TotalSeconds);
        var laps = Math.Max(0, a.LastLap - a.StartLap);

        double? warmupSec = null;
        int? warmupLaps = null;
        var warmupNotes = "Tyres were already warm, or warmup not completed.";
        if (a.WarmupStartedAt is DateTimeOffset ws && a.WarmupCompletedAt is DateTimeOffset we)
        {
            warmupSec = Math.Max(0, (we - ws).TotalSeconds);
            warmupLaps = Math.Max(0, (a.WarmupEndLap ?? a.LastLap) - (a.WarmupStartLap ?? a.StartLap));
            warmupNotes = $"Reached {a.WarmReadyC:0}°C carcass ready threshold.";
        }
        else if (a.WarmupStartedAt is not null)
        {
            warmupNotes = "Warmup started but ready temperature was not reached before session end.";
        }

        var fuelUsed = Math.Max(0, a.FuelStartL - a.FuelEndL);
        double? veUsed = null;
        if (a.VirtualEnergyStart is double vs && a.VirtualEnergyEnd is double ve)
            veUsed = Math.Max(0, vs - ve);

        var advice = TireCompoundAdvisor.Evaluate(new TelemetrySnapshot
        {
            Connected = true,
            TrackTempC = a.TrackTempEndC,
            AmbientTempC = a.AmbientTempEndC,
            Raining = a.RainEnd,
            AvgPathWetness = a.AvgWetnessEnd,
            MaxPathWetness = a.AvgWetnessEnd,
            FrontTireCompound = a.FrontCompound,
            RearTireCompound = a.RearCompound
        });

        return new SessionReport
        {
            ReportId = $"{a.StartedUtc:yyyyMMdd-HHmmss}-{Sanitize(a.Track)}",
            StartedUtc = a.StartedUtc,
            EndedUtc = ended,
            EndReason = reason,
            Track = a.Track,
            Vehicle = a.Vehicle,
            VehicleClass = a.VehicleClass,
            Driver = a.Driver,
            SessionType = a.SessionType,
            SessionCode = a.SessionCode,
            LapsCompleted = laps,
            BestPlace = a.BestPlace == int.MaxValue ? 0 : a.BestPlace,
            FinalPlace = a.FinalPlace,
            BestLapSec = a.BestLapSec,
            LastLapSec = a.LastLapSec,
            DurationSec = duration,
            FrontCompound = a.FrontCompound,
            RearCompound = a.RearCompound,
            WarmupSeconds = warmupSec,
            WarmupLaps = warmupLaps,
            WarmupReadyCarcassC = a.WarmupReadyCarcassC,
            WarmupNotes = warmupNotes,
            FuelStartL = a.FuelStartL,
            FuelEndL = a.FuelEndL,
            FuelUsedL = fuelUsed,
            FuelCapacityL = a.FuelCapacityL,
            VirtualEnergyStart = a.VirtualEnergyStart,
            VirtualEnergyEnd = a.VirtualEnergyEnd,
            VirtualEnergyUsed = veUsed,
            WearStart = a.WearStart,
            WearEnd = a.WearEnd,
            CarcassMaxC = a.CarcassMax,
            CarcassMinC = a.CarcassMin,
            HadPuncture = a.HadPuncture,
            FullCourseYellowCount = a.FullCourseYellowCount,
            BlueFlagCount = a.BlueFlagCount,
            LocalYellowCount = a.LocalYellowCount,
            TrackTempStartC = a.TrackTempStartC,
            TrackTempEndC = a.TrackTempEndC,
            TrackTempMinC = a.TrackTempMinC,
            TrackTempMaxC = a.TrackTempMaxC,
            TrackTempAvgC = a.TempSampleCount > 0 ? a.TrackTempSum / a.TempSampleCount : a.TrackTempEndC,
            AmbientTempStartC = a.AmbientTempStartC,
            AmbientTempEndC = a.AmbientTempEndC,
            AmbientTempMinC = a.AmbientTempMinC,
            AmbientTempMaxC = a.AmbientTempMaxC,
            AmbientTempAvgC = a.TempSampleCount > 0 ? a.AmbientTempSum / a.TempSampleCount : a.AmbientTempEndC,
            RainEnd = a.RainEnd,
            AvgWetnessEnd = a.AvgWetnessEnd,
            RecommendedCompoundEnd = advice.Recommended
        };
    }

    private List<string> Save(SessionReport report)
    {
        var saved = new List<string>();
        var baseName = report.ReportId;
        var md = RenderMarkdown(report);
        var json = JsonSerializer.Serialize(report, JsonOptions);

        foreach (var dir in ReportDirectories())
        {
            try
            {
                Directory.CreateDirectory(dir);
                var mdPath = Path.Combine(dir, baseName + ".md");
                var jsonPath = Path.Combine(dir, baseName + ".json");
                File.WriteAllText(mdPath, md, Encoding.UTF8);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);
                saved.Add(mdPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save session report to {Dir}", dir);
            }
        }

        return saved;
    }

    /// <summary>
    /// Lists saved reports across LocalAppData, app output, and repo Reports folders.
    /// Dedupes by ReportId (keeps newest file write).
    /// </summary>
    public IReadOnlyList<SessionReportSummary> ListReports()
    {
        var byId = new Dictionary<string, (SessionReportSummary Summary, DateTime WriteUtc)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var dir in ReportDirectories())
        {
            if (!Directory.Exists(dir))
                continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.json");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not list reports in {Dir}", dir);
                continue;
            }

            foreach (var path in files)
            {
                try
                {
                    var writeUtc = File.GetLastWriteTimeUtc(path);
                    var report = TryLoadReport(path);
                    if (report is null)
                        continue;

                    var id = string.IsNullOrWhiteSpace(report.ReportId)
                        ? Path.GetFileNameWithoutExtension(path)
                        : report.ReportId;

                    var summary = new SessionReportSummary
                    {
                        ReportId = id,
                        JsonPath = path,
                        Track = report.Track,
                        Vehicle = report.Vehicle,
                        VehicleClass = report.VehicleClass,
                        SessionType = report.SessionType,
                        StartedUtc = report.StartedUtc,
                        LapsCompleted = report.LapsCompleted,
                        BestLapSec = report.BestLapSec,
                        FinalPlace = report.FinalPlace
                    };

                    if (!byId.TryGetValue(id, out var existing) || writeUtc >= existing.WriteUtc)
                        byId[id] = (summary, writeUtc);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping report file {Path}", path);
                }
            }
        }

        return byId.Values
            .Select(v => v.Summary)
            .OrderByDescending(s => s.StartedUtc)
            .ToList();
    }

    public SessionReport? TryLoadReport(string jsonPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                return null;

            var json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<SessionReport>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load session report {Path}", jsonPath);
            return null;
        }
    }

    public static string RenderMarkdown(SessionReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Session report — {r.StartedUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("## Session");
        sb.AppendLine($"- **Track:** {OrDash(r.Track)}");
        sb.AppendLine($"- **Car:** {OrDash(r.Vehicle)} ({OrDash(r.VehicleClass)})");
        sb.AppendLine($"- **Driver:** {OrDash(r.Driver)}");
        sb.AppendLine($"- **Type:** {r.SessionType} (code {r.SessionCode})");
        sb.AppendLine($"- **Duration:** {FormatDuration(r.DurationSec)}");
        sb.AppendLine($"- **Laps:** {r.LapsCompleted}");
        sb.AppendLine($"- **Best lap:** {FormatLap(r.BestLapSec)}");
        sb.AppendLine($"- **Last lap:** {FormatLap(r.LastLapSec)}");
        sb.AppendLine($"- **Place:** best P{(r.BestPlace > 0 ? r.BestPlace : "—")} · final P{(r.FinalPlace > 0 ? r.FinalPlace : "—")}");
        sb.AppendLine($"- **Ended:** {r.EndReason}");
        sb.AppendLine();
        sb.AppendLine("## Tyres");
        sb.AppendLine($"- **Compound:** F {OrDash(r.FrontCompound)} · R {OrDash(r.RearCompound)}");
        sb.AppendLine($"- **Warmup:** {(r.WarmupSeconds is double ws ? $"{FormatDuration(ws)} / {r.WarmupLaps ?? 0} laps (ready carcass {r.WarmupReadyCarcassC:0}°C)" : "n/a")}");
        sb.AppendLine($"- **Warmup notes:** {r.WarmupNotes}");
        sb.AppendLine($"- **Wear start (remaining %):** {WearLine(r.WearStart)}");
        sb.AppendLine($"- **Wear end (remaining %):** {WearLine(r.WearEnd)}");
        sb.AppendLine($"- **Carcass min °C:** {TempLine(r.CarcassMinC)}");
        sb.AppendLine($"- **Carcass max °C:** {TempLine(r.CarcassMaxC)}");
        sb.AppendLine($"- **Puncture:** {(r.HadPuncture ? "YES" : "no")}");
        sb.AppendLine();
        sb.AppendLine("## Fuel / energy");
        sb.AppendLine($"- **Fuel:** {r.FuelStartL:0.0} L → {r.FuelEndL:0.0} L (used {r.FuelUsedL:0.0} L / capacity {r.FuelCapacityL:0.0} L)");
        if (r.VirtualEnergyStart is double || r.VirtualEnergyEnd is double)
        {
            sb.AppendLine(
                $"- **Virtual energy:** {FmtVe(r.VirtualEnergyStart)} → {FmtVe(r.VirtualEnergyEnd)} (used {FmtVe(r.VirtualEnergyUsed)})");
        }
        else if (IsHypercarClass(r.VehicleClass))
        {
            sb.AppendLine("- **Virtual energy:** unavailable (LMU REST / RepairAndRefuel)");
        }
        else
        {
            sb.AppendLine("- **Virtual energy:** n/a (non-Hypercar)");
        }

        sb.AppendLine();
        sb.AppendLine("## Temperatures (track + air)");
        sb.AppendLine($"- **Track °C:** start {r.TrackTempStartC:0.0} · end {r.TrackTempEndC:0.0} · min {r.TrackTempMinC:0.0} · max {r.TrackTempMaxC:0.0} · avg {r.TrackTempAvgC:0.0}");
        sb.AppendLine($"- **Air °C:** start {r.AmbientTempStartC:0.0} · end {r.AmbientTempEndC:0.0} · min {r.AmbientTempMinC:0.0} · max {r.AmbientTempMaxC:0.0} · avg {r.AmbientTempAvgC:0.0}");
        sb.AppendLine();
        sb.AppendLine("## Weather (end of session)");
        sb.AppendLine($"- **Rain / wetness:** {r.RainEnd * 100:0}% / avg {r.AvgWetnessEnd * 100:0}%");
        sb.AppendLine($"- **Suggested compound:** {r.RecommendedCompoundEnd}");
        sb.AppendLine();
        sb.AppendLine("## Flags");
        sb.AppendLine($"- **FCY:** {r.FullCourseYellowCount}");
        sb.AppendLine($"- **Local yellow:** {r.LocalYellowCount}");
        sb.AppendLine($"- **Blue:** {r.BlueFlagCount}");
        sb.AppendLine();
        sb.AppendLine($"_Generated by Race Ingenior · {r.ReportId}_");
        return sb.ToString();
    }

    private static IEnumerable<string> ReportDirectories()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaceIngenior",
            "Reports");
        yield return Path.Combine(AppContext.BaseDirectory, "Reports");

        var repo = FindRepoReportsDirectory();
        if (repo is not null)
            yield return repo;
    }

    private static string? FindRepoReportsDirectory()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var marker = Path.Combine(dir.FullName, "src", "RaceIngenior", "RaceIngenior.csproj");
                if (File.Exists(marker))
                    return Path.Combine(dir.FullName, "Reports");
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }


    private static string DescribeSession(int code) => code switch
    {
        0 => "Test day",
        >= 1 and <= 4 => "Practice",
        >= 5 and <= 8 => "Qualifying",
        9 => "Warmup",
        >= 10 and <= 13 => "Race",
        _ => $"Session {code}"
    };

    private static WheelWearSnapshot WearOf(WheelTemps w) => new()
    {
        Fl = w.FlWear,
        Fr = w.FrWear,
        Rl = w.RlWear,
        Rr = w.RrWear
    };

    private static WheelTempSnapshot TempsOf(WheelTemps w) => new()
    {
        Fl = w.FlCarcassC,
        Fr = w.FrCarcassC,
        Rl = w.RlCarcassC,
        Rr = w.RrCarcassC
    };

    private static void UpdateWearEnd(SessionAccumulator a, WheelTemps w)
    {
        a.WearEnd = WearOf(w);
    }

    private static void UpdateCarcassExtremes(SessionAccumulator a, WheelTemps w)
    {
        a.CarcassMax.Fl = Math.Max(a.CarcassMax.Fl, w.FlCarcassC);
        a.CarcassMax.Fr = Math.Max(a.CarcassMax.Fr, w.FrCarcassC);
        a.CarcassMax.Rl = Math.Max(a.CarcassMax.Rl, w.RlCarcassC);
        a.CarcassMax.Rr = Math.Max(a.CarcassMax.Rr, w.RrCarcassC);

        a.CarcassMin.Fl = MinTemp(a.CarcassMin.Fl, w.FlCarcassC);
        a.CarcassMin.Fr = MinTemp(a.CarcassMin.Fr, w.FrCarcassC);
        a.CarcassMin.Rl = MinTemp(a.CarcassMin.Rl, w.RlCarcassC);
        a.CarcassMin.Rr = MinTemp(a.CarcassMin.Rr, w.RrCarcassC);
    }

    private static double MinTemp(double current, double next) =>
        current <= 0 ? next : Math.Min(current, next);

    private static bool TryMinCarcass(WheelTemps w, out double min)
    {
        var temps = new[] { w.FlCarcassC, w.FrCarcassC, w.RlCarcassC, w.RrCarcassC };
        var valid = temps.Where(t => !double.IsNaN(t) && t is > 15 and < 180).ToArray();
        if (valid.Length < 4)
        {
            min = 0;
            return false;
        }

        min = valid.Min();
        return true;
    }

    private static string WearLine(WheelWearSnapshot w) =>
        $"FL {w.Fl * 100:0}%  FR {w.Fr * 100:0}%  RL {w.Rl * 100:0}%  RR {w.Rr * 100:0}%";

    private static string TempLine(WheelTempSnapshot t) =>
        $"FL {t.Fl:0}  FR {t.Fr:0}  RL {t.Rl:0}  RR {t.Rr:0}";

    private static string FormatDuration(double sec)
    {
        if (sec < 0 || double.IsNaN(sec))
            return "—";
        var ts = TimeSpan.FromSeconds(sec);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string FormatLap(double sec) => LapTimeFormat.Format(sec);

    private static string FmtOpt(double? v) =>
        v is double d ? d.ToString("0.0") : "—";

    private static string FmtVe(double? v) =>
        v is double d ? $"{d:0.0} %" : "—";

    private static bool IsHypercarClass(string? vehicleClass) =>
        !string.IsNullOrWhiteSpace(vehicleClass)
        && vehicleClass.Contains("Hyper", StringComparison.OrdinalIgnoreCase);

    private static string OrDash(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    private static string Sanitize(string track)
    {
        if (string.IsNullOrWhiteSpace(track))
            return "track";
        var chars = track.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var s = new string(chars);
        while (s.Contains("--", StringComparison.Ordinal))
            s = s.Replace("--", "-", StringComparison.Ordinal);
        return s.Trim('-');
    }

    private sealed class SessionAccumulator
    {
        public DateTimeOffset StartedUtc { get; set; }
        public string Track { get; set; } = "";
        public string Vehicle { get; set; } = "";
        public string VehicleClass { get; set; } = "";
        public string Driver { get; set; } = "";
        public string SessionType { get; set; } = "";
        public int SessionCode { get; set; }
        public int StartLap { get; set; }
        public int LastLap { get; set; }
        public int BestPlace { get; set; } = int.MaxValue;
        public int FinalPlace { get; set; }
        public double BestLapSec { get; set; }
        public double LastLapSec { get; set; }
        public string FrontCompound { get; set; } = "";
        public string RearCompound { get; set; } = "";
        public double FuelStartL { get; set; }
        public double FuelEndL { get; set; }
        public double FuelCapacityL { get; set; }
        public double? VirtualEnergyStart { get; set; }
        public double? VirtualEnergyEnd { get; set; }
        public WheelWearSnapshot WearStart { get; set; } = new();
        public WheelWearSnapshot WearEnd { get; set; } = new();
        public WheelTempSnapshot CarcassMax { get; set; } = new();
        public WheelTempSnapshot CarcassMin { get; set; } = new();
        public bool HadPuncture { get; set; }
        public int FullCourseYellowCount { get; set; }
        public int BlueFlagCount { get; set; }
        public int LocalYellowCount { get; set; }
        public bool PrevBlue { get; set; }
        public bool PrevLocalYellow { get; set; }
        public double TrackTempStartC { get; set; }
        public double TrackTempEndC { get; set; }
        public double TrackTempMinC { get; set; }
        public double TrackTempMaxC { get; set; }
        public double TrackTempSum { get; set; }
        public double AmbientTempStartC { get; set; }
        public double AmbientTempEndC { get; set; }
        public double AmbientTempMinC { get; set; }
        public double AmbientTempMaxC { get; set; }
        public double AmbientTempSum { get; set; }
        public int TempSampleCount { get; set; }
        public double RainEnd { get; set; }
        public double AvgWetnessEnd { get; set; }
        public double WarmReadyC { get; set; }
        public DateTimeOffset? WarmupStartedAt { get; set; }
        public DateTimeOffset? WarmupCompletedAt { get; set; }
        public int? WarmupStartLap { get; set; }
        public int? WarmupEndLap { get; set; }
        public double? WarmupReadyCarcassC { get; set; }
    }
}
