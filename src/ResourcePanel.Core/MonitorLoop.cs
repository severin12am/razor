namespace ResourcePanel;

public sealed class MonitorLoop : IDisposable
{
    readonly SystemProbe _processes = new();
    readonly PdhProbe _pdh = new();
    readonly NetworkTrace _network = new();
    readonly CancellationTokenSource _cancel = new();
    Thread? _thread;
    Dictionary<int, string[]> _services = new();
    int _serviceTick;
    int _disposed;

    public event Action<MachineSample>? Updated;

    public string? LastError { get; private set; }
    public bool PerProcessNetwork => _network.IsRunning;
    public string? NetworkError => _network.Error;
    public int SelfPid => _processes.SelfPid;

    public void Start()
    {
        _pdh.Open();
        try
        {
            _network.Start();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        _thread = new Thread(() => Loop(_cancel.Token))
        {
            IsBackground = true,
            Name = AppInfo.ProductName + " sample"
        };
        _thread.Start();
    }

    void Loop(CancellationToken token)
    {
        var previous = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!token.IsCancellationRequested)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                var seconds = Math.Max(0.2, System.Diagnostics.Stopwatch.GetElapsedTime(previous, started).TotalSeconds);
                previous = started;
                Publish(seconds);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            var remain = TimeSpan.FromSeconds(1) - System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (remain > TimeSpan.Zero)
                token.WaitHandle.WaitOne(remain);
        }
    }

    void Publish(double seconds)
    {
        if (++_serviceTick % 5 == 1)
            _services = ServiceControl.MapByProcess();

        var (processes, systemCpu, selfCpu, error) = _processes.ReadProcesses(seconds, _services);
        if (error != null)
            LastError = error;
        _network.ApplyDeltas(processes, seconds);
        var pdh = _pdh.Collect();
        var memory = MemoryProbe.Read();
        var battery = BatteryProbe.Read();
        AttributePower(processes, systemCpu, battery.DischargeWatts);

        Updated?.Invoke(new MachineSample
        {
            Timestamp = DateTimeOffset.Now,
            CpuPercent = systemCpu,
            RamUsedBytes = memory.Used,
            RamTotalBytes = memory.Total,
            GpuPercent = pdh.Gpu,
            DiskReadBytesPerSec = pdh.DiskRead,
            DiskWriteBytesPerSec = pdh.DiskWrite,
            NetDownBytesPerSec = pdh.NetDown,
            NetUpBytesPerSec = pdh.NetUp,
            BatteryPresent = battery.Present,
            BatteryPercent = battery.Percent,
            OnAcPower = battery.OnAc,
            DischargeWatts = battery.DischargeWatts,
            SelfCpuPercent = selfCpu,
            PerProcessNetwork = _network.IsRunning,
            ForegroundPid = ForegroundProcess.Pid(),
            Processes = processes,
            ProbeError = LastError
        });
    }

    static void AttributePower(List<ProcessSample> processes, double systemCpu, double? dischargeWatts)
    {
        if (dischargeWatts is not > 0.05 || systemCpu < 1)
            return;
        for (var i = 0; i < processes.Count; i++)
        {
            var process = processes[i];
            if (process.CpuPercent <= 0)
                continue;
            var share = process.CpuPercent / systemCpu;
            processes[i] = new ProcessSample
            {
                Pid = process.Pid,
                ParentPid = process.ParentPid,
                SessionId = process.SessionId,
                Name = process.Name,
                CpuPercent = process.CpuPercent,
                PrivateBytes = process.PrivateBytes,
                WorkingSetBytes = process.WorkingSetBytes,
                DiskReadBytesPerSec = process.DiskReadBytesPerSec,
                DiskWriteBytesPerSec = process.DiskWriteBytesPerSec,
                NetDownBytesPerSec = process.NetDownBytesPerSec,
                NetUpBytesPerSec = process.NetUpBytesPerSec,
                PowerEstimateWatts = dischargeWatts.Value * share,
                Path = process.Path,
                ServiceNames = process.ServiceNames
            };
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cancel.Cancel();
        _thread?.Join(2500);
        _network.Dispose();
        _pdh.Dispose();
        _processes.Dispose();
        _cancel.Dispose();
    }
}
