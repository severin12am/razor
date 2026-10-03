using System.Runtime.InteropServices;

namespace ResourcePanel;

public static class ServiceControl
{
    public static Dictionary<int, string[]> MapByProcess()
    {
        var grouped = new Dictionary<int, List<string>>();
        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect | NativeMethods.ScManagerEnumerate);
        if (manager == IntPtr.Zero)
            return [];
        try
        {
            var resume = 0;
            NativeMethods.EnumServicesStatusEx(manager, NativeMethods.ScEnumProcessInfo, NativeMethods.ServiceWin32, NativeMethods.ServiceStateAll, IntPtr.Zero, 0, out var needed, out _, ref resume, null);
            if (needed <= 0)
                return [];
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                resume = 0;
                if (!NativeMethods.EnumServicesStatusEx(manager, NativeMethods.ScEnumProcessInfo, NativeMethods.ServiceWin32, NativeMethods.ServiceStateAll, buffer, needed, out _, out var count, ref resume, null))
                    return [];
                var stride = Marshal.SizeOf<NativeMethods.EnumServiceStatusProcess>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<NativeMethods.EnumServiceStatusProcess>(IntPtr.Add(buffer, i * stride));
                    var pid = (int)item.Status.ProcessId;
                    if (pid <= 0)
                        continue;
                    var name = Marshal.PtrToStringUni(item.ServiceName);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (!grouped.TryGetValue(pid, out var list))
                    {
                        list = [];
                        grouped[pid] = list;
                    }
                    list.Add(name);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(manager);
        }

        return grouped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public static string? GetStartType(string serviceName)
    {
        if (!Tokens.IsSafeToken(serviceName))
            return null;
        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect);
        if (manager == IntPtr.Zero)
            return null;
        var service = IntPtr.Zero;
        try
        {
            service = NativeMethods.OpenService(manager, serviceName, NativeMethods.ServiceQueryConfig);
            if (service == IntPtr.Zero)
                return null;
            return ReadStartType(service);
        }
        finally
        {
            if (service != IntPtr.Zero)
                NativeMethods.CloseServiceHandle(service);
            NativeMethods.CloseServiceHandle(manager);
        }
    }

    public static ChangeResult SetStartType(string serviceName, string startType, out string? previous)
    {
        previous = null;
        if (!Tokens.IsSafeToken(serviceName))
            return new ChangeResult(false, "The service name was rejected.");
        var target = ToCode(startType);
        if (target is null)
            return new ChangeResult(false, "Unknown start type.");

        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect);
        if (manager == IntPtr.Zero)
            return new ChangeResult(false, "Couldn't open Service Manager. Restart as admin.");
        var service = IntPtr.Zero;
        try
        {
            service = NativeMethods.OpenService(manager, serviceName, NativeMethods.ServiceQueryConfig | NativeMethods.ServiceChangeConfig);
            if (service == IntPtr.Zero)
                return new ChangeResult(false, "Couldn't open " + serviceName + ". It may be protected or missing.");
            previous = ReadStartType(service);
            if (string.Equals(previous, startType, StringComparison.OrdinalIgnoreCase))
                return new ChangeResult(true, serviceName + " is already " + startType + ".");
            var ok = NativeMethods.ChangeServiceConfig(
                service,
                NativeMethods.ServiceNoChange,
                target.Value,
                NativeMethods.ServiceNoChange,
                null, null, IntPtr.Zero, null, null, null, null);
            if (!ok)
                return new ChangeResult(false, "Windows refused to change " + serviceName + " (error " + Marshal.GetLastWin32Error() + ").");
            return new ChangeResult(true, serviceName + " is now " + startType + ". It was " + (previous ?? "unknown") + ".");
        }
        finally
        {
            if (service != IntPtr.Zero)
                NativeMethods.CloseServiceHandle(service);
            NativeMethods.CloseServiceHandle(manager);
        }
    }

    static string? ReadStartType(IntPtr service)
    {
        NativeMethods.QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return null;
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!NativeMethods.QueryServiceConfig(service, buffer, needed, out _))
                return null;
            return FromCode(Marshal.ReadInt32(buffer, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string? FromCode(int code) => code switch
    {
        0 => "Boot",
        1 => "System",
        2 => "Automatic",
        3 => "Manual",
        4 => "Disabled",
        _ => null
    };

    static uint? ToCode(string start) => start.ToLowerInvariant() switch
    {
        "boot" => 0,
        "system" => 1,
        "automatic" => 2,
        "manual" => 3,
        "disabled" => 4,
        _ => null
    };
}
