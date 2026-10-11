using System.Windows;
using RaceIngenior.Models;
using RaceIngenior.Telemetry;
using RaceIngenior.ViewModels;

namespace RaceIngenior.Services;

public sealed class OverlayService
{
    private readonly OverlayViewModel _viewModel;
    private readonly UserPreferencesStore _prefs;
    private readonly TelemetryHub _hub;
    private OverlayWindow? _window;
    private bool _userEnabled;
    private bool _contextAllows = true;

    public bool IsVisible => _window is { IsVisible: true };
    public bool IsUserEnabled => _userEnabled;

    public OverlayService(OverlayViewModel viewModel, UserPreferencesStore prefs, TelemetryHub hub)
    {
        _viewModel = viewModel;
        _prefs = prefs;
        _hub = hub;
        _userEnabled = prefs.Current.OverlayVisible;
        _hub.Updated += OnTelemetry;
        _contextAllows = ContextAllows(_hub.Latest);
    }

    public void Show()
    {
        _userEnabled = true;
        EnsureWindow();
        if (_window is not null)
            _prefs.SaveOverlay(true, _window.Left, _window.Top, _prefs.Current.OverlayClickThrough);
        ApplyVisibility();
    }

    public void Hide()
    {
        _userEnabled = false;
        if (_window is not null)
            _prefs.SaveOverlay(false, _window.Left, _window.Top, _prefs.Current.OverlayClickThrough);
        ApplyVisibility();
    }

    public void Toggle()
    {
        if (_userEnabled)
            Hide();
        else
            Show();
    }

    public void SetClickThrough(bool enabled)
    {
        EnsureWindow();
        _window?.SetClickThrough(enabled);
        if (_window is not null)
            _prefs.SaveOverlay(_userEnabled, _window.Left, _window.Top, enabled);
    }

    public void RestoreFromPreferences()
    {
        _userEnabled = _prefs.Current.OverlayVisible;
        _contextAllows = ContextAllows(_hub.Latest);
        if (_userEnabled)
            EnsureWindow();
        ApplyVisibility();
    }

    public void PersistPosition()
    {
        if (_window is null)
            return;
        _prefs.SaveOverlay(_userEnabled, _window.Left, _window.Top, _prefs.Current.OverlayClickThrough);
    }

    private void OnTelemetry(TelemetrySnapshot snap)
    {
        var allows = ContextAllows(snap);
        if (allows == _contextAllows && _window is not null)
        {
            // Still refresh show/hide if window state drifted
            var want = _userEnabled && allows;
            if (want == IsVisible)
                return;
        }

        _contextAllows = allows;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyVisibility();
        else
            _ = dispatcher.InvokeAsync(ApplyVisibility);
    }

    private void ApplyVisibility()
    {
        if (!_userEnabled)
        {
            _window?.Hide();
            return;
        }

        EnsureWindow();
        if (_window is null)
            return;

        if (_contextAllows)
        {
            if (!_window.IsVisible)
                _window.Show();
        }
        else if (_window.IsVisible)
        {
            _window.Hide();
        }
    }

    private static bool ContextAllows(TelemetrySnapshot snap)
    {
        if (!snap.Connected)
            return false;
        // Hide only in the box/garage stall — keep visible in the pit lane.
        if (snap.InPitBox)
            return false;
        // Pause / not in realtime session view
        if (snap.IsPaused)
            return false;
        return true;
    }

    private void EnsureWindow()
    {
        if (_window is not null)
            return;

        _window = new OverlayWindow(_viewModel)
        {
            Left = _prefs.Current.OverlayLeft ?? SystemParameters.WorkArea.Right - 240,
            Top = _prefs.Current.OverlayTop ?? SystemParameters.WorkArea.Bottom - 200
        };
        _window.LocationChanged += (_, _) => PersistPosition();
        _window.SetClickThrough(_prefs.Current.OverlayClickThrough);
    }
}
