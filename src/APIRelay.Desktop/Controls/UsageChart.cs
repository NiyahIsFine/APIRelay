using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace APIRelay.Desktop.Controls;

public sealed record ChartPoint(string Label, long Input, long Output, long CacheRead, long CacheWrite, decimal Cost);

public sealed class UsageChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points),
        typeof(IReadOnlyList<ChartPoint>), typeof(UsageChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShowCostProperty = DependencyProperty.Register(nameof(ShowCost),
        typeof(bool), typeof(UsageChart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register("AxisBrush",
        typeof(Brush), typeof(UsageChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register("LabelBrush",
        typeof(Brush), typeof(UsageChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ChartPoint>? Points { get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public bool ShowCost { get => (bool)GetValue(ShowCostProperty); set => SetValue(ShowCostProperty, value); }
    private int hover = -1;
    private static readonly Color[] Colors = [Color.FromRgb(98, 213, 191), Color.FromRgb(130, 167, 247), Color.FromRgb(197, 156, 237), Color.FromRgb(245, 190, 118)];
    public UsageChart()
    {
        MinHeight = 210;
        SnapsToDevicePixels = true;
        SetResourceReference(AxisBrushProperty, "LineBrush");
        SetResourceReference(LabelBrushProperty, "MutedTextBrush");
        MouseMove += OnMove;
        MouseLeave += (_, _) => { hover = -1; ToolTip = null; InvalidateVisual(); };
        TextCatalog.Current.PropertyChanged += (_, _) => InvalidateVisual();
    }
    private Rect Plot => new(58, 16, Math.Max(1, ActualWidth - 78), Math.Max(1, ActualHeight - 49));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 120 || ActualHeight < 70) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var plot = Plot;
        var grid = new Pen((Brush)GetValue(AxisBrushProperty), 1);
        var text = (Brush)GetValue(LabelBrushProperty);
        var points = Points;
        double maximum = points?.Count > 0 ? points.Max(p => ShowCost ? (double)p.Cost : Math.Max(Math.Max(p.Input, p.Output), Math.Max(p.CacheRead, p.CacheWrite))) : 0;
        maximum = maximum <= 0 ? (ShowCost ? 0.01 : 100) : maximum * 1.15;
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = plot.Bottom - tick * plot.Height / 4;
            dc.DrawLine(grid, new(plot.Left, y), new(plot.Right, y));
            DrawText(dc, ShowCost ? "$" + (maximum * tick / 4).ToString("0.###") : Compact(maximum * tick / 4), text, new(0, y - 7), 11);
        }
        if (points == null || points.Count == 0) return;
        var step = plot.Width / Math.Max(1, points.Count - 1);
        for (var i = 0; i < points.Count; i += Math.Max(1, (int)Math.Ceiling(points.Count / Math.Max(2, plot.Width / 75))))
            DrawText(dc, points[i].Label, text, new(plot.Left + step * i - 15, plot.Bottom + 13), 10);
        dc.PushClip(new RectangleGeometry(new Rect(plot.Left - 3, plot.Top - 3, plot.Width + 6, plot.Height + 6)));
        for (var series = 0; series < (ShowCost ? 1 : 4); series++)
        {
            var polygon = points.Select((point, index) => new Point(plot.Left + step * index,
                plot.Bottom - Value(point, series) / maximum * plot.Height)).ToArray();
            var geometry = new StreamGeometry();
            using (var context = geometry.Open()) { context.BeginFigure(polygon[0], false, false); context.PolyLineTo(polygon.Skip(1).ToArray(), true, false); }
            if (series == 0 && polygon.Length > 1)
            {
                var fill = new StreamGeometry();
                using var context = fill.Open();
                context.BeginFigure(new(plot.Left, plot.Bottom), true, true);
                context.PolyLineTo(polygon, true, false);
                context.LineTo(new(polygon[^1].X, plot.Bottom), true, false);
                context.Close();
                dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(45, 98, 213, 191), Colors[0] with { A = 0 }, 90), null, fill);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Colors[series]), series == 0 ? 2.5 : 1.7), geometry);
            if (polygon.Length == 1) dc.DrawEllipse(new SolidColorBrush(Colors[series]), null, polygon[0], 3, 3);
        }
        if (hover >= 0 && hover < points.Count)
        {
            var x = plot.Left + step * hover;
            dc.DrawLine(new Pen(text, 1) { DashStyle = DashStyles.Dash }, new(x, plot.Top), new(x, plot.Bottom));
            for (var i = 0; i < (ShowCost ? 1 : 4); i++)
                dc.DrawEllipse(new SolidColorBrush(Colors[i]), new Pen((Brush)FindResource("CardBrush"), 2),
                    new(x, plot.Bottom - Value(points[hover], i) / maximum * plot.Height), 4, 4);
        }
        dc.Pop();
    }
    private double Value(ChartPoint point, int series) => ShowCost ? (double)point.Cost :
        series switch { 0 => point.Input, 1 => point.Output, 2 => point.CacheRead, _ => point.CacheWrite };
    private void OnMove(object sender, MouseEventArgs args)
    {
        if (Points is not { Count: > 0 }) return;
        var index = Math.Clamp((int)Math.Round((args.GetPosition(this).X - Plot.Left) / Plot.Width * (Points.Count - 1)), 0, Points.Count - 1);
        if (index == hover) return;
        hover = index;
        var p = Points[index];
        var t = TextCatalog.Current;
        ToolTip = $"{p.Label}\n{t["Input"]}  {p.Input:N0}\n{t["Output"]}  {p.Output:N0}\n{t["CacheRead"]}  {p.CacheRead:N0}\n{t["CacheWrite"]}  {p.CacheWrite:N0}\n{t["Cost"]}  ${p.Cost:0.0000}";
        InvalidateVisual();
    }
    private void DrawText(DrawingContext dc, string value, Brush color, Point at, double size) =>
        dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip), at);
    private static string Compact(double value) => value >= 1_000_000 ? $"{value / 1_000_000:0.#}M" : value >= 1000 ? $"{value / 1000:0.#}K" : $"{value:0}";
}
