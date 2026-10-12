using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RaceIngenior.Models;

namespace RaceIngenior.Controls;

public partial class TrackMapControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty CarsProperty = DependencyProperty.Register(
        nameof(Cars), typeof(IEnumerable), typeof(TrackMapControl),
        new PropertyMetadata(null, (d, _) => ((TrackMapControl)d).Redraw()));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(TrackLayout), typeof(TrackMapControl),
        new PropertyMetadata(null, (d, _) => ((TrackMapControl)d).Redraw()));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage), typeof(string), typeof(TrackMapControl),
        new PropertyMetadata("", (d, _) => ((TrackMapControl)d).UpdateBanner()));

    private static readonly SolidColorBrush TrackStroke = MakeBrush("#3A3A3A");
    private static readonly SolidColorBrush TrackFill = MakeBrush("#0E0E0E");
    private static readonly SolidColorBrush OutlineStroke = MakeBrush("#888888");
    private static readonly SolidColorBrush HypercarBrush = MakeBrush("#E10600");
    private static readonly SolidColorBrush Lmp2Brush = MakeBrush("#2E7DFF");
    private static readonly SolidColorBrush Lmgt3Brush = MakeBrush("#22C55E");
    private static readonly SolidColorBrush UnknownBrush = MakeBrush("#AAAAAA");
    private static readonly SolidColorBrush LabelBrush = MakeBrush("#F2F2F2");
    private static readonly SolidColorBrush SfBrush = MakeBrush("#E10600");
    private static readonly SolidColorBrush PlayerStroke = MakeBrush("#FFFFFF");

    public TrackMapControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    public IEnumerable? Cars
    {
        get => (IEnumerable?)GetValue(CarsProperty);
        set => SetValue(CarsProperty, value);
    }

    public TrackLayout? Layout
    {
        get => (TrackLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    private void UpdateBanner()
    {
        if (FallbackBanner is null || FallbackText is null)
            return;

        var hasLayout = Layout?.HasOutline == true;
        if (hasLayout || string.IsNullOrWhiteSpace(StatusMessage))
        {
            FallbackBanner.Visibility = Visibility.Collapsed;
            return;
        }

        FallbackText.Text = StatusMessage;
        FallbackBanner.Visibility = Visibility.Visible;
    }

    private void Redraw()
    {
        if (MapCanvas is null)
            return;

        MapCanvas.Children.Clear();
        UpdateBanner();

        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 40 || h < 40)
            return;

        var list = Cars?.OfType<TrackCar>().ToList() ?? [];
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (Layout?.HasOutline == true)
            DrawLearnedMap(w, h, list, Layout);
        else
            DrawOvalFallback(w, h, list);
    }

    private void DrawLearnedMap(double w, double h, List<TrackCar> cars, TrackLayout layout)
    {
        var pts = layout.Points.OrderBy(p => p.T).ToList();
        if (pts.Count < 2)
        {
            DrawOvalFallback(w, h, cars);
            return;
        }

        var minX = pts.Min(p => p.X);
        var maxX = pts.Max(p => p.X);
        var minZ = pts.Min(p => p.Z);
        var maxZ = pts.Max(p => p.Z);
        var spanX = Math.Max(1.0, maxX - minX);
        var spanZ = Math.Max(1.0, maxZ - minZ);
        var pad = 28.0;
        var scale = Math.Min((w - pad * 2) / spanX, (h - pad * 2 - 18) / spanZ);
        var ox = (w - spanX * scale) / 2;
        var oy = (h - spanZ * scale) / 2 - 4;

        System.Windows.Point Map(double x, double z) =>
            new(ox + (x - minX) * scale, oy + (maxZ - z) * scale);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var first = Map(pts[0].X, pts[0].Z);
            ctx.BeginFigure(first, false, false);
            for (var i = 1; i < pts.Count; i++)
                ctx.LineTo(Map(pts[i].X, pts[i].Z), true, false);
            // Close toward start if coverage wraps.
            if (pts[0].T < 0.05 && pts[^1].T > 0.9)
                ctx.LineTo(first, true, false);
        }
        geo.Freeze();

        MapCanvas.Children.Add(new Path
        {
            Data = geo,
            Stroke = OutlineStroke,
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        // S/F near t≈0
        var sf = Interpolate(pts, 0);
        var sfp = Map(sf.X, sf.Z);
        var sfDot = new Ellipse { Width = 6, Height = 6, Fill = SfBrush };
        Canvas.SetLeft(sfDot, sfp.X - 3);
        Canvas.SetTop(sfDot, sfp.Y - 3);
        MapCanvas.Children.Add(sfDot);

        foreach (var car in cars)
            DrawCarDot(Interpolate(pts, car.LapDistFraction), Map, car);
    }

    private void DrawOvalFallback(double w, double h, List<TrackCar> cars)
    {
        var cx = w * 0.5;
        var cy = h * 0.52;
        var rx = Math.Max(20, w * 0.38);
        var ry = Math.Max(16, h * 0.30);

        var track = new Ellipse
        {
            Width = rx * 2,
            Height = ry * 2,
            Stroke = TrackStroke,
            StrokeThickness = 3,
            Fill = TrackFill
        };
        Canvas.SetLeft(track, cx - rx);
        Canvas.SetTop(track, cy - ry);
        MapCanvas.Children.Add(track);

        MapCanvas.Children.Add(new Line
        {
            X1 = cx,
            Y1 = cy - ry - 6,
            X2 = cx,
            Y2 = cy - ry + 6,
            Stroke = SfBrush,
            StrokeThickness = 2
        });

        foreach (var car in cars)
        {
            var angle = car.LapDistFraction * Math.PI * 2.0 - Math.PI / 2.0;
            var x = cx + rx * Math.Cos(angle);
            var y = cy + ry * Math.Sin(angle);
            PlaceDot(x, y, car);
        }
    }

    private void DrawCarDot(TrackLayoutPoint world, Func<double, double, System.Windows.Point> map, TrackCar car)
    {
        var p = map(world.X, world.Z);
        PlaceDot(p.X, p.Y, car);
    }

    private void PlaceDot(double x, double y, TrackCar car)
    {
        var size = car.IsPlayer ? 12.0 : 9.0;
        var fill = BrushForClass(car.VehicleClass);

        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = car.IsPlayer ? PlayerStroke : null,
            StrokeThickness = car.IsPlayer ? 1.5 : 0,
            Opacity = car.InPits ? 0.55 : 1.0,
            ToolTip = FormatTip(car)
        };
        Canvas.SetLeft(dot, x - size / 2);
        Canvas.SetTop(dot, y - size / 2);
        MapCanvas.Children.Add(dot);

        if (car.Place > 0)
        {
            var label = new TextBlock
            {
                Text = car.Place.ToString(),
                Foreground = LabelBrush,
                FontSize = car.IsPlayer ? 10 : 9,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false
            };
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, y - size / 2 - label.DesiredSize.Height - 1);
            MapCanvas.Children.Add(label);
        }
    }

    private static TrackLayoutPoint Interpolate(List<TrackLayoutPoint> pts, double t)
    {
        t %= 1.0;
        if (t < 0) t += 1.0;

        if (t <= pts[0].T)
            return pts[0];
        if (t >= pts[^1].T)
            return pts[^1];

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            if (t < a.T || t > b.T)
                continue;

            var span = Math.Max(1e-6, b.T - a.T);
            var u = (t - a.T) / span;
            return new TrackLayoutPoint
            {
                T = t,
                X = a.X + (b.X - a.X) * u,
                Z = a.Z + (b.Z - a.Z) * u
            };
        }

        return pts[^1];
    }

    internal static SolidColorBrush BrushForClass(string? vehicleClass)
    {
        var c = (vehicleClass ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0)
            return UnknownBrush;

        if (c.Contains("HYPER", StringComparison.Ordinal)
            || c.Contains("LMH", StringComparison.Ordinal)
            || c.Contains("LMDH", StringComparison.Ordinal)
            || c.Contains("GTP", StringComparison.Ordinal))
            return HypercarBrush;

        if (c.Contains("LMP2", StringComparison.Ordinal))
            return Lmp2Brush;

        if (c.Contains("LMGT3", StringComparison.Ordinal)
            || c.Contains("GT3", StringComparison.Ordinal)
            || c.Contains("GTE", StringComparison.Ordinal))
            return Lmgt3Brush;

        return UnknownBrush;
    }

    private static string FormatTip(TrackCar car)
    {
        var name = string.IsNullOrWhiteSpace(car.Name) ? "Driver" : car.Name;
        var place = car.Place > 0 ? $"P{car.Place}" : "P—";
        var cls = string.IsNullOrWhiteSpace(car.VehicleClass) ? "" : $" · {car.VehicleClass}";
        var where = car.InPits ? " · pits" : "";
        var you = car.IsPlayer ? " · YOU" : "";
        return $"{place} {name}{cls}{where}{you}";
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        if (b.CanFreeze)
            b.Freeze();
        return b;
    }
}
