using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIngenior.Models;
using RaceIngenior.Services;
using SharpDX.DirectInput;

namespace RaceIngenior.Radio;

public sealed class PttDeviceOption
{
    public Guid InstanceGuid { get; init; }
    public string Name { get; init; } = "";
    public override string ToString() => Name;
}

public sealed class WheelPttService : BackgroundService
{
    /// <summary>Button poll interval while a device is acquired (responsive PTT).</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>
    /// DirectInput device enumeration is expensive on Windows and can stall the machine
    /// if hammered when no wheel is present — only re-scan this often while searching.
    /// </summary>
    private static readonly TimeSpan DeviceSearchInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger<WheelPttService> _logger;
    private readonly UserPreferencesStore _prefs;
    private readonly DirectInput _directInput = new();
    private readonly object _sync = new();

    private Joystick? _joystick;
    private Guid _boundGuid = Guid.Empty;
    private string _boundName = "";
    private int _buttonOffset;
    private string? _deviceNameFilter;
    private Guid? _preferredGuid;
    private bool _wasPressed;
    private bool _learning;
    private DateTimeOffset _learnStarted;
    private DateTimeOffset _nextDeviceSearch = DateTimeOffset.MinValue;

    public event Action? PttPressed;
    public event Action? PttReleased;
    public event Action? BindingChanged;

    public bool IsPressed { get; private set; }
    public bool IsLearning => _learning;
    public string Status { get; private set; } = "Searching wheel…";
    public int ButtonOffset => _buttonOffset;
    public string BoundDeviceName => _boundName;
    public Guid BoundDeviceGuid => _boundGuid;

    public WheelPttService(
        IOptions<AppSettings> settings,
        UserPreferencesStore prefs,
        ILogger<WheelPttService> logger)
    {
        _prefs = prefs;
        _logger = logger;

        var s = settings.Value;
        _buttonOffset = prefs.Current.PttJoystickOffset > 0 || prefs.Current.PttDeviceInstanceGuid is not null
            ? prefs.Current.PttJoystickOffset
            : s.PttJoystickOffset ?? 0;
        _deviceNameFilter = prefs.Current.PttDeviceNameContains ?? s.PttDeviceNameContains;

        if (Guid.TryParse(prefs.Current.PttDeviceInstanceGuid, out var g))
            _preferredGuid = g;
    }

    public IReadOnlyList<PttDeviceOption> ListDevices()
    {
        return EnumerateDevices()
            .Select(d => new PttDeviceOption
            {
                InstanceGuid = d.InstanceGuid,
                Name = d.InstanceName
            })
            .ToList();
    }

    public void ApplyBinding(Guid? deviceGuid, string? deviceName, int buttonOffset, bool persist = true)
    {
        lock (_sync)
        {
            _buttonOffset = Math.Clamp(buttonOffset, 0, 127);
            _deviceNameFilter = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
            _preferredGuid = deviceGuid is { } g && g != Guid.Empty ? g : null;
            _learning = false;
            DisposeJoystick();
            // Re-bind promptly after the user picks a device / button.
            _nextDeviceSearch = DateTimeOffset.MinValue;
        }

        if (persist)
            _prefs.SavePtt(_buttonOffset, _deviceNameFilter, _preferredGuid);

        Status = $"PTT set: button {_buttonOffset}" +
                 (_deviceNameFilter is null ? "" : $" on {_deviceNameFilter}");
        _logger.LogInformation("{Status}", Status);
        BindingChanged?.Invoke();
    }

    public void StartLearnMode()
    {
        lock (_sync)
        {
            _learning = true;
            _learnStarted = DateTimeOffset.UtcNow;
            _wasPressed = false;
            IsPressed = false;
        }

        Status = "Learning: press the wheel button to use as PTT…";
        BindingChanged?.Invoke();
    }

    public void CancelLearnMode()
    {
        lock (_sync) _learning = false;
        RefreshStatus();
        BindingChanged?.Invoke();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_joystick is null)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now >= _nextDeviceSearch)
                    {
                        TryBindDevice();
                        if (_joystick is null)
                            _nextDeviceSearch = now + DeviceSearchInterval;
                    }
                }

                if (_joystick is not null)
                    Poll();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "PTT poll error");
                DisposeJoystick();
                _nextDeviceSearch = DateTimeOffset.UtcNow + DeviceSearchInterval;
                Status = "Wheel disconnected";
                BindingChanged?.Invoke();
            }

            // Keep a short tick only when polling buttons; otherwise sleep until next search.
            var delay = _joystick is not null
                ? PollInterval
                : TimeSpan.FromMilliseconds(Math.Clamp(
                    (_nextDeviceSearch - DateTimeOffset.UtcNow).TotalMilliseconds,
                    50,
                    DeviceSearchInterval.TotalMilliseconds));

            await Task.Delay(delay, stoppingToken);
        }
    }

    private IEnumerable<DeviceInstance> EnumerateDevices() =>
        _directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AttachedOnly)
            .Concat(_directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly))
            .Concat(_directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));

    private void TryBindDevice()
    {
        var devices = EnumerateDevices().ToList();
        if (devices.Count == 0)
        {
            if (Status != "No wheel/joystick found")
            {
                Status = "No wheel/joystick found";
                BindingChanged?.Invoke();
            }

            return;
        }

        DeviceInstance? chosen = null;
        if (_preferredGuid is Guid pref)
            chosen = devices.FirstOrDefault(d => d.InstanceGuid == pref);

        chosen ??= devices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(_deviceNameFilter)
            && d.InstanceName.Contains(_deviceNameFilter, StringComparison.OrdinalIgnoreCase));

        chosen ??= devices[0];

        _joystick = new Joystick(_directInput, chosen.InstanceGuid);
        _joystick.Properties.BufferSize = 128;
        _joystick.Acquire();
        _boundGuid = chosen.InstanceGuid;
        _boundName = chosen.InstanceName;
        RefreshStatus();
        _logger.LogInformation("{Status}", Status);
        BindingChanged?.Invoke();
    }

    private void Poll()
    {
        if (_joystick is null)
            return;

        _joystick.Poll();
        var state = _joystick.GetCurrentState();
        var buttons = state.Buttons;

        if (_learning)
        {
            // Ignore first 300ms to avoid capturing the UI click
            if (DateTimeOffset.UtcNow - _learnStarted < TimeSpan.FromMilliseconds(350))
                return;

            for (var i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i])
                    continue;

                ApplyBinding(_boundGuid, _boundName, i, persist: true);
                Status = $"Learned PTT: {_boundName} button {i}";
                BindingChanged?.Invoke();
                return;
            }

            return;
        }

        var offset = _buttonOffset;
        if (offset < 0 || offset >= buttons.Length)
            offset = 0;

        var pressed = buttons[offset];
        IsPressed = pressed;

        if (pressed && !_wasPressed)
            PttPressed?.Invoke();
        else if (!pressed && _wasPressed)
            PttReleased?.Invoke();

        _wasPressed = pressed;
    }

    private void RefreshStatus()
    {
        if (_learning)
        {
            Status = "Learning: press the wheel button to use as PTT…";
            return;
        }

        Status = string.IsNullOrEmpty(_boundName)
            ? $"PTT button {_buttonOffset} (no device)"
            : $"PTT on: {_boundName} (button {_buttonOffset})";
    }

    private void DisposeJoystick()
    {
        try { _joystick?.Unacquire(); } catch { /* ignore */ }
        _joystick?.Dispose();
        _joystick = null;
        _wasPressed = false;
        IsPressed = false;
        _boundGuid = Guid.Empty;
        _boundName = "";
    }

    public override void Dispose()
    {
        DisposeJoystick();
        _directInput.Dispose();
        base.Dispose();
    }
}
