# ResourcePanel

ResourcePanel is a local, free, always-on-top Windows panel. It shows what the PC is doing right now, and it lets you pause the background apps that get in the way. Pausing is reversible. There is no account and no telemetry.

**Open it:** download `ResourcePanel.exe` from the latest release and double-click it. Nothing else to install. In the panel menu, **Put a shortcut on the desktop** makes the next open one click. **Top processes** is always the way back to the list of what is using the PC.

The name is a placeholder. Change `AppInfo.ProductName` in `src/ResourcePanel.Core/AppInfo.cs`, and the assembly name in `src/ResourcePanel/ResourcePanel.csproj`, when you pick another one.

## What you actually click

The panel is meant for the case where a laptop is loud, hot, or stuttering in a game, and the useful switches are buried in too many settings.

1. **Check PC** looks through what is running, what starts with Windows, and which optional apps are installed. It changes nothing.
2. **Free up now** pauses non-critical background apps after showing you the list. Uncheck anything you still want. Windows, security software, and the window in front are not on the safe list. **Resume all** puts the paused apps back, and also puts the power plan and Game Mode back if those were changed.
3. The process list is the manual path. **Pause** shows what it is doing, then the app stays paused until you resume it. **End** asks first.
4. **Speed up** is a list of Windows tweaks. Each one explains itself. **Do this** applies only that step. **Undo** puts it back. Security, Windows Update, search indexing, and similar steps are in the same list with a red warning and a confirm.
5. **Top processes** brings you back to the live list from any other page.

Free up now can also switch to the High performance power plan and turn on Windows Game Mode. Those two are how Windows itself asks a laptop to favor a game. They are checkboxes, and Resume all undoes them.

Optional apps (Xbox, Clipchamp, OEM trials, and so on) are not removed by Free up now. Open **Optional apps** when you want that, read what each item does, and apply only what you checked.

## Screenshot

A narrow dark panel sits on the right side of the desktop. The title bar reads ResourcePanel, with Pin, Top, Dock, minimize, and a menu. Under it, six readings sit in a grid: CPU 18%, Memory 9.2 GB / 16 GB, GPU 4%, Disk down 1.1 MB/s and up 0.2 MB/s, Network down 2.4 MB/s and up 0.1 MB/s, and Battery 64% discharging at 14 W. Two buttons fill the next row: Check PC, and Free up now in green. A line says "3 apps are paused" with Resume all. Below that, a search box, a CPU / Memory / Disk / Network / Name sort menu, and three chips: My apps, Hide Microsoft, Hide idle. The list shows Chrome at 11% and 1.4 GB, with disk and network on the second line, and Pause and End on the right. The selected row opens a details block: full path, command line, parent process, publisher, hosted services, startup entry, remote TCP and UDP endpoints, and two small sparklines for that app's CPU and network over the last minute.

Pin locks the panel where you put it. Top keeps it above other windows. Dock cycles between floating, the right edge, and the left edge. The edge dock reserves a strip like a bar, so maximized windows do not cover it. Minimize, and the close button, hide the panel to the notification area. Exit is in the ··· menu and in the tray icon.

## Build

You need the .NET 8 SDK and, for Visual Studio, the **.NET desktop development** workload in Visual Studio 2022.

```powershell
dotnet build ResourcePanel.sln -c Release
dotnet test ResourcePanel.sln -c Release
```

If `dotnet --list-sdks` is empty but the SDK was installed for your user, call it directly:

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build ResourcePanel.sln -c Release
```

`build.ps1` does that lookup, runs the tests, and publishes a portable 64-bit exe:

```powershell
.\build.ps1
```

The exe is `dist\ResourcePanel.exe`. It includes the runtime, so the PC you copy it to does not need .NET installed.

Open `ResourcePanel.sln` in Visual Studio 2022 and press F5 to debug. The project targets 64-bit Windows.

## Install

You can run `dist\ResourcePanel.exe` with no install.

Optional per-user install (Start menu shortcut, no administrator):

```powershell
.\installer\install.ps1
.\installer\install.ps1 -StartWithWindows -DesktopShortcut
```

`installer\ResourcePanel.iss` is an [Inno Setup 6](https://jrsoftware.org/isinfo.php) script for a setup exe. It installs under your local AppData folder and does not ask for administrator. Build the portable exe first so `dist\ResourcePanel.exe` exists, then compile the script.

The panel menu has **Start with Windows**. That writes one value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Permissions

The app starts as a normal user. It does not request administrator at launch.

| What | Why |
| --- | --- |
| Read process list, CPU, memory, disk counters, GPU counters, battery | The numbers in the panel. No extra permission. |
| Pause, resume, or end your own apps | Normal user. Ending asks first. Pause does not, because Resume puts the app back. |
| Per-app network bytes | A kernel network trace. Windows only allows this for an administrator, and only one NT Kernel Logger can run at a time. If another tool is using it, the panel keeps the system-wide network total and leaves per-app network blank. Restart as admin from the ··· menu when you want the per-app numbers. The trace stops when the panel exits. |
| TCP and UDP endpoints in the details | IP Helper. Shows remote address and port for the selected process. |
| Services, optional Windows features, restore points, HKLM startup entries | Administrator. The panel tells you when a change needs that. |
| Removing a Store app for the current user | Often works without administrator. Removing it for every account, and turning off a Windows feature, needs administrator. |
| Startup folder, HKCU Run, and scheduled tasks you own | Your own account. |

Logs and settings stay on the PC:

- `%LocalAppData%\ResourcePanel\actions.json` records every end, pause, service change, startup change, and app removal.
- `%LocalAppData%\ResourcePanel\profile.json` is the undo list.
- `%LocalAppData%\ResourcePanel\settings.json` is the window and filters.
- `%LocalAppData%\ResourcePanel\startup-backup\` holds Startup-folder files that were moved aside.

## Where the numbers come from

One background pass runs about once a second, and the window only draws the latest pass so the UI thread does not queue up work.

- CPU, memory, and per-process read/write bytes come from one `NtQuerySystemInformation` process snapshot, plus `GetSystemTimes` and `GlobalMemoryStatusEx`.
- The disk and network totals at the top, and the GPU percent, come from Windows performance counters (physical disk, network interface, GPU engine). GPU is the busiest 3D engine.
- Per-process network bytes come from ETW kernel TCP/UDP events when you are an administrator. The top-of-panel network total does not need that.
- The details list uses IP Helper (`GetExtendedTcpTable` / `GetExtendedUdpTable`) for remote addresses and ports.
- Battery percent comes from `GetSystemPowerStatus`. Discharge rate comes from `CallNtPowerInformation`. Per-app power is labeled **est.** and only appears while the battery is discharging. It is that app's share of the discharge, split by CPU. It is not a meter on the app.

Per-process disk in the list is process read/write bytes from the process snapshot. The disk number in the top strip is the physical disk counter. Those two are not the same measurement.

The footer shows this panel's own CPU so you can see whether the refresh stayed cheap.

## Optional apps

**Removing an optional app can break Store apps that depend on it.** A restore point is created before the first apply (and again if the last one is more than 30 minutes old). If System Protection is off, the panel says so and asks before continuing.

Nothing on that page is selected until you check it, or you click **Select common extras**. That button only checks low-risk installed extras such as the Xbox app, Clipchamp, Solitaire, tips, and OEM trials that are actually present. It does not check Mail, Copilot, or anything under Advanced.

**Advanced** is collapsed. It can set services such as DiagTrack, SysMain, Windows Search, and Xbox services to Manual or Disabled. Each row says what you lose. The previous start type is saved.

**Security and boot** is collapsed and empty of checkmarks. Defender, the firewall, Security Center, Windows Update, and boot-critical services (RPC, SAM, DHCP, and the rest) are only in this section. Applying them requires typing `DISABLE SECURITY` or `STOP BOOT SERVICES`. Do not use that section to make a game run better. Free up now does not touch it.

Export writes the undo profile. Import and **Undo saved changes** put back service start types, Run keys, Startup-folder files, scheduled tasks, optional features, and the Copilot button policy. A removed Store app is not put back by undo. Install it again from the Microsoft Store. The log says that.

## What this does not do

- No kernel driver.
- No cloud account and no telemetry.
- No "optimize RAM" button. Windows does not free memory that way, and the panel will not pretend.
- Security software is not paused, ended, or disabled unless you open the dangerous section and type the phrase.
- RunOnce keys are included in the startup list, but protected security commands are skipped.
- 32-bit and ARM Windows are not targeted. Publish and run the x64 build.

## Third-party code

Per-app network uses [Microsoft.Diagnostics.Tracing.TraceEvent](https://github.com/microsoft/perfview) (MIT) to read the kernel network events. Everything else is the Windows API or the .NET libraries.
