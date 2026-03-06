using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AntStressMonitor;

/// <summary>
/// Stealth overlay window – a tiny 150×20 px bar that blends into the
/// Visual Studio status bar (#007ACC blue).
/// </summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Vitality constants
    // ------------------------------------------------------------------
    private const int TargetBpmThreshold = 113;  // 60% of max HR: (220 - 32) * 0.60 ≈ 113 BPM
    private const int ZoneResetMinutes = 60;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------
    private readonly HeartRateMonitor _monitor = new();
    private readonly DispatcherTimer _uiTimer;

    private int _currentAverageBpm;
    private DateTime _zoneEntryTime;
    private bool _inZone;

    private static readonly SolidColorBrush GreenBrush = new(Colors.LimeGreen);
    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public MainWindow()
    {
        InitializeComponent();

        // UI refresh timer – updates the Time-in-Zone display every second
        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();

        // Start ANT+ monitoring asynchronously
        _monitor.BpmUpdated += OnBpmUpdated;
        _monitor.RadioError += OnRadioError;
        Loaded += async (_, _) => await _monitor.StartAsync();
        Closed += OnWindowClosed;
    }

    // ------------------------------------------------------------------
    // BPM callback (may arrive on a worker thread)
    // ------------------------------------------------------------------

    private void OnBpmUpdated(int averageBpm)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _currentAverageBpm = averageBpm;
            UpdateBpmDisplay(averageBpm);
        });
    }

    private void UpdateBpmDisplay(int bpm)
    {
        bool overThreshold = bpm >= TargetBpmThreshold;
        BpmText.Text = bpm > 0 ? $"{bpm} BPM" : "-- BPM";
        BpmText.Foreground = overThreshold ? GreenBrush : WhiteBrush;

        // Start Time-in-Zone timer when crossing the threshold upward
        if (overThreshold && !_inZone)
        {
            _inZone = true;
            _zoneEntryTime = DateTime.UtcNow;
        }
        else if (!overThreshold && _inZone)
        {
            _inZone = false;
        }
    }

    // ------------------------------------------------------------------
    // UI timer tick – refreshes Time-in-Zone label every second
    // ------------------------------------------------------------------

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (!_inZone)
        {
            ZoneText.Text = "0:00";
            ZoneText.Foreground = WhiteBrush;
            return;
        }

        // Check 60-minute reset
        var elapsed = DateTime.UtcNow - _zoneEntryTime;
        if (elapsed >= TimeSpan.FromMinutes(ZoneResetMinutes))
        {
            _zoneEntryTime = DateTime.UtcNow;
            elapsed = TimeSpan.Zero;
        }

        int minutes = (int)elapsed.TotalMinutes;
        int seconds = elapsed.Seconds;
        ZoneText.Text = $"{minutes}:{seconds:D2}";
        ZoneText.Foreground = GreenBrush;
    }

    // ------------------------------------------------------------------
    // Radio error fallback
    // ------------------------------------------------------------------

    private void OnRadioError(Exception ex)
    {
        Dispatcher.InvokeAsync(() =>
        {
            BpmText.Text = "No ANT+";
            BpmText.Foreground = WhiteBrush;
        });
    }

    // ------------------------------------------------------------------
    // Drag-to-move
    // ------------------------------------------------------------------

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    // ------------------------------------------------------------------
    // Cleanup
    // ------------------------------------------------------------------

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _uiTimer.Stop();
        _monitor.Dispose();
    }
}
