namespace RaceIngenior.Models;

/// <summary>One point on a learned track outline (lap fraction + world XZ).</summary>
public sealed class TrackLayoutPoint
{
    public double T { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
}

/// <summary>Learned circuit outline stored under <c>Tracks/*.json</c> (shared in the repo).</summary>
public sealed class TrackLayout
{
    public string TrackKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset RecordedUtc { get; set; }
    public int SampleCount { get; set; }
    public List<TrackLayoutPoint> Points { get; set; } = [];

    public bool HasOutline => Points.Count >= 24;
}
