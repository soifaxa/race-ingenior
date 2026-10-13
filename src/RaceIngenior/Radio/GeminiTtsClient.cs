using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;

namespace RaceIngenior.Radio;

public sealed record GeminiTtsAudio(byte[] Data, string MimeType);

/// <summary>Neural TTS via Gemini *-tts models (same API key as the engineer).</summary>
public sealed class GeminiTtsClient
{
    public const string VoiceIdPrefix = "gemini:";

    /// <summary>Curated prebuilt voices suited to calm radio callouts.</summary>
    public static readonly IReadOnlyList<(string Name, string Label)> PrebuiltVoices =
    [
        ("Charon", "Charon · informative"),
        ("Kore", "Kore · firm"),
        ("Orus", "Orus · firm"),
        ("Sadaltager", "Sadaltager · knowledgeable"),
        ("Gacrux", "Gacrux · mature"),
        ("Iapetus", "Iapetus · clear"),
        ("Schedar", "Schedar · even"),
        ("Aoede", "Aoede · breezy"),
        ("Zephyr", "Zephyr · bright"),
        ("Puck", "Puck · upbeat"),
        ("Fenrir", "Fenrir · excitable"),
        ("Sulafat", "Sulafat · warm"),
    ];

    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly ILogger<GeminiTtsClient> _logger;

    public GeminiTtsClient(
        HttpClient http,
        IOptions<AppSettings> settings,
        ILogger<GeminiTtsClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        if (_http.Timeout < TimeSpan.FromSeconds(45))
            _http.Timeout = TimeSpan.FromSeconds(45);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.GeminiApiKey);

    public string ModelName =>
        string.IsNullOrWhiteSpace(_settings.GeminiTtsModel)
            ? "gemini-2.5-flash-preview-tts"
            : _settings.GeminiTtsModel.Trim();

    public static string ToVoiceId(string prebuiltName) => VoiceIdPrefix + prebuiltName;

    public static bool TryParseVoiceId(string? voiceId, out string prebuiltName)
    {
        prebuiltName = "";
        if (string.IsNullOrWhiteSpace(voiceId) || !voiceId.StartsWith(VoiceIdPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        prebuiltName = voiceId[VoiceIdPrefix.Length..].Trim();
        return prebuiltName.Length > 0;
    }

    public async Task<GeminiTtsAudio?> SynthesizeAsync(
        string text,
        string voiceName,
        int rate,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        var spoken = TtsTextPrep.Prepare(text);
        if (string.IsNullOrWhiteSpace(spoken))
            return null;

        // Exact recitation only — do not put style prose in `text` (models speak it).
        var audio = await PostAsync(BuildPlainBody(spoken, voiceName), ct);
        if (audio is not null)
            return audio;

        // Gemini 3.8+ optional turn styling
        var style =
            $"calm French WEC race engineer on team radio, concise and clear, {TtsTextPrep.RateStyleHint(rate)}";
        return await PostAsync(BuildStyledBody(spoken, voiceName, style), ct);
    }

    private static JsonObject BuildPlainBody(string spoken, string voiceName) => new()
    {
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = spoken }
                }
            }
        },
        ["generationConfig"] = new JsonObject
        {
            ["responseModalities"] = new JsonArray { "AUDIO" },
            ["speechConfig"] = new JsonObject
            {
                ["voiceConfig"] = new JsonObject
                {
                    ["prebuiltVoiceConfig"] = new JsonObject
                    {
                        ["voiceName"] = voiceName
                    }
                }
            }
        }
    };

    private static JsonObject BuildStyledBody(string spoken, string voiceName, string style) => new()
    {
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["text"] = spoken,
                        ["speechMetadata"] = new JsonObject
                        {
                            ["style"] = style
                        }
                    }
                }
            }
        },
        ["generationConfig"] = new JsonObject
        {
            ["responseModalities"] = new JsonArray { "AUDIO" },
            ["speechConfig"] = new JsonObject
            {
                ["languageCode"] = "fr-FR",
                ["voiceConfig"] = new JsonObject
                {
                    ["prebuiltVoiceConfig"] = new JsonObject
                    {
                        ["voiceName"] = voiceName
                    }
                }
            }
        }
    };

    private async Task<GeminiTtsAudio?> PostAsync(JsonObject body, CancellationToken ct)
    {
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent?key={_settings.GeminiApiKey}";

        try
        {
            using var response = await _http.PostAsJsonAsync(url, body, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini TTS HTTP {Status}: {Body}", (int)response.StatusCode, Truncate(raw, 280));
                return null;
            }

            var audio = ExtractAudio(raw);
            if (audio is null)
                _logger.LogWarning("Gemini TTS 200 but no audio inlineData");
            else
                _logger.LogInformation("Gemini TTS OK · {Model} · {Bytes} bytes · {Mime}", ModelName, audio.Data.Length, audio.MimeType);

            return audio;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Gemini TTS request failed");
            return null;
        }
    }

    private static GeminiTtsAudio? ExtractAudio(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        var content = candidates[0].GetProperty("content");
        if (!content.TryGetProperty("parts", out var parts))
            return null;

        foreach (var part in parts.EnumerateArray())
        {
            if (!part.TryGetProperty("inlineData", out var inline) &&
                !part.TryGetProperty("inline_data", out inline))
                continue;

            if (!inline.TryGetProperty("data", out var dataEl))
                continue;

            var b64 = dataEl.GetString();
            if (string.IsNullOrWhiteSpace(b64))
                continue;

            var mime = "audio/L16;rate=24000";
            if (inline.TryGetProperty("mimeType", out var mimeEl) ||
                inline.TryGetProperty("mime_type", out mimeEl))
            {
                mime = mimeEl.GetString() ?? mime;
            }

            return new GeminiTtsAudio(Convert.FromBase64String(b64), mime);
        }

        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
