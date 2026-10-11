using System.Windows;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        TrayIcon.Icon = System.Drawing.SystemIcons.Information;
        TrayIcon.Visibility = Visibility.Visible;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        };
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

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowDashboard()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Maximized;
        Activate();
    }
}
