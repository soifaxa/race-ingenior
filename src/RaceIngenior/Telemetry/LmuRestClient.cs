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
            // Virtual energy history (Hypercar). Endpoint may vary by LMU build.
            using var response = await _http.GetAsync("rest/strategy/usage", ct);
            if (!response.IsSuccessStatusCode)
            {
                RestCache.LastNotes = $"REST {(int)response.StatusCode}";
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            double? energy = null;
            if (root.TryGetProperty("virtualEnergy", out var ve) && ve.TryGetDouble(out var v1))
                energy = v1;
            else if (root.TryGetProperty("VirtualEnergy", out var ve2) && ve2.TryGetDouble(out var v2))
                energy = v2;
            else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var last = root[root.GetArrayLength() - 1];
                if (last.TryGetProperty("virtualEnergy", out var ve3) && ve3.TryGetDouble(out var v3))
                    energy = v3;
            }

            RestCache.VirtualEnergy = energy;
            RestCache.LastNotes = "REST OK";
            RestCache.LastOk = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            RestCache.LastNotes = "REST unavailable";
            _logger.LogDebug(ex, "LMU REST enrichment failed");
        }
    }
}

internal static class RestCache
{
    public static double? VirtualEnergy { get; set; }
    public static string? LastNotes { get; set; }
    public static DateTimeOffset LastOk { get; set; }
}
