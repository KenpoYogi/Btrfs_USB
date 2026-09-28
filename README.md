# Linux USB Mounter (C# / .NET Framework 4.8)

A compiled port of the PowerShell tool: mount Linux- and Mac-formatted USB drives on Windows through
WSL2, with free-space bars, tray icon and auto-mount, plus scrub and offline checks for btrfs.

- .NET Framework 4.8, C# 7.3, WinForms, **no NuGet packages**
- `LinuxUsbMounter.exe` (plus its `.exe.config`) and a small console front end `LinuxUsbMounter.com`;
  .NET 4.8 ships with Windows 11
- Two ways to install (see [Install](#install)): the installer `LinuxUsbMounter-<version>-Setup.exe` (with an
  uninstaller), or the portable `LinuxUsbMounter-<version>-Portable.zip` (extract anywhere, nothing to install;
  you then set up WSL2 and the Linux distro yourself)
- Uses the same `%LOCALAPPDATA%\BtrfsUsbMounter` folder as the PowerShell version, so
  settings and mount state carry over

## Setting up WSL2 and a Linux distro

New to WSL? The step-by-step guides in [docs/distros](docs/distros/README.md) cover turning on WSL2,
installing a distro and adding the packages the app needs. They also show what works on each distro:

[openSUSE Tumbleweed](docs/distros/opensuse-tumbleweed.md) (recommended) ·
[openSUSE Leap](docs/distros/opensuse-leap.md) ·
[SUSE Linux Enterprise Server](docs/distros/sles.md) ·
[Ubuntu](docs/distros/ubuntu.md) · [Fedora](docs/distros/fedora.md) ·
[Kali Linux](docs/distros/kali.md) · [Debian](docs/distros/debian.md) ·
[CentOS / AlmaLinux](docs/distros/centos.md) · [Arch Linux](docs/distros/arch.md)

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

Output in `bin\Release\net48\`: `LinuxUsbMounter.exe`, `LinuxUsbMounter.exe.config` and
`LinuxUsbMounter.com` (the console front end, built from `launcher\Launcher.cs` by the same build).
The same build also makes the two downloads for users (see [Install](#install)): the installer
`bin\Release\LinuxUsbMounter-<version>-Setup.exe` (from `installer\Setup.cs`, with the program files packed
inside it) and the portable `bin\Release\LinuxUsbMounter-<version>-Portable.zip` (the `net48` folder zipped).

Other tasks (**Terminal > Run Task**): *build release*, *clean*, *run release (elevated)*.

## Debug

The program requires administrator rights (`requireAdministrator` in `app.manifest`), so
**start VS Code with "Run as administrator"**, then press **F5**. Two launch configurations
are included: the window, and the command line with `--list`. Debugging .NET Framework
programs uses the `clr` debugger of the C# extension (Windows only).

## Install

There are two ways to get the program onto a PC. Pick one; both run the same program with the same
settings (`%LOCALAPPDATA%\BtrfsUsbMounter`):

| | **Installer** (recommended) | **Portable** (no installation) |
|---|---|---|
| Download | `LinuxUsbMounter-<version>-Setup.exe` | `LinuxUsbMounter-<version>-Portable.zip` |
| Goes to | `C:\Program Files\Linux_USB`, or a folder you choose | any folder you extract it to (a USB stick works too) |
| Start menu / desktop shortcut, PATH | yes (you choose) | no; make your own shortcut if you want one |
| Listed in Installed apps, with an uninstaller | yes | no; to remove it, delete the folder |
| Checks WSL2 and sets up a Linux distro for you | yes (the **Linux check**, below) | **no: you install and set up WSL2 and the distro yourself** ([guides](docs/distros/README.md)) |
| Updating | run the newer setup | exit the program, replace the files |

Both are in `bin\Release\` after a build. The zip holds exactly the files the installer puts in place.

### Installer

Run **`LinuxUsbMounter-<version>-Setup.exe`** and click **Yes** when Windows asks for administrator rights.

- **Install to:** `C:\Program Files\Linux_USB` by default; type another folder or click **Browse...**
  (picking a folder that already holds other things installs into a `Linux_USB` folder inside it).
- **Options:** Start menu shortcut (on), desktop shortcut (off), add the folder to the system PATH so
  `LinuxUsbMounter --list` works in any terminal (off), start the program when setup finishes (on).
  The shortcuts are for all users.
- The program is registered in **Settings > Apps > Installed apps**, and setup copies itself into the
  folder as `Uninstall.exe`.
- **Linux check** (on by default): after copying the files, setup checks that WSL is installed and current
  and that a WSL2 distribution has the tools the program needs (`btrfs`, `blkid`, `modinfo`). If none does,
  it explains what is missing and offers to:
  - **install WSL** (`wsl --install --no-distribution`; Windows then needs a restart, after which you run
    setup again), or **update an old WSL** (`wsl --update`);
  - **install a distribution for you: openSUSE Tumbleweed** (recommended, pre-selected), **Ubuntu 26.04 LTS**
    or **Kali Linux**. Setup installs it, brings it up to date and adds `btrfs-progs`, `util-linux` and
    `kmod` (plus `libfsapfs` on openSUSE, for Mac drives), all as root, so no Linux user account is needed
    (open the distribution from the Start menu later if you want one). Optionally it becomes the default
    WSL distribution. This downloads a few hundred MB and takes about 5 to 20 minutes. Setup remembers it,
    so the uninstaller can offer to remove it too;
  - **add just the missing packages** to a distribution you already have (zypper, apt, dnf or pacman);
  - or let you **set it up yourself**: it opens the [step-by-step guides](docs/distros/README.md).

  The extra filesystem drivers (JFS, HFS+, UFS, ZFS, APFS read/write) are built later from the program:
  **Tools > Build filesystem drivers**.
- The program is not code-signed, so Windows SmartScreen may warn once about an unknown publisher
  (**More info > Run anyway**).

**Updating:** run the newer setup. It finds the installed copy and offers **Update** in the same folder
(or pick a new folder: the old one is removed and start at logon follows the move). A running copy is
closed first: mounted drives stay mounted, and a task it was busy with (eject, scrub, check) is
cancelled. Settings, mount state and the log are kept.

**Other copies:** if a copy that setup did not install is running (a hand-copied folder, or an older
`BtrfsUsbMounter.exe` / `XnixUsbMounter.exe`), setup asks whether to close it. Only one copy
runs at a time, so while it runs the new one would just bring that copy's window up. Its files are
left alone; delete that folder yourself when you no longer need it. The logon task is re-pointed to
the installed program the first time it starts.

### Uninstall

**Settings > Apps > Installed apps > Linux USB Mounter > Uninstall**, or run `Uninstall.exe` in the
install folder (or the setup file again, which offers **Uninstall...**). The uninstaller shows what
it is about to do and warns first when the program is running or drives are mounted. Then it:

1. closes the running program (a task it was busy with is cancelled);
2. unmounts every mounted drive with `LinuxUsbMounter --unmount-all`: pending writes are flushed
   first, which can take a while. **Don't unplug the drives until it says they are unmounted.** If a
   drive can't be unmounted, it asks before going on (the drive then stays attached to WSL until
   `wsl --shutdown` or a restart). If another copy of the program is still running from a different
   folder, the drives are left to that copy;
3. if setup installed a WSL Linux distribution for you (the Linux check above), asks under **What to remove**
   whether to remove only **the Windows app** (the default) or **the Windows app and that distribution**.
   Ticking the distribution deletes it and every file inside it with `wsl --unregister` (including files you
   saved there and the filesystem drivers built in it); this can't be undone. It is removed only after the
   drives are unmounted, and kept if drives stay mounted or another copy of the program is running;
4. removes start at logon (if it starts this copy), the shortcuts, the PATH entry, the Installed apps
   entry and the program's files. Files you put in the install folder yourself are kept, and then so
   is the folder;
5. deletes your settings and logs (`%LOCALAPPDATA%\BtrfsUsbMounter`) only if you tick that box.

WSL itself, and distributions you installed yourself, are never removed; neither are the filesystem drivers
built inside a distribution you keep (`/var/lib/wsl-modules`).

### Portable (copy the files, no installation)

> **You set up Linux yourself.** The portable version has no setup, so nothing checks or installs WSL2
> or a Linux distribution for you (that is the installer's **Linux check**). Before you mount a drive,
> install WSL2 and a WSL2 distribution by hand and add the packages the program needs (`btrfs-progs`,
> `util-linux`, `kmod`); the [step-by-step guides](docs/distros/README.md) show how for each distribution.
> Without them the program can list your drives but not mount them.

Extract `LinuxUsbMounter-<version>-Portable.zip` to a folder of your choice, e.g. `C:\Tools\LinuxUsbMounter\`
(or copy everything in `bin\Release\net48\` there), and start `LinuxUsbMounter.exe`. Nothing is written
outside that folder except the settings and log in `%LOCALAPPDATA%\BtrfsUsbMounter`, and the logon task if
you tick **Start in tray at logon**.

Keep the files together in the same layout: `LinuxUsbMounter.exe`, `LinuxUsbMounter.exe.config`,
`LinuxUsbMounter.com`, `LICENSE` **and the `tools\` folder** (with `build-wsl-modules.sh` inside). Without
`tools\`, **Tools > Build filesystem drivers** fails with *The driver build script is missing*; without
`LICENSE`, **About and license** falls back to the web page. (The `.pdb` file is optional.)

- **Update:** exit the program (tray icon > Exit), extract the new zip over the folder, start it again.
- **Remove:** exit the program, untick **Start in tray at logon** first if you use it (eject your drives
  first too), then delete the folder.
- **Moved the folder?** Untick and re-tick **Start in tray at logon** from the new location.
- **Switching to the installer later** is fine: setup offers to close the portable copy if it is running;
  delete the portable folder afterwards.

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
| HFS+ / HFSX | yes | read/write* | `wsl --mount --type hfsplus` with the locally built `hfsplus` module; journaled volumes mount read-only |
| ZFS | yes | read/write* | `wsl --mount --bare`, then `zpool import -R /mnt/wsl` (locally built OpenZFS module + `zfs` package); eject = `zpool export` |
| APFS | yes | read-only, or read/write* (experimental) | read-only: `fsapfsmount` (FUSE, package `libfsapfs`), all volumes; read/write: the locally built `linux-apfs-rw` driver after **Tools > Allow APFS writes**, first volume only; no FileVault |
| UFS1, UFS2 (FreeBSD, NetBSD, OpenBSD) | yes | read-only*, or read/write* (experimental) | `wsl --mount --type ufs --options ufstype=ufs2` (or `44bsd` for UFS1) with the locally built `ufs` module; read/write after **Tools > Allow UFS writes**. Solaris UFS, and drives not cleanly unmounted, stay read-only |

\* needs the drivers built for the running WSL kernel: **Tools > Build filesystem drivers**, or
`sh tools/build-wsl-modules.sh` as root in the distro. It works in distros with zypper (openSUSE, SLES) or
apt (Debian, Kali, Ubuntu); tested on openSUSE Tumbleweed and Kali. The stock WSL kernel has none of
these drivers, and the distros' own driver packages (`*-kmp-default`, `zfs-dkms`, `apfs-dkms`) are built
for the distro's kernel or need its headers, which WSL doesn't have. The script:

1. downloads the WSL kernel source for `uname -r` from github.com/microsoft/WSL2-Linux-Kernel and
   configures it with the running kernel's `/proc/config.gz`, plus JFS, HFS+, HFS and UFS (with write
   support) as modules
2. builds vmlinux once for symbol versions and checks them against Microsoft's own `btrfs.ko`
3. builds the in-tree drivers, OpenZFS (same version as the installed `zfs` package) and linux-apfs-rw.
   UFS is built from a copy of `fs/ufs` with a small change that keeps FreeBSD's view of the
   filesystem consistent (see UFS below)
4. keeps them in `/var/lib/wsl-modules/<release>` on the distro's disk, installs them in
   `/lib/modules/<release>/extra`, runs `depmod`, and loads each to test it. WSL keeps `/lib/modules/<release>`
   in memory, so after a WSL restart the app copies the drivers back (and runs `depmod`) before it
   checks or loads them

Rerun it after every `wsl --update`: a new WSL kernel needs its own build. The first run takes
20-40 minutes and about 5 GB of disk space in the distro (`/usr/src/wsl-modules`); later runs reuse
it. It uses the GCC major version that built the running kernel (installed from openSUSE's devel:gcc
project if needed), pins the compiler feature checks to the running kernel's values, and
refuses to install anything if the symbol versions differ from the running kernel.

Notes:

- Built modules taint the kernel ("out-of-tree", and CDDL for ZFS). That is expected and harmless.
- The distro's `zfs-kmp-default` / `bcachefs-kmp-default` packages (and the `kernel-default` they pull
  in) are for openSUSE's own kernel and never load under WSL. `zfs` depends on `zfs-kmp`, so they stay.
- ZFS: only pools on real disks (or loop devices) work; WSL's mount namespaces stop the ZFS module
  from opening image files that live inside the distro. Pools are imported by GUID, never with `-f`.

### UFS

Linux needs to be told which UFS variant it is mounting, and the app reads that from the superblock:
`ufstype=ufs2` for UFS2, `44bsd` for UFS1 from the BSDs, `sun` / `sunx86` for Solaris (read-only). The
drive mounts read-only unless **Tools > Allow UFS writes (experimental)** is ticked and the `ufs` driver
is the one **Build filesystem drivers** made (it is marked with `modinfo -F wsl_handoff ufs` = 1). Linux's
UFS write support is marked experimental by the kernel itself, so keep a backup.

Linux's driver does not know FreeBSD's newer features, so the build script changes it (in a copy of
`fs/ufs`) to hand the filesystem back the way FreeBSD expects:

- while mounted read/write the superblock says *not clean*, as FreeBSD does itself; a clean unmount
  (Eject) sets it back. Stock Linux leaves it *clean*, so a drive unplugged mid-write would look fine
  to FreeBSD and never get checked
- FreeBSD 12+ metadata **check hashes** (superblock, cylinder groups, inodes) are switched off on the
  first read/write mount, the way FreeBSD itself expects from a kernel that cannot maintain them. To
  turn them back on, run `fsck_ffs` on the unmounted filesystem on FreeBSD (without `-p` or `-y`) and
  answer yes to the *ADD ... CHECK-HASH PROTECTION* questions
- with **journaled soft updates** (FreeBSD's default), the mount time is updated, so FreeBSD's fsck
  never replays an old journal over Linux's changes; after an unclean unmount it does a full check
- only a superblock marked *clean* (1) may be written; NetBSD marks mounted filesystems with 2, which
  stock Linux also accepts as clean

Tested with FreeBSD 15.1 (`newfs -U -j`, UFS2 with journaled soft updates and check hashes, and
`newfs -O1 -U`, UFS1): after Linux wrote to them, FreeBSD's `fsck_ffs -n` found them clean and every
file checksum matched; a disk copied while Linux had it mounted read/write was refused by FreeBSD
(*not clean - run fsck*) and `fsck_ffs -p` did a full check. A UFS1 image made by NetBSD's `makefs`
(on Kali) also checked clean in FreeBSD, with every checksum matching, after Linux wrote to it. Real
NetBSD, OpenBSD and Solaris disks are not tested yet. FreeBSD's fsck may ask *UPDATE FILESYSTEM
TO TRACK DIRECTORY DEPTH* for folders made on Linux: harmless, answer yes (`-p` does it by itself).

Limits: GPT `freebsd-ufs` partitions and unpartitioned disks work. UFS inside an MBR slice with a BSD
disklabel (older FreeBSD installs, `ada0s1a` and so on) is normally not found: the app looks at the start
of each Windows partition, and does not read BSD disklabels. FreeBSD's gjournal is not supported (read-only). Fragments larger than 4 KiB cannot be mounted.
Linux ignores UFS2 extended attributes (FreeBSD ACLs and MAC labels): deleting such a file on Linux
leaves its attribute block allocated until the next `fsck_ffs`.

The Status column shows *Ready*, *Ready (read-only)*, *No driver*, *Needs tools* or *Not mountable*;
the log says why and what to do. The app only checks whether a driver exists (`modinfo`) and loads it
right before mounting. The *Mount options* box applies to btrfs only.

The label of HFS+ and APFS volumes is not in the superblock, so the list shows their ID instead;
APFS volume names are logged when the drive is mounted.

## Command line

Type the name **without** `.exe`, so the console front end `LinuxUsbMounter.com` runs. The terminal
then waits: output appears in order and `%ERRORLEVEL%` / `$LASTEXITCODE` hold the real exit code.
The `.exe` is a GUI program, and terminals don't wait for those.

```text
LinuxUsbMounter                     open the window
LinuxUsbMounter --tray              start hidden in the tray (used by the logon task)
LinuxUsbMounter --list              list detected filesystems and whether they can be mounted
LinuxUsbMounter --mount-all [--distro NAME] [--options compress=zstd]
LinuxUsbMounter --unmount-all
LinuxUsbMounter --help
LinuxUsbMounter --list --verbose    also print the troubleshooting detail (see Logging below)
```

In PowerShell, from the program folder (`C:\Program Files\Linux_USB` when installed with setup), prefix
it with `.\` (for example `.\LinuxUsbMounter --list`). If setup added the folder to the PATH, the plain
name works in any new terminal.

Run from an **administrator** terminal to see the output there. From a normal terminal, Windows asks
for administrator rights and the output opens in its own console window, which waits for Enter.
Redirected output (`LinuxUsbMounter --list > drives.txt`, or a program reading it through a pipe, as the
uninstaller does with `--unmount-all`) goes to the file or pipe, with no console window.
Exit code 0 = success.

## Project layout

```text
LinuxUsbMounter.csproj      SDK-style project, net48, C# 7.3
app.manifest                requireAdministrator, Windows 10/11 compatibility
App.config                  per-monitor DPI awareness
assets/LinuxUsbMount_v3.ico application, setup and tray icon (16-256 px)
assets/LinuxUsbMount_2048_v3.png  the same icon at 2048 x 2048
src/Program.cs              entry point, single instance, command-line mode
src/Core/Infrastructure.cs  paths, logger (rotates at 10 MB), formatting
src/Core/Diagnostics.cs     environment details written to the log at startup
src/Core/Models.cs          persisted state, disks, volumes, btrfs results
src/Core/StateStore.cs      thread-safe state.json (atomic writes)
src/Core/Wsl.cs             async wsl.exe runner (timeouts, cancel, streaming), distros
src/Core/Disks.cs           raw partition reader, cache, WMI Storage API enumeration
src/Core/FileSystems.cs     filesystem signatures (btrfs, ext, XFS, JFS, ZFS, HFS+, APFS, UFS), runtime support check
src/Core/BtrfsParsers.cs    btrfs usage / device stats / scrub status parsers
src/Core/MountManager.cs    mount, flush-and-eject, keep-alive, state sync, scanner
src/Core/Services.cs        maintenance (scrub, check, tools), logon task, job queue, engine
src/UI/UiKit.cs             shared controls (usage bar, flicker-free list)
src/UI/MainForm.cs          main window and tray
src/UI/DriveInfoForm.cs     drive info window
launcher/Launcher.cs        console front end, compiled to LinuxUsbMounter.com
installer/Setup.cs          installer + uninstaller, compiled to LinuxUsbMounter-<version>-Setup.exe
installer/setup.manifest    its manifest (administrator rights, DPI awareness)
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

**Credits:** the penguin in the icon is Tux, drawn by Larry Ewing (lewing@isc.tamu.edu) with The GIMP.
Linux® is the registered trademark of Linus Torvalds in the U.S. and other countries.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Build error MSB3644 | Install the .NET Framework 4.8 Developer Pack |
| F5 fails with "requires elevation" | Restart VS Code as administrator |
| Nothing happens on start | Another copy is running hidden; the new launch offers to end it after 2 s |
| Drive not listed | Tick *Include non-USB disks* (some enclosures report as SCSI), click Refresh |
| Mount fails | The log shows the error, a hint and, if relevant, the kernel messages |
| "The driver build script is missing" | Installer: run the setup again (it puts the `tools\` folder back). Portable: extract the `tools\` folder from the zip next to `LinuxUsbMounter.exe` |
| Setup or the uninstaller says the program "is still running" | It did not close within 15 s and could not be ended: exit it (tray icon > Exit) and try again |
| The uninstaller says drives are still mounted | Stop there (**No**), start the program, eject the drives (the log says why one is busy), then uninstall again |
| The new version doesn't start after setup, an old window appears | A copy from another folder is running: exit it (tray icon > Exit), or run setup again and let it close it |
| Logs | `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` (see Logging above); check reports in the `checks` subfolder |
