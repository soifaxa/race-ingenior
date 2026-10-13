using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using RaceIngenior.Models;
using RaceIngenior.Services;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly UserPreferencesStore _prefs;
    private readonly SessionReportService _sessionReports;
    private readonly TrackRecordsStore _trackRecords;
    private readonly BrandLogoStore _brandLogos;
    private SettingsWindow? _settingsWindow;
    private ReportsWindow? _reportsWindow;
    private RecordsWindow? _recordsWindow;
    private TrackMapWindow? _trackMapWindow;
    private SessionReportWindow? _reportWindow;
    private bool _restoringPlacement;
    private bool _forceClose;

    public MainWindow(
        MainViewModel viewModel,
        UserPreferencesStore prefs,
        SessionReportService sessionReports,
        TrackRecordsStore trackRecords,
        BrandLogoStore brandLogos)
    {
        _viewModel = viewModel;
        _prefs = prefs;
        _sessionReports = sessionReports;
        _trackRecords = trackRecords;
        _brandLogos = brandLogos;
        InitializeComponent();
        DataContext = viewModel;
        TrayIcon.Icon = System.Drawing.SystemIcons.Information;
        TrayIcon.Visibility = Visibility.Visible;
        RestorePlacement();
        LocationChanged += (_, _) => PersistPlacement();
        SizeChanged += (_, _) => PersistPlacement();
        StateChanged += (_, _) => PersistPlacement();
        Closing += OnClosing;
        _viewModel.OpenSessionReportRequested += OpenSessionReport;
    }

    private void OpenSessionReport(string path, SessionReport report)
    {
        if (_reportWindow is not null)
        {
            _reportWindow.Close();
            _reportWindow = null;
        }

        _reportWindow = new SessionReportWindow(path, report)
        {
            Owner = IsVisible ? this : null
        };
        _reportWindow.Closed += (_, _) => _reportWindow = null;
        _reportWindow.Show();
        _reportWindow.Activate();
        _reportsWindow?.RefreshList();
    }

    private void TrayIcon_OnTrayMouseDoubleClick(object sender, RoutedEventArgs e) => ShowDashboard();

    private void Show_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Reports_Click(object sender, RoutedEventArgs e) => OpenReports();

    private void Records_Click(object sender, RoutedEventArgs e) => OpenRecords();

    private void TrackMap_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        OpenTrackMap();
        e.Handled = true;
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void Overlay_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ToggleOverlayCommand.Execute(null);

    private void OpenReports()
    {
        if (_reportsWindow is not null)
        {
            _reportsWindow.RefreshList();
            _reportsWindow.Activate();
            return;
        }

        var vm = new ReportsBrowserViewModel(_sessionReports);
        _reportsWindow = new ReportsWindow(vm)
        {
            Owner = IsVisible ? this : null
        };
        _reportsWindow.OpenReportRequested += OpenSessionReport;
        _reportsWindow.Closed += (_, _) => _reportsWindow = null;
        _reportsWindow.Show();
        _reportsWindow.Activate();
    }

    private void OpenRecords()
    {
        if (_recordsWindow is not null)
        {
            _recordsWindow.RefreshList();
            _recordsWindow.Activate();
            return;
        }

        var vm = new RecordsBrowserViewModel(_trackRecords, _sessionReports, _brandLogos);
        _recordsWindow = new RecordsWindow(vm)
        {
            Owner = IsVisible ? this : null
        };
        _recordsWindow.Closed += (_, _) => _recordsWindow = null;
        _recordsWindow.Show();
        _recordsWindow.Activate();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_viewModel)
        {
            Owner = IsVisible ? this : null
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OpenTrackMap()
    {
        if (_trackMapWindow is not null)
        {
            _trackMapWindow.Activate();
            return;
        }

        _trackMapWindow = new TrackMapWindow(_viewModel)
        {
            Owner = IsVisible ? this : null
        };
        _trackMapWindow.Closed += (_, _) => _trackMapWindow = null;
        _trackMapWindow.Show();
        _trackMapWindow.Activate();
    }

    private void Restart_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RestartAppCommand.Execute(null);

    private void Exit_Click(object sender, RoutedEventArgs e) => RequestExit();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        PersistPlacement();

        if (_forceClose)
            return;

        e.Cancel = true;
        RequestExit();
    }

    private void RequestExit()
    {
        if (_forceClose)
            return;

        var dialog = new ConfirmExitWindow
        {
            Owner = IsVisible ? this : null
        };

        if (dialog.ShowDialog() != true)
            return;

        _forceClose = true;
        PersistPlacement();
        TrayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowDashboard()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = _prefs.Current.MainWindowMaximized
            ? WindowState.Maximized
            : WindowState.Normal;
        Activate();
    }

    private void RestorePlacement()
    {
        var p = _prefs.Current;
        if (p.MainWindowWidth is not > 0 || p.MainWindowHeight is not > 0)
            return;

        _restoringPlacement = true;
        try
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = Math.Max(MinWidth, p.MainWindowWidth.Value);
            Height = Math.Max(MinHeight, p.MainWindowHeight.Value);
            Left = p.MainWindowLeft ?? Left;
            Top = p.MainWindowTop ?? Top;

            var bounds = new Rect(Left, Top, Width, Height);
            if (!IsOnVirtualScreen(bounds))
            {
                Left = SystemParameters.WorkArea.Left + 40;
                Top = SystemParameters.WorkArea.Top + 40;
            }

            WindowState = p.MainWindowMaximized ? WindowState.Maximized : WindowState.Normal;
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void PersistPlacement()
    {
        if (_restoringPlacement || !IsLoaded)
            return;

        // Keep last Normal/Maximized geometry while minimized or hidden to tray.
        if (WindowState == WindowState.Minimized)
            return;

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _prefs.SaveMainWindow(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowState == WindowState.Maximized);
    }

    private static bool IsOnVirtualScreen(Rect bounds)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(bounds.Left, bounds.Top, Math.Max(1, bounds.Width), 40);
        return screen.IntersectsWith(titleBar);
    }
}
