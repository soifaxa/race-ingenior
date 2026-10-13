using System.IO;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using RaceIngenior.Services;

namespace RaceIngenior.Radio;

/// <summary>
/// Prefers Gemini neural TTS when an API key is available; falls back to Windows OneCore.
/// </summary>
public sealed class HybridTtsService : ITtsService, IDisposable
{
    private const string DefaultGeminiVoice = "Charon";

    private readonly GeminiTtsClient _gemini;
    private readonly WindowsTtsService _windows;
    private readonly ILogger<HybridTtsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<TtsVoiceOption> _voices = [];
    private int _rate;
    private string? _selectedVoiceId;
    private WaveOutEvent? _waveOut;

    public int Rate => _rate;
    public string? SelectedVoiceId => _selectedVoiceId;
    public IReadOnlyList<TtsVoiceOption> AvailableVoices => _voices;

    public HybridTtsService(
        GeminiTtsClient gemini,
        WindowsTtsService windows,
        UserPreferencesStore prefs,
        ILogger<HybridTtsService> logger)
    {
        _gemini = gemini;
        _windows = windows;
        _logger = logger;
        SetRate(prefs.Current.TtsRate);
        RebuildVoiceCatalog();
        SetVoice(prefs.Current.TtsVoiceId);
    }

    public void SetRate(int rate)
    {
        _rate = Math.Clamp(rate, -10, 10);
        _windows.SetRate(_rate);
    }

    public void SetVoice(string? voiceId)
    {
        _selectedVoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();

        if (GeminiTtsClient.TryParseVoiceId(_selectedVoiceId, out _))
        {
            // Keep Windows on its auto voice for fallback.
            _windows.SetVoice(null);
            return;
        }

        _windows.SetVoice(_selectedVoiceId);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            if (ShouldUseGemini())
            {
                var voice = ResolveGeminiVoiceName();
                var audio = await _gemini.SynthesizeAsync(text, voice, _rate, ct);
                if (audio is not null && audio.Data.Length > 0)
                {
                    _logger.LogDebug("Playing Gemini TTS ({Bytes} bytes, {Mime})", audio.Data.Length, audio.MimeType);
                    await PlayAudioAsync(audio.Data, audio.MimeType, ct);
                    return;
                }

                _logger.LogWarning("Gemini TTS unavailable; falling back to Windows voice");
            }

            await _windows.SpeakAsync(text, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        StopWaveOut();
        _gate.Dispose();
        // WindowsTtsService lifetime owned by DI
    }

    private bool ShouldUseGemini()
    {
        if (!_gemini.IsConfigured)
            return false;

        // Explicit Windows voice selected → stay offline.
        if (!string.IsNullOrEmpty(_selectedVoiceId)
            && !_selectedVoiceId.StartsWith(GeminiTtsClient.VoiceIdPrefix, StringComparison.OrdinalIgnoreCase)
            && _selectedVoiceId.Length > 0)
            return false;

        return true;
    }

    private string ResolveGeminiVoiceName()
    {
        if (GeminiTtsClient.TryParseVoiceId(_selectedVoiceId, out var name))
            return name;
        return DefaultGeminiVoice;
    }

    private void RebuildVoiceCatalog()
    {
        _voices.Clear();
        _voices.Add(new TtsVoiceOption(
            "",
            _gemini.IsConfigured
                ? "Auto · Gemini neural (Charon)"
                : "Auto · Windows (add Gemini key for neural)",
            "fr",
            true));

        if (_gemini.IsConfigured)
        {
            foreach (var (name, label) in GeminiTtsClient.PrebuiltVoices)
            {
                _voices.Add(new TtsVoiceOption(
                    GeminiTtsClient.ToVoiceId(name),
                    $"Neural · {label}",
                    "fr-FR",
                    name is "Charon" or "Kore" or "Orus"));
            }
        }

        foreach (var win in _windows.AvailableVoices.Where(v => !string.IsNullOrEmpty(v.Id)))
        {
            _voices.Add(new TtsVoiceOption(
                win.Id,
                $"Windows · {win.DisplayName}",
                win.Language,
                false));
        }
    }

    public void RefreshAvailableVoices()
    {
        RebuildVoiceCatalog();
        SetVoice(_selectedVoiceId);
    }

    private async Task PlayAudioAsync(byte[] data, string mimeType, CancellationToken ct)
    {
        StopWaveOut();

        var ms = new MemoryStream(data, writable: false);
        if (IsWav(data))
        {
            var reader = new WaveFileReader(ms);
            await PlayProviderAsync(reader, reader, ct);
            return;
        }

        var rate = ParseSampleRate(mimeType, defaultRate: 24000);
        var format = new WaveFormat(rate, 16, 1);
        var raw = new RawSourceWaveStream(ms, format);
        await PlayProviderAsync(raw, raw, ct);
    }

    private async Task PlayProviderAsync(IWaveProvider provider, IDisposable disposable, CancellationToken ct)
    {
        var output = new WaveOutEvent();
        _waveOut = output;
        try
        {
            output.Init(provider);
            output.Play();

            while (output.PlaybackState == PlaybackState.Playing)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(40, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            try { output.Stop(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            if (ReferenceEquals(_waveOut, output))
                _waveOut = null;
            output.Dispose();
            disposable.Dispose();
        }
    }

    private void StopWaveOut()
    {
        var o = _waveOut;
        _waveOut = null;
        if (o is null)
            return;
        try { o.Stop(); } catch { /* ignore */ }
        try { o.Dispose(); } catch { /* ignore */ }
    }

    private static bool IsWav(byte[] data) =>
        data.Length >= 12
        && data[0] == (byte)'R'
        && data[1] == (byte)'I'
        && data[2] == (byte)'F'
        && data[3] == (byte)'F';

    private static int ParseSampleRate(string mimeType, int defaultRate)
    {
        // e.g. audio/L16;rate=24000 or audio/pcm;rate=24000
        const string marker = "rate=";
        var idx = mimeType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return defaultRate;
        var slice = mimeType[(idx + marker.Length)..];
        var end = 0;
        while (end < slice.Length && char.IsDigit(slice[end]))
            end++;
        return end > 0 && int.TryParse(slice[..end], out var rate) && rate > 0
            ? rate
            : defaultRate;
    }
}
