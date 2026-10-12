using System.Windows;
using System.Windows.Threading;
using RaceIngenior.Models;
using RaceIngenior.Radio;
using RaceIngenior.Telemetry;
using RaceIngenior.ViewModels;

namespace RaceIngenior.Services;

public sealed class OverlayService
{
    private readonly OverlayViewModel _viewModel;
    private readonly UserPreferencesStore _prefs;
    private readonly TelemetryHub _hub;
    private readonly RadioPipelineService _radio;
    private OverlayWindow? _window;
    private ConversationOverlayWindow? _conversationWindow;
    private DispatcherTimer? _conversationHideTimer;
    private bool _userEnabled;
    private bool _contextAllows = true;
    private bool _forceShow;

    public bool IsVisible => _window is { IsVisible: true };
    public bool IsUserEnabled => _userEnabled;
    public bool IsForceShow => _forceShow;

    public OverlayService(
        OverlayViewModel viewModel,
        UserPreferencesStore prefs,
        TelemetryHub hub,
        RadioPipelineService radio)
    {
        _viewModel = viewModel;
        _prefs = prefs;
        _hub = hub;
        _radio = radio;
        _userEnabled = prefs.Current.OverlayVisible;
        _forceShow = prefs.Current.OverlayForceShow;
        _hub.Updated += OnTelemetry;
        _radio.ExchangeUpdated += OnRadioExchange;
        _contextAllows = ContextAllows(_hub.Latest);
    }

    public void Show()
    {
        _userEnabled = true;
        EnsureWindow();
        PersistTyreOverlay();
        ApplyVisibility();
    }

    public void Hide()
    {
        _userEnabled = false;
        PersistTyreOverlay();
        HideConversation();
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
        _conversationWindow?.SetClickThrough(enabled);
        PersistTyreOverlay();
    }

    public void SetForceShow(bool enabled)
    {
        _forceShow = enabled;
        _prefs.SaveOverlayForceShow(enabled);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            ApplyForceShow();
        else
            _ = dispatcher.InvokeAsync(ApplyForceShow);
    }

    public void RestoreFromPreferences()
    {
        _userEnabled = _prefs.Current.OverlayVisible;
        _forceShow = _prefs.Current.OverlayForceShow;
        _contextAllows = ContextAllows(_hub.Latest);
        if (_userEnabled || _forceShow)
            EnsureWindow();
        ApplyVisibility();
        if (_forceShow)
            ApplyForceShow();
    }

    public void PersistPosition()
    {
        PersistTyreOverlay();
    }

    public void ShowConversation(string driver, string engineer)
    {
        if (string.IsNullOrWhiteSpace(driver) && string.IsNullOrWhiteSpace(engineer))
            return;

        // User disabled overlays — keep toast quiet unless Force show is on for layout.
        if (!_userEnabled && !_forceShow)
            return;

        void Show()
        {
            _viewModel.SetConversation(driver, engineer);
            EnsureConversationWindow();
            if (_conversationWindow is null)
                return;

            if (!_conversationWindow.IsVisible)
                _conversationWindow.Show();

            // Stay up while force-show is used for layout / placement.
            if (_forceShow)
            {
                _conversationHideTimer?.Stop();
                return;
            }

            var seconds = Math.Clamp(_prefs.Current.ConversationOverlaySeconds, 5, 10);
            _conversationHideTimer ??= new DispatcherTimer();
            _conversationHideTimer.Stop();
            _conversationHideTimer.Interval = TimeSpan.FromSeconds(seconds);
            _conversationHideTimer.Tick -= OnConversationHideTick;
            _conversationHideTimer.Tick += OnConversationHideTick;
            _conversationHideTimer.Start();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Show();
        else
            _ = dispatcher.InvokeAsync(Show);
    }

    private void HideConversation()
    {
        _conversationHideTimer?.Stop();
        _conversationWindow?.Hide();
    }

    private void OnRadioExchange(string driver, string engineer) =>
        ShowConversation(driver, engineer);

    private void OnConversationHideTick(object? sender, EventArgs e)
    {
        if (_forceShow)
            return;
        _conversationHideTimer?.Stop();
        _conversationWindow?.Hide();
    }

    private void ApplyForceShow()
    {
        if (_forceShow)
        {
            EnsureWindow();
            EnsureConversationWindow();
            if (_conversationWindow is not null)
            {
                var driver = _viewModel.ConversationDriver;
                var engineer = _viewModel.ConversationEngineer;
                if (driver is "—" or "" || engineer is "—" or "")
                {
                    _viewModel.SetConversation(
                        "Sample driver call — drag to place",
                        "Sample engineer reply — stays while Force show is on");
                }

                _conversationHideTimer?.Stop();
                if (!_conversationWindow.IsVisible)
                    _conversationWindow.Show();
            }

            ApplyVisibility();
            return;
        }

        HideConversation();
        ApplyVisibility();
    }

    private void OnTelemetry(TelemetrySnapshot snap)
    {
        var allows = ContextAllows(snap);
        if (allows == _contextAllows && _window is not null)
        {
            var want = ShouldShowTyreOverlay(allows);
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

    private bool ShouldShowTyreOverlay(bool contextAllows) =>
        _forceShow || (_userEnabled && contextAllows);

    private void ApplyVisibility()
    {
        if (!ShouldShowTyreOverlay(_contextAllows))
        {
            _window?.Hide();
            return;
        }

        EnsureWindow();
        if (_window is null)
            return;

        if (!_window.IsVisible)
            _window.Show();
    }

    private static bool ContextAllows(TelemetrySnapshot snap)
    {
        if (!snap.Connected)
            return false;
        if (snap.InPitBox)
            return false;
        if (snap.IsPaused)
            return false;
        return true;
    }

    private void PersistTyreOverlay()
    {
        if (_window is null)
            return;
        _prefs.SaveOverlay(
            _userEnabled,
            _window.Left,
            _window.Top,
            _window.Scale,
            _prefs.Current.OverlayClickThrough);
    }

    private void PersistConversationPosition()
    {
        if (_conversationWindow is null)
            return;
        _prefs.SaveConversationOverlay(_conversationWindow.Left, _conversationWindow.Top);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
            return;

        _window = new OverlayWindow(_viewModel)
        {
            Left = _prefs.Current.OverlayLeft ?? SystemParameters.WorkArea.Right - 250,
            Top = _prefs.Current.OverlayTop ?? SystemParameters.WorkArea.Bottom - 240
        };
        _window.SetScale(_prefs.Current.OverlayScale);
        _window.LocationChanged += (_, _) => PersistTyreOverlay();
        _window.ScaleChanged += scale =>
        {
            if (_window is null)
                return;
            _prefs.SaveOverlay(
                _userEnabled,
                _window.Left,
                _window.Top,
                scale,
                _prefs.Current.OverlayClickThrough);
        };
        _window.SetClickThrough(_prefs.Current.OverlayClickThrough);
    }

    private void EnsureConversationWindow()
    {
        if (_conversationWindow is not null)
            return;

        _conversationWindow = new ConversationOverlayWindow(_viewModel)
        {
            Left = _prefs.Current.ConversationLeft
                   ?? SystemParameters.WorkArea.Left + 24,
            Top = _prefs.Current.ConversationTop
                  ?? SystemParameters.WorkArea.Bottom - 180
        };
        _conversationWindow.LocationChanged += (_, _) => PersistConversationPosition();
        _conversationWindow.SetClickThrough(_prefs.Current.OverlayClickThrough);
    }
}
