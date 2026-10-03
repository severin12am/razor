namespace ResourcePanel;

public static class ProcessControl
{
    public static ChangeResult Suspend(int pid, string name)
    {
        if (!Safety.CanControl(name))
            return new ChangeResult(false, name + " keeps Windows running, so it is left alone.");
        return WithProcess(pid, NativeMethods.ProcessSuspendResume, handle =>
        {
            var status = NativeMethods.NtSuspendProcess(handle);
            return status == 0
                ? new ChangeResult(true, "Paused " + name + ".")
                : new ChangeResult(false, "Couldn't pause " + name + " (0x" + status.ToString("X8") + ").");
        });
    }

    public static ChangeResult Resume(int pid)
    {
        return WithProcess(pid, NativeMethods.ProcessSuspendResume, handle =>
        {
            var status = NativeMethods.NtResumeProcess(handle);
            return status == 0
                ? new ChangeResult(true, "Resumed process " + pid + ".")
                : new ChangeResult(false, "Couldn't resume process " + pid + ".");
        });
    }

    public static ChangeResult End(int pid, string name)
    {
        if (!Safety.CanControl(name))
            return new ChangeResult(false, name + " keeps Windows running, so it is left alone.");
        return WithProcess(pid, NativeMethods.ProcessTerminate, handle =>
        {
            var ok = NativeMethods.TerminateProcess(handle, 1);
            return ok
                ? new ChangeResult(true, "Ended " + name + ".")
                : new ChangeResult(false, "Couldn't end " + name + " (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").");
        });
    }

    public static IReadOnlyList<(int Pid, string Name, ChangeResult Result)> EndTree(
        int rootPid,
        string rootName,
        IReadOnlyList<ProcessSample> snapshot)
    {
        var results = new List<(int, string, ChangeResult)>();
        if (!Safety.CanControl(rootName))
        {
            results.Add((rootPid, rootName, new ChangeResult(false, rootName + " keeps Windows running, so it is left alone.")));
            return results;
        }

        var children = new Dictionary<int, List<ProcessSample>>();
        foreach (var process in snapshot)
        {
            if (!children.TryGetValue(process.ParentPid, out var list))
            {
                list = [];
                children[process.ParentPid] = list;
            }
            list.Add(process);
        }

        var order = new List<ProcessSample>();
        var guard = new HashSet<int>();
        Walk(rootPid, children, order, guard);
        order.Reverse();
        var root = snapshot.FirstOrDefault(process => process.Pid == rootPid);
        order.Add(root ?? new ProcessSample { Pid = rootPid, Name = rootName });
        foreach (var process in order)
        {
            if (process.Pid != rootPid && !Safety.CanControl(process.Name))
            {
                results.Add((process.Pid, process.Name, new ChangeResult(false, process.Name + " was left running.")));
                continue;
            }
            results.Add((process.Pid, process.Name, End(process.Pid, process.Name)));
        }
        return results;
    }

    static void Walk(int pid, Dictionary<int, List<ProcessSample>> children, List<ProcessSample> order, HashSet<int> guard)
    {
        if (!guard.Add(pid) || !children.TryGetValue(pid, out var list))
            return;
        foreach (var child in list)
        {
            order.Add(child);
            Walk(child.Pid, children, order, guard);
        }
    }

    static ChangeResult WithProcess(int pid, uint access, Func<IntPtr, ChangeResult> action)
    {
        var handle = NativeMethods.OpenProcess(access, false, pid);
        if (handle == IntPtr.Zero)
            return new ChangeResult(false, "Couldn't open process " + pid + ". It may have exited, or this needs an administrator.");
        try
        {
            return action(handle);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
