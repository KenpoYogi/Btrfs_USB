# Btrfs USB Mounter (C# / .NET Framework 4.8)

A compiled port of the PowerShell tool: mount btrfs-formatted USB drives on Windows through
WSL2, with free-space bars, scrub, offline checks, tray icon and auto-mount.

- .NET Framework 4.8, C# 7.3, WinForms, **no NuGet packages**
- One `BtrfsUsbMounter.exe` (plus its `.exe.config`); .NET 4.8 ships with Windows 11
- Uses the same `%LOCALAPPDATA%\BtrfsUsbMounter` folder as the PowerShell version, so
  settings and mount state carry over

## Prerequisites (one time)

1. **.NET SDK** (8 or later): https://dotnet.microsoft.com/download - provides `dotnet build`.
2. **.NET Framework 4.8 Developer Pack** (reference assemblies): https://dotnet.microsoft.com/download/dotnet-framework/net48
   Without it the build fails with *MSB3644: reference assemblies for .NETFramework,Version=v4.8 were not found*.
3. **VS Code** with the **C#** extension (`ms-dotnettools.csharp`; VS Code suggests it when you open the folder).

## Build

Open the folder in VS Code and press **Ctrl+Shift+B** (default task: Debug build), or from a terminal:

```powershell
dotnet build -c Release
```

Output: `bin\Release\net48\BtrfsUsbMounter.exe` and `BtrfsUsbMounter.exe.config`.

Other tasks (**Terminal > Run Task**): *build release*, *clean*, *run release (elevated)*.

## Debug

The program requires administrator rights (`requireAdministrator` in `app.manifest`), so
**start VS Code with "Run as administrator"**, then press **F5**. Two launch configurations
are included: the window, and the command line with `--list`. Debugging .NET Framework
programs uses the `clr` debugger of the C# extension (Windows only).

## Install / migrate from the PowerShell version

1. Exit the PowerShell tool (tray icon > Exit).
2. Copy `BtrfsUsbMounter.exe` **and** `BtrfsUsbMounter.exe.config` to a permanent folder,
   e.g. `C:\Tools\BtrfsUsbMounter\`.
3. Start `BtrfsUsbMounter.exe`. On first start it:
   - reads your existing settings and mounted drives,
   - notices that the "start at logon" task still points at the PowerShell script and
     re-points it to the new program automatically.
4. Delete the old Desktop shortcut and `Mount-BtrfsUsb.cmd` (or keep the old folder as a fallback;
   the two versions can never run at the same time).

If you move the `.exe` later, untick and re-tick **Start in tray at logon** from the new location.

The program is not code-signed, so Windows SmartScreen may warn once about an unknown publisher.

## Command line

```text
BtrfsUsbMounter.exe                 open the window
BtrfsUsbMounter.exe --tray          start hidden in the tray (used by the logon task)
BtrfsUsbMounter.exe --list          list detected btrfs partitions
BtrfsUsbMounter.exe --mount-all [--distro NAME] [--options compress=zstd]
BtrfsUsbMounter.exe --unmount-all
```

Run from an **administrator** terminal to see the output there; otherwise a console window
opens and waits for Enter. Exit code 0 = success.

## What changed compared to the PowerShell version

| Area | PowerShell | C# |
|---|---|---|
| Background work | extra runspace + log queue + polling timer | `async`/`await`, `Task`, `CancellationToken` |
| Admin rights | self-elevation, execution-policy bypass, `.cmd` launcher | `requireAdministrator` manifest |
| Second launch | detect, then offer to end the other copy | asks the running copy to **show its window**; only if it doesn't respond (hung, or the old script) offers to end it |
| One-item lists | the quirk behind the startup bug | ordinary `List<T>` |
| Device changes | separate hidden window compiled at runtime | the main window's own `WndProc` |
| Startup task | `Register-ScheduledTask` | `schtasks /XML` (battery-safe settings, no extra modules) |
| High DPI | system scaling | per-monitor v2 (`App.config`) |

Behaviour is otherwise identical: flush with progress and Cancel on eject, unplugged-drive
handling, keep-alive, superblock cache (sleeping drives stay asleep), scrub with live progress,
read-only offline check with saved reports, tools installer, and no repair button by design.

## Project layout

```text
BtrfsUsbMounter.csproj      SDK-style project, net48, C# 7.3
app.manifest                requireAdministrator, Windows 10/11 compatibility
App.config                  per-monitor DPI awareness
assets/app.ico              application and tray icon
src/Program.cs              entry point, single instance, command-line mode
src/Core/Infrastructure.cs  paths, logger (rotates at 5 MB), formatting
src/Core/Models.cs          persisted state, disks, volumes, btrfs results
src/Core/StateStore.cs      thread-safe state.json (atomic writes)
src/Core/Wsl.cs             async wsl.exe runner (timeouts, cancel, streaming), distros
src/Core/Disks.cs           raw superblock reader, cache, WMI Storage API enumeration
src/Core/BtrfsParsers.cs    btrfs usage / device stats / scrub status parsers
src/Core/MountManager.cs    mount, flush-and-eject, keep-alive, state sync, scanner
src/Core/Services.cs        maintenance (scrub, check, tools), logon task, job queue, engine
src/UI/UiKit.cs             shared controls (usage bar, flicker-free list)
src/UI/MainForm.cs          main window and tray
src/UI/DriveInfoForm.cs     drive info window
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| Build error MSB3644 | Install the .NET Framework 4.8 Developer Pack |
| F5 fails with "requires elevation" | Restart VS Code as administrator |
| Nothing happens on start | Another copy is running hidden; the new launch offers to end it after 2 s |
| Drive not listed | Tick *Include non-USB disks* (some enclosures report as SCSI), click Refresh |
| Mount fails | The log shows the error, a hint and, if relevant, the kernel messages |
| Logs | `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log`; check reports in the `checks` subfolder |
