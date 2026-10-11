using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Services;

namespace RaceIngenior.Radio;

public interface ITtsService
{
    int Rate { get; }
    void SetRate(int rate);
    Task SpeakAsync(string text, CancellationToken ct = default);
}

public sealed class WindowsTtsService : ITtsService, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly ILogger<WindowsTtsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _rate;

    public int Rate => _rate;

    public WindowsTtsService(
        IOptions<AppSettings> settings,
        UserPreferencesStore prefs,
        ILogger<WindowsTtsService> logger)
    {
        _logger = logger;
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Volume = 100;
        SetRate(prefs.Current.TtsRate);

        var hint = settings.Value.PreferredFrenchVoiceContains;
        var voice = _synth.GetInstalledVoices()
            .Select(v => v.VoiceInfo)
            .FirstOrDefault(v =>
                v.Culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase)
                || v.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));

        if (voice is not null)
        {
            _synth.SelectVoice(voice.Name);
            _logger.LogInformation("TTS voice: {Voice}, rate {Rate}", voice.Name, _rate);
        }
        else
        {
            _logger.LogWarning("No French TTS voice found; using system default");
        }
    }

    public void SetRate(int rate)
    {
        _rate = Math.Clamp(rate, -10, 10);
        _synth.Rate = _rate;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                _synth.Rate = _rate;
                _synth.Speak(text);
            }, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _synth.Dispose();
        _gate.Dispose();
    }
}
