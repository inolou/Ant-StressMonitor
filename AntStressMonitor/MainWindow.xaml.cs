using System.Windows;
using System.Windows.Controls;
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

    private GraphWindow? _graphWindow;

    private static readonly SolidColorBrush GreenBrush = new(Colors.LimeGreen);
    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);

    // Garmin stress-zone palette
    private static readonly SolidColorBrush StressRestingBrush = new(Color.FromRgb(0x4D, 0xA6, 0xFF));  // blue  0-25
    private static readonly SolidColorBrush StressLowBrush     = new(Color.FromRgb(0x00, 0xC8, 0x51));  // green 26-50
    private static readonly SolidColorBrush StressMediumBrush  = new(Color.FromRgb(0xFF, 0xBB, 0x33));  // amber 51-75
    private static readonly SolidColorBrush StressHighBrush    = new(Color.FromRgb(0xFF, 0x44, 0x44));  // red   76-100

    private static SolidColorBrush GetStressBrush(int stress) => stress switch
    {
        <= 25 => StressRestingBrush,
        <= 50 => StressLowBrush,
        <= 75 => StressMediumBrush,
        _     => StressHighBrush
    };

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
        _monitor.StressUpdated += OnStressUpdated;
        _monitor.RadioError += OnRadioError;
        _monitor.DeviceStatusChanged += OnDeviceStatusChanged;
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
    // Stress callback (may arrive on a worker thread)
    // ------------------------------------------------------------------

    private void OnStressUpdated(int stress)
    {
        Dispatcher.InvokeAsync(() => UpdateStressDisplay(stress));
    }

    private void UpdateStressDisplay(int stress)
    {
        if (stress < 0)
        {
            StressText.Text = "S:--";
            StressText.Foreground = WhiteBrush;
        }
        else
        {
            StressText.Text = $"S:{stress}";
            StressText.Foreground = GetStressBrush(stress);
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
    // Device status updates
    // ------------------------------------------------------------------

    private void OnDeviceStatusChanged(string info)
    {
        Dispatcher.InvokeAsync(() =>
        {
            DeviceStatusMenuItem.Header = $"Device: {info}";
            ToolTipText.Text = $"Device: {info}";
            UpdateLockMenuItem();
        });
    }

    private void UpdateLockMenuItem()
    {
        if (_monitor.LockedDeviceNumber.HasValue)
            LockDeviceMenuItem.Header = $"Unlock Device (locked #{_monitor.LockedDeviceNumber})";
        else
            LockDeviceMenuItem.Header = "Lock to Current Device";
    }

    // ------------------------------------------------------------------
    // Context-menu handlers
    // ------------------------------------------------------------------

    private void ShowGraph_Click(object sender, RoutedEventArgs e)
    {
        if (_graphWindow == null)
        {
            _graphWindow = new GraphWindow(_monitor);
            _graphWindow.Closed += (_, _) => _graphWindow = null;
            _graphWindow.Show();
        }
        else
        {
            _graphWindow.Activate();
        }
    }

    private void LockDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor.LockedDeviceNumber.HasValue)
        {
            _monitor.UnlockDevice();
        }
        else
        {
            bool locked = _monitor.LockToCurrentDevice();
            if (!locked)
                MessageBox.Show(
                    "No device is currently connected. Connect a heart-rate monitor first.",
                    "Lock Device",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
        }
        UpdateLockMenuItem();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

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
