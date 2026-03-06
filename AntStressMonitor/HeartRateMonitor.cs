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
/// </summary>
internal sealed class HeartRateMonitor : IDisposable
{
    // ------------------------------------------------------------------
    // Rolling-average window
    // ------------------------------------------------------------------
    private const int RollingWindowSeconds = 60;
    private readonly Queue<(DateTime Timestamp, int Bpm)> _samples = new();
    private readonly object _samplesLock = new();

    // ------------------------------------------------------------------
    // ANT+ infrastructure (nullable until Start is called)
    // ------------------------------------------------------------------
    private AntRadio? _antRadio;
    private AntDeviceCollection? _collection;
    private HeartRate? _activeDevice;
    private bool _disposed;

    /// <summary>Raised on the calling thread (UI-safe via Dispatcher) when a new average BPM is available.</summary>
    public event Action<int>? BpmUpdated;

    /// <summary>Raised when the ANT+ radio cannot be found or throws during initialisation.</summary>
    public event Action<Exception>? RadioError;

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
            if (item is HeartRate hr && _activeDevice == null)
            {
                _activeDevice = hr;
                hr.PropertyChanged += OnHeartRatePropertyChanged;
            }
        }
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

        AddSample(bpm);
        BpmUpdated?.Invoke(RollingAverage());
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
            _activeDevice = null;
        }

        if (_antRadio is IDisposable disposableRadio)
            disposableRadio.Dispose();
    }
}
