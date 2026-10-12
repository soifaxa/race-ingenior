using System.Windows;
using System.Windows.Input;

namespace RaceIngenior;

public partial class ConfirmExitWindow : Window
{
    public ConfirmExitWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
