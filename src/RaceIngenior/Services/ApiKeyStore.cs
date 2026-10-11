using System.IO;
using Microsoft.Extensions.Logging;

namespace RaceIngenior.Services;

/// <summary>
/// Loads the Gemini API key from a local file (preferred), then settings/env.
/// </summary>
public sealed class ApiKeyStore
{
    public const string RelativeKeyFile = "secrets/gemini_api_key.txt";
    public const string RelativeSecretsJson = "secrets/secrets.json";

    private readonly ILogger<ApiKeyStore> _logger;

    public ApiKeyStore(ILogger<ApiKeyStore> logger) => _logger = logger;

    public string ResolveKey(string configuredKey)
    {
        foreach (var root in CandidateRoots())
        {
            var txt = Path.Combine(root, RelativeKeyFile);
            if (TryReadPlainKey(txt, out var fromTxt))
            {
                _logger.LogInformation("API key loaded from {Path}", txt);
                return fromTxt;
            }

            var json = Path.Combine(root, RelativeSecretsJson);
            if (TryReadJsonKey(json, out var fromJson))
            {
                _logger.LogInformation("API key loaded from {Path}", json);
                return fromJson;
            }
        }

        if (!string.IsNullOrWhiteSpace(configuredKey))
            return configuredKey.Trim();

        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? "" : env.Trim();
    }

    public static string EnsureSecretsTemplate()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "secrets");
        Directory.CreateDirectory(dir);

        var keyPath = Path.Combine(dir, "gemini_api_key.txt");
        if (!File.Exists(keyPath))
            File.WriteAllText(keyPath, "");

        var example = Path.Combine(dir, "gemini_api_key.example.txt");
        if (!File.Exists(example))
            File.WriteAllText(example, "PASTE_YOUR_GOOGLE_AI_STUDIO_KEY_HERE\n");

        return keyPath;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return AppContext.BaseDirectory;

        // Dev convenience: project secrets/ next to repo when running from bin/
        var projectGuess = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        if (Directory.Exists(projectGuess))
            yield return projectGuess;

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RaceIngenior");
        yield return appData;
    }

    private static bool TryReadPlainKey(string path, out string key)
    {
        key = "";
        if (!File.Exists(path))
            return false;

        var lines = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("//"));

        key = lines.FirstOrDefault() ?? "";
        return key.Length > 0 && !key.Contains("PASTE_YOUR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadJsonKey(string path, out string key)
    {
        key = "";
        if (!File.Exists(path))
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("GeminiApiKey", out var p))
                key = p.GetString()?.Trim() ?? "";
            else if (doc.RootElement.TryGetProperty("gemini_api_key", out var p2))
                key = p2.GetString()?.Trim() ?? "";
        }
        catch
        {
            return false;
        }

        return key.Length > 0;
    }
}
