using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RaceIngenior.Models;
using RaceIngenior.Radio;
using RaceIngenior.Services;
using RaceIngenior.Telemetry;

namespace RaceIngenior.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TelemetryHub _hub;
    private readonly RadioPipelineService _radio;
    private readonly WheelPttService _ptt;
    private readonly IEngineerLlm _llm;
    private readonly ITtsService _tts;
    private readonly EngineerMemory _memory;
    private readonly UserPreferencesStore _prefs;
    private readonly OverlayService _overlay;
    private readonly StartupInfo _startup;
    private bool _speechRateReady;

    [ObservableProperty] private string _connectionStatus = "Waiting for LMU…";
    [ObservableProperty] private string _pttStatus = "";
    [ObservableProperty] private string _radioMode = "Idle";
    [ObservableProperty] private string _track = "—";
    [ObservableProperty] private string _vehicle = "—";
    [ObservableProperty] private string _place = "—";
    [ObservableProperty] private string _fuel = "—";
    [ObservableProperty] private string _speed = "—";
    [ObservableProperty] private string _lastLap = "—";
    [ObservableProperty] private string _phase = "—";
    [ObservableProperty] private string _energy = "—";
    [ObservableProperty] private string _tires = "—";
    [ObservableProperty] private string _tireTemps = "—";
    [ObservableProperty] private string _tireCarcass = "—";
    [ObservableProperty] private string _lastDriver = "";
    [ObservableProperty] private string _lastEngineer = "";
    [ObservableProperty] private string _geminiStatus = "";
    [ObservableProperty] private string _memoryStatus = "";
    [ObservableProperty] private string _apiKeyPath = "";
    [ObservableProperty] private PttDeviceOption? _selectedDevice;
    [ObservableProperty] private int _selectedButtonIndex;
    [ObservableProperty] private string _learnButtonLabel = "Detect button";
    [ObservableProperty] private bool _isLearning;
    [ObservableProperty] private int _speechRate;
    [ObservableProperty] private string _speechRateLabel = "Rate 1";
    [ObservableProperty] private string _overlayToggleLabel = "Show carcass overlay";
    [ObservableProperty] private bool _overlayClickThrough = true;

    public ObservableCollection<string> LogLines { get; } = [];
    public ObservableCollection<PttDeviceOption> PttDevices { get; } = [];
    public ObservableCollection<int> ButtonIndexes { get; } = new(Enumerable.Range(0, 32));

    public MainViewModel(
        TelemetryHub hub,
        RadioPipelineService radio,
        WheelPttService ptt,
        IEngineerLlm llm,
        ITtsService tts,
        EngineerMemory memory,
        UserPreferencesStore prefs,
        OverlayService overlay,
        StartupInfo startup)
    {
        _hub = hub;
        _radio = radio;
        _ptt = ptt;
        _llm = llm;
        _tts = tts;
        _memory = memory;
        _prefs = prefs;
        _overlay = overlay;
        _startup = startup;

        ApiKeyPath = _startup.ApiKeyFilePath;
        GeminiStatus = _llm.IsConfigured
            ? $"Key loaded · model {_llm.ModelName} · click Test Gemini"
            : $"Missing key — paste into {_startup.ApiKeyFilePath}";
        SelectedButtonIndex = _ptt.ButtonOffset;
        SpeechRate = _tts.Rate;
        OverlayClickThrough = _prefs.Current.OverlayClickThrough;
        UpdateSpeechRateLabel();
        UpdateOverlayLabel();
        _speechRateReady = true;
        RefreshMemoryStatus();
        RefreshDevices();

        _hub.Updated += OnTelemetry;
        _radio.LogLine += OnLog;
        _ptt.BindingChanged += OnPttBindingChanged;
        OnTelemetry(_hub.Latest);
        OnPttBindingChanged();
    }

    private void UpdateOverlayLabel() =>
        OverlayToggleLabel = _overlay.IsUserEnabled ? "Hide tyre overlay" : "Show tyre overlay";

    partial void OnSpeechRateChanged(int value)
    {
        if (!_speechRateReady)
            return;

        _tts.SetRate(value);
        _prefs.SaveTtsRate(value);
        UpdateSpeechRateLabel();
    }

    private void UpdateSpeechRateLabel()
    {
        SpeechRateLabel = SpeechRate switch
        {
            < -3 => $"Slow ({SpeechRate})",
            > 3 => $"Fast ({SpeechRate})",
            _ => $"Rate {SpeechRate}"
        };
    }

    private void RefreshMemoryStatus() =>
        MemoryStatus = $"{_memory.FactCount} facts · {_memory.TurnCount} radio turns";

    private void OnPttBindingChanged()
    {
        void Apply()
        {
            PttStatus = _ptt.Status;
            IsLearning = _ptt.IsLearning;
            LearnButtonLabel = _ptt.IsLearning ? "Cancel detect" : "Detect button";
            SelectedButtonIndex = _ptt.ButtonOffset;

            if (_ptt.BoundDeviceGuid != Guid.Empty)
            {
                var match = PttDevices.FirstOrDefault(d => d.InstanceGuid == _ptt.BoundDeviceGuid);
                if (match is not null)
                    SelectedDevice = match;
            }

            if (!_ptt.IsLearning && !string.IsNullOrWhiteSpace(_ptt.BoundDeviceName))
                LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {_ptt.Status}");
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private void OnTelemetry(TelemetrySnapshot snap)
    {
        void Apply()
        {
            ConnectionStatus = snap.Connected ? "LMU shared memory connected" : "LMU not connected";
            PttStatus = _ptt.Status;
            RadioMode = _radio.Mode;
            Track = string.IsNullOrWhiteSpace(snap.Track) ? "—" : snap.Track;
            Vehicle = string.IsNullOrWhiteSpace(snap.Vehicle) ? "—" : snap.Vehicle;
            Place = snap.Connected ? $"P{snap.Place}  Lap {snap.Lap}" : "—";
            Fuel = snap.Connected ? $"{snap.FuelL:0.0} / {snap.FuelCapacityL:0.0} L" : "—";
            Speed = snap.Connected ? $"{snap.SpeedKmh:0} km/h  {snap.Rpm:0} rpm" : "—";
            LastLap = snap.Connected ? $"{snap.LastLap:0.000} (best {snap.BestLap:0.000})" : "—";
            Phase = snap.Connected ? $"Phase {snap.GamePhase}  Yel {snap.YellowFlagState}" : "—";
            Energy = snap.VirtualEnergy is double e ? $"{e:0.0}" : (snap.RestNotes ?? "—");
            Tires = snap.Connected
                ? $"FL {snap.Wheels.FlWear * 100:0}%  FR {snap.Wheels.FrWear * 100:0}%  RL {snap.Wheels.RlWear * 100:0}%  RR {snap.Wheels.RrWear * 100:0}%"
                : "—";
            TireCarcass = snap.Connected
                ? $"FL {snap.Wheels.FlCarcassC:0}°C  FR {snap.Wheels.FrCarcassC:0}°C  RL {snap.Wheels.RlCarcassC:0}°C  RR {snap.Wheels.RrCarcassC:0}°C"
                : "—";
            TireTemps = snap.Connected
                ? $"FL {snap.Wheels.FlTempC:0}°C  FR {snap.Wheels.FrTempC:0}°C  RL {snap.Wheels.RlTempC:0}°C  RR {snap.Wheels.RrTempC:0}°C"
                : "—";
            LastDriver = _radio.LastDriverText;
            LastEngineer = _radio.LastEngineerText;
            RefreshMemoryStatus();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private void OnLog(string line)
    {
        void Apply()
        {
            LogLines.Insert(0, line);
            while (LogLines.Count > 200)
                LogLines.RemoveAt(LogLines.Count - 1);
            RadioMode = _radio.Mode;
            LastDriver = _radio.LastDriverText;
            LastEngineer = _radio.LastEngineerText;
            RefreshMemoryStatus();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        var previous = SelectedDevice?.InstanceGuid;
        PttDevices.Clear();
        foreach (var d in _ptt.ListDevices())
            PttDevices.Add(d);

        SelectedDevice = previous is Guid g
            ? PttDevices.FirstOrDefault(d => d.InstanceGuid == g) ?? PttDevices.FirstOrDefault()
            : PttDevices.FirstOrDefault(d => d.InstanceGuid == _ptt.BoundDeviceGuid)
              ?? PttDevices.FirstOrDefault();
    }

    [RelayCommand]
    private void SavePttBinding()
    {
        _ptt.ApplyBinding(
            SelectedDevice?.InstanceGuid,
            SelectedDevice?.Name,
            SelectedButtonIndex,
            persist: true);
        PttStatus = _ptt.Status;
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Saved {_ptt.Status}");
    }

    [RelayCommand]
    private void ToggleLearnPtt()
    {
        if (_ptt.IsLearning)
        {
            _ptt.CancelLearnMode();
        }
        else
        {
            if (SelectedDevice is not null)
            {
                _ptt.ApplyBinding(
                    SelectedDevice.InstanceGuid,
                    SelectedDevice.Name,
                    SelectedButtonIndex,
                    persist: false);
            }

            _ptt.StartLearnMode();
        }

        OnPttBindingChanged();
    }

    [RelayCommand]
    private async Task TestRadioAsync()
    {
        const string msg = "Radio check. Ingénieur à l'écoute.";
        LastEngineer = msg;
        await _tts.SpeakAsync(msg);
    }

    [RelayCommand]
    private async Task TestGeminiAsync()
    {
        GeminiStatus = "Testing Gemini…";
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Testing Gemini ({_llm.ModelName})…");

        var result = await _llm.TestConnectionAsync();
        GeminiStatus = result.Ok
            ? $"Gemini connected · {_llm.ModelName}"
            : $"Gemini FAILED · {result.Message}";

        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {(result.Ok ? "Gemini OK" : "Gemini FAILED")}: {result.Message}");

        if (result.Ok)
        {
            LastEngineer = "Gemini connecté.";
            await _tts.SpeakAsync("Gemini connecté.");
        }
    }

    [RelayCommand]
    private void ClearMemory()
    {
        _memory.ClearSession();
        RefreshMemoryStatus();
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Memory cleared");
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        _overlay.Toggle();
        UpdateOverlayLabel();
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Overlay {(_overlay.IsUserEnabled ? "enabled" : "disabled")} (auto-hides in box / pause; visible in pitlane)");
    }

    partial void OnOverlayClickThroughChanged(bool value)
    {
        _overlay.SetClickThrough(value);
    }

    [RelayCommand]
    private void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                exe = Path.Combine(AppContext.BaseDirectory, "RaceIngenior.exe");

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Restart failed: {ex.Message}");
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }
}
