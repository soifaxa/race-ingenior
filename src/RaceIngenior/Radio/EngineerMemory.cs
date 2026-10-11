using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Radio;

public sealed class RadioTurn
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Driver { get; set; } = "";
    public string Engineer { get; set; } = "";
}

/// <summary>
/// Compact session memory: recent radio + durable facts. Persisted to disk.
/// </summary>
public sealed class EngineerMemory
{
    private readonly MemorySettings _settings;
    private readonly ILogger<EngineerMemory> _logger;
    private readonly object _sync = new();
    private readonly List<RadioTurn> _turns = [];
    private readonly List<string> _facts = [];
    private string _sessionId = Guid.NewGuid().ToString("N")[..8];
    private string _track = "";
    private string _vehicle = "";

    public EngineerMemory(IOptions<AppSettings> settings, ILogger<EngineerMemory> logger)
    {
        _settings = settings.Value.Memory;
        _logger = logger;
        Load();
    }

    public int TurnCount
    {
        get { lock (_sync) return _turns.Count; }
    }

    public int FactCount
    {
        get { lock (_sync) return _facts.Count; }
    }

    public void ObserveTelemetry(TelemetrySnapshot snap)
    {
        if (!snap.Connected)
            return;

        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(snap.Track) && snap.Track != _track)
            {
                _track = snap.Track;
                UpsertFact($"piste={_track}");
            }

            if (!string.IsNullOrWhiteSpace(snap.Vehicle) && snap.Vehicle != _vehicle)
            {
                _vehicle = snap.Vehicle;
                UpsertFact($"voiture={_vehicle}");
            }

            if (snap.GamePhase == 6)
                UpsertFact("état=FCY/SC");
            else if (snap.GamePhase == 5)
                RemoveFactPrefix("état=FCY");
        }
    }

    public void RememberExchange(string driver, string engineer, TelemetrySnapshot snap)
    {
        lock (_sync)
        {
            ObserveTelemetryUnlocked(snap);

            _turns.Add(new RadioTurn
            {
                At = DateTimeOffset.UtcNow,
                Driver = Trunc(driver, 160),
                Engineer = Trunc(engineer, 160)
            });

            while (_turns.Count > _settings.MaxRecentTurns)
                _turns.RemoveAt(0);

            ExtractHeuristicFacts(driver, engineer, snap);
            SaveUnlocked();
        }
    }

    public void RememberAlert(string alert, TelemetrySnapshot snap)
    {
        lock (_sync)
        {
            ObserveTelemetryUnlocked(snap);
            UpsertFact($"alerte:{Trunc(alert, 80)}");
            SaveUnlocked();
        }
    }

    /// <summary>Token-cheap memory block for the LLM prompt.</summary>
    public string BuildPromptBlock()
    {
        lock (_sync)
        {
            if (_facts.Count == 0 && _turns.Count == 0)
                return "";

            var sb = new StringBuilder(256);
            sb.Append("MEM s=").Append(_sessionId);
            if (_facts.Count > 0)
            {
                sb.Append(" | F:");
                sb.Append(string.Join(';', _facts.Take(_settings.MaxFacts)));
            }

            if (_turns.Count > 0)
            {
                sb.Append(" | R:");
                for (var i = 0; i < _turns.Count; i++)
                {
                    var t = _turns[i];
                    if (i > 0) sb.Append(" || ");
                    sb.Append("P:").Append(CompactLine(t.Driver));
                    sb.Append(" > I:").Append(CompactLine(t.Engineer));
                }
            }

            return sb.ToString();
        }
    }

    public void ClearSession()
    {
        lock (_sync)
        {
            _turns.Clear();
            _facts.Clear();
            _sessionId = Guid.NewGuid().ToString("N")[..8];
            _track = "";
            _vehicle = "";
            SaveUnlocked();
        }
    }

    private void ObserveTelemetryUnlocked(TelemetrySnapshot snap)
    {
        if (!snap.Connected)
            return;

        if (!string.IsNullOrWhiteSpace(snap.Track) && snap.Track != _track)
        {
            _track = snap.Track;
            UpsertFact($"piste={_track}");
        }

        if (!string.IsNullOrWhiteSpace(snap.Vehicle) && snap.Vehicle != _vehicle)
        {
            _vehicle = snap.Vehicle;
            UpsertFact($"voiture={_vehicle}");
        }
    }

    private void ExtractHeuristicFacts(string driver, string engineer, TelemetrySnapshot snap)
    {
        var blob = $"{driver} {engineer}".ToLowerInvariant();

        if (blob.Contains("box") || blob.Contains("pit"))
            UpsertFact("plan=box discutée");
        if (blob.Contains("pneu") || blob.Contains("tire"))
            UpsertFact("focus=pneus");
        if (blob.Contains("fuel") || blob.Contains("carburant") || blob.Contains("essence"))
            UpsertFact("focus=carburant");
        if (blob.Contains("énergie") || blob.Contains("energie") || blob.Contains("virtual"))
            UpsertFact("focus=énergie");
        if (blob.Contains("économ") || blob.Contains("save"))
            UpsertFact("mode=économie");
        if (blob.Contains("push") || blob.Contains("attaque"))
            UpsertFact("mode=push");

        if (snap.FuelCapacityL > 0)
            UpsertFact($"fuel%={(100 * snap.FuelL / snap.FuelCapacityL):0}");
        if (snap.Place > 0)
            UpsertFact($"P={snap.Place}");
    }

    private void UpsertFact(string fact)
    {
        var key = fact.Split('=', 2)[0];
        _facts.RemoveAll(f => f.StartsWith(key + "=", StringComparison.Ordinal)
                              || f.StartsWith(key + ":", StringComparison.Ordinal)
                              || f == fact);
        _facts.Add(fact);
        while (_facts.Count > _settings.MaxFacts)
            _facts.RemoveAt(0);
    }

    private void RemoveFactPrefix(string prefix)
    {
        _facts.RemoveAll(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private string MemoryPath =>
        Path.IsPathRooted(_settings.MemoryFilePath)
            ? _settings.MemoryFilePath
            : Path.Combine(AppContext.BaseDirectory, _settings.MemoryFilePath);

    private void Load()
    {
        try
        {
            var path = MemoryPath;
            if (!File.Exists(path))
                return;

            var dto = JsonSerializer.Deserialize<MemoryFileDto>(File.ReadAllText(path));
            if (dto is null)
                return;

            lock (_sync)
            {
                _sessionId = string.IsNullOrWhiteSpace(dto.SessionId) ? _sessionId : dto.SessionId;
                _track = dto.Track ?? "";
                _vehicle = dto.Vehicle ?? "";
                _facts.Clear();
                _facts.AddRange(dto.Facts ?? []);
                _turns.Clear();
                _turns.AddRange((dto.Turns ?? []).TakeLast(_settings.MaxRecentTurns));
            }

            _logger.LogInformation("Memory loaded ({Facts} facts, {Turns} turns)", FactCount, TurnCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load memory file");
        }
    }

    private void SaveUnlocked()
    {
        try
        {
            var path = MemoryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var dto = new MemoryFileDto
            {
                SessionId = _sessionId,
                Track = _track,
                Vehicle = _vehicle,
                Facts = _facts.ToList(),
                Turns = _turns.ToList()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Memory save failed");
        }
    }

    private static string Trunc(string s, int n)
    {
        s = (s ?? "").Trim();
        return s.Length <= n ? s : s[..n];
    }

    private static string CompactLine(string s) =>
        Trunc(s, 80).Replace('|', '/').Replace('\n', ' ');

    private sealed class MemoryFileDto
    {
        public string SessionId { get; set; } = "";
        public string Track { get; set; } = "";
        public string Vehicle { get; set; } = "";
        public List<string> Facts { get; set; } = [];
        public List<RadioTurn> Turns { get; set; } = [];
    }
}
