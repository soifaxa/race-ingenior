using System.Windows;
using System.Windows.Input;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class TrackMapWindow : Window
{
    public TrackMapWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        StateChanged += (_, _) => SyncMaximizeChrome();
        SyncMaximizeChrome();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11)
            {
                ToggleMaximize();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Escape)
                return;

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                e.Handled = true;
                return;
            }

            Close();
        };
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
            return;

        DragMove();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void SyncMaximizeChrome()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        ResizeMode = maximized ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    }
}
