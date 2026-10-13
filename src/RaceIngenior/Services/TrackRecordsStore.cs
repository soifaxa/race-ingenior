using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RaceIngenior.Models;

namespace RaceIngenior.Services;

/// <summary>
/// Personal best lap times per track + car, stored in LocalAppData.
/// </summary>
public sealed class TrackRecordsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<TrackRecordsStore> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, TrackRecord> _byPair = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    public event Action? RecordsChanged;

    public TrackRecordsStore(ILogger<TrackRecordsStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaceIngenior");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "track_records.json");
        Load();
    }

    public IReadOnlyList<TrackRecord> GetAll()
    {
        lock (_sync)
        {
            return _byPair.Values
                .OrderBy(r => r.TrackName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.BestLapSec)
                .ThenBy(r => r.VehicleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public TrackRecord? TryGet(string? trackName, string? vehicleName)
    {
        var key = PairKey(trackName, vehicleName);
        if (key.Length == 0)
            return null;

        lock (_sync)
            return _byPair.TryGetValue(key, out var r) ? Clone(r) : null;
    }

    /// <summary>Returns true if this is a new personal best (or first record).</summary>
    public bool Consider(
        string? trackName,
        string? vehicleName,
        string? vehicleClass,
        double bestLapSec,
        DateTimeOffset setAtUtc,
        string? sourceReportId = null,
        string? vehFilename = null)
    {
        if (bestLapSec <= 0 || double.IsNaN(bestLapSec) || double.IsInfinity(bestLapSec))
            return false;

        var trackKey = NormalizeKey(trackName);
        var vehicleKey = NormalizeKey(vehicleName);
        if (trackKey.Length == 0 || vehicleKey.Length == 0)
            return false;

        var pair = PairKey(trackKey, vehicleKey);
        var changed = false;
        var brand = BrandResolver.ResolveDisplay(
            vehFilename, vehicleName: vehicleName, vehicleClass: vehicleClass);

        lock (_sync)
        {
            if (_byPair.TryGetValue(pair, out var existing))
            {
                if (bestLapSec + 0.0005 < existing.BestLapSec)
                {
                    existing.BestLapSec = bestLapSec;
                    existing.SetAtUtc = setAtUtc;
                    existing.SourceReportId = sourceReportId;
                    if (!string.IsNullOrWhiteSpace(trackName))
                        existing.TrackName = trackName.Trim();
                    if (!string.IsNullOrWhiteSpace(vehicleName))
                        existing.VehicleName = vehicleName.Trim();
                    if (!string.IsNullOrWhiteSpace(vehicleClass))
                        existing.VehicleClass = vehicleClass.Trim();
                    if (!string.IsNullOrWhiteSpace(brand))
                        existing.Brand = brand;
                    else if (string.IsNullOrWhiteSpace(existing.Brand))
                        existing.Brand = BrandResolver.ResolveDisplay(existing.VehicleName);
                    changed = true;
                }
                else if (string.IsNullOrWhiteSpace(existing.Brand) && !string.IsNullOrWhiteSpace(brand))
                {
                    existing.Brand = brand;
                    changed = true;
                }
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(vehicleName) ? vehicleKey : vehicleName.Trim();
                _byPair[pair] = new TrackRecord
                {
                    TrackKey = trackKey,
                    TrackName = string.IsNullOrWhiteSpace(trackName) ? trackKey : trackName.Trim(),
                    VehicleKey = vehicleKey,
                    VehicleName = name,
                    VehicleClass = vehicleClass?.Trim() ?? "",
                    Brand = brand,
                    BestLapSec = bestLapSec,
                    SetAtUtc = setAtUtc,
                    SourceReportId = sourceReportId
                };
                changed = true;
            }

            if (changed)
                SaveUnlocked();
        }

        if (changed)
            RecordsChanged?.Invoke();

        return changed;
    }

    public bool ConsiderReport(SessionReport report) =>
        Consider(
            report.Track,
            report.Vehicle,
            report.VehicleClass,
            report.BestLapSec,
            report.EndedUtc == default ? report.StartedUtc : report.EndedUtc,
            report.ReportId,
            report.VehFilename);

    public void MergeFromReports(IEnumerable<SessionReportSummary> summaries, Func<string, SessionReport?> load)
    {
        var any = false;
        foreach (var s in summaries)
        {
            var report = load(s.JsonPath);
            if (report is null)
            {
                if (Consider(s.Track, s.Vehicle, s.VehicleClass, s.BestLapSec, s.StartedUtc, s.ReportId))
                    any = true;
                continue;
            }

            if (ConsiderReport(report))
                any = true;
        }

        if (any)
            _logger.LogInformation("Track records updated from session reports ({Count} pairs)", Count);
    }

    public int Count
    {
        get
        {
            lock (_sync)
                return _byPair.Count;
        }
    }

    private void Load()
    {
        lock (_sync)
        {
            _byPair.Clear();
            if (!File.Exists(_path))
                return;

            try
            {
                var file = JsonSerializer.Deserialize<TrackRecordsFile>(File.ReadAllText(_path), JsonOptions);
                if (file?.Records is null)
                    return;

                var brandBackfill = false;
                foreach (var r in file.Records)
                {
                    if (r.BestLapSec <= 0)
                        continue;
                    if (string.IsNullOrWhiteSpace(r.TrackKey))
                        r.TrackKey = NormalizeKey(r.TrackName);
                    if (string.IsNullOrWhiteSpace(r.VehicleKey))
                        r.VehicleKey = NormalizeKey(r.VehicleName);
                    var pair = PairKey(r.TrackKey, r.VehicleKey);
                    if (pair.Length == 0)
                        continue;

                    if (string.IsNullOrWhiteSpace(r.Brand))
                    {
                        r.Brand = BrandResolver.ResolveDisplay(r.VehicleName);
                        if (!string.IsNullOrWhiteSpace(r.Brand))
                            brandBackfill = true;
                    }

                    if (!_byPair.TryGetValue(pair, out var existing) || r.BestLapSec < existing.BestLapSec)
                        _byPair[pair] = r;
                }

                if (brandBackfill)
                    SaveUnlocked();

                _logger.LogInformation("Loaded {Count} track record(s)", _byPair.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load track records from {Path}", _path);
            }
        }
    }

    private void SaveUnlocked()
    {
        try
        {
            var file = new TrackRecordsFile
            {
                Records = _byPair.Values
                    .OrderBy(r => r.TrackName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.BestLapSec)
                    .ToList()
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save track records to {Path}", _path);
        }
    }

    private static string PairKey(string? track, string? vehicle)
    {
        var t = NormalizeKey(track);
        var v = NormalizeKey(vehicle);
        return t.Length == 0 || v.Length == 0 ? "" : $"{t}|{v}";
    }

    public static string NormalizeKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var s = name.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    private static TrackRecord Clone(TrackRecord r) => new()
    {
        TrackKey = r.TrackKey,
        TrackName = r.TrackName,
        VehicleKey = r.VehicleKey,
        VehicleName = r.VehicleName,
        VehicleClass = r.VehicleClass,
        Brand = r.Brand,
        BestLapSec = r.BestLapSec,
        SetAtUtc = r.SetAtUtc,
        SourceReportId = r.SourceReportId
    };
}
