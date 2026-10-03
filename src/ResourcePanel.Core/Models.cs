namespace ResourcePanel;

public sealed class MachineSample
{
    public DateTimeOffset Timestamp { get; init; }
    public double CpuPercent { get; init; }
    public ulong RamUsedBytes { get; init; }
    public ulong RamTotalBytes { get; init; }
    public double? GpuPercent { get; init; }
    public double DiskReadBytesPerSec { get; init; }
    public double DiskWriteBytesPerSec { get; init; }
    public double NetDownBytesPerSec { get; init; }
    public double NetUpBytesPerSec { get; init; }
    public bool BatteryPresent { get; init; }
    public int? BatteryPercent { get; init; }
    public bool OnAcPower { get; init; }
    public double? DischargeWatts { get; init; }
    public double SelfCpuPercent { get; init; }
    public bool PerProcessNetwork { get; init; }
    public int? ForegroundPid { get; init; }
    public IReadOnlyList<ProcessSample> Processes { get; init; } = [];
    public string? ProbeError { get; init; }
}

public sealed class ProcessSample
{
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public int SessionId { get; init; }
    public string Name { get; init; } = "";
    public double CpuPercent { get; init; }
    public long PrivateBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public double DiskReadBytesPerSec { get; init; }
    public double DiskWriteBytesPerSec { get; init; }
    public double NetDownBytesPerSec { get; init; }
    public double NetUpBytesPerSec { get; init; }
    public double? PowerEstimateWatts { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<string> ServiceNames { get; init; } = [];
}

public enum ScanBucket
{
    SafePause,
    Ask,
    Startup
}

public sealed class ScanFinding
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required ScanBucket Bucket { get; init; }
    public bool CheckedByDefault { get; init; }
    public int? Pid { get; init; }
    public string? StartupId { get; init; }
}

public sealed class ScanReport
{
    public List<ScanFinding> PauseCandidates { get; init; } = [];
    public List<ScanFinding> Startup { get; init; } = [];
}

public sealed class ProcessFact
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public bool IsService { get; init; }
    public int SessionId { get; init; }
}

public enum DebloatTier
{
    Suggested,
    Choice,
    Advanced,
    Security,
    Boot
}

public enum DebloatAction
{
    Appx,
    UninstallName,
    Feature,
    Service,
    CopilotPolicy
}

public sealed class DebloatItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required DebloatTier Tier { get; init; }
    public required DebloatAction Action { get; init; }
    public string[] AppxNames { get; init; } = [];
    public string[] UninstallNameContains { get; init; } = [];
    public string? FeatureName { get; init; }
    public string? ServiceName { get; init; }
    public string? ServiceStart { get; init; }
}

public sealed class DebloatChoice
{
    public required DebloatItem Item { get; init; }
    public bool Present { get; set; }
    public bool AlreadyDone { get; set; }
    public string Status { get; set; } = "";
    public string? UninstallDisplayName { get; set; }
    public string? PreviousServiceStart { get; set; }
}

public sealed class StartupEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public required string Kind { get; init; }
    public string? Hive { get; init; }
    public string? KeyPath { get; init; }
    public string? ValueName { get; init; }
    public string? FilePath { get; init; }
    public string? TaskPath { get; init; }
}

public sealed class ProfileDocument
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = AppInfo.ProductName;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public List<ProfileChange> Changes { get; set; } = [];
}

public sealed class ProfileChange
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Previous { get; set; }
    public string? Applied { get; set; }
    public string? Extra { get; set; }
    public bool Reversible { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
}

public sealed class ActionEntry
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public int? Pid { get; set; }
    public bool Success { get; set; }
    public string? Detail { get; set; }
}

public sealed class ActionLogFile
{
    public List<ActionEntry> Entries { get; set; } = [];
}

public sealed class AppSettings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 380;
    public double Height { get; set; } = 760;
    public bool Topmost { get; set; } = true;
    public bool Pinned { get; set; }
    public string Dock { get; set; } = "Float";
    public bool UserAppsOnly { get; set; } = true;
    public bool HideMicrosoft { get; set; }
    public bool HideIdle { get; set; } = true;
    public string Sort { get; set; } = "CPU";
    public bool StartWithWindows { get; set; }
    public bool CoachDismissed { get; set; }
    public bool Compact { get; set; }
    public DateTimeOffset? LastRestorePoint { get; set; }
    public List<int> PausedPids { get; set; } = [];
    public string? SavedPowerPlan { get; set; }
    public bool? SavedGameMode { get; set; }
    public bool PowerPlanChanged { get; set; }
    public bool GameModeChanged { get; set; }
}

public sealed record ChangeResult(bool Success, string Message);

public sealed class FreeUpPlan
{
    public List<ScanFinding> Items { get; init; } = [];
    public bool CanSetHighPerformance { get; init; }
    public bool AlreadyHighPerformance { get; init; }
    public bool GameModeAlreadyOn { get; init; }
}

public sealed class FreeUpRequest
{
    public List<ScanFinding> Selected { get; init; } = [];
    public bool EndInsteadOfPause { get; init; }
    public bool SetHighPerformance { get; init; }
    public bool EnableGameMode { get; init; }
}
