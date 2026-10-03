using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace ResourcePanel;

public sealed class NetworkTrace : IDisposable
{
    readonly ConcurrentDictionary<int, Counter> _counters = new();
    readonly Dictionary<int, (long Down, long Up)> _previous = new();
    TraceEventSession? _session;
    Thread? _thread;
    int _disposed;

    public bool IsRunning { get; private set; }
    public string? Error { get; private set; }

    public void Start()
    {
        if (!Admin.IsCurrentProcessElevated())
        {
            Error = "Per-app network needs an administrator.";
            return;
        }

        try
        {
            _session = new TraceEventSession(KernelTraceEventParser.KernelSessionName)
            {
                StopOnDispose = true
            };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            var source = _session.Source.Kernel;
            source.TcpIpRecv += data => Add(data.ProcessID, data.size, up: false);
            source.TcpIpSend += data => Add(data.ProcessID, data.size, up: true);
            source.TcpIpRecvIPV6 += data => Add(data.ProcessID, data.size, up: false);
            source.TcpIpSendIPV6 += data => Add(data.ProcessID, data.size, up: true);
            source.UdpIpRecv += data => Add(data.ProcessID, data.size, up: false);
            source.UdpIpSend += data => Add(data.ProcessID, data.size, up: true);
            source.UdpIpRecvIPV6 += data => Add(data.ProcessID, data.size, up: false);
            source.UdpIpSendIPV6 += data => Add(data.ProcessID, data.size, up: true);
            _thread = new Thread(Pump)
            {
                IsBackground = true,
                Name = AppInfo.ProductName + " network"
            };
            _thread.Start();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            IsRunning = false;
            StopSession();
        }
    }

    public void ApplyDeltas(List<ProcessSample> processes, double seconds)
    {
        if (!IsRunning || seconds <= 0)
            return;
        var seen = new HashSet<int>();
        for (var i = 0; i < processes.Count; i++)
        {
            var process = processes[i];
            seen.Add(process.Pid);
            var counter = _counters.GetOrAdd(process.Pid, static _ => new Counter());
            var down = Interlocked.Read(ref counter.Down);
            var up = Interlocked.Read(ref counter.Up);
            var downRate = double.NaN;
            var upRate = double.NaN;
            if (_previous.TryGetValue(process.Pid, out var previous))
            {
                downRate = Math.Max(0, down - previous.Down) / seconds;
                upRate = Math.Max(0, up - previous.Up) / seconds;
            }
            _previous[process.Pid] = (down, up);
            processes[i] = Copy(process, downRate, upRate);
        }

        if (_previous.Count > processes.Count + 32)
        {
            foreach (var key in _previous.Keys.Where(key => !seen.Contains(key)).ToList())
                _previous.Remove(key);
        }
    }

    void Add(int pid, int size, bool up)
    {
        if (pid <= 0 || size <= 0)
            return;
        var counter = _counters.GetOrAdd(pid, static _ => new Counter());
        if (up)
            Interlocked.Add(ref counter.Up, size);
        else
            Interlocked.Add(ref counter.Down, size);
    }

    void Pump()
    {
        try
        {
            _session?.Source.Process();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            IsRunning = false;
        }
    }

    void StopSession()
    {
        try { _session?.Stop(noThrow: true); } catch { /* already stopped */ }
        try { _session?.Dispose(); } catch { /* pump thread may be exiting */ }
        _session = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        IsRunning = false;
        StopSession();
        _thread?.Join(2000);
    }

    static ProcessSample Copy(ProcessSample process, double down, double up) => new()
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
        NetDownBytesPerSec = down,
        NetUpBytesPerSec = up,
        PowerEstimateWatts = process.PowerEstimateWatts,
        Path = process.Path,
        ServiceNames = process.ServiceNames
    };

    sealed class Counter
    {
        public long Down;
        public long Up;
    }
}
