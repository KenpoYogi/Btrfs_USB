# Btrfs USB Mounter - project context

Standalone Windows desktop tool (NOT a Cimatron plugin; no Cimatron/ACIS references).
Mounts Linux/Mac-formatted USB drives through WSL2 (`wsl --mount --type <fs>`): btrfs, ext2/3/4 and
XFS read/write with the stock kernel; JFS, ReiserFS, HFS+, ZFS and APFS (rw experimental) with
modules built by tools/build-wsl-modules.sh; APFS read-only via fsapfsmount (FUSE) otherwise;
Reiser4 detect-only. It started as a btrfs tool because the
WinBtrfs driver is blocked by the Windows "Cross Certificates for Code Integrity Exceptions"
policy (event 3077, policy 8f9cb695-5d48-48d6-a329-7202b44607e3).

## License
- PolyForm Noncommercial 1.0.0 (noncommercial use only), Copyright (c) 2026 Jay W. NOT GPL: GPL
  cannot forbid commercial use. Every .cs file starts with the SPDX header; notices come from
  `AppInfo` (Infrastructure.cs): --help, Tools > About and license, startup log line, assembly Copyright.
  LICENSE is copied to the build output

## Constraints
- .NET Framework 4.8, C# 7.3 (`<LangVersion>7.3</LangVersion>`), WinForms, UI built in code (no designer files)
- No NuGet packages. Framework assemblies only (System.Management, System.Runtime.Serialization, ...)
- SDK-style csproj, built with `dotnet build`; needs the .NET Framework 4.8 Developer Pack
- `requireAdministrator` manifest: running or debugging needs an elevated VS Code
- AnyCPU, Prefer32Bit=false (a 32-bit process would be redirected away from the real wsl.exe)

## Commands
- Build: `dotnet build -c Release` -> `bin\Release\net48\BtrfsUsbMounter.exe` (+ `.exe.config`, `.com`)
- CLI check without the GUI: `BtrfsUsbMounter --list` (elevated terminal; no extension, so the `.com` runs);
  add `--verbose` to see the DEBUG lines too
- Logs: `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log`; state: `state.json` in the same folder

## Environment (developer machine)
- WSL2 distro: openSUSE-Tumbleweed (default, only distro), user `chippy`
- btrfsprogs and util-linux installed in the distro
- Test drive: 2 TB Seagate ST32000641AS in a USB enclosure, disk 2, GPT, btrfs partition 1,
  label `ExtDrive`, mounted at `/mnt/wsl/ExtDrive`, top folder owned by chippy

## Architecture
- `src/Core`: no UI dependencies. `Engine` wires StateStore, SuperblockCache, MountManager,
  VolumeScanner, Maintenance. `JobQueue` runs one background task at a time
  (flags: Mutating, Quiet, Cancellable, Unique) and raises `Changed` on the UI thread
- `src/UI`: MainForm (tray, owner-drawn Space column, WM_DEVICECHANGE in WndProc, tools menu),
  DriveInfoForm (usage bar, allocation, error counters, scrub)
- `src/Program.cs`: single instance (mutex `Local\BtrfsUsbMounter.GUI`, shared with the old
  PowerShell version), show-window broadcast + ack event, CLI mode

## Filesystems
- `src/Core/FileSystems.cs`: `FsProbe` parses one cached 272 KiB read per partition (priority order,
  btrfs crc32c checked, libblkid-style sanity checks for ReiserFS and ZFS); `FsSupport` asks the
  distro at runtime which kinds the kernel has (`/proc/filesystems`, `modinfo`) and if fsapfsmount/zpool exist
- `MountEntry.FsType` missing = btrfs (older and PowerShell-written state.json)
- Scrub, drive info, offline check are btrfs-only; the Mount options box is btrfs-only
- Mount methods (`MountEntry.MountMethod`): kernel (`wsl --mount --type`), fuse (APFS via
  fsapfsmount), zfs (`--bare` + `zpool import -R /mnt/wsl <guid>`, never `-f`; eject = `zpool export`)
- Locally built modules live in `/lib/modules/$(uname -r)/extra` in the distro (persistent overlay).
  WSL module signing is off. Config must be EXACTLY /proc/config.gz plus the added =m drivers:
  turning DEBUG_INFO_BTF off changed struct module (DEBUG_INFO_BTF_MODULES) and 250 of 582 CRCs.
  resolve_btfids fails with new glibc: fixed with HOSTCFLAGS=-Wno-error=discarded-qualifiers (host
  tools only). Built with gcc-11 like Microsoft (devel:gcc Factory, temporary repo). JFS needs
  KBUILD_EXTRA_SYMBOLS from fs/nls (nls_ucs2_utils is a shipped module). OpenZFS needs KERNEL_CC and a
  "gcc" shim. linux-apfs-rw needs ./genver.sh first. The script checks CRCs against btrfs.ko.
- ZFS vdevs are opened from kernel threads in the VM's root mount namespace: file vdevs inside the
  distro fail (vdev.open_failed); block devices (/dev/sdX from wsl --mount --bare, loop devices) work
- `zfs` requires `zfs-kmp` (RPM dependency), so openSUSE's kernel-default/zfs-kmp-default stay installed
- Rebuild modules after `wsl --update`. `FsSupport` uses `modinfo` (no loading); modules load right before mount
- Verified on this machine (2026-09-23, kernel 6.6.87.2): all 7 modules load; JFS rw + fsck clean;
  APFS kernel ro by default, readwrite + fsck.apfs clean; ZFS pool on a loop device: import by GUID
  under /mnt/wsl, zfs get, export; real-pool ZFS detection matches blkid. HFS+/ReiserFS: load only
- Test fixtures are made with mkfs in WSL (e2fsprogs, xfsprogs, jfsutils, apfsprogs, btrfsprogs
  installed in the distro) and cross-checked with `blkid -p`

## Logging
- `Log.Debug` = troubleshooting detail: always in mounter.log, hidden on screen unless Tools >
  Show detailed log lines / `--verbose`. `Log.Exception` = short error on screen + full stack as debug
- `Wsl.RunAsync` / `RunStreamingAsync` log every call (numbered `wsl#N`) with args, exit, duration, output
- Don't log in code that runs every 30 s (snapshot, quiet jobs) unless something changed or failed

## Rules learned the hard way (keep these)
- No double hyphen inside XML comments (app.manifest, csproj, App.config): the Windows loader
  rejects the manifest and the exe fails with a side-by-side configuration error
- The exe is WinExe, so terminals don't wait for it. CLI use goes through `BtrfsUsbMounter.com`
  (`launcher/Launcher.cs`, compiled by the `BuildConsoleLauncher` target), which runs the exe in
  its console and relays the exit code
- Never block the UI thread: all wsl.exe, WMI and raw disk I/O go through JobQueue
- Every wsl.exe call needs a timeout AND must honour cancellation
- Eject = `sync -f /mnt/wsl/<name>` (this filesystem only) streamed with progress; cancelling
  must leave the drive mounted. If the disk is already gone, skip the flush and just release it
- Keep-alive process (`wsl -u root --exec sleep infinity`) while anything is mounted, or WSL's
  idle shutdown silently detaches the disk
- Superblock reads are cached per partition so rescans don't spin up sleeping drives
- A hidden/hung instance must never silently block new launches
- No `btrfs check --repair` feature by design
- state.json must stay compatible with the PowerShell version's field names

## Verification done before handover
Compiles against the 4.8 reference assemblies; 32 core tests passed under Mono (superblock
parser on a real btrfs image, btrfs output parsers, argument quoting, PowerShell-format
state.json, flush command, cancel/timeout, job queue). The GUI and `dotnet build` itself have
not yet been run on Windows - do that first.
