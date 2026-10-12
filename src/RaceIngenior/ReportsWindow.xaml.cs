using System.Windows;
using System.Windows.Input;
using RaceIngenior.Models;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class ReportsWindow : Window
{
    private readonly ReportsBrowserViewModel _vm;

    public event Action<string, SessionReport>? OpenReportRequested;

    public ReportsWindow(ReportsBrowserViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;
        _vm.OpenReportRequested += (path, report) => OpenReportRequested?.Invoke(path, report);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    public void RefreshList() => _vm.RefreshCommand.Execute(null);

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void List_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.HasSelection)
            _vm.OpenSelectedCommand.Execute(null);
    }
}
