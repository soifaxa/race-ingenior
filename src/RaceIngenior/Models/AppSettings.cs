namespace RaceIngenior.Models;

public sealed class AppSettings
{
    public const string SectionName = "RaceIngenior";

    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-3.6-flash";
    /// <summary>Dedicated TTS model (neural speech). Separate from chat model.</summary>
    public string GeminiTtsModel { get; set; } = "gemini-2.5-flash-preview-tts";
    public string LmuRestBaseUrl { get; set; } = "http://localhost:6397";
    /// <summary>Whisper ggml path. Prefer base/small over tiny for French PTT accuracy.</summary>
    public string WhisperModelPath { get; set; } = "Models/ggml-base.bin";
    public string OutputDeviceHint { get; set; } = "";
    public int? PttJoystickOffset { get; set; }
    public string? PttDeviceNameContains { get; set; }
    /// <summary>Hint when auto-picking (e.g. Julie, Denise, Natural).</summary>
    public string PreferredFrenchVoiceContains { get; set; } = "Julie";
    public double TelemetryPollMs { get; set; } = 100;
    public double RulesPollMs { get; set; } = 250;
    public int MaxOutputTokens { get; set; } = 256;
    /// <summary>Below this (°C), pit-exit cold warning.</summary>
    public double TireColdBelowC { get; set; } = 60;
    /// <summary>At/above this (°C) on all tires → push OK call.</summary>
    public double TireWarmReadyC { get; set; } = 75;
    public MemorySettings Memory { get; set; } = new();
}

public sealed class MemorySettings
{
    public int MaxRecentTurns { get; set; } = 4;
    public int MaxFacts { get; set; } = 12;
    public string MemoryFilePath { get; set; } = "Data/session_memory.json";
}
