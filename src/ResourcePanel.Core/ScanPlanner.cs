namespace ResourcePanel;

public static class ScanPlanner
{
    public static ScanReport Build(
        IReadOnlyList<ProcessFact> processes,
        IReadOnlyList<StartupEntry> startup,
        int sessionId,
        int selfPid,
        int? foregroundPid)
    {
        var report = new ScanReport();
        foreach (var process in processes)
        {
            if (process.Pid <= 4 || process.Pid == selfPid)
                continue;
            if (process.SessionId != sessionId || process.IsService)
                continue;
            if (Safety.IsNeverKill(process.Name))
                continue;

            var foreground = foregroundPid is int fg && fg == process.Pid;
            if (Safety.IsSafePause(process.Name))
            {
                report.PauseCandidates.Add(new ScanFinding
                {
                    Id = "proc:" + process.Pid,
                    Title = process.Name,
                    Detail = foreground
                        ? "This is the window in front. It is not selected."
                        : Safety.Blurb(process.Name),
                    Bucket = ScanBucket.SafePause,
                    CheckedByDefault = !foreground,
                    Pid = process.Pid
                });
                continue;
            }

            var heavy = process.CpuPercent >= Safety.HeavyCpuPercent
                || process.WorkingSetBytes >= Safety.HeavyWorkingSetBytes;
            if (!Safety.IsAskName(process.Name) && !heavy)
                continue;

            var detail = Safety.IsAskName(process.Name)
                ? Safety.Blurb(process.Name)
                : process.CpuPercent >= 15
                    ? "Using a lot of CPU. Leave this unchecked if it is your game."
                    : "Using " + Formatting.Percent(process.CpuPercent) + " CPU and "
                        + Formatting.Bytes(process.WorkingSetBytes) + " of memory.";
            if (foreground)
                detail = "This is the window in front. " + detail;

            report.PauseCandidates.Add(new ScanFinding
            {
                Id = "proc:" + process.Pid,
                Title = process.Name,
                Detail = detail,
                Bucket = ScanBucket.Ask,
                CheckedByDefault = false,
                Pid = process.Pid
            });
        }

        report.PauseCandidates.Sort(static (a, b) =>
        {
            var bucket = a.Bucket.CompareTo(b.Bucket);
            return bucket != 0 ? bucket : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var entry in startup)
        {
            if (Safety.StartupCommandIsProtected(entry.Command))
                continue;
            report.Startup.Add(new ScanFinding
            {
                Id = entry.Id,
                Title = entry.Name,
                Detail = entry.Location + " — " + Trim(entry.Command, 140),
                Bucket = ScanBucket.Startup,
                CheckedByDefault = false,
                StartupId = entry.Id
            });
        }

        return report;
    }

    static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
