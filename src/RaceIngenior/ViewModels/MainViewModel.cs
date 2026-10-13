using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
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
    private readonly TrackLayoutStore _trackLayouts;
    private readonly TrackLayoutLearner _trackLearner;
    private readonly SessionReportService _sessionReports;
    private readonly BrandLogoStore _brandLogos;
    private readonly AppSettings _settings;
    private bool _speechRateReady;
    private long _lastStandingsRefreshTicks;

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
    [ObservableProperty] private string _geminiApiKeyInput = "";
    [ObservableProperty] private PttDeviceOption? _selectedDevice;
    [ObservableProperty] private int _selectedButtonIndex;
    [ObservableProperty] private string _learnButtonLabel = "Detect button";
    [ObservableProperty] private bool _isLearning;
    [ObservableProperty] private int _speechRate;
    [ObservableProperty] private string _speechRateLabel = "Rate 1";
    [ObservableProperty] private string _overlayToggleLabel = "Show overlays";
    [ObservableProperty] private bool _overlayClickThrough = true;
    [ObservableProperty] private bool _overlayForceShow;
    [ObservableProperty] private int _conversationOverlaySeconds = 8;
    [ObservableProperty] private string _appVersion = "";
    [ObservableProperty] private IReadOnlyList<TrackCar> _trackCars = Array.Empty<TrackCar>();
    [ObservableProperty] private IReadOnlyList<StandingEntry> _standings = Array.Empty<StandingEntry>();
    [ObservableProperty] private string _standingsCountLabel = "0";
    [ObservableProperty] private TrackLayout? _currentTrackLayout;
    [ObservableProperty] private string _trackMapStatus = "";
    [ObservableProperty] private string _trackMapModeLabel = "OVAL";
    [ObservableProperty] private string _tireRecommended = "—";
    [ObservableProperty] private string _tireConditionLabel = "NO DATA";
    [ObservableProperty] private string _tireAdviceReason = "";
    [ObservableProperty] private string _tireFitted = "—";
    [ObservableProperty] private string _weatherTrackTemp = "—";
    [ObservableProperty] private string _weatherAmbientTemp = "—";
    [ObservableProperty] private string _weatherRain = "—";
    [ObservableProperty] private string _weatherWetness = "—";
    [ObservableProperty] private string _weatherCloud = "—";
    [ObservableProperty] private string _flagStatus = "GREEN";
    [ObservableProperty] private string _flagDetail = "No flags";
    [ObservableProperty] private bool _autoOpenSessionReport = true;

    public ObservableCollection<string> LogLines { get; } = [];
    public event Action<string, SessionReport>? OpenSessionReportRequested;
    public ObservableCollection<PttDeviceOption> PttDevices { get; } = [];
    public ObservableCollection<int> ButtonIndexes { get; } = new(Enumerable.Range(0, 32));
    public ObservableCollection<int> ConversationOverlayDurations { get; } = new([5, 8, 10]);

    public MainViewModel(
        TelemetryHub hub,
        RadioPipelineService radio,
        WheelPttService ptt,
        IEngineerLlm llm,
        ITtsService tts,
        EngineerMemory memory,
        UserPreferencesStore prefs,
        OverlayService overlay,
        TrackLayoutStore trackLayouts,
        TrackLayoutLearner trackLearner,
        SessionReportService sessionReports,
        BrandLogoStore brandLogos,
        IOptions<AppSettings> settings)
    {
        _hub = hub;
        _radio = radio;
        _ptt = ptt;
        _llm = llm;
        _tts = tts;
        _memory = memory;
        _prefs = prefs;
        _overlay = overlay;
        _trackLayouts = trackLayouts;
        _trackLearner = trackLearner;
        _sessionReports = sessionReports;
        _brandLogos = brandLogos;
        _settings = settings.Value;

        AppVersion = ResolveAppVersion();
        RefreshGeminiStatus();
        SelectedButtonIndex = _ptt.ButtonOffset;
        SpeechRate = _tts.Rate;
        OverlayClickThrough = _prefs.Current.OverlayClickThrough;
        OverlayForceShow = _prefs.Current.OverlayForceShow;
        ConversationOverlaySeconds = _prefs.Current.ConversationOverlaySeconds;
        AutoOpenSessionReport = _prefs.IsAutoOpenSessionReportEnabled;
        UpdateSpeechRateLabel();
        UpdateOverlayLabel();
        _speechRateReady = true;
        RefreshMemoryStatus();
        RefreshDevices();

        _hub.Updated += OnTelemetry;
        _radio.LogLine += OnLog;
        _ptt.BindingChanged += OnPttBindingChanged;
        _trackLayouts.LayoutSaved += OnTrackLayoutSaved;
        _trackLearner.StatusChanged += OnTrackLearnerStatusChanged;
        _sessionReports.ReportSaved += OnSessionReportSaved;
        OnTelemetry(_hub.Latest);
        OnPttBindingChanged();
    }

    private void UpdateOverlayLabel() =>
        OverlayToggleLabel = _overlay.IsUserEnabled ? "Hide overlays" : "Show overlays";

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
            Vehicle = BrandResolver.FormatClassAndBrand(
                snap.VehFilename, vehicleName: snap.Vehicle, vehicleClass: snap.VehicleClass);
            Place = snap.Connected ? $"P{snap.Place}  Lap {snap.Lap}" : "—";
            Fuel = snap.Connected ? $"{snap.FuelL:0.0} / {snap.FuelCapacityL:0.0} L" : "—";
            Speed = snap.Connected ? $"{snap.SpeedKmh:0} km/h  {snap.Rpm:0} rpm" : "—";
            LastLap = snap.Connected
                ? $"{LapTimeFormat.Format(snap.LastLap)}  (best {LapTimeFormat.Format(snap.BestLap)})"
                : "—";
            Phase = snap.Connected ? snap.GamePhaseDisplay : "—";
            Energy = snap.VirtualEnergy is double e
                ? $"{e:0.0} %"
                : (snap.RestNotes ?? "—");
            Tires = snap.Connected
                ? FormatTireWear(snap.Wheels)
                : "—";
            TireCarcass = snap.Connected
                ? $"FL {snap.Wheels.FlCarcassC:0}°C  FR {snap.Wheels.FrCarcassC:0}°C  RL {snap.Wheels.RlCarcassC:0}°C  RR {snap.Wheels.RrCarcassC:0}°C"
                : "—";
            TireTemps = snap.Connected
                ? $"FL {snap.Wheels.FlTempC:0}°C  FR {snap.Wheels.FrTempC:0}°C  RL {snap.Wheels.RlTempC:0}°C  RR {snap.Wheels.RrTempC:0}°C"
                : "—";
            LastDriver = _radio.LastDriverText;
            LastEngineer = _radio.LastEngineerText;
            SyncFlags(snap);
            SyncTrackCars(snap);
            SyncTrackLayout(snap);
            SyncTireAdvice(snap);
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
        _overlay.ShowConversation(LastDriver, LastEngineer);
        await _tts.SpeakAsync(msg);
    }

    [RelayCommand]
    private void SaveGeminiApiKey()
    {
        var key = GeminiApiKeyInput?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            GeminiStatus = "Paste a Gemini API key, then Save";
            return;
        }

        _settings.GeminiApiKey = key;
        _prefs.SaveGeminiApiKey(key);
        GeminiApiKeyInput = "";
        RefreshGeminiStatus();
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Gemini API key saved");
    }

    [RelayCommand]
    private void ClearGeminiApiKey()
    {
        _settings.GeminiApiKey = "";
        _prefs.SaveGeminiApiKey(null);
        GeminiApiKeyInput = "";
        RefreshGeminiStatus();
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Gemini API key cleared");
    }

    private void RefreshGeminiStatus()
    {
        GeminiStatus = _llm.IsConfigured
            ? $"Key saved · model {_llm.ModelName} · click Test Gemini"
            : "No key — paste your Gemini API key below and Save";
    }

    [RelayCommand]
    private async Task TestGeminiAsync()
    {
        if (!_llm.IsConfigured)
        {
            RefreshGeminiStatus();
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Gemini FAILED: no API key saved");
            return;
        }

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
            _overlay.ShowConversation(LastDriver, LastEngineer);
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
    private void CopyLastExchange()
    {
        if (string.IsNullOrWhiteSpace(LastDriver) && string.IsNullOrWhiteSpace(LastEngineer))
        {
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Nothing to copy");
            return;
        }

        var text = $"Driver: {LastDriver}{Environment.NewLine}Engineer: {LastEngineer}";
        try
        {
            System.Windows.Clipboard.SetText(text);
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Copied last exchange");
        }
        catch (Exception ex)
        {
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Copy failed: {ex.Message}");
        }
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

    partial void OnOverlayForceShowChanged(bool value)
    {
        if (!_speechRateReady)
            return;
        _overlay.SetForceShow(value);
        LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Overlay force show {(value ? "ON (layout / test)" : "OFF")}");
    }

    partial void OnConversationOverlaySecondsChanged(int value)
    {
        if (!_speechRateReady)
            return;
        _prefs.SaveConversationSeconds(value);
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

    private void SyncTrackCars(TelemetrySnapshot snap)
    {
        if (!snap.Connected || snap.Field.Count == 0)
        {
            TrackCars = Array.Empty<TrackCar>();
            if (Standings.Count > 0)
            {
                Standings = Array.Empty<StandingEntry>();
                StandingsCountLabel = "0";
            }
            _lastStandingsRefreshTicks = 0;
            return;
        }

        TrackCars = snap.Field;

        // Standings panel: refresh at most once every 5 s (map still uses TrackCars at poll rate).
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = _lastStandingsRefreshTicks == 0
            ? 5000.0
            : (now - _lastStandingsRefreshTicks) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < 5000.0)
            return;

        _lastStandingsRefreshTicks = now;
        Standings = snap.Field.Select(ToStandingEntry).ToList();
        StandingsCountLabel = Standings.Count.ToString();
    }

    private StandingEntry ToStandingEntry(TrackCar car)
    {
        var (classKey, classLabel) = BrandResolver.Classify(car.VehicleClass);
        var brand = BrandResolver.ResolveDisplay(
            car.VehFilename, vehicleName: car.VehicleName, vehicleClass: car.VehicleClass);
        var vehFile = FormatVehIdentity(car.VehFilename);
        return new StandingEntry
        {
            Place = car.Place,
            PlaceText = car.Place > 0 ? car.Place.ToString() : "—",
            Name = string.IsNullOrWhiteSpace(car.Name) ? "Driver" : car.Name,
            Brand = string.IsNullOrEmpty(brand) ? "—" : brand,
            VehFilename = vehFile,
            BrandHint = $"{(string.IsNullOrEmpty(brand) ? "—" : brand)}\n{vehFile}\n{car.VehicleName}",
            BrandLogo = _brandLogos.GetLogoForIdentity(
                car.VehFilename, vehicleName: car.VehicleName, vehicleClass: car.VehicleClass),
            ClassKey = classKey,
            ClassLabel = classLabel,
            LastLap = LapTimeFormat.Format(car.LastLapTime),
            BestLap = LapTimeFormat.Format(car.BestLapTime),
            Gap = FormatStandingGap(car),
            Pit = FormatStandingPit(car),
            InPits = car.InPits,
            IsPlayer = car.IsPlayer
        };
    }

    private static string FormatStandingPit(TrackCar car)
    {
        if (car.InPits)
            return "IN";
        return car.PitStops > 0 ? car.PitStops.ToString() : "—";
    }

    private static string FormatVehIdentity(string? vehFilename)
    {
        if (string.IsNullOrWhiteSpace(vehFilename))
            return "—";
        if (BrandResolver.IsOpaqueId(vehFilename))
            return $"id:{vehFilename.Trim()[..8]}…";
        return vehFilename.Trim();
    }

    private static string FormatStandingGap(TrackCar car)
    {
        if (car.Place <= 1)
            return "LEADER";

        if (car.LapsBehindLeader > 0)
            return $"+{car.LapsBehindLeader}L";

        if (car.TimeBehindLeader > 0.0005)
            return "+" + FormatGapSeconds(car.TimeBehindLeader);

        return "—";
    }

    private static string FormatGapSeconds(double seconds)
    {
        if (seconds < 60.0)
            return seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        return LapTimeFormat.Format(seconds);
    }

    private void SyncTireAdvice(TelemetrySnapshot snap)
    {
        var advice = TireCompoundAdvisor.Evaluate(snap);
        TireRecommended = advice.Recommended;
        TireConditionLabel = advice.ConditionLabel;
        TireAdviceReason = advice.Reason;
        TireFitted = advice.Fitted;
        WeatherTrackTemp = advice.TrackTempText;
        WeatherAmbientTemp = advice.AmbientTempText;
        WeatherRain = advice.RainText;
        WeatherWetness = advice.WetnessText;
        WeatherCloud = advice.CloudText;
    }

    private void SyncFlags(TelemetrySnapshot snap)
    {
        if (!snap.Connected)
        {
            FlagStatus = "—";
            FlagDetail = "No telemetry";
            return;
        }

        if (snap.GamePhase == 7)
        {
            FlagStatus = "RED / STOP";
            FlagDetail = "Session stopped";
            return;
        }

        if (snap.IsFullCourseYellow)
        {
            FlagStatus = "FCY";
            FlagDetail = DescribeYellowDetail(snap.YellowFlagState);
            return;
        }

        if (snap.IsBlueFlag)
        {
            FlagStatus = "BLUE";
            FlagDetail = "Let faster traffic past";
            return;
        }

        if (snap.HasLocalYellow)
        {
            var sectors = new List<string>(3);
            if (snap.SectorFlags.Length > 1 && snap.SectorFlags[1] > 0) sectors.Add("S1");
            if (snap.SectorFlags.Length > 2 && snap.SectorFlags[2] > 0) sectors.Add("S2");
            if (snap.SectorFlags.Length > 0 && snap.SectorFlags[0] > 0) sectors.Add("S3");
            FlagStatus = "LOCAL YELLOW";
            FlagDetail = sectors.Count == 0 ? "Caution on track" : string.Join(" · ", sectors);
            return;
        }

        if (snap.YellowFlagState > 0)
        {
            FlagStatus = "YELLOW";
            FlagDetail = DescribeYellowDetail(snap.YellowFlagState);
            return;
        }

        FlagStatus = "GREEN";
        FlagDetail = "No flags";
    }

    private static string DescribeYellowDetail(sbyte state) => state switch
    {
        1 => "Pending",
        2 => "Pits closed",
        3 => "Pit lead lap",
        4 => "Pits open",
        5 => "Last SC lap",
        6 => "Resume / restart",
        7 => "Race halt",
        _ => $"State {state}"
    };

    private void SyncTrackLayout(TelemetrySnapshot snap)
    {
        if (!snap.Connected || string.IsNullOrWhiteSpace(snap.Track))
        {
            CurrentTrackLayout = null;
            TrackMapModeLabel = "OVAL";
            TrackMapStatus = "Connect to LMU to load or learn a track layout.";
            return;
        }

        var layout = _trackLayouts.TryGet(snap.Track);
        CurrentTrackLayout = layout;

        if (layout?.HasOutline == true)
        {
            TrackMapModeLabel = "LEARNED";
            TrackMapStatus = "";
            return;
        }

        TrackMapModeLabel = "OVAL";
        TrackMapStatus =
            $"No learned layout for “{snap.Track}” yet — showing a temporary oval. " +
            "Developers: drive full on-track laps (not in pits) to record the outline; " +
            "it is saved under Tracks/ so everyone can use it. " +
            _trackLearner.Status;
    }

    private void OnTrackLayoutSaved(TrackLayout layout)
    {
        void Apply()
        {
            CurrentTrackLayout = layout;
            TrackMapModeLabel = "LEARNED";
            TrackMapStatus = "";
            LogLines.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Track layout saved: {layout.DisplayName} ({layout.Points.Count} pts)");
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private void OnTrackLearnerStatusChanged()
    {
        void Apply()
        {
            if (CurrentTrackLayout?.HasOutline == true)
                return;
            if (string.IsNullOrWhiteSpace(Track) || Track == "—")
                return;

            TrackMapStatus =
                $"No learned layout for “{Track}” yet — showing a temporary oval. " +
                "Developers: drive full on-track laps (not in pits) to record the outline; " +
                "it is saved under Tracks/ so everyone can use it. " +
                _trackLearner.Status;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    partial void OnAutoOpenSessionReportChanged(bool value)
    {
        if (!_speechRateReady)
            return;
        _prefs.SaveAutoOpenSessionReport(value);
    }

    private void OnSessionReportSaved(string path, SessionReport report)
    {
        void Apply()
        {
            LogLines.Insert(0,
                $"[{DateTime.Now:HH:mm:ss}] Session report saved · {report.Track} · {report.LapsCompleted} laps → {path}");

            if (AutoOpenSessionReport)
                OpenSessionReportRequested?.Invoke(path, report);
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private static string FormatTireWear(WheelTemps w)
    {
        static string Corner(string name, double wear, bool flat) =>
            flat ? $"{name} FLAT" : $"{name} {wear * 100:0}%";

        return $"{Corner("FL", w.FlWear, w.FlFlat)}  {Corner("FR", w.FrWear, w.FrFlat)}  " +
               $"{Corner("RL", w.RlWear, w.RlFlat)}  {Corner("RR", w.RrWear, w.RrFlat)}";
    }

    private static string ResolveAppVersion()
    {
        var informational = typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip optional "+<commit>" / metadata suffix from some SDK builds.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return typeof(MainViewModel).Assembly.GetName().Version?.ToString(4) ?? "—";
    }
}
