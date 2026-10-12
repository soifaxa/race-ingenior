using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Telemetry;

public sealed class LmuRestClient
{
    private readonly HttpClient _http;
    private readonly ILogger<LmuRestClient> _logger;
    private readonly AppSettings _settings;

    public LmuRestClient(HttpClient http, IOptions<AppSettings> settings, ILogger<LmuRestClient> logger)
    {
        _http = http;
        _logger = logger;
        _settings = settings.Value;
        _http.BaseAddress = new Uri(_settings.LmuRestBaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMilliseconds(800);
    }

    public async Task EnrichAsync(TelemetrySnapshot snapshot, CancellationToken ct)
    {
        _ = snapshot;
        try
        {
            // Hypercar Virtual Energy is not in rF2 shared memory.
            // Confirmed source: garage RepairAndRefuel → fuelInfo.current/maxVirtualEnergy.
            var energy = await TryReadVirtualEnergyFromRepairAndRefuelAsync(ct);
            energy ??= await TryReadVirtualEnergyFromStrategyUsageAsync(ct);

            if (energy is double pct)
            {
                RestCache.VirtualEnergy = pct;
                RestCache.LastNotes = "REST OK";
                RestCache.LastOk = DateTimeOffset.UtcNow;
            }
            else
            {
                RestCache.LastNotes = "REST VE missing";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            RestCache.LastNotes = "REST unavailable";
            _logger.LogDebug(ex, "LMU REST enrichment failed");
        }
    }

    private async Task<double?> TryReadVirtualEnergyFromRepairAndRefuelAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("rest/garage/UIScreen/RepairAndRefuel", ct);
        if (!response.IsSuccessStatusCode)
        {
            RestCache.LastNotes = $"REST {(int)response.StatusCode}";
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseVirtualEnergyPercent(doc.RootElement);
    }

    private async Task<double?> TryReadVirtualEnergyFromStrategyUsageAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("rest/strategy/usage", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (TryGetDouble(root, "virtualEnergy", out var v1))
            return NormalizePossiblyRawPercent(v1);
        if (TryGetDouble(root, "VirtualEnergy", out var v2))
            return NormalizePossiblyRawPercent(v2);

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var last = root[root.GetArrayLength() - 1];
            if (TryGetDouble(last, "virtualEnergy", out var v3))
                return NormalizePossiblyRawPercent(v3);
        }

        return ParseVirtualEnergyPercent(root);
    }

    /// <summary>
    /// Returns remaining Virtual Energy as 0–100 %.
    /// LMU exposes raw current/max integers (often huge); we normalize current/max.
    /// </summary>
    private static double? ParseVirtualEnergyPercent(JsonElement root)
    {
        JsonElement fuel = default;
        var hasFuel = root.TryGetProperty("fuelInfo", out fuel)
                      || (root.TryGetProperty("FuelInfo", out fuel));

        if (hasFuel)
        {
            if (TryGetDouble(fuel, "currentVirtualEnergy", out var cur)
                && TryGetDouble(fuel, "maxVirtualEnergy", out var max)
                && max > 0)
                return ClampPercent(cur / max * 100.0);

            if (TryGetDouble(fuel, "CurrentVirtualEnergy", out var cur2)
                && TryGetDouble(fuel, "MaxVirtualEnergy", out var max2)
                && max2 > 0)
                return ClampPercent(cur2 / max2 * 100.0);
        }

        if (TryGetDouble(root, "currentVirtualEnergy", out var c)
            && TryGetDouble(root, "maxVirtualEnergy", out var m)
            && m > 0)
            return ClampPercent(c / m * 100.0);

        return null;
    }

    private static double NormalizePossiblyRawPercent(double value)
    {
        // Already a fraction 0–1
        if (value is >= 0 and <= 1.5)
            return ClampPercent(value * 100.0);
        // Already a percent 0–100
        if (value is > 1.5 and <= 100.5)
            return ClampPercent(value);
        // Raw absolute — without max we cannot normalize reliably.
        return ClampPercent(value);
    }

    private static double ClampPercent(double pct) => Math.Clamp(pct, 0, 100);

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value))
            return true;
        if (p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;
        return false;
    }
}

internal static class RestCache
{
    /// <summary>Remaining Virtual Energy as 0–100 % (Hypercar).</summary>
    public static double? VirtualEnergy { get; set; }
    public static string? LastNotes { get; set; }
    public static DateTimeOffset LastOk { get; set; }
}
