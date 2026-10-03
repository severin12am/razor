using System.Runtime.InteropServices;

namespace ResourcePanel;

sealed class PdhProbe : IDisposable
{
    const uint FormatDouble = 0x00000200;
    const uint MoreData = 0x800007D2;

    IntPtr _query;
    IntPtr _diskRead;
    IntPtr _diskWrite;
    IntPtr _netDown;
    IntPtr _netUp;
    IntPtr _gpu;
    bool _primed;
    bool _open;

    public void Open()
    {
        if (NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            return;
        _open = true;
        TryAdd(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec", out _diskRead);
        TryAdd(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec", out _diskWrite);
        TryAdd(@"\Network Interface(*)\Bytes Received/sec", out _netDown);
        TryAdd(@"\Network Interface(*)\Bytes Sent/sec", out _netUp);
        TryAdd(@"\GPU Engine(*)\Utilization Percentage", out _gpu);
    }

    public (double DiskRead, double DiskWrite, double NetDown, double NetUp, double? Gpu) Collect()
    {
        if (!_open)
            return (0, 0, 0, 0, null);
        var status = NativeMethods.PdhCollectQueryData(_query);
        if (status != 0 && status != 0x800007D5)
            return (0, 0, 0, 0, null);
        if (!_primed)
        {
            _primed = true;
            return (0, 0, 0, 0, null);
        }

        return (ReadDouble(_diskRead), ReadDouble(_diskWrite), SumNetwork(_netDown), SumNetwork(_netUp), ReadGpu(_gpu));
    }

    void TryAdd(string path, out IntPtr counter)
    {
        counter = IntPtr.Zero;
        _ = NativeMethods.PdhAddEnglishCounter(_query, path, IntPtr.Zero, out counter);
    }

    static double ReadDouble(IntPtr counter)
    {
        if (counter == IntPtr.Zero)
            return 0;
        if (NativeMethods.PdhGetFormattedCounterValue(counter, FormatDouble, out _, out var value) != 0)
            return 0;
        if (value.Status != 0 && value.Status != 1)
            return 0;
        return value.DoubleValue < 0 ? 0 : value.DoubleValue;
    }

    static double SumNetwork(IntPtr counter)
    {
        double sum = 0;
        foreach (var (name, value) in ReadArray(counter))
        {
            if (IsIgnoredNic(name))
                continue;
            if (value > 0)
                sum += value;
        }
        return sum;
    }

    static double? ReadGpu(IntPtr counter)
    {
        double? best = null;
        var any = false;
        foreach (var (name, value) in ReadArray(counter))
        {
            if (value < 0 || value > 100)
                continue;
            any = true;
            var is3D = name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase);
            if (!is3D && best is not null)
                continue;
            if (is3D || best is null)
                best = best is null ? value : Math.Max(best.Value, value);
            if (!is3D && best is null)
                best = value;
        }

        return any ? best : null;
    }

    static bool IsIgnoredNic(string name) =>
        name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
        || name.Contains("isatap", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Teredo", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Pseudo", StringComparison.OrdinalIgnoreCase);

    static List<(string Name, double Value)> ReadArray(IntPtr counter)
    {
        var items = new List<(string, double)>();
        if (counter == IntPtr.Zero)
            return items;
        try
        {
            uint size = 0;
            uint count = 0;
            var status = NativeMethods.PdhGetFormattedCounterArray(counter, FormatDouble, ref size, ref count, IntPtr.Zero);
            if (status != MoreData || size == 0 || size > 2_000_000)
                return items;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = NativeMethods.PdhGetFormattedCounterArray(counter, FormatDouble, ref size, ref count, buffer);
                if (status != 0 || count == 0)
                    return items;
                const int stride = 24;
                var max = Math.Min(count, (uint)(size / stride));
                for (var i = 0; i < max; i++)
                {
                    var namePtr = Marshal.ReadIntPtr(buffer, i * stride);
                    var itemStatus = (uint)Marshal.ReadInt32(buffer, i * stride + 8);
                    var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buffer, i * stride + 16));
                    if (namePtr == IntPtr.Zero || (itemStatus != 0 && itemStatus != 1))
                        continue;
                    var name = Marshal.PtrToStringUni(namePtr) ?? "";
                    items.Add((name, value));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return items;
        }
        return items;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            NativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
