using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Radio;

public sealed record GeminiTestResult(bool Ok, string Message);

public interface IEngineerLlm
{
    bool IsConfigured { get; }
    string? LastError { get; }
    string ModelName { get; }
    Task<string?> AskAsync(string driverText, TelemetrySnapshot snapshot, CancellationToken ct = default);
    Task<GeminiTestResult> TestConnectionAsync(CancellationToken ct = default);
}

public sealed class GeminiEngineerService : IEngineerLlm
{
    private const string SystemPrompt =
        "Ingénieur radio WEC FR, concis (≤18 mots). Spotter/stratégie/coach/alertes. Texte radio seul, pas de markdown.";

    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly EngineerMemory _memory;
    private readonly TelemetryPromptBuilder _telemetryPrompt;
    private readonly ILogger<GeminiEngineerService> _logger;

    public GeminiEngineerService(
        HttpClient http,
        IOptions<AppSettings> settings,
        EngineerMemory memory,
        TelemetryPromptBuilder telemetryPrompt,
        ILogger<GeminiEngineerService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _memory = memory;
        _telemetryPrompt = telemetryPrompt;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.GeminiApiKey);
    public string? LastError { get; private set; }
    public string ModelName => _settings.GeminiModel;

    public async Task<GeminiTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            LastError = "No API key loaded";
            return new GeminiTestResult(false, LastError);
        }

        var body = BuildRequest(
            systemPrompt: null,
            userText: "Reply with exactly: OK",
            maxOutputTokens: 256);

        try
        {
            using var response = await _http.PostAsJsonAsync(BuildUrl(), body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                LastError = FormatHttpError((int)response.StatusCode, raw);
                _logger.LogWarning("Gemini test failed: {Error}", LastError);
                return new GeminiTestResult(false, LastError);
            }

            var text = ExtractText(raw, out var finishReason);
            if (string.IsNullOrWhiteSpace(text))
            {
                LastError = string.IsNullOrWhiteSpace(finishReason)
                    ? "HTTP 200 but empty reply"
                    : $"HTTP 200 empty reply (finishReason={finishReason})";
                return new GeminiTestResult(false, LastError);
            }

            LastError = null;
            return new GeminiTestResult(true, $"OK · model {_settings.GeminiModel} · reply \"{Truncate(text.Trim(), 40)}\"");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LastError = ex is TaskCanceledException
                ? "Timeout contacting Gemini"
                : ex.Message;
            _logger.LogWarning(ex, "Gemini test exception");
            return new GeminiTestResult(false, LastError);
        }
    }

    public async Task<string?> AskAsync(string driverText, TelemetrySnapshot snapshot, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            LastError = "No API key loaded";
            return null;
        }

        _memory.ObserveTelemetry(snapshot);

        var user = new StringBuilder(320);
        var mem = _memory.BuildPromptBlock();
        if (!string.IsNullOrEmpty(mem))
            user.Append(mem).Append('\n');

        user.Append("TEL ").Append(_telemetryPrompt.Build(snapshot)).Append('\n');
        user.Append("PTT ");
        user.Append(string.IsNullOrWhiteSpace(driverText) ? "-" : driverText.Trim());

        // Leave headroom above radio length: thinking models may still reserve some tokens.
        var maxOut = Math.Clamp(_settings.MaxOutputTokens, 128, 512);
        var body = BuildRequest(SystemPrompt, user.ToString(), maxOut);

        try
        {
            using var response = await _http.PostAsJsonAsync(BuildUrl(), body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = FormatHttpError((int)response.StatusCode, raw);
                _logger.LogWarning("Gemini HTTP fail: {Error}", LastError);
                return null;
            }

            var reply = ExtractText(raw, out var finishReason)?.Trim();
            if (string.IsNullOrWhiteSpace(reply))
            {
                LastError = string.IsNullOrWhiteSpace(finishReason)
                    ? "Empty Gemini reply"
                    : $"Empty Gemini reply (finishReason={finishReason})";
                return null;
            }

            LastError = null;
            _memory.RememberExchange(driverText, reply, snapshot);
            return reply;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            LastError = ex is TaskCanceledException ? "Timeout contacting Gemini" : ex.Message;
            _logger.LogWarning(ex, "Gemini request failed");
            return null;
        }
    }

    private static JsonObject BuildRequest(string? systemPrompt, string userText, int maxOutputTokens)
    {
        var generationConfig = new JsonObject
        {
            ["maxOutputTokens"] = maxOutputTokens,
            ["thinkingConfig"] = new JsonObject
            {
                ["thinkingLevel"] = "minimal"
            }
        };

        var root = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = userText }
                    }
                }
            },
            ["generationConfig"] = generationConfig
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            root["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemPrompt }
                }
            };
        }

        return root;
    }

    private string BuildUrl() =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{_settings.GeminiModel}:generateContent?key={_settings.GeminiApiKey}";

    private static string? ExtractText(string raw, out string? finishReason)
    {
        finishReason = null;
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var fr))
            finishReason = fr.GetString();

        if (!candidate.TryGetProperty("content", out var content))
            return null;
        if (!content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            return null;

        // Prefer the first part that has text (skip thought-only parts if any).
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
            {
                var value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static string FormatHttpError(int status, string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                var code = err.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (!string.IsNullOrWhiteSpace(msg))
                    return $"HTTP {status} {code}: {Truncate(msg!, 220)}";
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return $"HTTP {status}: {Truncate(raw, 220)}";
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
