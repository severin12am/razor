using ResourcePanel;
using Xunit;

namespace ResourcePanel.Tests;

public class FormattingTests
{
    [Fact]
    public void Bytes_uses_binary_units()
    {
        Assert.Equal("1.0 KB", Formatting.Bytes(1024));
        Assert.Equal("1.5 KB", Formatting.Bytes(1536));
        Assert.Equal("1.0 MB", Formatting.Bytes(1024 * 1024));
    }

    [Fact]
    public void Percent_and_rate_have_stable_text()
    {
        Assert.Equal("12.5%", Formatting.Percent(12.5));
        Assert.Equal("—", Formatting.Percent(double.NaN));
        Assert.Equal("1.0 KB/s", Formatting.Rate(1024));
        Assert.Equal("—", Formatting.Watts(null));
        Assert.Equal("1.5 W", Formatting.Watts(1.5));
    }
}

public class SafetyTests
{
    [Theory]
    [InlineData("lsass")]
    [InlineData("svchost.exe")]
    [InlineData("MsMpEng")]
    [InlineData("explorer")]
    [InlineData("csrss")]
    public void Protected_processes_cannot_be_controlled(string name)
    {
        Assert.False(Safety.CanControl(name));
    }

    [Fact]
    public void OneDrive_is_safe_and_chrome_is_a_choice()
    {
        Assert.True(Safety.IsSafePause("OneDrive.exe"));
        Assert.True(Safety.IsAskName("chrome"));
        Assert.False(Safety.IsSafePause("chrome"));
    }

    [Fact]
    public void Publisher_names_are_shortened()
    {
        Assert.Equal("Microsoft Corporation", Safety.ShortPublisher("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond"));
        Assert.True(Safety.LooksLikeMicrosoftPublisher("CN=Microsoft Windows, O=Microsoft Corporation"));
        Assert.False(Safety.LooksLikeMicrosoftPublisher("CN=Google LLC, O=Google LLC"));
    }
}

public class ScanPlannerTests
{
    [Fact]
    public void Free_up_list_leaves_windows_and_security_alone()
    {
        var report = ScanPlanner.Build(new[]
        {
            Fact(4, "System", 1, 1_000_000),
            Fact(100, "lsass", 0, 20_000_000),
            Fact(101, "svchost", 2, 80_000_000, service: true),
            Fact(102, "MsMpEng", 4, 200_000_000),
            Fact(103, "explorer", 1, 150_000_000),
            Fact(200, "OneDrive", 0.4, 80_000_000),
            Fact(201, "chrome", 22, 900_000_000),
            Fact(202, "notepad", 0.01, 10_000_000),
            Fact(8, "ResourcePanel", 0.2, 40_000_000)
        }, [], sessionId: 1, selfPid: 8, foregroundPid: null);

        var titles = report.PauseCandidates.Select(item => item.Title).ToList();
        Assert.Contains("OneDrive", titles);
        Assert.Contains("chrome", titles);
        Assert.DoesNotContain("lsass", titles);
        Assert.DoesNotContain("svchost", titles);
        Assert.DoesNotContain("MsMpEng", titles);
        Assert.DoesNotContain("explorer", titles);
        Assert.DoesNotContain("notepad", titles);
        Assert.DoesNotContain("ResourcePanel", titles);
        Assert.True(report.PauseCandidates.Single(item => item.Title == "OneDrive").CheckedByDefault);
        Assert.False(report.PauseCandidates.Single(item => item.Title == "chrome").CheckedByDefault);
        var game = ScanPlanner.Build(new[] { Fact(300, "MyGame", 40, 2_000_000_000) }, [], 1, 1, null);
        Assert.False(game.PauseCandidates.Single().CheckedByDefault);
    }

    [Fact]
    public void Foreground_safe_app_is_not_preselected()
    {
        var report = ScanPlanner.Build(new[] { Fact(50, "Widgets", 1, 40_000_000) }, [], 1, 1, foregroundPid: 50);
        var finding = Assert.Single(report.PauseCandidates);
        Assert.False(finding.CheckedByDefault);
        Assert.Contains("window in front", finding.Detail);
    }

    [Fact]
    public void Protected_startup_commands_are_skipped()
    {
        var report = ScanPlanner.Build([], new[]
        {
            Entry("def", "SecurityHealth", @"C:\Windows\System32\SecurityHealthSystray.exe"),
            Entry("note", "Notes", @"C:\Apps\Notes.exe")
        }, 1, 1, null);
        Assert.DoesNotContain(report.Startup, item => item.Title == "SecurityHealth");
        Assert.Contains(report.Startup, item => item.Title == "Notes");
    }

    static ProcessFact Fact(int pid, string name, double cpu, long ram, bool service = false) => new()
    {
        Pid = pid,
        Name = name,
        CpuPercent = cpu,
        WorkingSetBytes = ram,
        IsService = service,
        SessionId = 1
    };

    static StartupEntry Entry(string id, string name, string command) => new()
    {
        Id = id,
        Name = name,
        Command = command,
        Location = "test",
        Kind = "runKey"
    };
}

public class DebloatTests
{
    [Fact]
    public void Suggested_items_do_not_touch_security_or_boot()
    {
        Assert.DoesNotContain(DebloatCatalog.Suggested, item => item.Tier is DebloatTier.Security or DebloatTier.Boot);
        Assert.DoesNotContain(DebloatCatalog.Items.Where(item => item.Tier == DebloatTier.Suggested), item =>
            item.ServiceName is "WinDefend" or "wuauserv" or "RpcSs" or "mpssvc");
        Assert.Contains(DebloatCatalog.Items, item => item.Id == "svc-windefend" && item.Tier == DebloatTier.Security);
        Assert.Contains(DebloatCatalog.Items, item => item.Id == "svc-rpcss" && item.Tier == DebloatTier.Boot);
    }

    [Fact]
    public void Dangerous_items_need_a_typed_phrase()
    {
        var security = new DebloatChoice
        {
            Item = DebloatCatalog.Items.Single(item => item.Id == "svc-windefend"),
            Present = true
        };
        var boot = new DebloatChoice
        {
            Item = DebloatCatalog.Items.Single(item => item.Id == "svc-rpcss"),
            Present = true
        };
        Assert.Equal(Safety.SecurityPhrase, DebloatRunner.RequiredPhrase(new[] { security }));
        Assert.Equal(Safety.BootPhrase, DebloatRunner.RequiredPhrase(new[] { security, boot }));
        Assert.Null(DebloatRunner.PhraseProblem(new[] { security }, Safety.SecurityPhrase));
        Assert.NotNull(DebloatRunner.PhraseProblem(new[] { security }, "yes"));
    }

    [Theory]
    [InlineData("Microsoft.GamingApp_1.0.0.0_x64__8wekyb3d8bbwe", true)]
    [InlineData("Microsoft.GamingApp; Remove-Item C:\\", false)]
    [InlineData("-Feature", false)]
    [InlineData("", false)]
    public void Package_names_are_restricted(string value, bool safe) =>
        Assert.Equal(safe, Tokens.IsSafeToken(value));

    [Fact]
    public void Speed_steps_explain_themselves_and_skip_security()
    {
        var steps = SpeedSteps.Detect();
        Assert.Contains(steps, step => step.Id == "window-animations");
        Assert.All(steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Detail));
            Assert.DoesNotContain("Defender", step.Title);
            Assert.DoesNotContain("Windows Update", step.Title);
        });
    }

    [Fact]
    public void Profile_round_trips()
    {
        var document = new ProfileDocument();
        document.Changes.Add(new ProfileChange
        {
            Kind = "service",
            Id = "XblGameSave",
            Previous = "Manual",
            Applied = "Disabled",
            Reversible = true
        });
        var json = System.Text.Json.JsonSerializer.Serialize(document, JsonFiles.Options);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<ProfileDocument>(json, JsonFiles.Options);
        Assert.NotNull(loaded);
        Assert.Equal("XblGameSave", loaded!.Changes.Single().Id);
        Assert.Equal("Manual", loaded.Changes[0].Previous);
    }
}
