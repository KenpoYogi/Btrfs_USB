# Btrfs USB Mounter (C# / .NET Framework 4.8)

A compiled port of the PowerShell tool: mount Linux- and Mac-formatted USB drives on Windows through
WSL2, with free-space bars, tray icon and auto-mount, plus scrub and offline checks for btrfs.

- .NET Framework 4.8, C# 7.3, WinForms, **no NuGet packages**
- `BtrfsUsbMounter.exe` (plus its `.exe.config`) and a small console front end `BtrfsUsbMounter.com`;
  .NET 4.8 ships with Windows 11
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

Output in `bin\Release\net48\`: `BtrfsUsbMounter.exe`, `BtrfsUsbMounter.exe.config` and
`BtrfsUsbMounter.com` (the console front end, built from `launcher\Launcher.cs` by the same build).

Other tasks (**Terminal > Run Task**): *build release*, *clean*, *run release (elevated)*.

## Debug

The program requires administrator rights (`requireAdministrator` in `app.manifest`), so
**start VS Code with "Run as administrator"**, then press **F5**. Two launch configurations
are included: the window, and the command line with `--list`. Debugging .NET Framework
programs uses the `clr` debugger of the C# extension (Windows only).

## Install / migrate from the PowerShell version

1. Exit the PowerShell tool (tray icon > Exit).
2. Copy `BtrfsUsbMounter.exe`, `BtrfsUsbMounter.exe.config` **and** `BtrfsUsbMounter.com` to a permanent folder,
   e.g. `C:\Tools\BtrfsUsbMounter\`.
3. Start `BtrfsUsbMounter.exe`. On first start it:
   - reads your existing settings and mounted drives,
   - notices that the "start at logon" task still points at the PowerShell script and
     re-points it to the new program automatically.
4. Delete the old Desktop shortcut and `Mount-BtrfsUsb.cmd` (or keep the old folder as a fallback;
   the two versions can never run at the same time).

If you move the `.exe` later, untick and re-tick **Start in tray at logon** from the new location.

The program is not code-signed, so Windows SmartScreen may warn once about an unknown publisher.

## Supported filesystems

Every partition on a USB disk (or on any non-boot disk with *Include non-USB disks*) is identified
from its superblock, with one raw read per partition that is cached so sleeping drives stay asleep.
Whether a filesystem can be mounted depends on the WSL kernel, and is checked at runtime:

| Filesystem | Detected | Mounted | How |
|---|---|---|---|
| btrfs | yes | read/write | `wsl --mount --type btrfs`; scrub, drive info and offline check are btrfs-only |
| ext2, ext3, ext4 | yes | read/write | `wsl --mount --type ext2/ext3/ext4` (built into the WSL kernel) |
| XFS | yes | read/write | `wsl --mount --type xfs` (built into the WSL kernel) |
| JFS | yes | read/write* | `wsl --mount --type jfs` with the locally built `jfs` module |
| ReiserFS 3.x | yes | read/write* | `wsl --mount --type reiserfs` with the locally built `reiserfs` module |
| HFS+ / HFSX | yes | read/write* | `wsl --mount --type hfsplus` with the locally built `hfsplus` module; journaled volumes mount read-only |
| ZFS | yes | read/write* | `wsl --mount --bare`, then `zpool import -R /mnt/wsl` (locally built OpenZFS module + `zfs` package); eject = `zpool export` |
| APFS | yes | read-only, or read/write* (experimental) | read-only: `fsapfsmount` (FUSE, package `libfsapfs`), all volumes; read/write: the locally built `linux-apfs-rw` driver after **Tools > Allow APFS writes**, first volume only; no FileVault |
| Reiser4 | yes | no | never in mainline Linux; its patches stop at Linux 5.16 |

\* needs the drivers built for the running WSL kernel: **Tools > Build filesystem drivers**, or
`sh tools/build-wsl-modules.sh` as root in the distro. The stock WSL kernel has none of these, and the
distro's `*-kmp-default` packages are built for openSUSE's own kernel, which WSL never boots. The script:

1. downloads the WSL kernel source for `uname -r` from github.com/microsoft/WSL2-Linux-Kernel and
   configures it with the running kernel's `/proc/config.gz`, plus JFS, ReiserFS, HFS+ and HFS as modules
2. builds vmlinux once for symbol versions and checks them against Microsoft's own `btrfs.ko`
3. builds the in-tree drivers, OpenZFS (same version as the installed `zfs` package) and linux-apfs-rw
4. installs them in `/lib/modules/<release>/extra` (persistent), runs `depmod`, and loads each to test it

Rerun it after every `wsl --update`: a new WSL kernel needs its own build. The first run takes
20-40 minutes and about 5 GB of disk space in the distro (`/usr/src/wsl-modules`); later runs reuse
it. It uses GCC 11 like Microsoft's build (installed from openSUSE's devel:gcc project if needed) and
refuses to install anything if the symbol versions differ from the running kernel.

Notes:

- Built modules taint the kernel ("out-of-tree", and CDDL for ZFS). That is expected and harmless.
- The distro's `zfs-kmp-default` / `bcachefs-kmp-default` packages (and the `kernel-default` they pull
  in) are for openSUSE's own kernel and never load under WSL. `zfs` depends on `zfs-kmp`, so they stay.
- ZFS: only pools on real disks (or loop devices) work; WSL's mount namespaces stop the ZFS module
  from opening image files that live inside the distro. Pools are imported by GUID, never with `-f`.

The Status column shows *Ready*, *Ready (read-only)*, *No driver*, *Needs tools* or *Not mountable*;
the log says why and what to do. The app only checks whether a driver exists (`modinfo`) and loads it
right before mounting. The *Mount options* box applies to btrfs only.

The label of HFS+ and APFS volumes is not in the superblock, so the list shows their ID instead;
APFS volume names are logged when the drive is mounted.

## Command line

Type the name **without** `.exe`, so the console front end `BtrfsUsbMounter.com` runs. The terminal
then waits: output appears in order and `%ERRORLEVEL%` / `$LASTEXITCODE` hold the real exit code.
The `.exe` is a GUI program, and terminals don't wait for those.

```text
BtrfsUsbMounter                     open the window
BtrfsUsbMounter --tray              start hidden in the tray (used by the logon task)
BtrfsUsbMounter --list              list detected filesystems and whether they can be mounted
BtrfsUsbMounter --mount-all [--distro NAME] [--options compress=zstd]
BtrfsUsbMounter --unmount-all
BtrfsUsbMounter --help
BtrfsUsbMounter --list --verbose    also print the troubleshooting detail (see Logging below)
```

In PowerShell, from the program folder, prefix it with `.\` (for example `.\BtrfsUsbMounter --list`).

Run from an **administrator** terminal to see the output there. From a normal terminal, Windows asks
for administrator rights and the output opens in its own console window, which waits for Enter.
Exit code 0 = success.

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
src/Core/Infrastructure.cs  paths, logger (rotates at 10 MB), formatting
src/Core/Diagnostics.cs     environment details written to the log at startup
src/Core/Models.cs          persisted state, disks, volumes, btrfs results
src/Core/StateStore.cs      thread-safe state.json (atomic writes)
src/Core/Wsl.cs             async wsl.exe runner (timeouts, cancel, streaming), distros
src/Core/Disks.cs           raw partition reader, cache, WMI Storage API enumeration
src/Core/FileSystems.cs     filesystem signatures (btrfs, ext, XFS, JFS, Reiser, ZFS, HFS+, APFS), runtime support check
src/Core/BtrfsParsers.cs    btrfs usage / device stats / scrub status parsers
src/Core/MountManager.cs    mount, flush-and-eject, keep-alive, state sync, scanner
src/Core/Services.cs        maintenance (scrub, check, tools), logon task, job queue, engine
src/UI/UiKit.cs             shared controls (usage bar, flicker-free list)
src/UI/MainForm.cs          main window and tray
src/UI/DriveInfoForm.cs     drive info window
launcher/Launcher.cs        console front end, compiled to BtrfsUsbMounter.com
```

## Logging

Everything goes to `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` (in the window: **Tools > Open log file**).
At 10 MB the file is renamed to `mounter.log.old` and a new one starts.

Besides the messages shown in the window, the file holds **DEBUG** lines for troubleshooting:

- a header at every start: program version and path, command line, Windows build, .NET release,
  elevation, `wsl.exe` version and `wsl --version` output
- every `wsl.exe` call, numbered (`wsl#12`): full command line, timeout, exit code, duration, and
  its output (more of it when the call failed); timeouts and cancellations say so
- every disk scan: each disk with bus type, size and partition style, whether it was scanned or
  skipped, each partition, and per partition whether the btrfs superblock was read, came from the
  cache, or could not be read (with the Windows error)
- mount-state sync with WSL, keep-alive start/stop, settings and mount-list changes
- background tasks: start, finish, duration, and the full exception with stack trace on failure
- device plug/unplug events, the logon task, single-instance decisions, window close requests

Lines carry the date, milliseconds and thread id. The window hides DEBUG lines unless
**Tools > Show detailed log lines** is ticked; on the command line add `--verbose`.
Frequent background checks (every 30 s) are logged only when something changes, is slow or fails.

When reporting a problem, attach `mounter.log` (and `mounter.log.old` if the problem was a while ago).

## License

Copyright (c) 2026 Jay Weiner. Licensed under the [MIT License](https://opensource.org/license/mit)
with the [Commons Clause License Condition v1.0](https://commonsclause.com/)
(SPDX: `LicenseRef-MIT-Commons-Clause`); the full text is in [LICENSE](LICENSE).

- You may use, copy, modify, merge and distribute the software, for any purpose.
- **You may not sell it:** no charging for the software itself, or for a product or service
  (including hosting or consulting/support services) whose value derives entirely or
  substantially from its functionality.
- Anyone who passes on a copy must keep the copyright notice, the MIT permission notice and the
  Commons Clause notice. The build copies LICENSE next to the program for that.
- Provided as is, without warranty or liability.

This is a source-available license, not an open-source one: the Commons Clause restricts selling,
which open-source licenses may not do.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Build error MSB3644 | Install the .NET Framework 4.8 Developer Pack |
| F5 fails with "requires elevation" | Restart VS Code as administrator |
| Nothing happens on start | Another copy is running hidden; the new launch offers to end it after 2 s |
| Drive not listed | Tick *Include non-USB disks* (some enclosures report as SCSI), click Refresh |
| Mount fails | The log shows the error, a hint and, if relevant, the kernel messages |
| Logs | `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` (see Logging above); check reports in the `checks` subfolder |
