namespace RaceIngenior.Models;

/// <summary>Personal best lap for one track + car combination.</summary>
public sealed class TrackRecord
{
    public string TrackKey { get; set; } = "";
    public string TrackName { get; set; } = "";
    public string VehicleKey { get; set; } = "";
    public string VehicleName { get; set; } = "";
    public string VehicleClass { get; set; } = "";
    /// <summary>Manufacturer display name (e.g. Ferrari), resolved from vehicle name.</summary>
    public string Brand { get; set; } = "";
    public double BestLapSec { get; set; }
    public DateTimeOffset SetAtUtc { get; set; }
    public string? SourceReportId { get; set; }
}

public sealed class TrackRecordsFile
{
    public List<TrackRecord> Records { get; set; } = [];
}
