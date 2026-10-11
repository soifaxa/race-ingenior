using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;

    private bool _clickThrough;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => ApplyExtendedStyles();
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        ApplyExtendedStyles();
        if (enabled)
            Cursor = System.Windows.Input.Cursors.None;
        else
            Cursor = System.Windows.Input.Cursors.SizeAll;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough)
            return;
        DragMove();
    }

    private void ApplyExtendedStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var style = GetWindowLong(hwnd, GwlExstyle);
        style |= WsExToolwindow | WsExNoactivate;
        if (_clickThrough)
            style |= WsExTransparent;
        else
            style &= ~WsExTransparent;
        SetWindowLong(hwnd, GwlExstyle, style);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
