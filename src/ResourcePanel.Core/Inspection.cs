using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ResourcePanel;

public static class ProcessInspector
{
    public static string? CommandLine(int pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var length = 0;
            _ = NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, ref length);
            if (length < 16)
                length = 1024;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                var status = NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, buffer, length, ref length);
                if (status == unchecked((int)0xC0000004) || status == unchecked((int)0xC0000023))
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(Math.Max(length, 16));
                    status = NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, buffer, length, ref length);
                }
                if (status != 0)
                    return null;
                var byteLength = Marshal.ReadInt16(buffer);
                var pointer = Marshal.ReadIntPtr(buffer, 8);
                if (byteLength <= 0 || pointer == IntPtr.Zero)
                    return null;
                return Marshal.PtrToStringUni(pointer, byteLength / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}

public static class ConnectionQuery
{
    public static IReadOnlyList<string> Describe(int pid, int limit = 12)
    {
        var lines = new List<string>();
        try { ReadTcp(pid, NativeMethods.AfInet, lines); } catch { /* table unavailable */ }
        try { ReadTcp(pid, NativeMethods.AfInet6, lines); } catch { /* table unavailable */ }
        try { ReadUdp(pid, NativeMethods.AfInet, lines); } catch { /* table unavailable */ }
        try { ReadUdp(pid, NativeMethods.AfInet6, lines); } catch { /* table unavailable */ }
        if (lines.Count <= limit)
            return lines;
        var extra = lines.Count - limit;
        var trimmed = lines.Take(limit).ToList();
        trimmed.Add(extra + " more");
        return trimmed;
    }

    static void ReadTcp(int pid, int family, List<string> lines)
    {
        var buffer = AllocTable((IntPtr pointer, ref int size) => NativeMethods.GetExtendedTcpTable(pointer, ref size, false, family, NativeMethods.TcpTableOwnerPidAll, 0));
        if (buffer == IntPtr.Zero)
            return;
        try
        {
            var count = Marshal.ReadInt32(buffer);
            if (family == NativeMethods.AfInet)
            {
                const int stride = 24;
                for (var i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * stride);
                    var owner = Marshal.ReadInt32(row, 20);
                    if (owner != pid)
                        continue;
                    var state = Marshal.ReadInt32(row);
                    var local = Ipv4(unchecked((uint)Marshal.ReadInt32(row, 4)));
                    var localPort = Port(unchecked((uint)Marshal.ReadInt32(row, 8)));
                    var remote = Ipv4(unchecked((uint)Marshal.ReadInt32(row, 12)));
                    var remotePort = Port(unchecked((uint)Marshal.ReadInt32(row, 16)));
                    lines.Add(state == 2
                        ? "TCP listening " + local + ":" + localPort
                        : "TCP " + remote + ":" + remotePort);
                }
            }
            else
            {
                const int stride = 56;
                for (var i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * stride);
                    var owner = Marshal.ReadInt32(row, 52);
                    if (owner != pid)
                        continue;
                    var remote = Ipv6(row, 24);
                    var remotePort = Port(unchecked((uint)Marshal.ReadInt32(row, 44)));
                    var state = Marshal.ReadInt32(row, 48);
                    lines.Add(state == 2
                        ? "TCP listening [" + Ipv6(row, 0) + "]"
                        : "TCP [" + remote + "]:" + remotePort);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    static void ReadUdp(int pid, int family, List<string> lines)
    {
        var buffer = AllocTable((IntPtr pointer, ref int size) => NativeMethods.GetExtendedUdpTable(pointer, ref size, false, family, NativeMethods.UdpTableOwnerPid, 0));
        if (buffer == IntPtr.Zero)
            return;
        try
        {
            var count = Marshal.ReadInt32(buffer);
            if (family == NativeMethods.AfInet)
            {
                const int stride = 12;
                for (var i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * stride);
                    if (Marshal.ReadInt32(row, 8) != pid)
                        continue;
                    lines.Add("UDP " + Ipv4(unchecked((uint)Marshal.ReadInt32(row))) + ":" + Port(unchecked((uint)Marshal.ReadInt32(row, 4))));
                }
            }
            else
            {
                const int stride = 28;
                for (var i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * stride);
                    if (Marshal.ReadInt32(row, 24) != pid)
                        continue;
                    lines.Add("UDP [" + Ipv6(row, 0) + "]:" + Port(unchecked((uint)Marshal.ReadInt32(row, 20))));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    delegate uint TableCall(IntPtr pointer, ref int size);

    static IntPtr AllocTable(TableCall call)
    {
        var size = 0;
        var result = call(IntPtr.Zero, ref size);
        if (result != 122 || size <= 0 || size > 8_000_000)
            return IntPtr.Zero;
        var buffer = Marshal.AllocHGlobal(size);
        result = call(buffer, ref size);
        if (result != 0)
        {
            Marshal.FreeHGlobal(buffer);
            return IntPtr.Zero;
        }
        return buffer;
    }

    static int Port(uint value)
    {
        var low = (int)(value & 0xFFFF);
        return ((low & 0xFF) << 8) | ((low >> 8) & 0xFF);
    }

    static string Ipv4(uint value) => new IPAddress(BitConverter.GetBytes(value)).ToString();

    static string Ipv6(IntPtr row, int offset)
    {
        var bytes = new byte[16];
        Marshal.Copy(IntPtr.Add(row, offset), bytes, 0, 16);
        return new IPAddress(bytes).ToString();
    }
}

public sealed class PublisherCache : IDisposable
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Collections.Concurrent.ConcurrentQueue<string> _queue = new();
    readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    readonly CancellationTokenSource _cancel = new();
    readonly Thread _thread;

    public PublisherCache()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = AppInfo.ProductName + " publisher" };
        _thread.Start();
    }

    public string? TryGet(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return _cache.TryGetValue(path, out var value) ? value : null;
    }

    public void Request(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _cache.ContainsKey(path))
            return;
        lock (_gate)
        {
            if (!_queued.Add(path))
                return;
        }
        _queue.Enqueue(path);
    }

    void Loop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var path))
            {
                Thread.Sleep(80);
                continue;
            }
            _cache[path] = Read(path);
            Thread.Sleep(120);
        }
    }

    static string Read(string path)
    {
        try
        {
            var certificate = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
            return certificate.Subject ?? "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _thread.Join(500);
        _cancel.Dispose();
    }
}

public static class ShellCommands
{
    public static (int ExitCode, string Output, string Error) RunPowerShell(string script, int timeoutMs = 90_000)
    {
        var exe = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        return Run(start, timeoutMs);
    }

    public static (int ExitCode, string Output, string Error) RunWinget(IReadOnlyList<string> args, int timeoutMs = 120_000)
    {
        var start = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return Run(start, timeoutMs);
    }

    static (int ExitCode, string Output, string Error) Run(ProcessStartInfo start, int timeoutMs)
    {
        using var process = Process.Start(start);
        if (process == null)
            return (-1, "", "Couldn't start " + start.FileName + ".");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "", "Timed out.");
        }
        process.WaitForExit();
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
