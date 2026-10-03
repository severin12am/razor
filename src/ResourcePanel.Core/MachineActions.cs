using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ResourcePanel;

public static class PowerPlans
{
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static Guid? GetActive()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    public static bool Exists(Guid scheme)
    {
        uint size = 0;
        var result = NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        return result == 0 || size > 2;
    }

    public static ChangeResult SetActive(Guid scheme)
    {
        var result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        return result == 0
            ? new ChangeResult(true, "Switched to the High performance power plan.")
            : new ChangeResult(false, "Couldn't change the power plan (" + result + ").");
    }
}

public static class GameModeSetting
{
    const string KeyPath = @"Software\Microsoft\GameBar";
    const string ValueName = "AutoGameModeEnabled";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is int value && value != 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
    }
}

public static class RestorePoints
{
    const int BeginSystemChange = 100;
    const int EndSystemChange = 101;
    const int ModifySettings = 12;

    public static ChangeResult Create(string description)
    {
        if (description.Length > 64)
            description = description[..64];
        var info = new NativeMethods.RestorePointInfo
        {
            EventType = BeginSystemChange,
            RestorePointType = ModifySettings,
            SequenceNumber = 0,
            Description = description
        };
        var begun = NativeMethods.SRSetRestorePoint(ref info, out var status);
        if (begun == 0)
            return new ChangeResult(false, "Couldn't create a restore point (status " + status.Status + "). Turn on System Protection for the Windows drive if you want one.");

        info.EventType = EndSystemChange;
        info.SequenceNumber = status.SequenceNumber;
        _ = NativeMethods.SRSetRestorePoint(ref info, out _);
        return new ChangeResult(true, "Created a restore point named \"" + description + "\".");
    }
}
