namespace RaceIngenior.Models;

/// <summary>Lightweight row for the in-app reports browser.</summary>
public sealed class SessionReportSummary
{
    public string ReportId { get; init; } = "";
    public string JsonPath { get; init; } = "";
    public string Track { get; init; } = "";
    public string Vehicle { get; init; } = "";
    public string VehicleClass { get; init; } = "";
    public string SessionType { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public int LapsCompleted { get; init; }
    public double BestLapSec { get; init; }
    public int FinalPlace { get; init; }

    public string TitleLine =>
        string.IsNullOrWhiteSpace(Track) ? ReportId : Track.ToUpperInvariant();

    public string MetaLine
    {
        get
        {
            var car = string.IsNullOrWhiteSpace(Vehicle) ? "—" : Vehicle;
            var cls = string.IsNullOrWhiteSpace(VehicleClass) ? "" : $" ({VehicleClass})";
            var best = LapTimeFormat.Format(BestLapSec);
            var place = FinalPlace > 0 ? $"P{FinalPlace}" : "—";
            return $"{StartedUtc:yyyy-MM-dd HH:mm} UTC  ·  {SessionType}  ·  {LapsCompleted} laps  ·  best {best}  ·  {place}  ·  {car}{cls}";
        }
    }
}
