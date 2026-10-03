namespace ResourcePanel;

public static class Safety
{
    public const double HeavyCpuPercent = 3;
    public const long HeavyWorkingSetBytes = 350L * 1024 * 1024;

    public const string SecurityPhrase = "DISABLE SECURITY";
    public const string BootPhrase = "STOP BOOT SERVICES";

    static readonly HashSet<string> NeverKill = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "LsaIso", "Secure System", "Memory Compression", "MemCompression", "svchost",
        "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "RuntimeBroker",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost",
        "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv",
        "MpDefenderCoreService", "smartscreen", "SgrmBroker", "audiodg", "spoolsv",
        "nvcontainer", "NVDisplay.Container", "amdow", "atiesrxx", "WUDFHost", "dasHost",
        "unsecapp", "LockApp", "LogonUI", "UserOOBEBroker", "SystemSettingsBroker",
        "CompPkgSrv", "backgroundTaskHost", "ApplicationFrameHost", "WidgetService",
        "conhost", "dllhost", "TiWorker", "TrustedInstaller", "WmiPrvSE",
        "SearchProtocolHost", "SearchFilterHost", "MoUsoCoreWorker", "MusNotification",
        "SecurityHealthHost", "MpCmdRun", "MSASCuiL", "WmiApSrv"
    };

    static readonly HashSet<string> SafePause = new(StringComparer.OrdinalIgnoreCase)
    {
        "OneDrive", "GameBar", "GameBarFTServer", "GameBarPresenceWriter",
        "XboxGameBarWidgets", "XboxPcApp", "XboxApp", "Widgets", "Copilot", "CopilotApp",
        "AdobeIPCBroker", "AdobeNotificationClient", "CCXProcess", "CoreSync",
        "GoogleUpdate", "GoogleCrashHandler", "GoogleCrashHandler64", "iTunesHelper",
        "jusched", "MicrosoftEdgeUpdate", "edgeupdate", "PhoneExperienceHost"
    };

    static readonly HashSet<string> AskNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Discord", "Spotify", "Steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper",
        "Battle.net", "EADesktop", "EABackgroundService", "RiotClientServices", "RiotClientUx",
        "chrome", "msedge", "firefox", "opera", "brave", "Teams", "ms-teams", "Slack", "Zoom",
        "SearchIndexer", "RazerAppEngine", "RazerCentral", "RzSDKServer", "RzSDKService",
        "iCUE", "LogiOptions", "LogiOptionsMgr", "logioptionsplus_agent", "ArmouryCrate",
        "ArmouryCrate.UserSessionHelper", "LightingService", "MSIAfterburner"
    };

    static readonly HashSet<string> LikelyMicrosoft = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "RuntimeBroker", "dllhost", "conhost", "taskhostw", "sihost", "ctfmon",
        "explorer", "dwm", "csrss", "lsass", "services", "winlogon", "wininit", "smss",
        "fontdrvhost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "TextInputHost", "ApplicationFrameHost", "SystemSettings", "backgroundTaskHost",
        "CompPkgSrv", "WidgetService", "Widgets", "PhoneExperienceHost", "GameBar",
        "GameBarFTServer", "SearchIndexer", "spoolsv", "WmiPrvSE", "audiodg", "MoUsoCoreWorker",
        "TiWorker", "TrustedInstaller", "SecurityHealthService", "SecurityHealthSystray",
        "MsMpEng", "NisSrv", "smartscreen", "LockApp", "LogonUI", "UserOOBEBroker",
        "SystemSettingsBroker", "dasHost", "unsecapp", "WUDFHost", "Registry", "System",
        "Memory Compression", "Secure System", "Copilot", "XboxPcApp", "XboxApp",
        "OneDrive", "msedge", "MicrosoftEdgeUpdate"
    };

    public static string BareName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var file = name.Trim();
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            file = file[..^4];
        return file;
    }

    public static bool IsNeverKill(string name) => NeverKill.Contains(BareName(name));

    public static bool CanControl(string name) => !IsNeverKill(name);

    public static bool IsSafePause(string name) => SafePause.Contains(BareName(name));

    public static bool IsAskName(string name) => AskNames.Contains(BareName(name));

    public static bool IsLikelyMicrosoft(string name) => LikelyMicrosoft.Contains(BareName(name));

    public static bool LooksLikeMicrosoftPublisher(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;
        return subject.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
            || subject.Contains("Microsoft Windows", StringComparison.OrdinalIgnoreCase)
            || subject.Contains("Microsoft Windows Publisher", StringComparison.OrdinalIgnoreCase);
    }

    public static string ShortPublisher(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return "Not signed";
        foreach (var part in subject.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..];
        }

        return subject.Length > 48 ? subject[..48] : subject;
    }

    public static string Blurb(string name)
    {
        var key = BareName(name).ToLowerInvariant();
        return key switch
        {
            "onedrive" => "Cloud sync. Pausing stops uploads until you resume it.",
            "gamebar" or "gamebarftserver" or "gamebarpresencewriter" or "xboxgamebarwidgets"
                => "Xbox Game Bar overlay. Pausing hides it until you resume.",
            "xboxpcapp" or "xboxapp" => "The Xbox app window. Installed games stay installed.",
            "widgets" => "The Widgets board. Search and the taskbar stay.",
            "copilot" or "copilotapp" => "The Copilot window. Windows Security is not touched.",
            "phoneexperiencehost" => "Phone Link's window. Your phone stays paired.",
            "discord" or "slack" or "teams" or "ms-teams" or "zoom"
                => "Chat. Leave it unchecked if you are in a call.",
            "spotify" => "Music. Leave it unchecked if you want it playing.",
            "steam" or "steamwebhelper" or "epicgameslauncher" or "epicwebhelper"
                or "battle.net" or "eadesktop" or "riotclientservices" or "riotclientux"
                => "A game launcher. Leave it unchecked if you are about to play.",
            "chrome" or "msedge" or "firefox" or "opera" or "brave"
                => "A browser. Unsaved tabs are safer if you pause instead of ending it.",
            "searchindexer" => "Windows Search is indexing files. Pausing quiets the disk for a while.",
            "razerappengine" or "razercentral" or "rzsdkserver" or "rzsdksservice"
                or "icue" or "logioptions" or "logioptionsmgr" or "logioptionsplus_agent"
                or "armourycrate" or "armourycrate.usersessionhelper" or "lightingservice"
                => "Mouse, keyboard, or RGB software. Pause it only if you do not need it while playing.",
            "msiafterburner" => "An overlay and GPU tool. Leave it if you use it in games.",
            "adobeipcbroker" or "adobenotificationclient" or "ccxprocess" or "coresync"
                => "Adobe background helper. Creative Cloud apps stay installed.",
            "googleupdate" or "googlecrashhandler" or "googlecrashhandler64"
                or "microsoftedgeupdate" or "edgeupdate" or "ituneshelper" or "jusched"
                => "An updater. Pausing it does not remove the app.",
            _ => "Running in the background."
        };
    }

    public static bool StartupCommandIsProtected(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;
        return command.Contains("MsMpEng", StringComparison.OrdinalIgnoreCase)
            || command.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
            || command.Contains("SecurityHealth", StringComparison.OrdinalIgnoreCase)
            || command.Contains("WinDefend", StringComparison.OrdinalIgnoreCase);
    }
}
