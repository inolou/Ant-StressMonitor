using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmallEarthTech.AntPlus;
using SmallEarthTech.AntPlus.DeviceProfiles;
using SmallEarthTech.AntUsbStick;

namespace AntStressMonitor;

/// <summary>
/// Manages ANT+ USB radio, discovers HeartRate sensors, and exposes a
/// 1-minute rolling-average BPM value via the <see cref="BpmUpdated"/> event.
/// Also records up to 24 hours of raw BPM history for graph display.
/// </summary>
public sealed class HeartRateMonitor : IDisposable
{
    // ------------------------------------------------------------------
    // Rolling-average window
    // ------------------------------------------------------------------
    private const int RollingWindowSeconds = 60;
    private readonly Queue<(DateTime Timestamp, int Bpm)> _samples = new();
    private readonly object _samplesLock = new();

    // ------------------------------------------------------------------
    // 24-hour BPM history
    // ------------------------------------------------------------------
    private const int HistoryHours = 24;
    private readonly Queue<(DateTime Timestamp, int Bpm)> _history = new();
    private readonly object _historyLock = new();

    // ------------------------------------------------------------------
    // ANT+ infrastructure (nullable until Start is called)
    // ------------------------------------------------------------------
    private AntRadio? _antRadio;
    private AntDeviceCollection? _collection;
    private HeartRate? _activeDevice;
    private bool _disposed;

    // ------------------------------------------------------------------
    // Device lock
    // ------------------------------------------------------------------
    private uint? _lockedDeviceNumber;

    /// <summary>Gets the ANT+ device number that is currently locked, or null if unlocked.</summary>
    public uint? LockedDeviceNumber => _lockedDeviceNumber;

    /// <summary>Gets a human-readable description of the current device connection state.</summary>
    public string CurrentDeviceInfo { get; private set; } = "Searching…";

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    /// <summary>Raised on the calling thread (UI-safe via Dispatcher) when a new average BPM is available.</summary>
    public event Action<int>? BpmUpdated;

    /// <summary>Raised when the ANT+ radio cannot be found or throws during initialisation.</summary>
    public event Action<Exception>? RadioError;

    /// <summary>Raised whenever the device connection status changes (connect, disconnect, offline).</summary>
    public event Action<string>? DeviceStatusChanged;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Asynchronously opens the first available ANT+ USB stick and begins
    /// scanning for heart rate broadcast messages.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            var loggerFactory = NullLoggerFactory.Instance;
            _antRadio = new AntRadio(loggerFactory.CreateLogger<AntRadio>());
            _collection = new AntDeviceCollection(_antRadio, loggerFactory, antDeviceTimeout: 5000);
            _collection.CollectionChanged += OnCollectionChanged;
            await _collection.StartScanning();
        }
        catch (Exception ex)
        {
            RadioError?.Invoke(ex);
        }
    }

    // ------------------------------------------------------------------
    // Device discovery
    // ------------------------------------------------------------------

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null)
            return;

        foreach (var item in e.NewItems)
        {
            if (item is not HeartRate hr)
                continue;

            // If already have an active device, skip
            if (_activeDevice != null)
                continue;

            // If locked to a specific device, only accept that device number
            if (_lockedDeviceNumber.HasValue && hr.ChannelId.DeviceNumber != _lockedDeviceNumber.Value)
                continue;

            _activeDevice = hr;
            hr.PropertyChanged += OnHeartRatePropertyChanged;
            hr.DeviceWentOffline += OnDeviceWentOffline;

            var info = $"Connected: #{hr.ChannelId.DeviceNumber}";
            CurrentDeviceInfo = info;
            DeviceStatusChanged?.Invoke(info);
        }
    }

    private void OnDeviceWentOffline(object? sender, EventArgs e)
    {
        if (_activeDevice == null)
            return;

        var info = $"Offline: #{_activeDevice.ChannelId.DeviceNumber}";

        // Unsubscribe from the offline device so it can be GC'd
        _activeDevice.PropertyChanged -= OnHeartRatePropertyChanged;
        _activeDevice.DeviceWentOffline -= OnDeviceWentOffline;
        _activeDevice = null;

        CurrentDeviceInfo = info;
        DeviceStatusChanged?.Invoke(info);
    }

    private void OnHeartRatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HeartRate.HeartRateData))
            return;

        if (_activeDevice?.HeartRateData is not { } data)
            return;

        int bpm = data.ComputedHeartRate;
        if (bpm <= 0)
            return;

        AddHistorySample(bpm);
        AddSample(bpm);
        BpmUpdated?.Invoke(RollingAverage());
    }

    // ------------------------------------------------------------------
    // History helpers
    // ------------------------------------------------------------------

    private void AddHistorySample(int bpm)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-HistoryHours);

        lock (_historyLock)
        {
            _history.Enqueue((now, bpm));

            // Evict samples older than 24 hours
            while (_history.Count > 0 && _history.Peek().Timestamp < cutoff)
                _history.Dequeue();
        }
    }

    /// <summary>
    /// Returns a snapshot of BPM history samples within the specified age window.
    /// </summary>
    public IReadOnlyList<(DateTime Timestamp, int Bpm)> GetHistorySamples(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        lock (_historyLock)
        {
            var result = new List<(DateTime, int)>(_history.Count);
            foreach (var sample in _history)
            {
                if (sample.Timestamp >= cutoff)
                    result.Add(sample);
            }
            return result;
        }
    }

    // ------------------------------------------------------------------
    // Device lock / unlock
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks to the currently connected device number so only that device
    /// is used after a reconnect or during scanning. Returns false if no
    /// device is currently connected.
    /// </summary>
    public bool LockToCurrentDevice()
    {
        if (_activeDevice == null)
            return false;

        _lockedDeviceNumber = _activeDevice.ChannelId.DeviceNumber;
        var info = $"Locked: #{_lockedDeviceNumber}";
        CurrentDeviceInfo = info;
        DeviceStatusChanged?.Invoke(info);
        return true;
    }

    /// <summary>Removes any device lock, allowing any heart-rate sensor to be used.</summary>
    public void UnlockDevice()
    {
        _lockedDeviceNumber = null;
        var info = _activeDevice != null
            ? $"Connected: #{_activeDevice.ChannelId.DeviceNumber}"
            : "Searching…";
        CurrentDeviceInfo = info;
        DeviceStatusChanged?.Invoke(info);
    }

    // ------------------------------------------------------------------
    // Rolling average helpers
    // ------------------------------------------------------------------

    private void AddSample(int bpm)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-RollingWindowSeconds);

        lock (_samplesLock)
        {
            _samples.Enqueue((now, bpm));

            // Evict samples older than 60 seconds
            while (_samples.Count > 0 && _samples.Peek().Timestamp < cutoff)
                _samples.Dequeue();
        }
    }

    private int RollingAverage()
    {
        lock (_samplesLock)
        {
            if (_samples.Count == 0)
                return 0;

            double sum = 0;
            foreach (var (_, bpm) in _samples)
                sum += bpm;

            return (int)Math.Round(sum / _samples.Count);
        }
    }

    // ------------------------------------------------------------------
    // IDisposable
    // ------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_collection != null)
        {
            _collection.CollectionChanged -= OnCollectionChanged;
            // AntDeviceCollection does not expose Dispose; stop listening.
        }

        if (_activeDevice != null)
        {
            _activeDevice.PropertyChanged -= OnHeartRatePropertyChanged;
            _activeDevice.DeviceWentOffline -= OnDeviceWentOffline;
            _activeDevice = null;
        }

        if (_antRadio is IDisposable disposableRadio)
            disposableRadio.Dispose();
    }
}
