using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ResourcePanel;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ProcessRow : ObservableObject
{
    static readonly Brush Normal = Freeze(Color.FromRgb(0xE8, 0xEA, 0xF0));
    static readonly Brush Warm = Freeze(Color.FromRgb(0xE2, 0xB1, 0x5A));
    static readonly Brush Hot = Freeze(Color.FromRgb(0xF0, 0x71, 0x78));

    public int Pid { get; }
    public string PidText => Pid.ToString();

    string _name = "";
    double _cpu;
    long _ramBytes;
    double _diskBytes;
    double _netBytes;
    string _subLine = "";
    string _publisher = "";
    string _kind = "App";
    string _path = "";
    string _parent = "";
    string _services = "";
    bool _isUserApp;
    bool _isMicrosoft;
    bool _isPaused;
    bool _canControl = true;
    string _power = "";

    public ProcessRow(int pid) => Pid = pid;

    public string Name { get => _name; private set => Set(ref _name, value); }
    public double Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public long RamBytes { get => _ramBytes; private set => Set(ref _ramBytes, value); }
    public double DiskBytes { get => _diskBytes; private set => Set(ref _diskBytes, value); }
    public double NetBytes { get => _netBytes; private set => Set(ref _netBytes, value); }
    public string CpuText => Formatting.Percent(_cpu);
    public string RamText => Formatting.Bytes(_ramBytes);
    public string SubLine { get => _subLine; private set => Set(ref _subLine, value); }
    public string Publisher { get => _publisher; private set => Set(ref _publisher, value); }
    public string Kind { get => _kind; private set => Set(ref _kind, value); }
    public string Path { get => _path; private set => Set(ref _path, value); }
    public string Parent { get => _parent; private set => Set(ref _parent, value); }
    public string Services { get => _services; private set => Set(ref _services, value); }
    public bool IsUserApp { get => _isUserApp; private set => Set(ref _isUserApp, value); }
    public bool IsMicrosoft { get => _isMicrosoft; private set => Set(ref _isMicrosoft, value); }
    public bool IsPaused { get => _isPaused; set { if (Set(ref _isPaused, value)) Raise(nameof(PauseLabel)); } }
    public bool CanControl { get => _canControl; private set => Set(ref _canControl, value); }
    public string Power { get => _power; private set => Set(ref _power, value); }
    public double BarValue { get; private set; }
    public double RamBar { get; private set; }
    public double DiskBar { get; private set; }
    public double NetBar { get; private set; }
    public string DiskLabel { get; private set; } = "—";
    public string NetLabel { get; private set; } = "—";
    string _activity = "";
    public string Activity
    {
        get => _activity;
        set { if (Set(ref _activity, value)) Raise(nameof(PauseLabel)); }
    }
    public string PauseLabel => string.IsNullOrEmpty(_activity) ? (_isPaused ? "Resume" : "Pause") : _activity;
    public Brush CpuBrush => _cpu >= 50 ? Hot : _cpu >= 15 ? Warm : Normal;

    public void Update(ProcessSample sample, string? parentName, string kind, bool userApp, bool microsoft, string publisher, bool paused, bool canControl)
    {
        Name = sample.Name;
        if (Set(ref _cpu, sample.CpuPercent, nameof(Cpu)))
        {
            Raise(nameof(CpuText));
            Raise(nameof(CpuBrush));
            BarValue = sample.CpuPercent <= 0.05 ? 0 : Math.Clamp(sample.CpuPercent * 2.5, 4, 100);
            Raise(nameof(BarValue));
        }
        if (Set(ref _ramBytes, sample.PrivateBytes > 0 ? sample.PrivateBytes : sample.WorkingSetBytes, nameof(RamBytes)))
            Raise(nameof(RamText));
        var disk = sample.DiskReadBytesPerSec + sample.DiskWriteBytesPerSec;
        var netKnown = !double.IsNaN(sample.NetDownBytesPerSec);
        var net = netKnown ? sample.NetDownBytesPerSec + sample.NetUpBytesPerSec : -1;
        Set(ref _diskBytes, disk, nameof(DiskBytes));
        Set(ref _netBytes, net, nameof(NetBytes));
        DiskLabel = Formatting.Rate(disk);
        NetLabel = netKnown ? Formatting.Rate(net) : "—";
        RamBar = Scale(RamBytes, 8L * 1024 * 1024 * 1024);
        DiskBar = disk <= 8 * 1024 ? 0 : Scale(disk, 2 * 1024 * 1024);
        NetBar = net <= 1024 ? 0 : Scale(net, 512 * 1024);
        Raise(nameof(DiskLabel));
        Raise(nameof(NetLabel));
        Raise(nameof(RamBar));
        Raise(nameof(DiskBar));
        Raise(nameof(NetBar));
        Kind = kind;
        Path = sample.Path ?? "";
        Parent = parentName == null ? "" : parentName + " (" + sample.ParentPid + ")";
        Services = sample.ServiceNames.Count == 0 ? "" : string.Join(", ", sample.ServiceNames);
        IsUserApp = userApp;
        IsMicrosoft = microsoft;
        Publisher = publisher;
        IsPaused = paused;
        CanControl = canControl;
        Power = sample.PowerEstimateWatts is null ? "" : "est. " + Formatting.Watts(sample.PowerEstimateWatts);
        SubLine = "pid " + sample.Pid + "  ·  " + kind
            + (string.IsNullOrWhiteSpace(publisher) ? "" : "  ·  " + publisher)
            + (string.IsNullOrWhiteSpace(Power) ? "" : "  ·  " + Power);
    }

    static double Scale(double amount, double full) =>
        Math.Clamp(amount / full * 100, amount > 0 ? 6 : 0, 100);

    static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class FindingRow : ObservableObject
{
    public required ScanFinding Finding { get; init; }
    bool _checked;
    public string Title => Finding.Title;
    public string Detail => Finding.Detail;
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

public sealed class SpeedStepRow : ObservableObject
{
    string _status = "";
    string _action = "Do this";
    bool _optimized;

    public SpeedStepRow(SpeedStep step) => Refresh(step);

    public string Id { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ActionLabel { get => _action; private set => Set(ref _action, value); }
    string _warning = "";
    public string Warning { get => _warning; private set => Set(ref _warning, value); }
    public bool HasWarning => _warning.Length > 0;
    public bool Optimized { get => _optimized; private set => Set(ref _optimized, value); }

    public void Refresh(SpeedStep step)
    {
        Id = step.Id;
        Title = step.Title;
        Detail = step.Detail;
        Warning = step.Warning;
        Raise(nameof(HasWarning));
        Optimized = step.Optimized;
        Status = step.Optimized ? "On" : "Not applied";
        ActionLabel = step.Optimized ? "Undo" : "Do this";
    }
}

public sealed class DebloatRow : ObservableObject
{
    public required DebloatChoice Choice { get; init; }
    bool _checked;
    public string Title => Choice.Item.Title;
    public string Description => Choice.Item.Description;
    public string Status => Choice.Status;
    public bool CanCheck => Choice.Present && !Choice.AlreadyDone;
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

public sealed class MainViewModel : ObservableObject
{
    readonly PublisherCache _publishers = new();
    readonly Dictionary<int, ProcessRow> _rows = new();
    readonly Dictionary<int, History> _cpuHistory = new();
    readonly Dictionary<int, History> _netHistory = new();
    readonly History _machineCpu = new();
    readonly History _machineNet = new();
    readonly HashSet<int> _paused = new();
    readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    readonly int _selfPid = Process.GetCurrentProcess().Id;
    readonly object _gate = new();

    MachineSample? _latest;
    IReadOnlyList<StartupEntry> _startup = [];
    string _page = "Home";
    string _search = "";
    string _sort = "CPU";
    ProcessRow? _selected;
    string _commandLine = "";
    string _connections = "";
    string _status = "";
    bool _busy;
    string _busyText = "";
    double[] _cpuSeries = [];
    double[] _netSeries = [];

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _sort = settings.Sort;
        foreach (var pid in settings.PausedPids)
            _paused.Add(pid);
        IsCompact = settings.Compact;
        IsAdmin = Admin.IsCurrentProcessElevated();
    }

    public AppSettings Settings { get; }
    public ObservableCollection<ProcessRow> Processes { get; } = new();
    public ObservableCollection<FindingRow> SafeItems { get; } = new();
    public ObservableCollection<FindingRow> AskItems { get; } = new();
    public ObservableCollection<FindingRow> StartupItems { get; } = new();
    public ObservableCollection<SpeedStepRow> SpeedItems { get; } = new();
    public ObservableCollection<ProcessRow> CompactRows { get; } = new();
    public ObservableCollection<DebloatRow> BloatItems { get; } = new();
    public ObservableCollection<DebloatRow> OtherItems { get; } = new();
    public ObservableCollection<DebloatRow> CommonItems { get; } = new();
    public ObservableCollection<DebloatRow> AdvancedItems { get; } = new();
    public ObservableCollection<DebloatRow> DangerItems { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public string[] SortNames { get; } = ["CPU", "Memory", "Disk", "Network", "Name"];

    public bool IsAdmin { get; }
    public bool IsCompact { get; private set; }
    public bool ShowFull => !IsCompact;
    public bool ShowFullHome => _page == "Home" && !IsCompact;
    public bool ShowCompactHome => _page == "Home" && IsCompact;
    public bool ShowHome => _page == "Home";
    public bool ShowCheck => _page == "Check";
    public bool ShowOptional => _page == "Optional";
    public bool ShowLog => _page == "Log";
    public bool ShowSpeed => _page == "Speed";
    public bool ShowBack => _page != "Home";
    public bool ShowCoach => !Settings.CoachDismissed && !IsCompact;
    public bool ShowResume => _paused.Count > 0 || Settings.PowerPlanChanged || Settings.GameModeChanged;
    public string ResumeText => _paused.Count == 0
        ? "Put the power settings back"
        : _paused.Count + (_paused.Count == 1 ? " app is paused" : " apps are paused");

    public double CpuValue { get; private set; }
    public double RamValue { get; private set; }
    public double GpuValue { get; private set; }
    public double[] MachineCpuSeries { get; private set; } = [];
    public double[] MachineNetSeries { get; private set; } = [];
    public string CpuText { get; private set; } = "—";
    public string RamText { get; private set; } = "—";
    public string GpuText { get; private set; } = "—";
    public string DiskText { get; private set; } = "—";
    public string NetText { get; private set; } = "—";
    public string BatteryText { get; private set; } = "—";
    public string FooterText { get; private set; } = "";
    public string CheckSummary { get; private set; } = "";
    public string OptionalSummary { get; private set; } = "Optional Windows apps are listed separately, so nothing is uninstalled from this page.";
    public string DetailStartup { get; private set; } = "";

    public string SearchText { get => _search; set { if (Set(ref _search, value)) Raise(nameof(SearchText)); } }
    public string Sort { get => _sort; set { if (Set(ref _sort, value)) { Settings.Sort = value; SettingsStore.Save(Settings); } } }
    public bool UserAppsOnly { get => Settings.UserAppsOnly; set { Settings.UserAppsOnly = value; Raise(); SettingsStore.Save(Settings); } }
    public bool HideMicrosoft { get => Settings.HideMicrosoft; set { Settings.HideMicrosoft = value; Raise(); SettingsStore.Save(Settings); } }
    public bool HideIdle { get => Settings.HideIdle; set { Settings.HideIdle = value; Raise(); SettingsStore.Save(Settings); } }
    public ProcessRow? SelectedProcess { get => _selected; set { if (Set(ref _selected, value)) OnSelected(); } }
    public string CommandLine { get => _commandLine; private set => Set(ref _commandLine, value); }
    public string Connections { get => _connections; private set => Set(ref _connections, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
    public double[] SelectedCpuSeries { get => _cpuSeries; private set => Set(ref _cpuSeries, value); }
    public double[] SelectedNetSeries { get => _netSeries; private set => Set(ref _netSeries, value); }
    public bool HasSelection => _selected != null;

    public void SetCompact(bool compact)
    {
        if (IsCompact == compact)
            return;
        IsCompact = compact;
        Settings.Compact = compact;
        SettingsStore.Save(Settings);
        Raise(nameof(IsCompact));
        Raise(nameof(ShowFull));
        Raise(nameof(ShowFullHome));
        Raise(nameof(ShowCompactHome));
        Raise(nameof(ShowCoach));
    }

    public void DismissCoach()
    {
        Settings.CoachDismissed = true;
        Raise(nameof(ShowCoach));
        SettingsStore.Save(Settings);
    }

    public void ShowPage(string page)
    {
        _page = page;
        Raise(nameof(ShowHome));
        Raise(nameof(ShowFullHome));
        Raise(nameof(ShowCompactHome));
        Raise(nameof(ShowCheck));
        Raise(nameof(ShowOptional));
        Raise(nameof(ShowSpeed));
        Raise(nameof(ShowLog));
        Raise(nameof(ShowBack));
        if (page == "Log")
            RefreshLog();
    }

    public bool Accepts(ProcessRow row)
    {
        if (UserAppsOnly && !row.IsUserApp)
            return false;
        if (HideMicrosoft && row.IsMicrosoft)
            return false;
        if (HideIdle && row.Cpu < 0.1 && row.DiskBytes < 10 * 1024 && (row.NetBytes < 0 || row.NetBytes < 1024))
            return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            var haystack = row.Name + " " + row.PidText + " " + row.Publisher;
            if (!haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public void Apply(MachineSample sample)
    {
        _latest = sample;
        var byPid = sample.Processes.ToDictionary(process => process.Pid);
        var seen = new HashSet<int>();
        foreach (var process in sample.Processes)
        {
            if (process.Pid <= 0)
                continue;
            seen.Add(process.Pid);
            if (!_rows.TryGetValue(process.Pid, out var row))
            {
                row = new ProcessRow(process.Pid);
                _rows[process.Pid] = row;
                Processes.Add(row);
            }
            byPid.TryGetValue(process.ParentPid, out var parent);
            var subject = _publishers.TryGet(process.Path);
            if (subject == null && !string.IsNullOrWhiteSpace(process.Path))
                _publishers.Request(process.Path);
            var publisher = subject == null ? "" : Safety.ShortPublisher(subject.Length == 0 ? null : subject);
            var microsoft = subject == null
                ? Safety.IsLikelyMicrosoft(process.Name)
                : subject.Length > 0 && Safety.LooksLikeMicrosoftPublisher(subject);
            var kind = process.ServiceNames.Count > 0
                ? "Service"
                : StartupControl.Matches(_startup, process.Path, process.Name) ? "Startup" : "App";
            var userApp = process.SessionId == _sessionId && process.ServiceNames.Count == 0 && !Safety.IsNeverKill(process.Name);
            var paused = _paused.Contains(process.Pid);
            row.Update(process, parent?.Name, kind, userApp, microsoft, publisher, paused, Safety.CanControl(process.Name) && process.Pid != _selfPid);
            Remember(process);
        }

        foreach (var pid in _rows.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            var row = _rows[pid];
            if (ReferenceEquals(_selected, row))
                SelectedProcess = null;
            Processes.Remove(row);
            _rows.Remove(pid);
            _cpuHistory.Remove(pid);
            _netHistory.Remove(pid);
            _paused.Remove(pid);
        }

        CpuValue = sample.CpuPercent;
        RamValue = sample.RamTotalBytes == 0 ? 0 : sample.RamUsedBytes * 100.0 / sample.RamTotalBytes;
        GpuValue = sample.GpuPercent ?? 0;
        CpuText = Formatting.Percent(sample.CpuPercent);
        RamText = Formatting.RamPair(sample.RamUsedBytes, sample.RamTotalBytes);
        GpuText = sample.GpuPercent is null ? "—" : Formatting.Percent(sample.GpuPercent.Value);
        _machineCpu.Add(sample.CpuPercent);
        var netTotal = sample.NetDownBytesPerSec + sample.NetUpBytesPerSec;
        _machineNet.Add(double.IsNaN(netTotal) ? 0 : netTotal);
        MachineCpuSeries = _machineCpu.ToArray();
        MachineNetSeries = _machineNet.ToArray();
        RefreshCompact();
        DiskText = "↓ " + Formatting.Rate(sample.DiskReadBytesPerSec) + "  ↑ " + Formatting.Rate(sample.DiskWriteBytesPerSec);
        NetText = "↓ " + Formatting.Rate(sample.NetDownBytesPerSec) + "  ↑ " + Formatting.Rate(sample.NetUpBytesPerSec);
        BatteryText = FormatBattery(sample);
        FooterText = "This panel " + Formatting.Percent(sample.SelfCpuPercent)
            + (IsAdmin ? "  ·  Administrator" : "  ·  Standard user")
            + (sample.PerProcessNetwork ? "  ·  Per-app network on" : "  ·  Per-app network needs admin");
        Raise(nameof(CpuValue));
        Raise(nameof(RamValue));
        Raise(nameof(GpuValue));
        Raise(nameof(MachineCpuSeries));
        Raise(nameof(MachineNetSeries));
        Raise(nameof(CpuText));
        Raise(nameof(RamText));
        Raise(nameof(GpuText));
        Raise(nameof(DiskText));
        Raise(nameof(NetText));
        Raise(nameof(BatteryText));
        Raise(nameof(FooterText));
        Raise(nameof(ShowResume));
        Raise(nameof(ResumeText));
        if (_selected != null)
            RefreshSeries(_selected.Pid);
    }

    void RefreshCompact()
    {
        var top = Processes.Where(Accepts).OrderByDescending(row => row.Cpu).ThenByDescending(row => row.RamBytes).Take(4).ToList();
        if (top.Count == CompactRows.Count && top.Select(row => row.Pid).SequenceEqual(CompactRows.Select(row => row.Pid)))
            return;
        CompactRows.Clear();
        foreach (var row in top)
            CompactRows.Add(row);
    }

    public async Task<ChangeResult> TogglePauseAsync(ProcessRow row)
    {
        var pausing = !_paused.Contains(row.Pid);
        row.Activity = pausing ? "Pausing…" : "Resuming…";
        IsBusy = true;
        BusyText = row.Activity + " " + row.Name;
        Status = BusyText;
        ChangeResult result;
        try
        {
            result = await Task.Run(() =>
            {
                lock (_gate)
                {
                    if (!pausing)
                    {
                        var resumed = ProcessControl.Resume(row.Pid);
                        if (resumed.Success)
                            _paused.Remove(row.Pid);
                        return resumed;
                    }
                    var paused = ProcessControl.Suspend(row.Pid, row.Name);
                    if (paused.Success)
                        _paused.Add(row.Pid);
                    return paused;
                }
            });
        }
        finally
        {
            row.Activity = "";
            IsBusy = false;
        }
        row.IsPaused = _paused.Contains(row.Pid);
        ActionLog.Append(row.IsPaused ? "pause" : "resume", row.Name, result.Success, result.Message, row.Pid);
        PersistPaused();
        Status = result.Success
            ? (row.IsPaused ? "Paused " + row.Name + ". It is still there. Resume brings it back." : "Resumed " + row.Name + ".")
            : result.Message;
        Raise(nameof(ShowResume));
        Raise(nameof(ResumeText));
        return result;
    }

    public async Task<ChangeResult> EndAsync(ProcessRow row)
    {
        IsBusy = true;
        BusyText = "Ending " + row.Name + "…";
        Status = BusyText;
        ChangeResult result;
        try
        {
            result = await Task.Run(() => ProcessControl.End(row.Pid, row.Name));
        }
        finally
        {
            IsBusy = false;
        }
        ActionLog.Append("end-process", row.Name, result.Success, result.Message, row.Pid);
        if (result.Success)
            _paused.Remove(row.Pid);
        PersistPaused();
        Status = result.Message;
        return result;
    }

    public string EndTree(ProcessRow row)
    {
        var snapshot = _latest?.Processes ?? [];
        var results = ProcessControl.EndTree(row.Pid, row.Name, snapshot);
        foreach (var (pid, name, result) in results)
        {
            ActionLog.Append("end-process-tree", name, result.Success, result.Message, pid);
            if (result.Success)
                _paused.Remove(pid);
        }
        PersistPaused();
        var ended = results.Count(item => item.Result.Success);
        Status = "Ended " + ended + " process" + (ended == 1 ? "" : "es") + ".";
        return Status;
    }

    public FreeUpPlan BuildFreeUpPlan()
    {
        var report = BuildReport();
        var active = PowerPlans.GetActive();
        return new FreeUpPlan
        {
            Items = report.PauseCandidates,
            CanSetHighPerformance = PowerPlans.Exists(PowerPlans.HighPerformance),
            AlreadyHighPerformance = active == PowerPlans.HighPerformance,
            GameModeAlreadyOn = GameModeSetting.IsEnabled()
        };
    }

    public async Task<string> ApplyFreeUpAsync(FreeUpRequest request)
    {
        var targets = request.Selected.Where(item => item.Pid is int).ToList();
        var messages = new List<string>();
        var paused = 0;
        var failed = 0;
        IsBusy = true;
        try
        {
            for (var index = 0; index < targets.Count; index++)
            {
                var finding = targets[index];
                var pid = finding.Pid!.Value;
                var name = finding.Title;
                BusyText = (request.EndInsteadOfPause ? "Ending " : "Pausing ") + (index + 1) + " of " + targets.Count + "\n" + name;
                Status = BusyText.Replace("\n", " ");
                await Task.Delay(40);
                var result = await Task.Run(() =>
                {
                    if (request.EndInsteadOfPause)
                        return ProcessControl.End(pid, name);
                    var suspended = ProcessControl.Suspend(pid, name);
                    if (suspended.Success)
                        lock (_gate) _paused.Add(pid);
                    return suspended;
                });
                ActionLog.Append(request.EndInsteadOfPause ? "end-process" : "pause", name, result.Success, result.Message, pid);
                if (result.Success) paused++;
                else failed++;
            }

            if (request.SetHighPerformance && PowerPlans.GetActive() != PowerPlans.HighPerformance)
            {
                BusyText = "Switching the power plan…";
                var previous = PowerPlans.GetActive();
                var result = await Task.Run(() => PowerPlans.SetActive(PowerPlans.HighPerformance));
                ActionLog.Append("power-plan", "High performance", result.Success, result.Message);
                if (result.Success && previous is Guid guid)
                {
                    Settings.SavedPowerPlan = guid.ToString();
                    Settings.PowerPlanChanged = true;
                }
                messages.Add(result.Message);
            }

            if (request.EnableGameMode && !GameModeSetting.IsEnabled())
            {
                BusyText = "Turning on Game Mode…";
                Settings.SavedGameMode = false;
                Settings.GameModeChanged = true;
                GameModeSetting.SetEnabled(true);
                ActionLog.Append("game-mode", "On", true, "Turned on Windows Game Mode.");
                messages.Add("Turned on Windows Game Mode.");
            }
        }
        finally
        {
            IsBusy = false;
        }

        PersistPaused();
        var verb = request.EndInsteadOfPause ? "Ended" : "Paused";
        var summary = verb + " " + paused + ".";
        if (failed > 0)
            summary += " " + failed + " could not be changed.";
        if (messages.Count > 0)
            summary += " " + string.Join(" ", messages);
        summary += " Nothing you left unchecked was touched.";
        Status = summary;
        Raise(nameof(ShowResume));
        Raise(nameof(ResumeText));
        return summary;
    }

    public string ResumeAll()
    {
        List<int> pids;
        lock (_gate)
            pids = _paused.ToList();
        var resumed = 0;
        foreach (var pid in pids)
        {
            var result = ProcessControl.Resume(pid);
            ActionLog.Append("resume", pid.ToString(), result.Success, result.Message, pid);
            if (result.Success)
            {
                lock (_gate) _paused.Remove(pid);
                resumed++;
            }
        }

        if (Settings.PowerPlanChanged && Guid.TryParse(Settings.SavedPowerPlan, out var plan))
        {
            var result = PowerPlans.SetActive(plan);
            ActionLog.Append("power-plan", "restore", result.Success, result.Message);
            if (result.Success)
                Settings.PowerPlanChanged = false;
        }
        if (Settings.GameModeChanged)
        {
            GameModeSetting.SetEnabled(Settings.SavedGameMode == true);
            Settings.GameModeChanged = false;
            ActionLog.Append("game-mode", "restore", true, "Restored Game Mode.");
        }
        PersistPaused();
        Status = resumed == 0 && pids.Count == 0 ? "Nothing was paused." : "Resumed " + resumed + ".";
        Raise(nameof(ShowResume));
        Raise(nameof(ResumeText));
        return Status;
    }

    public async Task RunCheckAsync()
    {
        IsBusy = true;
        BusyText = "Checking startup items…";
        IReadOnlyList<StartupEntry> startup;
        try
        {
            startup = await Task.Run(StartupControl.List);
        }
        finally
        {
            IsBusy = false;
        }
        _startup = startup;
        var report = BuildReport();
        Fill(SafeItems, report.PauseCandidates.Where(item => item.Bucket == ScanBucket.SafePause));
        Fill(AskItems, report.PauseCandidates.Where(item => item.Bucket == ScanBucket.Ask));
        Fill(StartupItems, report.Startup);
        var safe = SafeItems.Count;
        CheckSummary = safe == 0 && AskItems.Count == 0
            ? "Nothing obvious is sitting in the background. You can still end an app from the list."
            : safe + " background " + (safe == 1 ? "app is" : "apps are") + " safe to pause. "
                + AskItems.Count + " more " + (AskItems.Count == 1 ? "is" : "are") + " up to you. "
                + StartupItems.Count + " start with Windows.";
        Raise(nameof(CheckSummary));
        ShowPage("Check");
        Status = "Check finished. Nothing was changed.";
    }

    public async Task<string> PauseCheckedFindingsAsync()
    {
        var selected = SafeItems.Concat(AskItems).Where(row => row.IsChecked).Select(row => row.Finding).ToList();
        if (selected.Count == 0)
            return "Nothing is selected.";
        return await ApplyFreeUpAsync(new FreeUpRequest { Selected = selected });
    }

    public void LoadSpeed()
    {
        SpeedItems.Clear();
        foreach (var step in SpeedSteps.Detect())
            SpeedItems.Add(new SpeedStepRow(step));
        ShowPage("Speed");
    }

    public async Task ApplySpeedAsync(SpeedStepRow row)
    {
        IsBusy = true;
        BusyText = (row.Optimized ? "Putting back\n" : "Applying\n") + row.Title;
        Status = BusyText.Replace("\n", " ");
        ChangeResult result;
        try
        {
            await Task.Delay(40);
            result = await Task.Run(() => row.Optimized ? SpeedSteps.Undo(row.Id) : SpeedSteps.Apply(row.Id));
        }
        finally
        {
            IsBusy = false;
        }
        var fresh = SpeedSteps.Detect().FirstOrDefault(step => step.Id == row.Id);
        if (fresh != null)
            row.Refresh(fresh);
        Status = result.Message;
    }

    public string DisableCheckedStartup()
    {
        var selected = StartupItems.Where(row => row.IsChecked).ToList();
        if (selected.Count == 0)
            return "No startup items are selected.";
        var ok = 0;
        foreach (var row in selected)
        {
            var entry = _startup.FirstOrDefault(item => item.Id == row.Finding.StartupId);
            if (entry == null)
                continue;
            var result = StartupControl.Disable(entry);
            ActionLog.Append("startup-disable", entry.Name, result.Success, result.Message);
            if (result.Success)
                ok++;
        }
        Status = "Turned off " + ok + " startup item" + (ok == 1 ? "" : "s") + ".";
        return Status;
    }

    public async Task LoadOptionalAsync()
    {
        IsBusy = true;
        BusyText = "Looking for optional apps…";
        List<DebloatChoice> choices;
        try
        {
            choices = await Task.Run(DebloatRunner.Detect);
        }
        finally
        {
            IsBusy = false;
        }
        BloatItems.Clear();
        OtherItems.Clear();
        CommonItems.Clear();
        AdvancedItems.Clear();
        DangerItems.Clear();
        foreach (var choice in choices.Where(choice => choice.Present))
        {
            var row = new DebloatRow { Choice = choice };
            switch (choice.Item.Tier)
            {
                case DebloatTier.Suggested:
                    BloatItems.Add(row);
                    CommonItems.Add(row);
                    break;
                case DebloatTier.Choice:
                    OtherItems.Add(row);
                    CommonItems.Add(row);
                    break;
                case DebloatTier.Advanced:
                    AdvancedItems.Add(row);
                    break;
                default:
                    DangerItems.Add(row);
                    break;
            }
        }
        OptionalSummary = BloatItems.Count + " usually-not-needed items are installed. Other optional apps are listed under them. Nothing is selected.";
        Raise(nameof(OptionalSummary));
        ShowPage("Optional");
    }

    public void SelectSuggested()
    {
        foreach (var row in CommonItems)
            row.IsChecked = row.CanCheck && row.Choice.Item.Tier == DebloatTier.Suggested;
    }

    public IReadOnlyList<DebloatChoice> SelectedDebloat() =>
        CommonItems.Concat(AdvancedItems).Concat(DangerItems)
            .Where(row => row.IsChecked && row.CanCheck)
            .Select(row => row.Choice)
            .ToList();

    public async Task<string> ApplyDebloatAsync(IReadOnlyList<DebloatChoice> selected, string? phrase, bool allowWithoutRestore)
    {
        var phraseProblem = DebloatRunner.PhraseProblem(selected, phrase);
        if (phraseProblem != null)
            return phraseProblem;
        IsBusy = true;
        BusyText = "Creating a restore point…";
        string summary;
        try
        {
            var needRestore = Settings.LastRestorePoint is null
                || DateTimeOffset.Now - Settings.LastRestorePoint > TimeSpan.FromMinutes(30);
            if (needRestore)
            {
                var restore = await Task.Run(() => RestorePoints.Create(AppInfo.ProductName + " before optional changes"));
                ActionLog.Append("restore-point", "optional changes", restore.Success, restore.Message);
                if (restore.Success)
                {
                    Settings.LastRestorePoint = DateTimeOffset.Now;
                    SettingsStore.Save(Settings);
                }
                else if (!allowWithoutRestore)
                    return restore.Message;
            }
            BusyText = "Applying the selected changes…";
            var results = await Task.Run(() => DebloatRunner.Apply(selected, phrase));
            var succeeded = results.Count(result => result.Success);
            summary = succeeded + " of " + results.Count + " finished. " + string.Join(" ", results.Select(result => result.Detail));
        }
        finally
        {
            IsBusy = false;
        }
        Status = summary;
        return summary;
    }

    public async Task<string> UndoProfileAsync()
    {
        var document = ProfileStore.Load();
        if (document.Changes.Count == 0)
            return "There are no saved changes to undo.";
        IsBusy = true;
        BusyText = "Undoing saved changes…";
        try
        {
            var results = await Task.Run(() => DebloatRunner.UndoProfile(document));
            document.Changes.Clear();
            ProfileStore.Save(document);
            var summary = string.Join(" ", results.Select(result => result.Title + ": " + result.Detail));
            Status = summary;
            return summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string? StartupDetailForSelected()
    {
        if (_selected == null)
            return null;
        var entry = _startup.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(_selected.Path) && item.Command.Contains(_selected.Path, StringComparison.OrdinalIgnoreCase))
            || item.Command.Contains("\\" + _selected.Name + ".exe", StringComparison.OrdinalIgnoreCase));
        return entry == null ? null : entry.Name + " — " + entry.Location;
    }

    public ChangeResult DisableStartupForSelected()
    {
        if (_selected == null)
            return new ChangeResult(false, "Select an app first.");
        var entry = _startup.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(_selected.Path) && item.Command.Contains(_selected.Path, StringComparison.OrdinalIgnoreCase))
            || item.Command.Contains("\\" + _selected.Name + ".exe", StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return new ChangeResult(false, "This app has no startup entry we can remove.");
        var result = StartupControl.Disable(entry);
        ActionLog.Append("startup-disable", entry.Name, result.Success, result.Message, _selected.Pid);
        Status = result.Message;
        return result;
    }

    public IReadOnlyList<string> ServicesForSelected() =>
        _selected == null ? [] : (_latest?.Processes.FirstOrDefault(process => process.Pid == _selected.Pid)?.ServiceNames ?? []);

    public async Task<ChangeResult> SetServiceStartAsync(string serviceName, string start)
    {
        var previous = ServiceControl.GetStartType(serviceName);
        var result = await Task.Run(() => ServiceControl.SetStartType(serviceName, start, out _));
        if (result.Success && previous != null && !string.Equals(previous, start, StringComparison.OrdinalIgnoreCase))
        {
            ProfileStore.Add(new ProfileChange
            {
                Kind = "service",
                Id = serviceName,
                Previous = previous,
                Applied = start,
                Reversible = true
            });
        }
        ActionLog.Append("service-start", serviceName, result.Success, result.Message);
        Status = result.Message;
        return result;
    }

    public void RefreshLog()
    {
        LogLines.Clear();
        foreach (var entry in ActionLog.ReadRecent())
        {
            var pid = entry.Pid is int value ? " pid " + value : "";
            LogLines.Add(entry.Time.ToString("yyyy-MM-dd HH:mm") + "  " + entry.Action + "  " + entry.Target + pid
                + (entry.Success ? "" : "  FAILED")
                + (string.IsNullOrWhiteSpace(entry.Detail) ? "" : "  " + entry.Detail));
        }
        if (LogLines.Count == 0)
            LogLines.Add("No changes have been logged yet. The file is " + AppPaths.ActionsFile);
    }

    public void SetStartWithWindows(bool enabled)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        LoginStartup.Set(enabled, path);
        Settings.StartWithWindows = enabled;
        SettingsStore.Save(Settings);
    }

    ScanReport BuildReport()
    {
        var sample = _latest;
        var facts = sample?.Processes.Select(process => new ProcessFact
        {
            Pid = process.Pid,
            Name = process.Name,
            CpuPercent = process.CpuPercent,
            WorkingSetBytes = process.WorkingSetBytes,
            IsService = process.ServiceNames.Count > 0,
            SessionId = process.SessionId
        }).ToList() ?? [];
        return ScanPlanner.Build(facts, _startup, _sessionId, Process.GetCurrentProcess().Id, sample?.ForegroundPid);
    }

    void OnSelected()
    {
        Raise(nameof(HasSelection));
        CommandLine = "";
        Connections = "";
        DetailStartup = "";
        Raise(nameof(DetailStartup));
        if (_selected == null)
        {
            SelectedCpuSeries = [];
            SelectedNetSeries = [];
            return;
        }
        RefreshSeries(_selected.Pid);
        var pid = _selected.Pid;
        var path = _selected.Path;
        var name = _selected.Name;
        _ = Task.Run(() =>
        {
            var command = ProcessInspector.CommandLine(pid) ?? "";
            var connections = string.Join("\n", ConnectionQuery.Describe(pid));
            var startup = _startup.FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(path) && item.Command.Contains(path, StringComparison.OrdinalIgnoreCase))
                || item.Command.Contains("\\" + name + ".exe", StringComparison.OrdinalIgnoreCase));
            return (command, connections, startup?.Name + (startup == null ? "" : " — " + startup.Location));
        }).ContinueWith(task =>
        {
            if (task.IsFaulted || _selected?.Pid != pid)
                return;
            CommandLine = string.IsNullOrWhiteSpace(task.Result.command) ? "Command line unavailable." : task.Result.command;
            Connections = string.IsNullOrWhiteSpace(task.Result.connections) ? "No TCP or UDP endpoints." : task.Result.connections;
            DetailStartup = task.Result.Item3 ?? "";
            Raise(nameof(DetailStartup));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void Remember(ProcessSample sample)
    {
        if (!_cpuHistory.TryGetValue(sample.Pid, out var cpu))
        {
            cpu = new History();
            _cpuHistory[sample.Pid] = cpu;
        }
        cpu.Add(sample.CpuPercent);
        if (!_netHistory.TryGetValue(sample.Pid, out var net))
        {
            net = new History();
            _netHistory[sample.Pid] = net;
        }
        var total = double.IsNaN(sample.NetDownBytesPerSec) ? 0 : sample.NetDownBytesPerSec + sample.NetUpBytesPerSec;
        net.Add(total);
    }

    void RefreshSeries(int pid)
    {
        SelectedCpuSeries = _cpuHistory.TryGetValue(pid, out var cpu) ? cpu.ToArray() : [];
        SelectedNetSeries = _netHistory.TryGetValue(pid, out var net) ? net.ToArray() : [];
    }

    void PersistPaused()
    {
        lock (_gate)
            Settings.PausedPids = _paused.ToList();
        SettingsStore.Save(Settings);
    }

    static void Fill(ObservableCollection<FindingRow> target, IEnumerable<ScanFinding> findings)
    {
        target.Clear();
        foreach (var finding in findings)
            target.Add(new FindingRow { Finding = finding, IsChecked = finding.CheckedByDefault });
    }

    static string FormatBattery(MachineSample sample)
    {
        if (!sample.BatteryPresent)
            return "No battery";
        var percent = sample.BatteryPercent is int value ? value + "%" : "—";
        if (sample.OnAcPower && sample.DischargeWatts is null)
            return percent + "  ·  plugged in";
        if (sample.DischargeWatts is double watts)
            return percent + "  ·  " + watts.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " W";
        return sample.OnAcPower ? percent + "  ·  plugged in" : percent;
    }

    sealed class History
    {
        readonly double[] _values = new double[60];
        int _count;
        int _next;

        public void Add(double value)
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
            if (_count < _values.Length)
                _count++;
        }

        public double[] ToArray()
        {
            var result = new double[_count];
            var start = _count == _values.Length ? _next : 0;
            for (var i = 0; i < _count; i++)
                result[i] = _values[(start + i) % _values.Length];
            return result;
        }
    }
}
