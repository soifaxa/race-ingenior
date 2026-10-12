using System.Windows;
using System.Windows.Input;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class RecordsWindow : Window
{
    private readonly RecordsBrowserViewModel _vm;

    public RecordsWindow(RecordsBrowserViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

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
}
