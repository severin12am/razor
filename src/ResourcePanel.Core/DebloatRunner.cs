namespace ResourcePanel;

public static class DebloatRunner
{
    public sealed record StepResult(string Title, bool Success, string Detail, string Command);

    public static List<DebloatChoice> Detect()
    {
        var installed = new HashSet<string>(ReadAppxNames(), StringComparer.OrdinalIgnoreCase);
        var uninstalls = ReadUninstallNames();
        var features = Admin.IsCurrentProcessElevated() ? ReadFeatures() : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var choices = new List<DebloatChoice>();
        foreach (var item in DebloatCatalog.Items)
        {
            var choice = new DebloatChoice { Item = item, Status = "Not installed" };
            switch (item.Action)
            {
                case DebloatAction.Appx:
                    var found = item.AppxNames.Where(installed.Contains).ToArray();
                    choice.Present = found.Length > 0;
                    choice.Status = choice.Present ? "Installed" : "Not installed";
                    break;
                case DebloatAction.UninstallName:
                    var display = uninstalls.FirstOrDefault(name =>
                        item.UninstallNameContains.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));
                    choice.Present = display != null;
                    choice.UninstallDisplayName = display;
                    choice.Status = display ?? "Not installed";
                    break;
                case DebloatAction.Feature:
                    choice.Present = true;
                    if (item.FeatureName != null && features.TryGetValue(item.FeatureName, out var state))
                    {
                        choice.Status = state;
                        choice.AlreadyDone = state.Contains("Disabled", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                        choice.Status = Admin.IsCurrentProcessElevated() ? "Not present" : "Needs administrator to check";
                    choice.Present = choice.Status is not "Not present";
                    break;
                case DebloatAction.Service:
                    var start = item.ServiceName == null ? null : ServiceControl.GetStartType(item.ServiceName);
                    choice.Present = start != null;
                    choice.PreviousServiceStart = start;
                    choice.AlreadyDone = start != null && item.ServiceStart != null
                        && string.Equals(start, item.ServiceStart, StringComparison.OrdinalIgnoreCase);
                    choice.Status = start == null ? "Not installed" : "Start: " + start;
                    break;
                case DebloatAction.CopilotPolicy:
                    var policy = CopilotPolicy.Read();
                    choice.Present = true;
                    choice.AlreadyDone = policy.Hidden;
                    choice.Status = policy.Hidden ? "Already hidden" : "Button can be hidden";
                    break;
            }
            choices.Add(choice);
        }
        return choices;
    }

    public static IReadOnlyList<string> Preview(DebloatChoice choice)
    {
        var item = choice.Item;
        var lines = new List<string>();
        switch (item.Action)
        {
            case DebloatAction.Appx:
                foreach (var name in item.AppxNames.Where(Tokens.IsSafeToken))
                    lines.Add("Remove-AppxPackage for " + name);
                break;
            case DebloatAction.UninstallName when Tokens.IsSafeDisplayName(choice.UninstallDisplayName):
                lines.Add("winget uninstall --name \"" + choice.UninstallDisplayName + "\" --exact");
                break;
            case DebloatAction.Feature when Tokens.IsSafeToken(item.FeatureName):
                lines.Add("Disable-WindowsOptionalFeature -Online -FeatureName " + item.FeatureName + " -NoRestart");
                break;
            case DebloatAction.Service:
                lines.Add("Set service " + item.ServiceName + " to " + item.ServiceStart);
                break;
            case DebloatAction.CopilotPolicy:
                lines.Add(@"Set HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot\TurnOffWindowsCopilot = 1");
                break;
        }
        return lines;
    }

    public static string? RequiredPhrase(IReadOnlyList<DebloatChoice> selected)
    {
        if (selected.Any(choice => choice.Item.Tier == DebloatTier.Boot))
            return Safety.BootPhrase;
        if (selected.Any(choice => choice.Item.Tier == DebloatTier.Security))
            return Safety.SecurityPhrase;
        return null;
    }

    public static string? PhraseProblem(IReadOnlyList<DebloatChoice> selected, string? phrase)
    {
        var required = RequiredPhrase(selected);
        if (required == null)
            return null;
        if (!string.Equals(phrase?.Trim(), required, StringComparison.Ordinal))
            return "Type " + required + " to confirm this.";
        return null;
    }

    public static List<StepResult> Apply(IReadOnlyList<DebloatChoice> selected, string? phrase)
    {
        var problem = PhraseProblem(selected, phrase);
        if (problem != null)
            return [new StepResult("Confirmation", false, problem, "")];

        var results = new List<StepResult>();
        foreach (var choice in selected)
        {
            if (choice.AlreadyDone)
            {
                results.Add(new StepResult(choice.Item.Title, true, "Already in the requested state.", ""));
                continue;
            }
            try
            {
                results.Add(ApplyOne(choice));
            }
            catch (Exception ex)
            {
                results.Add(new StepResult(choice.Item.Title, false, ex.Message, ""));
            }
        }
        return results;
    }

    static StepResult ApplyOne(DebloatChoice choice)
    {
        var item = choice.Item;
        switch (item.Action)
        {
            case DebloatAction.Appx:
                return RemoveAppx(item);
            case DebloatAction.UninstallName:
                return UninstallByName(choice);
            case DebloatAction.Feature:
                return DisableFeature(item);
            case DebloatAction.Service:
                return ChangeService(item, choice.PreviousServiceStart);
            case DebloatAction.CopilotPolicy:
                var hidden = CopilotPolicy.Hide();
                ActionLog.Append("copilot-policy", item.Title, hidden.Success, hidden.Message);
                return new StepResult(item.Title, hidden.Success, hidden.Message, Preview(choice).FirstOrDefault() ?? "");
            default:
                return new StepResult(item.Title, false, "Unknown action.", "");
        }
    }

    static StepResult RemoveAppx(DebloatItem item)
    {
        var messages = new List<string>();
        var ok = true;
        foreach (var name in item.AppxNames)
        {
            if (!Tokens.IsSafeToken(name))
            {
                ok = false;
                messages.Add("Rejected package name " + name);
                continue;
            }
            var script = "$ErrorActionPreference='Stop'; Get-AppxPackage -Name '" + name + "' | Remove-AppxPackage";
            var ran = ShellCommands.RunPowerShell(script);
            var success = ran.ExitCode == 0;
            if (!success && Admin.IsCurrentProcessElevated())
            {
                var allUsers = "$ErrorActionPreference='Stop'; Get-AppxPackage -AllUsers -Name '" + name + "' | Remove-AppxPackage -AllUsers";
                ran = ShellCommands.RunPowerShell(allUsers);
                success = ran.ExitCode == 0;
            }
            if (success && Admin.IsCurrentProcessElevated())
            {
                var provisioned = "$ErrorActionPreference='Stop'; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq '" + name + "' | Remove-AppxProvisionedPackage -Online";
                _ = ShellCommands.RunPowerShell(provisioned);
            }
            ok &= success;
            messages.Add(success ? "Removed " + name : "Could not remove " + name + " " + Trim(ran.Error + ran.Output));
            if (success)
            {
                ProfileStore.Add(new ProfileChange
                {
                    Kind = "appx",
                    Id = name,
                    Reversible = false,
                    Note = "Install it again from the Microsoft Store if you want it back."
                });
            }
            ActionLog.Append("remove-appx", name, success, messages[^1]);
        }
        return new StepResult(item.Title, ok, string.Join(" ", messages), "Remove-AppxPackage");
    }

    static StepResult UninstallByName(DebloatChoice choice)
    {
        var display = choice.UninstallDisplayName ?? "";
        if (!Tokens.IsSafeDisplayName(display))
            return new StepResult(choice.Item.Title, false, "The uninstall name looked unsafe, so it was skipped.", "");
        var ran = ShellCommands.RunWinget(
        [
            "uninstall", "--name", display, "--exact", "--disable-interactivity", "--accept-source-agreements"
        ]);
        var success = ran.ExitCode == 0;
        var detail = success ? "Uninstalled " + display + "." : "winget could not remove " + display + ". " + Trim(ran.Error + ran.Output);
        if (success)
        {
            ProfileStore.Add(new ProfileChange
            {
                Kind = "uninstall",
                Id = display,
                Reversible = false,
                Note = "Reinstall it from the vendor if you want it back."
            });
        }
        ActionLog.Append("winget-uninstall", display, success, detail);
        return new StepResult(choice.Item.Title, success, detail, "winget uninstall --name \"" + display + "\" --exact");
    }

    static StepResult DisableFeature(DebloatItem item)
    {
        var feature = item.FeatureName ?? "";
        if (!Tokens.IsSafeToken(feature))
            return new StepResult(item.Title, false, "Rejected feature name.", "");
        if (!Admin.IsCurrentProcessElevated())
            return new StepResult(item.Title, false, "Restart as admin to change Windows features.", "");
        var script = "$ErrorActionPreference='Stop'; Disable-WindowsOptionalFeature -Online -FeatureName '" + feature + "' -NoRestart";
        var ran = ShellCommands.RunPowerShell(script, 180_000);
        var success = ran.ExitCode == 0;
        if (success)
        {
            ProfileStore.Add(new ProfileChange
            {
                Kind = "feature",
                Id = feature,
                Previous = "Enabled",
                Applied = "Disabled",
                Reversible = true
            });
        }
        var detail = success ? "Turned off " + feature + "." : Trim(ran.Error + ran.Output);
        ActionLog.Append("disable-feature", feature, success, detail);
        return new StepResult(item.Title, success, detail, "Disable-WindowsOptionalFeature -FeatureName " + feature);
    }

    public static ChangeResult EnableFeature(string feature)
    {
        if (!Tokens.IsSafeToken(feature))
            return new ChangeResult(false, "Rejected feature name.");
        var script = "$ErrorActionPreference='Stop'; Enable-WindowsOptionalFeature -Online -FeatureName '" + feature + "' -NoRestart";
        var ran = ShellCommands.RunPowerShell(script, 180_000);
        return ran.ExitCode == 0
            ? new ChangeResult(true, "Turned " + feature + " back on.")
            : new ChangeResult(false, Trim(ran.Error + ran.Output));
    }

    static StepResult ChangeService(DebloatItem item, string? previous)
    {
        var name = item.ServiceName ?? "";
        var target = item.ServiceStart ?? "";
        var result = ServiceControl.SetStartType(name, target, out var observed);
        previous ??= observed;
        if (result.Success && !string.Equals(previous, target, StringComparison.OrdinalIgnoreCase))
        {
            ProfileStore.Add(new ProfileChange
            {
                Kind = "service",
                Id = name,
                Previous = previous,
                Applied = target,
                Reversible = true,
                Note = item.Title
            });
        }
        ActionLog.Append("service-start", name, result.Success, result.Message);
        return new StepResult(item.Title, result.Success, result.Message, "Set " + name + " to " + target);
    }

    public static List<StepResult> UndoProfile(ProfileDocument document)
    {
        var results = new List<StepResult>();
        for (var i = document.Changes.Count - 1; i >= 0; i--)
        {
            var change = document.Changes[i];
            ChangeResult result = change.Kind switch
            {
                "service" when change.Previous != null => ServiceControl.SetStartType(change.Id, change.Previous, out _),
                "feature" => EnableFeature(change.Id),
                "runKey" or "startupFile" or "scheduledTask" or "copilotPolicy" => StartupControl.Restore(change),
                "appx" or "uninstall" => new ChangeResult(false, change.Note ?? "Install it again from the Store or the vendor."),
                _ => new ChangeResult(false, "No undo for " + change.Kind + ".")
            };
            ActionLog.Append("undo", change.Id, result.Success, result.Message);
            results.Add(new StepResult(change.Note ?? change.Id, result.Success, result.Message, change.Kind));
        }
        return results;
    }

    static IEnumerable<string> ReadAppxNames()
    {
        var ran = ShellCommands.RunPowerShell("Get-AppxPackage | ForEach-Object { $_.Name }", 60_000);
        if (ran.ExitCode != 0)
            return [];
        return ran.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(static line => line.Trim());
    }

    static List<string> ReadUninstallNames()
    {
        var names = new List<string>();
        string[] paths =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            foreach (var path in paths)
            {
                using var key = hive.OpenSubKey(path);
                if (key == null)
                    continue;
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        if (sub?.GetValue("DisplayName") is string display && !string.IsNullOrWhiteSpace(display))
                            names.Add(display.Trim());
                    }
                    catch
                    {
                        // Some uninstall keys are not readable.
                    }
                }
            }
        }
        return names;
    }

    static Dictionary<string, string> ReadFeatures()
    {
        var names = DebloatCatalog.Items
            .Select(item => item.FeatureName)
            .Where(Tokens.IsSafeToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
            return new(StringComparer.OrdinalIgnoreCase);
        var list = string.Join(",", names.Select(name => "'" + name + "'"));
        var script = "$names=@(" + list + "); foreach($n in $names){ $f=Get-WindowsOptionalFeature -Online -FeatureName $n -ErrorAction SilentlyContinue; if($f){ \"$($f.FeatureName)=$($f.State)\" } }";
        var ran = ShellCommands.RunPowerShell(script, 180_000);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ran.ExitCode != 0)
            return map;
        foreach (var line in ran.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var split = line.Trim().Split('=', 2);
            if (split.Length == 2)
                map[split[0]] = split[1];
        }
        return map;
    }

    static string Trim(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 240 ? text : text[..240];
    }
}
