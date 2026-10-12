using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RaceIngenior.Models;

namespace RaceIngenior.Services;

/// <summary>
/// Loads/saves learned track outlines from <c>Tracks/</c> (repo + app output).
/// </summary>
public sealed class TrackLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<TrackLayoutStore> _logger;
    private readonly Dictionary<string, TrackLayout> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public event Action<TrackLayout>? LayoutSaved;

    public TrackLayoutStore(ILogger<TrackLayoutStore> logger)
    {
        _logger = logger;
        Reload();
    }

    public void Reload()
    {
        lock (_sync)
        {
            _cache.Clear();
            foreach (var dir in CandidateDirectories())
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    try
                    {
                        var layout = JsonSerializer.Deserialize<TrackLayout>(File.ReadAllText(file), JsonOptions);
                        if (layout is null || string.IsNullOrWhiteSpace(layout.TrackKey) || !layout.HasOutline)
                            continue;

                        _cache[layout.TrackKey] = layout;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load track layout {File}", file);
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} learned track layout(s)", _cache.Count);
        }
    }

    public TrackLayout? TryGet(string? trackName)
    {
        var key = NormalizeKey(trackName);
        if (key.Length == 0)
            return null;

        lock (_sync)
            return _cache.TryGetValue(key, out var layout) ? layout : null;
    }

    public bool HasLayout(string? trackName) => TryGet(trackName) is not null;

    public void Save(TrackLayout layout)
    {
        layout.TrackKey = NormalizeKey(layout.TrackKey);
        if (layout.TrackKey.Length == 0 || !layout.HasOutline)
            return;

        lock (_sync)
            _cache[layout.TrackKey] = layout;

        foreach (var dir in SaveDirectories())
        {
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, layout.TrackKey + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(layout, JsonOptions));
                _logger.LogInformation("Saved track layout {Key} ({Points} pts) → {Path}",
                    layout.TrackKey, layout.Points.Count, path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save track layout to {Dir}", dir);
            }
        }

        LayoutSaved?.Invoke(layout);
    }

    public static string NormalizeKey(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
            return "";

        var s = trackName.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Tracks");

        var repo = FindRepoTracksDirectory();
        if (repo is not null)
            yield return repo;
    }

    private static IEnumerable<string> SaveDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Tracks");

        var repo = FindRepoTracksDirectory();
        if (repo is not null)
            yield return repo;
    }

    /// <summary>Prefer repo <c>Tracks/</c> so learned maps can be committed for everyone.</summary>
    private static string? FindRepoTracksDirectory()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var tracks = Path.Combine(dir.FullName, "Tracks");
                var marker = Path.Combine(dir.FullName, "src", "RaceIngenior", "RaceIngenior.csproj");
                if (File.Exists(marker))
                    return tracks;

                if (Directory.Exists(tracks) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                    return tracks;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
