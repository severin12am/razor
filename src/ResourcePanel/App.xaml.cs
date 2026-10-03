using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ResourcePanel;

public partial class App : Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;

    public static bool SmokeTest { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                AppPaths.Ensure();
                File.WriteAllText(Path.Combine(AppPaths.Root, "crash.txt"), args.Exception.ToString());
            }
            catch
            {
                // The crash log is best-effort.
            }
            args.Handled = false;
        };

        if (e.Args.Any(arg => arg == "--self-test"))
        {
            var path = Path.Combine(Path.GetTempPath(), "resourcepanel-selftest.txt");
            var index = Array.IndexOf(e.Args, "--self-test-out");
            if (index >= 0 && index + 1 < e.Args.Length)
                path = e.Args[index + 1];
            Shutdown(SelfTest.Run(path));
            return;
        }

        SmokeTest = e.Args.Any(arg => arg == "--smoke");
        if (!SmokeTest && !AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (!SmokeTest)
            ListenForShow(window);
    }

    bool AcquireSingleInstance()
    {
        _mutex = new Mutex(true, AppInfo.MutexName, out var created);
        if (created)
            return true;
        try
        {
            EventWaitHandle.OpenExisting(AppInfo.ShowEventName).Set();
        }
        catch
        {
            // The first instance may already be closing.
        }
        return false;
    }

    void ListenForShow(Window window)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowEventName);
        _ = Task.Run(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (Dispatcher.HasShutdownStarted)
                    break;
                Dispatcher.BeginInvoke(() =>
                {
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Show();
                    window.Activate();
                });
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}

static class SelfTest
{
    public static int Run(string path)
    {
        var lines = new List<string>();
        var code = 0;
        using var monitor = new MonitorLoop();
        MachineSample? last = null;
        monitor.Updated += sample => last = sample;
        try
        {
            monitor.Start();
            Thread.Sleep(3200);
        }
        catch (Exception ex)
        {
            lines.Add("exception: " + ex);
            code = 1;
        }

        if (last == null)
        {
            lines.Add("no sample");
            code = 1;
        }
        else
        {
            lines.Add("processes: " + last.Processes.Count);
            lines.Add("cpu: " + last.CpuPercent.ToString("0.0"));
            lines.Add("ram: " + last.RamUsedBytes + "/" + last.RamTotalBytes);
            lines.Add("gpu: " + (last.GpuPercent?.ToString("0.0") ?? "none"));
            lines.Add("disk: " + last.DiskReadBytesPerSec.ToString("0") + " " + last.DiskWriteBytesPerSec.ToString("0"));
            lines.Add("net: " + last.NetDownBytesPerSec.ToString("0") + " " + last.NetUpBytesPerSec.ToString("0"));
            lines.Add("battery: " + last.BatteryPresent + " " + last.BatteryPercent);
            lines.Add("selfCpu: " + last.SelfCpuPercent.ToString("0.00"));
            lines.Add("perProcessNetwork: " + last.PerProcessNetwork);
            lines.Add("networkError: " + monitor.NetworkError);
            lines.Add("probe: " + last.ProbeError);
            foreach (var process in last.Processes.OrderByDescending(process => process.WorkingSetBytes).Take(8))
                lines.Add("proc " + process.Name + " pid=" + process.Pid + " session=" + process.SessionId + " cpu=" + process.CpuPercent.ToString("0.0") + " ws=" + process.WorkingSetBytes);
            var known = last.Processes.Any(process =>
                process.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                || process.Name.Equals("svchost", StringComparison.OrdinalIgnoreCase)
                || process.Name.Equals("dwm", StringComparison.OrdinalIgnoreCase)
                || process.Name.Equals("System", StringComparison.OrdinalIgnoreCase));
            if (last.Processes.Count < 8 || last.RamTotalBytes == 0 || !known)
                code = 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Path.GetTempPath());
        File.WriteAllLines(path, lines);
        return code;
    }
}
