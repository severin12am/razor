using System.Net;
using System.Runtime.InteropServices;

namespace ResourcePanel;

public static class ForegroundProcess
{
    public static int? Pid()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
            return null;
        _ = NativeMethods.GetWindowThreadProcessId(window, out var pid);
        return pid == 0 ? null : pid;
    }
}

public static class Admin
{
    public static bool IsCurrentProcessElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}

public static class Tokens
{
    public static bool IsSafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 220)
            return false;
        if (value[0] == '-' || value[0] == '.')
            return false;
        foreach (var ch in value)
        {
            var ok = char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-';
            if (!ok)
                return false;
        }
        return true;
    }

    public static bool IsSafeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value[0] == '-')
            return false;
        foreach (var ch in value)
        {
            if (char.IsControl(ch) || ch is '"' or '`' or '|')
                return false;
        }
        return true;
    }
}

sealed class SystemProbe : IDisposable
{
    const int NameOffset = 56;
    const int NameBufferOffset = 64;
    const int UserTimeOffset = 40;
    const int KernelTimeOffset = 48;
    const int PidOffset = 80;
    const int ParentOffset = 88;
    const int SessionOffset = 100;
    const int PrivateWsOffset = 8;
    const int WorkingSetOffset = 144;
    const int ReadBytesOffset = 232;
    const int WriteBytesOffset = 240;

    readonly Dictionary<int, long> _cpu = new();
    readonly Dictionary<int, long> _read = new();
    readonly Dictionary<int, long> _write = new();
    readonly Dictionary<int, string> _paths = new();
    readonly int _processors = Math.Max(1, Environment.ProcessorCount);
    readonly int _selfPid = (int)NativeMethods.GetCurrentProcessId();
    long _prevIdle;
    long _prevKernel;
    long _prevUser;
    long _prevStamp;
    bool _haveCpu;
    IntPtr _buffer;
    int _bufferSize = 1024 * 1024;

    public int SelfPid => _selfPid;

    public SystemProbe()
    {
        _buffer = Marshal.AllocHGlobal(_bufferSize);
    }

    public (List<ProcessSample> Processes, double SystemCpu, double SelfCpu, string? Error) ReadProcesses(double seconds, Dictionary<int, string[]> services)
    {
        if (!TryQuery(out var error))
            return ([], 0, 0, error);

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = _prevStamp == 0 ? seconds : Math.Max(0.2, System.Diagnostics.Stopwatch.GetElapsedTime(_prevStamp, now).TotalSeconds);
        _prevStamp = now;

        NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user);
        double systemCpu = 0;
        if (_haveCpu)
        {
            var totalDelta = (kernel + user) - (_prevKernel + _prevUser);
            var busyDelta = (kernel - idle + user) - (_prevKernel - _prevIdle + _prevUser);
            if (totalDelta > 0)
                systemCpu = Math.Clamp(busyDelta * 100.0 / totalDelta, 0, 100);
        }
        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;
        _haveCpu = true;

        var list = new List<ProcessSample>(256);
        var pathBudget = 24;
        double selfCpu = 0;
        var offset = 0;
        while (offset + 256 < _bufferSize)
        {
            var next = Marshal.ReadInt32(_buffer, offset);
            var name = ReadName(offset);
            var pid = ReadPid(offset + PidOffset);
            var parent = ReadPid(offset + ParentOffset);
            var session = Marshal.ReadInt32(_buffer, offset + SessionOffset);
            var userTime = Marshal.ReadInt64(_buffer, offset + UserTimeOffset);
            var kernelTime = Marshal.ReadInt64(_buffer, offset + KernelTimeOffset);
            var cpuTime = userTime + kernelTime;
            var privateBytes = Marshal.ReadInt64(_buffer, offset + PrivateWsOffset);
            var workingSet = Marshal.ReadInt64(_buffer, offset + WorkingSetOffset);
            var readBytes = Marshal.ReadInt64(_buffer, offset + ReadBytesOffset);
            var writeBytes = Marshal.ReadInt64(_buffer, offset + WriteBytesOffset);

            if (pid == 4 && string.IsNullOrWhiteSpace(name))
                name = "System";
            if (pid == 0)
                name = "Idle";

            var bare = Safety.BareName(name);
            double cpu = 0;
            double readRate = 0;
            double writeRate = 0;
            if (_cpu.TryGetValue(pid, out var previousCpu))
            {
                var delta = cpuTime - previousCpu;
                if (delta >= 0)
                    cpu = Math.Clamp(delta / 10_000_000.0 / elapsed / _processors * 100.0, 0, 100);
            }
            if (_read.TryGetValue(pid, out var previousRead) && readBytes >= previousRead)
                readRate = (readBytes - previousRead) / elapsed;
            if (_write.TryGetValue(pid, out var previousWrite) && writeBytes >= previousWrite)
                writeRate = (writeBytes - previousWrite) / elapsed;
            _cpu[pid] = cpuTime;
            _read[pid] = readBytes;
            _write[pid] = writeBytes;

            if (pid == _selfPid)
                selfCpu = cpu;

            string? path = null;
            if (pid > 4 && _paths.TryGetValue(pid, out var cached))
                path = cached.Length == 0 ? null : cached;
            else if (pid > 4 && pathBudget-- > 0)
            {
                path = QueryPath(pid);
                _paths[pid] = path ?? "";
            }

            if (pid > 0)
            {
                services.TryGetValue(pid, out var hosted);
                list.Add(new ProcessSample
                {
                    Pid = pid,
                    ParentPid = parent,
                    SessionId = session,
                    Name = string.IsNullOrWhiteSpace(bare) ? name : bare,
                    CpuPercent = cpu,
                    PrivateBytes = privateBytes > 0 ? privateBytes : workingSet,
                    WorkingSetBytes = workingSet,
                    DiskReadBytesPerSec = readRate,
                    DiskWriteBytesPerSec = writeRate,
                    NetDownBytesPerSec = double.NaN,
                    NetUpBytesPerSec = double.NaN,
                    Path = path,
                    ServiceNames = hosted ?? []
                });
            }

            if (next <= 0)
                break;
            var nextOffset = offset + next;
            if (nextOffset <= offset || nextOffset >= _bufferSize)
                break;
            offset = nextOffset;
        }

        Prune(_cpu, list);
        Prune(_read, list);
        Prune(_write, list);
        return (list, systemCpu, selfCpu, null);
    }

    bool TryQuery(out string? error)
    {
        error = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessInformation, _buffer, _bufferSize, out var needed);
            if (status == 0)
                return true;
            if (status != unchecked((int)0xC0000004) && status != unchecked((int)0xC0000023))
            {
                error = "Process list failed (0x" + status.ToString("X8") + ").";
                return false;
            }

            var nextSize = Math.Max(needed + 65_536, _bufferSize * 2);
            Marshal.FreeHGlobal(_buffer);
            _bufferSize = nextSize;
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }

        error = "Process list buffer was not large enough.";
        return false;
    }

    string ReadName(int offset)
    {
        var length = Marshal.ReadInt16(_buffer, offset + NameOffset);
        var pointer = Marshal.ReadIntPtr(_buffer, offset + NameBufferOffset);
        if (length <= 1 || pointer == IntPtr.Zero)
            return "";
        return Marshal.PtrToStringUni(pointer, length / 2)?.Trim() ?? "";
    }

    int ReadPid(int offset)
    {
        var pointer = Marshal.ReadIntPtr(_buffer, offset);
        return unchecked((int)pointer.ToInt64());
    }

    static string? QueryPath(int pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var chars = new char[1024];
            var size = chars.Length;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, chars, ref size) || size <= 0)
                return null;
            return new string(chars, 0, size);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    static void Prune(Dictionary<int, long> map, List<ProcessSample> live)
    {
        if (map.Count < live.Count + 32)
            return;
        var keep = new HashSet<int>(live.Select(static p => p.Pid));
        foreach (var key in map.Keys.Where(key => !keep.Contains(key)).ToList())
            map.Remove(key);
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}

static class MemoryProbe
{
    public static (ulong Used, ulong Total) Read()
    {
        var status = new NativeMethods.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
            return (0, 0);
        var used = status.TotalPhys - status.AvailPhys;
        return (used, status.TotalPhys);
    }
}

static class BatteryProbe
{
    public static (bool Present, int? Percent, bool OnAc, double? DischargeWatts) Read()
    {
        var present = false;
        int? percent = null;
        var onAc = true;
        if (NativeMethods.GetSystemPowerStatus(out var power))
        {
            onAc = power.AcLineStatus == 1;
            present = power.BatteryFlag != 128 && power.BatteryFlag != 255 && power.BatteryLifePercent != 255;
            if (power.BatteryLifePercent <= 100)
                percent = power.BatteryLifePercent;
        }

        double? discharge = null;
        try
        {
            var result = NativeMethods.CallNtPowerInformation(
                NativeMethods.SystemBatteryStateLevel,
                IntPtr.Zero,
                0,
                out var battery,
                Marshal.SizeOf<NativeMethods.SystemBatteryState>());
            if (result == 0 && battery.BatteryPresent != 0)
            {
                present = true;
                if (battery.Discharging != 0 && battery.Rate != 0)
                    discharge = Math.Abs(battery.Rate) / 1000.0;
            }
        }
        catch (DllNotFoundException)
        {
            // Desktop images without powrprof still report the percent above.
        }

        return (present, percent, onAc, discharge);
    }
}
