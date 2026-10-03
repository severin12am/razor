namespace ResourcePanel;

public static class DebloatCatalog
{
    public static IReadOnlyList<DebloatItem> Items { get; } = Build();

    public static IEnumerable<DebloatItem> Suggested =>
        Items.Where(item => item.Tier == DebloatTier.Suggested);

    public static bool IsDangerous(DebloatTier tier) =>
        tier is DebloatTier.Security or DebloatTier.Boot;

    static List<DebloatItem> Build()
    {
        var items = new List<DebloatItem>
        {
            App("xbox-app", "Xbox app", DebloatTier.Suggested,
                "The Xbox window. Games you already installed stay installed. Game saves in the cloud are not deleted.",
                "Microsoft.GamingApp", "Microsoft.XboxApp"),
            App("xbox-gamebar", "Xbox Game Bar", DebloatTier.Suggested,
                "The overlay that opens with Win+G. Removing it does not remove your games.",
                "Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay"),
            App("clipchamp", "Clipchamp", DebloatTier.Suggested,
                "The built-in video editor. Your video files stay where they are.",
                "Clipchamp.Clipchamp"),
            App("solitaire", "Solitaire Collection", DebloatTier.Suggested,
                "The Solitaire app. Nothing else in Windows depends on it.",
                "Microsoft.MicrosoftSolitaireCollection"),
            App("mixed-reality", "Mixed Reality Portal", DebloatTier.Suggested,
                "The headset portal. Leave this if you use a Windows Mixed Reality headset.",
                "Microsoft.MixedReality.Portal"),
            App("widgets", "News and widgets", DebloatTier.Suggested,
                "The Widgets board and news feed. Windows search stays.",
                "MicrosoftWindows.Client.WebExperience", "Microsoft.BingNews"),
            App("tips", "Tips", DebloatTier.Suggested,
                "The introductory Tips app.",
                "Microsoft.Getstarted"),
            App("get-help", "Get Help", DebloatTier.Suggested,
                "The Get Help app. Windows itself keeps working.",
                "Microsoft.GetHelp"),
            App("viewer-3d", "3D Viewer", DebloatTier.Suggested,
                "The 3D model viewer. Your files are not deleted.",
                "Microsoft.Microsoft3DViewer"),
            App("paint-3d", "Paint 3D", DebloatTier.Suggested,
                "Paint 3D. This is not the normal Paint app.",
                "Microsoft.MSPaint"),
            App("feedback", "Feedback Hub", DebloatTier.Suggested,
                "The app for sending feedback to Microsoft.",
                "Microsoft.WindowsFeedbackHub"),
            App("people", "People", DebloatTier.Suggested,
                "The old People contact app.",
                "Microsoft.People"),
            App("skype", "Skype", DebloatTier.Suggested,
                "The Store Skype app. A separately installed Skype is left alone.",
                "Microsoft.SkypeApp"),
            App("office-hub", "Office hub", DebloatTier.Suggested,
                "The promotional Office app. Word, Excel, and the rest are not removed.",
                "Microsoft.MicrosoftOfficeHub"),
            Uninstall("oem-mcafee", "McAfee trial", DebloatTier.Suggested,
                "McAfee trial software that often ships on new laptops. Windows Security is not this app.",
                "McAfee"),
            Uninstall("oem-norton", "Norton trial", DebloatTier.Suggested,
                "Norton trial software. Windows Security is not this app.",
                "Norton"),
            Uninstall("oem-wildtangent", "WildTangent games", DebloatTier.Suggested,
                "OEM game promotions that ship on some laptops.",
                "WildTangent"),
            App("candy", "Candy Crush and similar trials", DebloatTier.Suggested,
                "Store games that often arrive on a new PC without being asked for.",
                "king.com.CandyCrushSaga", "king.com.CandyCrushSodaSaga", "king.com.BubbleWitch3Saga"),

            App("copilot-app", "Copilot app", DebloatTier.Choice,
                "The consumer Copilot app. This does not turn off Windows Security.",
                "Microsoft.Copilot"),
            new()
            {
                Id = "copilot-button",
                Title = "Hide the Copilot button",
                Description = "Hides the Copilot taskbar button for this user. You may need to sign out before the button disappears. Windows Security is not changed.",
                Tier = DebloatTier.Choice,
                Action = DebloatAction.CopilotPolicy
            },
            App("mail", "Mail and Calendar", DebloatTier.Choice,
                "Removes the built-in Mail and Calendar apps. A browser or another mail app keeps working.",
                "microsoft.windowscommunicationsapps"),
            App("your-phone", "Phone Link", DebloatTier.Choice,
                "The Phone Link app. Your phone is not wiped.",
                "Microsoft.YourPhone"),
            App("teams", "Teams (personal)", DebloatTier.Choice,
                "The personal Teams app. A work or school Teams install is a different program and is not removed.",
                "MSTeams"),
            App("maps", "Maps", DebloatTier.Choice,
                "The Windows Maps app.",
                "Microsoft.WindowsMaps"),
            App("movies", "Movies & TV", DebloatTier.Choice,
                "The Movies & TV player. Your video files stay.",
                "Microsoft.ZuneVideo"),
            App("music", "Groove Music", DebloatTier.Choice,
                "The old Groove Music app. Your music files stay.",
                "Microsoft.ZuneMusic"),
            App("weather", "Weather", DebloatTier.Choice,
                "The Weather app.",
                "Microsoft.BingWeather"),
            App("sticky", "Sticky Notes", DebloatTier.Choice,
                "Sticky Notes. Notes you care about should be copied out first.",
                "Microsoft.MicrosoftStickyNotes"),
            App("cortana", "Cortana", DebloatTier.Choice,
                "The old Cortana app, on PCs that still have it.",
                "Microsoft.549981C3F5F10"),
            App("xbox-signin", "Xbox sign-in helpers", DebloatTier.Choice,
                "Small Xbox sign-in pieces. Some Store games use them. Leave them if a game asks you to sign in with Xbox.",
                "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI", "Microsoft.XboxSpeechToTextOverlay"),

            Feature("feature-wmp", "Legacy Windows Media Player", DebloatTier.Choice,
                "The old Windows Media Player optional feature. The Movies & TV app is separate.",
                "WindowsMediaPlayer"),
            Feature("feature-xps", "XPS Viewer", DebloatTier.Choice,
                "Opens old XPS documents. Most people never use it.",
                "Xps-Foundation-Xps-Viewer"),
            Feature("feature-workfolders", "Work Folders client", DebloatTier.Choice,
                "A company file-sync client. Leave it if your workplace set it up.",
                "WorkFolders-Client"),

            Service("svc-diagtrack", "Diagnostic telemetry", DebloatTier.Advanced,
                "Connected User Experiences and Telemetry (DiagTrack). Windows still runs. Some feedback features stop.",
                "DiagTrack", "Disabled"),
            Service("svc-dmwappush", "Device management messages", DebloatTier.Advanced,
                "WAP Push Message Routing. Home PCs usually do not need it.",
                "dmwappushservice", "Disabled"),
            Service("svc-sysmain", "SysMain (Superfetch)", DebloatTier.Advanced,
                "Preloads apps into memory. On a slow disk it can hitch games. On an SSD it can make everyday apps open faster.",
                "SysMain", "Disabled"),
            Service("svc-wsearch", "Windows Search index", DebloatTier.Advanced,
                "The file index. The disk gets quieter. Searching in File Explorer gets slower until you turn it back on.",
                "WSearch", "Disabled"),
            Service("svc-xblauth", "Xbox Live Auth Manager", DebloatTier.Advanced,
                "Xbox sign-in service. Store games that use an Xbox account may ask for it again.",
                "XblAuthManager", "Manual"),
            Service("svc-xblsave", "Xbox Live Game Save", DebloatTier.Advanced,
                "Cloud saves for Xbox-signed-in games. Leave it if you use those saves.",
                "XblGameSave", "Manual"),
            Service("svc-xboxnet", "Xbox networking", DebloatTier.Advanced,
                "Xbox multiplayer networking. Local games that do not use Xbox services keep working.",
                "XboxNetApiSvc", "Manual"),
            Service("svc-xboxgip", "Xbox accessory service", DebloatTier.Advanced,
                "Used by some Xbox controllers. Leave it if a controller stops responding.",
                "XboxGipSvc", "Manual"),
            Service("svc-maps", "Downloaded maps manager", DebloatTier.Advanced,
                "Background service for offline maps.",
                "MapsBroker", "Disabled"),
            Service("svc-retaildemo", "Retail demo", DebloatTier.Advanced,
                "The store-demo mode. It should not be running on a personal PC.",
                "RetailDemo", "Disabled"),

            Service("svc-windefend", "Windows Defender Antivirus", DebloatTier.Security,
                "Real-time antivirus. Turning this off makes the PC easier to infect. Not recommended.",
                "WinDefend", "Disabled"),
            Service("svc-wsc", "Security Center", DebloatTier.Security,
                "The service that warns you when antivirus or the firewall is off.",
                "wscsvc", "Disabled"),
            Service("svc-mpssvc", "Windows Firewall", DebloatTier.Security,
                "The firewall service. Turning it off exposes the PC on networks. Not recommended.",
                "mpssvc", "Disabled"),
            Service("svc-wuauserv", "Windows Update", DebloatTier.Security,
                "Stops downloading Windows fixes. The PC will miss security updates. Not recommended.",
                "wuauserv", "Disabled"),
            Service("svc-uso", "Update Orchestrator", DebloatTier.Security,
                "Helps Windows Update run. Windows may turn update work back on by itself.",
                "UsoSvc", "Manual"),
            Service("svc-waas", "Windows Update Medic", DebloatTier.Security,
                "Turns Windows Update back on if it was disabled. Fighting it is not recommended.",
                "WaaSMedicSvc", "Disabled"),

            Service("svc-dhcp", "DHCP client", DebloatTier.Boot,
                "Gets the PC's network address. Disabling it can drop you off the network. The PC may not boot cleanly.",
                "Dhcp", "Disabled"),
            Service("svc-dns", "DNS client", DebloatTier.Boot,
                "Name lookups. Disabling it breaks a lot of Windows networking.",
                "Dnscache", "Disabled"),
            Service("svc-bfe", "Base Filtering Engine", DebloatTier.Boot,
                "Required by the firewall and much of the network stack.",
                "BFE", "Disabled"),
            Service("svc-nsi", "Network Store", DebloatTier.Boot,
                "Network location awareness. Lots of Windows features depend on it.",
                "nsi", "Disabled"),
            Service("svc-plugplay", "Plug and Play", DebloatTier.Boot,
                "Device installation. Disabling it can stop hardware, including the disk, from starting.",
                "PlugPlay", "Disabled"),
            Service("svc-power", "Power", DebloatTier.Boot,
                "Power management. Sleep, shutdown, and battery behavior can break.",
                "Power", "Disabled"),
            Service("svc-rpcss", "Remote Procedure Call", DebloatTier.Boot,
                "Windows will not work without this. Disabling it can prevent the PC from booting.",
                "RpcSs", "Disabled"),
            Service("svc-samss", "Security Accounts Manager", DebloatTier.Boot,
                "Sign-in accounts. Disabling it can lock you out.",
                "SamSs", "Disabled"),
            Service("svc-dcom", "DCOM Server Process Launcher", DebloatTier.Boot,
                "Starts many Windows services. Disabling it can prevent startup.",
                "DcomLaunch", "Disabled"),
            Service("svc-lsm", "Local Session Manager", DebloatTier.Boot,
                "Sign-in sessions. Disabling it can stop you from signing in.",
                "LSM", "Disabled")
        };

        return items;
    }

    static DebloatItem App(string id, string title, DebloatTier tier, string description, params string[] names) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            Tier = tier,
            Action = DebloatAction.Appx,
            AppxNames = names
        };

    static DebloatItem Uninstall(string id, string title, DebloatTier tier, string description, params string[] contains) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            Tier = tier,
            Action = DebloatAction.UninstallName,
            UninstallNameContains = contains
        };

    static DebloatItem Feature(string id, string title, DebloatTier tier, string description, string feature) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            Tier = tier,
            Action = DebloatAction.Feature,
            FeatureName = feature
        };

    static DebloatItem Service(string id, string title, DebloatTier tier, string description, string service, string start) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            Tier = tier,
            Action = DebloatAction.Service,
            ServiceName = service,
            ServiceStart = start
        };
}
