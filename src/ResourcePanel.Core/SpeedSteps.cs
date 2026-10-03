using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ResourcePanel;

public sealed class SpeedStep
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public bool Optimized { get; init; }
}

public static class SpeedSteps
{
    const uint UpdateAndNotify = 0x01 | 0x02;

    public static IReadOnlyList<SpeedStep> Detect() =>
        Definitions.Select(step => new SpeedStep
        {
            Id = step.Id,
            Title = step.Title,
            Detail = step.Detail,
            Optimized = SafeRead(step.IsOptimized)
        }).ToList();

    public static ChangeResult Apply(string id) => Run(id, def => def.Apply);
    public static ChangeResult Undo(string id) => Run(id, def => def.Undo);

    static ChangeResult Run(string id, Func<StepDef, Func<ChangeResult>> pick)
    {
        var step = Definitions.FirstOrDefault(item => item.Id == id);
        if (step == null)
            return new ChangeResult(false, "That step is not in the list.");
        try
        {
            var result = pick(step)();
            ActionLog.Append(pick(step) == step.Apply ? "speed-up" : "speed-undo", step.Title, result.Success, result.Message);
            return result;
        }
        catch (Exception ex)
        {
            return new ChangeResult(false, ex.Message);
        }
    }

    static bool SafeRead(Func<bool> read)
    {
        try { return read(); }
        catch { return false; }
    }

    sealed class StepDef
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Detail { get; init; }
        public required Func<bool> IsOptimized { get; init; }
        public required Func<ChangeResult> Apply { get; init; }
        public required Func<ChangeResult> Undo { get; init; }
    }

    static readonly StepDef[] Definitions =
    [
        new()
        {
            Id = "window-animations",
            Title = "Turn off window open and close animations",
            Detail = "Windows stops the grow-and-shrink effect when a window opens, closes, or is minimized. Menus still work. You can turn the animation back on.",
            IsOptimized = () => ReadAnimation() == 0,
            Apply = () => WriteAnimation(0, "Window animations are off."),
            Undo = () => WriteAnimation(1, "Window animations are back on.")
        },
        new()
        {
            Id = "menu-animations",
            Title = "Turn off menu and fade animations",
            Detail = "Menus, tooltips, and selected items appear immediately instead of fading in. This is the usual tweak from those 'make Windows faster' videos.",
            IsOptimized = () => !ReadBool(0x1002) && !ReadBool(0x1016) && !ReadBool(0x1042),
            Apply = () => WriteMenuMotion(false),
            Undo = () => WriteMenuMotion(true)
        },
        new()
        {
            Id = "drag-frames",
            Title = "Stop drawing the window while you drag it",
            Detail = "While you move a window you see an outline instead of the live contents. On a slow PC this makes dragging lighter. Let go and the window draws normally.",
            IsOptimized = () => !ReadBool(0x0026),
            Apply = () => WriteBool(0x0025, false, "Dragging a window now shows an outline."),
            Undo = () => WriteBool(0x0025, true, "Dragging a window shows its contents again.")
        },
        new()
        {
            Id = "transparency",
            Title = "Turn off transparency effects",
            Detail = "The taskbar, Start menu, and some windows stop using the blur effect. They look flatter and the GPU does a little less work.",
            IsOptimized = () => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") == 0,
            Apply = () => WriteDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, "Transparency is off."),
            Undo = () => WriteDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1, "Transparency is back on.")
        },
        new()
        {
            Id = "game-dvr",
            Title = "Turn off Game DVR background recording",
            Detail = "Windows stops recording gameplay in the background. Clips you already saved stay where they are. You can still take a screenshot.",
            IsOptimized = () => ReadDword(@"System\GameConfigStore", "GameDVR_Enabled") == 0,
            Apply = () =>
            {
                WriteDword(@"System\GameConfigStore", "GameDVR_Enabled", 0, "");
                return WriteDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, "Background game recording is off.");
            },
            Undo = () =>
            {
                WriteDword(@"System\GameConfigStore", "GameDVR_Enabled", 1, "");
                return WriteDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1, "Background game recording is back on.");
            }
        },
        new()
        {
            Id = "startup-delay",
            Title = "Remove the startup wait",
            Detail = "Windows waits a few seconds after you sign in before it opens your startup apps. This removes that extra wait. It does not remove the apps themselves.",
            IsOptimized = () => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec") == 0,
            Apply = () => WriteDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, "Startup apps no longer wait out that extra delay."),
            Undo = () =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize");
                key.DeleteValue("StartupDelayInMSec", false);
                return new ChangeResult(true, "The normal startup wait is restored.");
            }
        },
        new()
        {
            Id = "tips",
            Title = "Turn off tips and suggestions",
            Detail = "Hides the extra suggestions Windows adds to Start, Settings, and the lock screen. Windows itself stays the same.",
            IsOptimized = () => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled") == 0,
            Apply = () => WriteTips(0, "Tips and suggestions are off."),
            Undo = () => WriteTips(1, "Tips and suggestions are back on.")
        },
        new()
        {
            Id = "thumbnails",
            Title = "Show icons instead of folder pictures",
            Detail = "File Explorer stops building preview pictures for folders. Lists open with less disk work. Your files are not changed.",
            IsOptimized = () => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly") == 1,
            Apply = () => WriteDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 1, "Explorer will use icons instead of folder pictures."),
            Undo = () => WriteDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "IconsOnly", 0, "Folder pictures are back.")
        },
        new()
        {
            Id = "power-plan",
            Title = "Use the High performance power plan",
            Detail = "Asks Windows to favor speed over battery life. Laptops run warmer and use more power. Resume is not required: this step has its own Undo, which puts the previous plan back.",
            IsOptimized = () => PowerPlans.GetActive() == PowerPlans.HighPerformance,
            Apply = () =>
            {
                if (!PowerPlans.Exists(PowerPlans.HighPerformance))
                    return new ChangeResult(false, "This PC has no High performance plan.");
                var previous = PowerPlans.GetActive();
                var result = PowerPlans.SetActive(PowerPlans.HighPerformance);
                if (result.Success && previous is Guid guid)
                {
                    ProfileStore.Add(new ProfileChange
                    {
                        Kind = "power-plan",
                        Id = "power-plan",
                        Previous = guid.ToString(),
                        Applied = PowerPlans.HighPerformance.ToString(),
                        Reversible = true
                    });
                }
                return result;
            },
            Undo = () =>
            {
                var saved = ProfileStore.Load().Changes.LastOrDefault(change => change.Kind == "power-plan" && change.Id == "power-plan");
                var guid = saved?.Previous != null && Guid.TryParse(saved.Previous, out var parsed)
                    ? parsed
                    : new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
                var result = PowerPlans.SetActive(guid);
                return result.Success ? new ChangeResult(true, "The previous power plan is back.") : result;
            }
        },
        new()
        {
            Id = "game-mode",
            Title = "Turn on Windows Game Mode",
            Detail = "Tells Windows to give a full-screen game priority over background work. It does not close any apps.",
            IsOptimized = GameModeSetting.IsEnabled,
            Apply = () =>
            {
                GameModeSetting.SetEnabled(true);
                return new ChangeResult(true, "Game Mode is on.");
            },
            Undo = () =>
            {
                GameModeSetting.SetEnabled(false);
                return new ChangeResult(true, "Game Mode is off.");
            }
        }
    ];

    [StructLayout(LayoutKind.Sequential)]
    struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    static int ReadAnimation()
    {
        var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
        return SystemParametersInfo(0x0048, info.Size, ref info, 0) ? info.MinAnimate : 1;
    }

    static ChangeResult WriteAnimation(int enabled, string message)
    {
        var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = enabled };
        var ok = SystemParametersInfo(0x0049, info.Size, ref info, UpdateAndNotify);
        return ok ? new ChangeResult(true, message) : new ChangeResult(false, "Windows did not change the window animation.");
    }

    static bool ReadBool(uint getAction)
    {
        var value = 0;
        return SystemParametersInfo(getAction, 0, ref value, 0) && value != 0;
    }

    static ChangeResult WriteBool(uint setAction, bool enabled, string message)
    {
        var ok = SystemParametersInfo(setAction, enabled ? 1u : 0u, IntPtr.Zero, UpdateAndNotify);
        return ok ? new ChangeResult(true, message) : new ChangeResult(false, "Windows did not apply that setting.");
    }

    static ChangeResult WriteMenuMotion(bool enabled)
    {
        var ok = true;
        foreach (var action in new uint[] { 0x1003, 0x1005, 0x1015, 0x1017, 0x1043 })
            ok &= SystemParametersInfo(action, enabled ? 1u : 0u, IntPtr.Zero, UpdateAndNotify);
        return ok
            ? new ChangeResult(true, enabled ? "Menu and fade animations are back on." : "Menu and fade animations are off.")
            : new ChangeResult(false, "Some animation settings could not be changed.");
    }

    static int? ReadDword(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value : null;
    }

    static ChangeResult WriteDword(string path, string name, int value, string message)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
        return new ChangeResult(true, message);
    }

    static ChangeResult WriteTips(int value, string message)
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        string[] names =
        [
            "SubscribedContent-338388Enabled",
            "SubscribedContent-338389Enabled",
            "SubscribedContent-310093Enabled",
            "SoftLandingEnabled",
            "SystemPaneSuggestionsEnabled"
        ];
        foreach (var name in names)
            WriteDword(path, name, value, "");
        return new ChangeResult(true, message);
    }
}

public static class DesktopLink
{
    public static ChangeResult Create()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return new ChangeResult(false, "Couldn't find this program's file.");
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null)
            return new ChangeResult(false, "Couldn't create a shortcut.");
        dynamic shell = Activator.CreateInstance(type)!;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        dynamic link = shell.CreateShortcut(Path.Combine(desktop, AppInfo.ProductName + ".lnk"));
        link.TargetPath = exe;
        link.WorkingDirectory = Path.GetDirectoryName(exe);
        link.Description = "See what is using this PC.";
        link.Save();
        return new ChangeResult(true, "A shortcut is on your desktop. Next time, double-click " + AppInfo.ProductName + ".");
    }
}
