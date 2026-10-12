using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RaceIngenior.ViewModels;

namespace RaceIngenior;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolwindow = 0x80;
    private const int WsExNoactivate = 0x08000000;

    private bool _clickThrough;
    private double _scale = 1.0;

    public event Action<double>? ScaleChanged;

    public double Scale => _scale;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => ApplyExtendedStyles();
    }

    public void SetScale(double scale)
    {
        _scale = Math.Clamp(scale, 0.6, 2.0);
        Root.LayoutTransform = new ScaleTransform(_scale, _scale);
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

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_clickThrough)
            return;

        var step = e.Delta > 0 ? 0.1 : -0.1;
        var next = Math.Clamp(_scale + step, 0.6, 2.0);
        if (Math.Abs(next - _scale) < 0.001)
            return;

        SetScale(next);
        ScaleChanged?.Invoke(_scale);
        e.Handled = true;
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
