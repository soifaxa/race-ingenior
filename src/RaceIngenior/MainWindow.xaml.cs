using System.ComponentModel;
using System.Windows;
using RaceIngenior.Services;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly UserPreferencesStore _prefs;
    private bool _restoringPlacement;
    private bool _forceClose;

    public MainWindow(MainViewModel viewModel, UserPreferencesStore prefs)
    {
        _viewModel = viewModel;
        _prefs = prefs;
        InitializeComponent();
        DataContext = viewModel;
        TrayIcon.Icon = System.Drawing.SystemIcons.Information;
        TrayIcon.Visibility = Visibility.Visible;
        RestorePlacement();
        LocationChanged += (_, _) => PersistPlacement();
        SizeChanged += (_, _) => PersistPlacement();
        StateChanged += (_, _) => PersistPlacement();
        Closing += OnClosing;
    }

    private void TrayIcon_OnTrayMouseDoubleClick(object sender, RoutedEventArgs e) => ShowDashboard();

    private void Show_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void Overlay_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ToggleOverlayCommand.Execute(null);

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
