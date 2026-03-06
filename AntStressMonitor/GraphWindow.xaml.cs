using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AntStressMonitor;

/// <summary>
/// Floating graph window that shows a live ECG-style BPM chart.
/// Supports four time-range views: Live (last 5 minutes), 1h, 4h, 24h.
/// </summary>
public partial class GraphWindow : Window
{
    // ── Constants ──────────────────────────────────────────────────────
    private const int TargetBpmThreshold = 113;   // same as MainWindow
    private static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(5);

    // ── Brushes (static, allocated once) ───────────────────────────────
    private static readonly Brush GridBrush      = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush AxisBrush      = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush ThresholdBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
    private static readonly Brush BpmLineBrush   = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x51));
    private static readonly Brush LabelBrush     = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
    private static readonly Brush NoDataBrush    = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    // ── Chart margins ───────────────────────────────────────────────────
    private const double LeftMargin       = 40;   // room for Y-axis labels
    private const double BottomMargin     = 22;   // room for X-axis labels
    private const double RightMargin      = 8;
    private const double TopMargin        = 6;
    private const double ApproxCharWidth  = 5.5;  // approx px/char for Segoe UI 9pt

    // ── State ───────────────────────────────────────────────────────────
    private readonly HeartRateMonitor _monitor;
    private readonly DispatcherTimer  _timer;
    private TimeSpan                  _selectedRange = LiveWindow;

    // ── Construction ────────────────────────────────────────────────────

    public GraphWindow(HeartRateMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();

        _monitor.DeviceStatusChanged += OnDeviceStatusChanged;
        DeviceStatusText.Text = _monitor.CurrentDeviceInfo;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => DrawChart();
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _monitor.DeviceStatusChanged -= OnDeviceStatusChanged;
        };
    }

    // ── Device-status callback ──────────────────────────────────────────

    private void OnDeviceStatusChanged(string info)
    {
        Dispatcher.InvokeAsync(() => DeviceStatusText.Text = info);
    }

    // ── Range selection ─────────────────────────────────────────────────

    private void OnRangeChanged(object sender, RoutedEventArgs e)
    {
        if (RbLive.IsChecked == true)       _selectedRange = LiveWindow;
        else if (Rb1h.IsChecked == true)    _selectedRange = TimeSpan.FromHours(1);
        else if (Rb4h.IsChecked == true)    _selectedRange = TimeSpan.FromHours(4);
        else if (Rb24h.IsChecked == true)   _selectedRange = TimeSpan.FromHours(24);

        DrawChart();
    }

    // ── Canvas size changed ─────────────────────────────────────────────

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    // ── Chart drawing ────────────────────────────────────────────────────

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();

        double canvasW = ChartCanvas.ActualWidth;
        double canvasH = ChartCanvas.ActualHeight;
        if (canvasW < 10 || canvasH < 10)
            return;

        double plotW = canvasW - LeftMargin - RightMargin;
        double plotH = canvasH - TopMargin  - BottomMargin;
        if (plotW < 10 || plotH < 10)
            return;

        var samples = _monitor.GetHistorySamples(_selectedRange);

        // ── Y range ────────────────────────────────────────────────────
        int yMin, yMax;
        if (samples.Count < 2)
        {
            yMin = 40;
            yMax = 180;
        }
        else
        {
            int dataMin = samples.Min(s => s.Bpm);
            int dataMax = samples.Max(s => s.Bpm);
            int padding = Math.Max(10, (dataMax - dataMin) / 4);
            yMin = (Math.Max(30, dataMin - padding) / 10) * 10;
            yMax = ((dataMax + padding + 9) / 10) * 10;
            if (yMax - yMin < 20) yMax = yMin + 20;
        }

        // ── X range ────────────────────────────────────────────────────
        DateTime xEnd   = DateTime.UtcNow;
        DateTime xStart = xEnd - _selectedRange;

        // ── Helpers ────────────────────────────────────────────────────
        double ToScreenX(DateTime t)
            => LeftMargin + (t - xStart).TotalSeconds / _selectedRange.TotalSeconds * plotW;
        double ToScreenY(double bpm)
            => TopMargin + (1.0 - (bpm - yMin) / (yMax - yMin)) * plotH;

        // ── Horizontal grid lines + Y labels ───────────────────────────
        int yStep = PickNiceStep(yMax - yMin, 5);
        for (int bpm = (yMin / yStep) * yStep; bpm <= yMax; bpm += yStep)
        {
            double y = ToScreenY(bpm);
            if (y < TopMargin || y > TopMargin + plotH + 1) continue;

            AddLine(LeftMargin, y, LeftMargin + plotW, y, GridBrush, 0.5);

            var lbl = MakeLabel(bpm.ToString(), LabelBrush, 9);
            Canvas.SetRight(lbl, canvasW - LeftMargin + 3);
            Canvas.SetTop(lbl, y - 7);
            ChartCanvas.Children.Add(lbl);
        }

        // ── Threshold line ─────────────────────────────────────────────
        if (TargetBpmThreshold >= yMin && TargetBpmThreshold <= yMax)
        {
            double ty = ToScreenY(TargetBpmThreshold);
            var threshLine = new Line
            {
                X1 = LeftMargin, Y1 = ty, X2 = LeftMargin + plotW, Y2 = ty,
                Stroke = ThresholdBrush, StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };
            ChartCanvas.Children.Add(threshLine);
        }

        // ── Axis borders ───────────────────────────────────────────────
        AddLine(LeftMargin, TopMargin, LeftMargin, TopMargin + plotH, AxisBrush, 1);
        AddLine(LeftMargin, TopMargin + plotH, LeftMargin + plotW, TopMargin + plotH, AxisBrush, 1);

        // ── X-axis time labels ─────────────────────────────────────────
        DrawXLabels(xStart, xEnd, plotW, TopMargin + plotH);

        // ── BPM polyline ───────────────────────────────────────────────
        if (samples.Count < 2)
        {
            var msg = MakeLabel(
                samples.Count == 0 ? "No data – waiting for heart-rate signal" : "Collecting data…",
                NoDataBrush, 12);
            Canvas.SetLeft(msg, LeftMargin + plotW / 2 - 120);
            Canvas.SetTop(msg, TopMargin + plotH / 2 - 8);
            ChartCanvas.Children.Add(msg);
        }
        else
        {
            var points = new PointCollection(samples.Count);
            foreach (var (ts, bpm) in samples)
            {
                double x = ToScreenX(ts);
                double y = ToScreenY(bpm);
                // Clamp to plot area
                x = Math.Clamp(x, LeftMargin, LeftMargin + plotW);
                y = Math.Clamp(y, TopMargin, TopMargin + plotH);
                points.Add(new Point(x, y));
            }

            var polyline = new Polyline
            {
                Points          = points,
                Stroke          = BpmLineBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin  = PenLineJoin.Round
            };
            ChartCanvas.Children.Add(polyline);

            // Current BPM annotation at the right edge
            var last = samples[^1];
            double lastY = Math.Clamp(ToScreenY(last.Bpm), TopMargin, TopMargin + plotH);
            var bpmLabel = MakeLabel($"{last.Bpm}", BpmLineBrush, 10, bold: true);
            Canvas.SetLeft(bpmLabel, LeftMargin + plotW + 2);
            Canvas.SetTop(bpmLabel, lastY - 7);
            ChartCanvas.Children.Add(bpmLabel);
        }

        // ── Status line ────────────────────────────────────────────────
        StatusText.Text = samples.Count == 0
            ? "0 samples"
            : $"{samples.Count} samples  ·  {RangeName()}  ·  {xStart.ToLocalTime():HH:mm} – {xEnd.ToLocalTime():HH:mm}";
    }

    // ── X-axis label helpers ─────────────────────────────────────────────

    private void DrawXLabels(DateTime xStart, DateTime xEnd, double plotW, double axisY)
    {
        int tickCount = PickXTickCount(_selectedRange);
        double intervalSec = _selectedRange.TotalSeconds / tickCount;
        double labelY = axisY + 4;

        for (int i = 0; i <= tickCount; i++)
        {
            var t = xStart + TimeSpan.FromSeconds(i * intervalSec);
            double x = LeftMargin + (t - xStart).TotalSeconds / _selectedRange.TotalSeconds * plotW;

            // Tick
            AddLine(x, axisY, x, axisY + 3, AxisBrush, 1);

            // Label
            string text = _selectedRange <= TimeSpan.FromMinutes(10)
                ? t.ToLocalTime().ToString("HH:mm:ss")
                : t.ToLocalTime().ToString("HH:mm");

            var lbl = MakeLabel(text, LabelBrush, 9);
            // Measure approximate text width (Segoe UI 9pt ≈ ApproxCharWidth px/char)
            double approxW = text.Length * ApproxCharWidth;
            Canvas.SetLeft(lbl, x - approxW / 2);
            Canvas.SetTop(lbl, labelY);
            ChartCanvas.Children.Add(lbl);
        }
    }

    // ── Drawing primitives ───────────────────────────────────────────────

    private void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        ChartCanvas.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = thickness
        });
    }

    private static TextBlock MakeLabel(string text, Brush foreground, double fontSize, bool bold = false)
        => new()
        {
            Text       = text,
            Foreground = foreground,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize   = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };

    // ── Axis-step helpers ────────────────────────────────────────────────

    private static int PickNiceStep(int range, int maxLines)
    {
        int[] nice = { 5, 10, 20, 25, 50, 100 };
        foreach (var s in nice)
            if (range / s <= maxLines) return s;
        return 50;
    }

    private static int PickXTickCount(TimeSpan range)
    {
        if (range <= TimeSpan.FromMinutes(10)) return 5;
        if (range <= TimeSpan.FromHours(1))    return 6;
        if (range <= TimeSpan.FromHours(4))    return 8;
        return 6;
    }

    private string RangeName() => _selectedRange switch
    {
        var r when r == LiveWindow               => "Live (5 min)",
        var r when r == TimeSpan.FromHours(1)    => "Last 1 hour",
        var r when r == TimeSpan.FromHours(4)    => "Last 4 hours",
        _                                        => "Last 24 hours"
    };
}
