using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceIngenior.Telemetry;

namespace RaceIngenior.Radio;

public sealed class RadioPipelineService : IHostedService
{
    private readonly WheelPttService _ptt;
    private readonly MicRecorder _mic = new();
    private readonly RadioBeepService _beeps;
    private readonly ISttService _stt;
    private readonly IEngineerLlm _llm;
    private readonly ITtsService _tts;
    private readonly RuleEngineService _rules;
    private readonly EngineerMemory _memory;
    private readonly TelemetryHub _hub;
    private readonly ILogger<RadioPipelineService> _logger;
    private int _processing;
    private int _pttOpenSeq;

    public event Action<string>? LogLine;
    /// <summary>Raised when a driver/engineer exchange (or alert) is ready for UI overlays.</summary>
    public event Action<string, string>? ExchangeUpdated;
    public string LastDriverText { get; private set; } = "";
    public string LastEngineerText { get; private set; } = "";
    public string Mode { get; private set; } = "Idle";

    public RadioPipelineService(
        WheelPttService ptt,
        RadioBeepService beeps,
        ISttService stt,
        IEngineerLlm llm,
        ITtsService tts,
        RuleEngineService rules,
        EngineerMemory memory,
        TelemetryHub hub,
        ILogger<RadioPipelineService> logger)
    {
        _ptt = ptt;
        _beeps = beeps;
        _stt = stt;
        _llm = llm;
        _tts = tts;
        _rules = rules;
        _memory = memory;
        _hub = hub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ptt.PttPressed += OnPttPressed;
        _ptt.PttReleased += OnPttReleased;
        _hub.Updated += OnTelemetry;
        _ = _stt.EnsureModelAsync(cancellationToken);
        Emit($"Radio ready · memory {_memory.FactCount}f/{_memory.TurnCount}t");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ptt.PttPressed -= OnPttPressed;
        _ptt.PttReleased -= OnPttReleased;
        _hub.Updated -= OnTelemetry;
        return Task.CompletedTask;
    }

    private void OnPttPressed()
    {
        Mode = "PTT listening";
        Emit("PTT down — recording");
        // Beep before mic so the tone is not captured by Whisper.
        var seq = Interlocked.Increment(ref _pttOpenSeq);
        _ = OpenPttAsync(seq);
    }

    private async Task OpenPttAsync(int seq)
    {
        try
        {
            await _beeps.PlayOpenAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PTT open beep failed");
        }

        // Abandoned if user already released (or pressed again).
        if (seq != Volatile.Read(ref _pttOpenSeq) || !_ptt.IsPressed)
            return;

        if (!_mic.IsRecording)
            _mic.Start();
    }

    private void OnPttReleased()
    {
        Interlocked.Increment(ref _pttOpenSeq);
        var wav = _mic.Stop();
        Mode = "Processing";
        Emit("PTT up — transcribing");
        _ = _beeps.PlayCloseAsync();
        _ = HandlePttAsync(wav);
    }

    private async Task HandlePttAsync(byte[] wav)
    {
        if (Interlocked.Exchange(ref _processing, 1) == 1)
        {
            Emit("Busy — ignored overlapping PTT");
            return;
        }

        try
        {
            var text = wav.Length > 44 ? await _stt.TranscribeAsync(wav) : "";
            LastDriverText = text;
            Emit($"Driver: {text}");

            var snap = _hub.Latest;
            string? reply = null;

            if (_llm.IsConfigured)
                reply = await _llm.AskAsync(text, snap);
            else
                Emit("Gemini skipped: no API key");

            if (string.IsNullOrWhiteSpace(reply))
            {
                if (!string.IsNullOrWhiteSpace(_llm.LastError))
                    Emit($"Gemini failed: {_llm.LastError}");

                reply = snap.Connected
                    ? $"Copie. P{snap.Place}, fuel {snap.FuelL:0.0} L, last {snap.LastLap:0.000}."
                    : "Ingénieur hors ligne. Pas de télémétrie.";
                Mode = "Offline/fallback";
                _memory.RememberExchange(text, reply, snap);
            }
            else
            {
                Mode = "Gemini";
                Emit("Gemini OK");
            }

            LastEngineerText = reply;
            Emit($"Engineer: {reply}");
            ExchangeUpdated?.Invoke(LastDriverText, LastEngineerText);
            await _tts.SpeakAsync(reply);
            Mode = "Idle";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PTT pipeline failed");
            Emit($"Error: {ex.Message}");
            Mode = "Error";
        }
        finally
        {
            Interlocked.Exchange(ref _processing, 0);
        }
    }

    private void OnTelemetry(Models.TelemetrySnapshot snap)
    {
        _memory.ObserveTelemetry(snap);

        if (_mic.IsRecording || _processing == 1)
            return;

        var alert = _rules.Evaluate(snap);
        if (alert is null)
            return;

        Emit($"Alert: {alert}");
        LastEngineerText = alert;
        ExchangeUpdated?.Invoke(LastDriverText, LastEngineerText);
        _memory.RememberAlert(alert, snap);
        _ = _tts.SpeakAsync(alert);
    }

    private void Emit(string line)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] {line}";
        _logger.LogInformation("{Msg}", line);
        LogLine?.Invoke(msg);
    }
}
