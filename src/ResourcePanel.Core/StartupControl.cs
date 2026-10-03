using Microsoft.Win32;

namespace ResourcePanel;

public static class StartupControl
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    public static IReadOnlyList<StartupEntry> List()
    {
        var entries = new List<StartupEntry>();
        try { ReadRun(Registry.CurrentUser, "HKCU", RunKey, "Registry HKCU Run", entries); } catch { /* one hive failing is not fatal */ }
        try { ReadRun(Registry.CurrentUser, "HKCU", RunOnceKey, "Registry HKCU RunOnce", entries); } catch { }
        try { ReadRun(Registry.LocalMachine, "HKLM", RunKey, "Registry HKLM Run", entries); } catch { }
        try { ReadRun(Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Registry HKLM Run (32-bit)", entries); } catch { }
        try { ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Your Startup folder", entries); } catch { }
        try { ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup folder", entries); } catch { }
        try { entries.AddRange(ReadTasks()); } catch { }
        return entries;
    }

    public static bool Matches(IReadOnlyList<StartupEntry> entries, string? path, string name)
    {
        var needle = "\\" + Safety.BareName(name) + ".exe";
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(path)
                && entry.Command.Contains(path, StringComparison.OrdinalIgnoreCase))
                return true;
            if (entry.Command.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static ChangeResult Disable(StartupEntry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case "runKey":
                    return DisableRunKey(entry);
                case "startupFile":
                    return DisableStartupFile(entry);
                case "scheduledTask":
                    return DisableTask(entry);
                default:
                    return new ChangeResult(false, "Unknown startup kind.");
            }
        }
        catch (Exception ex)
        {
            return new ChangeResult(false, ex.Message);
        }
    }

    public static ChangeResult Restore(ProfileChange change)
    {
        try
        {
            switch (change.Kind)
            {
                case "runKey":
                    return RestoreRunKey(change);
                case "startupFile":
                    return RestoreStartupFile(change);
                case "scheduledTask":
                    return EnableTask(change.Id);
                case "copilotPolicy":
                    return CopilotPolicy.Restore(change.Previous);
                default:
                    return new ChangeResult(false, "This change cannot be restored here.");
            }
        }
        catch (Exception ex)
        {
            return new ChangeResult(false, ex.Message);
        }
    }

    static void ReadRun(RegistryKey hive, string hiveName, string keyPath, string location, List<StartupEntry> entries)
    {
        using var key = hive.OpenSubKey(keyPath);
        if (key == null)
            return;
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command))
                continue;
            if (Safety.StartupCommandIsProtected(command))
                continue;
            entries.Add(new StartupEntry
            {
                Id = "run:" + hiveName + ":" + keyPath + ":" + name,
                Name = name,
                Command = command,
                Location = location,
                Kind = "runKey",
                Hive = hiveName,
                KeyPath = keyPath,
                ValueName = name
            });
        }
    }

    static void ReadFolder(string folder, string location, List<StartupEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                continue;
            entries.Add(new StartupEntry
            {
                Id = "file:" + file,
                Name = name,
                Command = file,
                Location = location,
                Kind = "startupFile",
                FilePath = file
            });
        }
    }

    static List<StartupEntry> ReadTasks()
    {
        var results = new List<StartupEntry>();
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type == null)
            return results;
        dynamic scheduler = Activator.CreateInstance(type)!;
        scheduler.Connect();
        Walk(scheduler.GetFolder("\\"), results, 0);
        return results;
    }

    static void Walk(dynamic folder, List<StartupEntry> results, int depth)
    {
        if (depth > 6 || results.Count > 400)
            return;
        dynamic tasks = folder.GetTasks(1);
        foreach (dynamic task in tasks)
        {
            try
            {
                string path = task.Path;
                if (path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Convert.ToBoolean(task.Enabled))
                    continue;
                string user = "";
                string author = "";
                try { user = (string)task.Definition.Principal.UserId; } catch { }
                try { author = (string)task.Definition.RegistrationInfo.Author; } catch { }
                var current = Environment.UserName;
                var owned = user.Contains(current, StringComparison.OrdinalIgnoreCase)
                    || author.Contains(current, StringComparison.OrdinalIgnoreCase);
                if (!owned)
                    continue;
                string name = task.Name;
                results.Add(new StartupEntry
                {
                    Id = "task:" + path,
                    Name = name,
                    Command = path,
                    Location = "Scheduled task you own",
                    Kind = "scheduledTask",
                    TaskPath = path
                });
            }
            catch
            {
                // One unreadable task should not hide the rest.
            }
        }

        dynamic folders = folder.GetFolders(0);
        foreach (dynamic child in folders)
            Walk(child, results, depth + 1);
    }

    static ChangeResult DisableRunKey(StartupEntry entry)
    {
        var hive = OpenHive(entry.Hive);
        if (hive == null || entry.KeyPath == null || entry.ValueName == null)
            return new ChangeResult(false, "Missing registry location.");
        using var key = hive.OpenSubKey(entry.KeyPath, writable: true);
        if (key == null)
            return new ChangeResult(false, "Couldn't open the startup key. HKLM needs an administrator.");
        var current = key.GetValue(entry.ValueName) as string;
        key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
        ProfileStore.Add(new ProfileChange
        {
            Kind = "runKey",
            Id = entry.Id,
            Previous = current,
            Extra = entry.Hive + "|" + entry.KeyPath + "|" + entry.ValueName,
            Reversible = true,
            Note = entry.Name
        });
        return new ChangeResult(true, "Removed " + entry.Name + " from startup.");
    }

    static ChangeResult RestoreRunKey(ProfileChange change)
    {
        var parts = (change.Extra ?? "").Split('|');
        if (parts.Length != 3)
            return new ChangeResult(false, "The saved startup entry is incomplete.");
        var hive = OpenHive(parts[0]);
        if (hive == null)
            return new ChangeResult(false, "Unknown registry hive.");
        using var key = hive.OpenSubKey(parts[1], writable: true) ?? hive.CreateSubKey(parts[1]);
        if (change.Previous == null)
            return new ChangeResult(false, "There was no previous value to put back.");
        key.SetValue(parts[2], change.Previous);
        return new ChangeResult(true, "Restored startup entry " + parts[2] + ".");
    }

    static ChangeResult DisableStartupFile(StartupEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FilePath) || !File.Exists(entry.FilePath))
            return new ChangeResult(false, "The startup file is already gone.");
        AppPaths.Ensure();
        Directory.CreateDirectory(AppPaths.StartupBackup);
        var backup = Path.Combine(AppPaths.StartupBackup, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(entry.FilePath));
        File.Move(entry.FilePath, backup);
        ProfileStore.Add(new ProfileChange
        {
            Kind = "startupFile",
            Id = entry.Id,
            Previous = entry.FilePath,
            Applied = backup,
            Reversible = true,
            Note = entry.Name
        });
        return new ChangeResult(true, "Moved " + entry.Name + " out of the Startup folder.");
    }

    static ChangeResult RestoreStartupFile(ProfileChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Previous) || string.IsNullOrWhiteSpace(change.Applied))
            return new ChangeResult(false, "The backup path is missing.");
        if (!File.Exists(change.Applied))
            return new ChangeResult(false, "The backup file is missing.");
        var directory = Path.GetDirectoryName(change.Previous);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        if (File.Exists(change.Previous))
            return new ChangeResult(false, "Something is already in the original Startup location.");
        File.Move(change.Applied, change.Previous);
        return new ChangeResult(true, "Put " + Path.GetFileName(change.Previous) + " back in Startup.");
    }

    static ChangeResult DisableTask(StartupEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.TaskPath))
            return new ChangeResult(false, "Missing task path.");
        var task = OpenTask(entry.TaskPath) ?? throw new InvalidOperationException("The scheduled task was not found.");
        task.Enabled = false;
        ProfileStore.Add(new ProfileChange
        {
            Kind = "scheduledTask",
            Id = entry.TaskPath,
            Previous = "enabled",
            Applied = "disabled",
            Reversible = true,
            Note = entry.Name
        });
        return new ChangeResult(true, "Turned off the scheduled task " + entry.Name + ".");
    }

    static ChangeResult EnableTask(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ChangeResult(false, "Missing task path.");
        var task = OpenTask(path);
        if (task == null)
            return new ChangeResult(false, "The scheduled task is gone.");
        task.Enabled = true;
        return new ChangeResult(true, "Turned the scheduled task back on.");
    }

    static dynamic? OpenTask(string fullPath)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type == null)
            return null;
        dynamic scheduler = Activator.CreateInstance(type)!;
        scheduler.Connect();
        var slash = fullPath.LastIndexOf('\\');
        var folderPath = slash <= 0 ? "\\" : fullPath[..slash];
        var name = fullPath[(slash + 1)..];
        dynamic folder = scheduler.GetFolder(folderPath);
        return folder.GetTask(name);
    }

    static RegistryKey? OpenHive(string? hive) => hive switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        _ => null
    };
}

public static class CopilotPolicy
{
    const string KeyPath = @"Software\Policies\Microsoft\Windows\WindowsCopilot";
    const string ValueName = "TurnOffWindowsCopilot";

    public static (bool Hidden, string? Previous) Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key?.GetValue(ValueName) is int value)
            return (value != 0, value.ToString());
        return (false, null);
    }

    public static ChangeResult Hide()
    {
        var previous = Read().Previous;
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        ProfileStore.Add(new ProfileChange
        {
            Kind = "copilotPolicy",
            Id = KeyPath,
            Previous = previous ?? "missing",
            Applied = "1",
            Reversible = true,
            Note = "Hide Copilot button"
        });
        return new ChangeResult(true, "The Copilot button is set to hidden. Sign out if it is still on the taskbar.");
    }

    public static ChangeResult Restore(string? previous)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (string.IsNullOrWhiteSpace(previous) || previous == "missing")
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return new ChangeResult(true, "The Copilot button policy was removed.");
        }
        if (!int.TryParse(previous, out var value))
            return new ChangeResult(false, "The saved Copilot setting was not a number.");
        key.SetValue(ValueName, value, RegistryValueKind.DWord);
        return new ChangeResult(true, "Restored the Copilot button policy.");
    }
}

public static class LoginStartup
{
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(AppInfo.ProductName) != null;
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            key.SetValue(AppInfo.ProductName, "\"" + exePath + "\"");
        else
            key.DeleteValue(AppInfo.ProductName, throwOnMissingValue: false);
    }
}
