using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

/// <summary>A treadmill that connects, reports speeds and answers Control Point writes as told.</summary>
internal sealed class FakeTreadmillDevice : ITreadmillDevice
{
    private readonly Queue<Func<ControlPointWriteResult>> answers = new();

    public bool ConnectSucceeds { get; set; } = true;

    public Exception? ConnectThrows { get; set; }

    public (double MinKmh, double MaxKmh, double IncrementKmh)? SpeedRange { get; set; } = (0.5, 10.0, 0.1);

    /// <summary>A belt speed to report while the range is read, i.e. once the client is waiting for one.</summary>
    public double? SpeedWhileReadingRange { get; set; }

    public byte RequestControlResult { get; set; } = FtmsConstants.ResultSuccess;

    public List<string> Warnings { get; } = [];

    public List<byte[]> Writes { get; } = [];

    public bool Disposed { get; private set; }

    public bool IsConnected { get; set; }

    public IReadOnlyList<string> LastConnectWarnings => Warnings;

    public event EventHandler<byte[]>? TreadmillDataReceived;

    public event EventHandler? ConnectionLost;

    public void Answer(Func<ControlPointWriteResult> answer) => answers.Enqueue(answer);

    public void ReportSpeed(double kmh)
    {
        var raw = (ushort)Math.Round(kmh * 100);
        TreadmillDataReceived?.Invoke(this, [0x00, 0x00, (byte)(raw & 0xFF), (byte)(raw >> 8)]);
    }

    public void ReportRaw(byte[] data) => TreadmillDataReceived?.Invoke(this, data);

    public void LoseConnection() => ConnectionLost?.Invoke(this, EventArgs.Empty);

    public Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        if (ConnectThrows is not null)
        {
            return Task.FromException<bool>(ConnectThrows);
        }

        IsConnected = ConnectSucceeds;
        return Task.FromResult(ConnectSucceeds);
    }

    public Task<(double MinKmh, double MaxKmh, double IncrementKmh)?> ReadSupportedSpeedRangeAsync()
    {
        if (SpeedWhileReadingRange is { } speed)
        {
            ReportSpeed(speed);
        }

        return Task.FromResult(SpeedRange);
    }

    public Task<ControlPointWriteResult> WriteControlPointAndAwaitAsync(byte[] data)
    {
        Writes.Add(data);
        if (answers.Count > 0)
        {
            return Task.FromResult(answers.Dequeue()());
        }

        var code = data[0] == FtmsConstants.Opcode.RequestControl ? RequestControlResult : FtmsConstants.ResultSuccess;
        return Task.FromResult(new ControlPointWriteResult(data[0], true, true, code));
    }

    public void Dispose()
    {
        Disposed = true;
        IsConnected = false;
    }
}

/// <summary>Builds a <see cref="TreadmillClient"/> over fakes: a scan that finds a treadmill straight away, and devices from the given factory.</summary>
internal sealed class FakeTreadmill
{
    public FakeTreadmill(Func<FakeTreadmillDevice>? createDevice = null, TimeProvider? time = null)
    {
        CreateDevice = createDevice ?? (() => new FakeTreadmillDevice { SpeedWhileReadingRange = 3.0 });
        Watcher.OnStart.Add(new BleAdvertisement(1, [FtmsConstants.FitnessMachineService]));
        Client = new TreadmillClient(
            new FitnessMachineScanner(() => Watcher, time ?? TimeProvider.System),
            () =>
            {
                var device = CreateDevice();
                Devices.Add(device);
                return device;
            },
            time ?? TimeProvider.System);
    }

    public FakeAdvertisementWatcher Watcher { get; } = new();

    public Func<FakeTreadmillDevice> CreateDevice { get; }

    public List<FakeTreadmillDevice> Devices { get; } = [];

    public TreadmillClient Client { get; }
}

/// <summary>An advertisement watcher that sends the queued advertisements as soon as it starts.</summary>
internal sealed class FakeAdvertisementWatcher : IAdvertisementWatcher
{
    public List<BleAdvertisement> OnStart { get; } = [];

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public event EventHandler<BleAdvertisement>? Received;

    public void Send(BleAdvertisement advertisement) => Received?.Invoke(this, advertisement);

    public void Start()
    {
        Started = true;
        foreach (var advertisement in OnStart)
        {
            Send(advertisement);
        }
    }

    public void Stop() => Stopped = true;
}
