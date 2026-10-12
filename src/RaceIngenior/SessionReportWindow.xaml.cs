using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using RaceIngenior.Models;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class SessionReportWindow : Window
{
    private readonly string _reportPath;

    public SessionReportWindow(string reportPath, SessionReport report)
    {
        InitializeComponent();
        _reportPath = reportPath;
        PathText.Text = reportPath;
        DataContext = new SessionReportViewModel(report);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(_reportPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_reportPath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }
}
