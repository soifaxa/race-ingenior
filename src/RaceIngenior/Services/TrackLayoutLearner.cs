using Microsoft.Extensions.Logging;
using RaceIngenior.Models;
using RaceIngenior.Telemetry;

namespace RaceIngenior.Services;

/// <summary>
/// Learns a track outline from the player's world XZ while completing on-track laps.
/// </summary>
public sealed class TrackLayoutLearner
{
    private const double MinSampleSpacingM = 8.0;
    private const double MinSpeedKmh = 40.0;
    private const int MinPointsToCommit = 40;
    private const double MinCoverage = 0.85;

    private readonly TrackLayoutStore _store;
    private readonly ILogger<TrackLayoutLearner> _logger;
    private readonly object _sync = new();
    private readonly List<TrackLayoutPoint> _lapBuffer = [];

    private string _trackKey = "";
    private string _displayName = "";
    private double _lastFrac = -1;
    private double _lastX;
    private double _lastZ;
    private bool _hasLastSample;

    public string Status { get; private set; } = "Idle";
    public event Action? StatusChanged;

    public TrackLayoutLearner(
        TelemetryHub hub,
        TrackLayoutStore store,
        ILogger<TrackLayoutLearner> logger)
    {
        _store = store;
        _logger = logger;
        hub.Updated += Observe;
    }

    private void Observe(TelemetrySnapshot snap)
    {
        if (!snap.Connected || string.IsNullOrWhiteSpace(snap.Track))
        {
            SetStatus("Idle");
            return;
        }

        var key = TrackLayoutStore.NormalizeKey(snap.Track);
        lock (_sync)
        {
            if (key != _trackKey)
            {
                _trackKey = key;
                _displayName = snap.Track.Trim();
                _lapBuffer.Clear();
                _lastFrac = -1;
                _hasLastSample = false;
            }
        }

        if (_store.HasLayout(snap.Track))
        {
            SetStatus("Layout ready");
            return;
        }

        if (!snap.IsInLiveSession || snap.InPitBox || snap.InGarage)
        {
            SetStatus("No layout yet — drive on track to learn (dev)");
            return;
        }

        if (snap.SpeedKmh < MinSpeedKmh)
        {
            SetStatus("Learning… drive faster to sample the outline");
            return;
        }

        var frac = snap.LapDistFraction;
        if (double.IsNaN(frac) || double.IsInfinity(frac))
            return;

        frac %= 1.0;
        if (frac < 0) frac += 1.0;

        var x = snap.PlayerWorldX;
        var z = snap.PlayerWorldZ;

        lock (_sync)
        {
            // Lap wrap → try to commit the previous lap buffer.
            if (_lastFrac >= 0 && _lastFrac > 0.80 && frac < 0.20 && _lapBuffer.Count >= MinPointsToCommit)
            {
                TryCommitUnlocked();
                _lapBuffer.Clear();
                _hasLastSample = false;
            }

            if (_hasLastSample)
            {
                var dx = x - _lastX;
                var dz = z - _lastZ;
                if (Math.Sqrt(dx * dx + dz * dz) < MinSampleSpacingM)
                {
                    _lastFrac = frac;
                    SetStatusUnlocked();
                    return;
                }
            }

            _lapBuffer.Add(new TrackLayoutPoint { T = frac, X = x, Z = z });
            _lastFrac = frac;
            _lastX = x;
            _lastZ = z;
            _hasLastSample = true;
            SetStatusUnlocked();
        }
    }

    private void TryCommitUnlocked()
    {
        var points = _lapBuffer
            .OrderBy(p => p.T)
            .ToList();

        // Deduplicate near-identical T.
        var cleaned = new List<TrackLayoutPoint>();
        foreach (var p in points)
        {
            if (cleaned.Count == 0 || Math.Abs(p.T - cleaned[^1].T) > 0.002)
                cleaned.Add(p);
        }

        if (cleaned.Count < MinPointsToCommit)
            return;

        var coverage = cleaned[^1].T - cleaned[0].T;
        if (coverage < MinCoverage && cleaned[0].T > 0.05)
            return;

        // Prefer denser / more complete layouts.
        var existing = _store.TryGet(_trackKey);
        if (existing is not null && existing.Points.Count >= cleaned.Count)
            return;

        var layout = new TrackLayout
        {
            TrackKey = _trackKey,
            DisplayName = _displayName,
            RecordedUtc = DateTimeOffset.UtcNow,
            SampleCount = cleaned.Count,
            Points = cleaned
        };

        _store.Save(layout);
        _logger.LogInformation("Learned track layout {Key} with {Count} points", _trackKey, cleaned.Count);
        Status = "Layout saved — commit Tracks/ to share";
        StatusChanged?.Invoke();
    }

    private void SetStatus(string status)
    {
        if (Status == status)
            return;
        Status = status;
        StatusChanged?.Invoke();
    }

    private void SetStatusUnlocked()
    {
        if (_store.TryGet(_trackKey) is not null)
        {
            SetStatus("Layout ready");
            return;
        }

        SetStatus($"Learning… {_lapBuffer.Count} samples this lap — complete a full lap to save");
    }
}
