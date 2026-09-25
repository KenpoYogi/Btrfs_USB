# Xnix USB Mounter - project context

Standalone Windows desktop tool (NOT a Cimatron plugin; no Cimatron/ACIS references).
Mounts Linux/Mac-formatted USB drives through WSL2 (`wsl --mount --type <fs>`): btrfs, ext2/3/4 and
XFS read/write with the stock kernel; JFS, ReiserFS (kernels before 6.13), HFS+, ZFS, APFS (rw experimental) and
UFS1/UFS2 (ro, rw experimental) with modules built by tools/build-wsl-modules.sh; APFS read-only via fsapfsmount
(FUSE) otherwise; Reiser4 detect-only. It started as a btrfs tool because the
WinBtrfs driver is blocked by the Windows "Cross Certificates for Code Integrity Exceptions"
policy (event 3077, policy 8f9cb695-5d48-48d6-a329-7202b44607e3).

## License
- MIT + Commons Clause v1.0 (no selling), Copyright (c) 2026 Jay Weiner, SPDX
  `LicenseRef-MIT-Commons-Clause`. NOT GPL: GPL cannot forbid selling. Every .cs file (and
  tools/build-wsl-modules.sh) starts with the SPDX header; notices come from
  `AppInfo` (Infrastructure.cs): --help, Tools > About and license, startup log line, assembly Copyright.
  LICENSE is copied to the build output

## Constraints
- .NET Framework 4.8, C# 7.3 (`<LangVersion>7.3</LangVersion>`), WinForms, UI built in code (no designer files)
- No NuGet packages. Framework assemblies only (System.Management, System.Runtime.Serialization, ...)
- SDK-style csproj, built with `dotnet build`; needs the .NET Framework 4.8 Developer Pack
- `requireAdministrator` manifest: running or debugging needs an elevated VS Code
- AnyCPU, Prefer32Bit=false (a 32-bit process would be redirected away from the real wsl.exe)

## Commands
- Build: `dotnet build -c Release` -> `bin\Release\net48\XnixUsbMounter.exe` (+ `.exe.config`, `.com`)
- CLI check without the GUI: `XnixUsbMounter --list` (elevated terminal; no extension, so the `.com` runs);
  add `--verbose` to see the DEBUG lines too
- Logs: `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log`; state: `state.json` in the same folder

## Name
- Renamed Btrfs USB Mounter / BtrfsUsbMounter -> Xnix USB Mounter / XnixUsbMounter (2026-09-25): project
  XnixUsbMounter.csproj, assembly and namespaces XnixUsbMounter, output XnixUsbMounter.exe / .com. Repo folder is
  still Btrfs_USB. Deliberately KEPT the old name (compatibility with older copies, do not rename): the data
  folder `%LOCALAPPDATA%\BtrfsUsbMounter`, mutex / ack event / ShowWindow message `BtrfsUsbMounter.*`, the logon
  task name `BtrfsUsbMounter` (PointsElsewhere re-points it), the `BtrfsUsbMounter.ps1` check for the PowerShell version

## Environment (developer machine)
- WSL2 distros: openSUSE-Tumbleweed (default), user `chippy`; btrfsprogs and util-linux installed.
  kali-linux (2026-09-24, test distro for the apt build path; root only, no user created; remove with
  `wsl --unregister kali-linux`). Both have the 6.18 drivers built (each distro keeps its own)
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
- User docs (README, docs/distros, --help, menu and dialog texts) do NOT mention ReiserFS or Reiser4 (user decision
  2026-09-25: they cannot be mounted on current WSL kernels). The probe, its "can't mount" notes and the build script's
  pre-6.13 reiserfs path stay in the code; don't add them back to the docs
- `src/Core/FileSystems.cs`: `FsProbe` parses one cached 272 KiB read per partition (priority order,
  btrfs crc32c checked, libblkid-style sanity checks for ReiserFS and ZFS); `FsSupport` asks the
  distro at runtime which kinds the kernel has (`/proc/filesystems`, `modinfo`) and if fsapfsmount/zpool exist
- `MountEntry.FsType` missing = btrfs (older and PowerShell-written state.json)
- Scrub, drive info, offline check are btrfs-only; the Mount options box is btrfs-only (UFS gets its own
  ufstype option from the probe)
- Mount methods (`MountEntry.MountMethod`): kernel (`wsl --mount --type`), fuse (APFS via
  fsapfsmount), zfs (`--bare` + `zpool import -R /mnt/wsl <guid>`, never `-f`; eject = `zpool export`)
- After a kernel mount asked for rw, /proc/mounts is checked: a driver that fell back to ro (UFS/ext4 needing
  fsck) is recorded as read-only, so eject skips the flush
- UFS: kernel method with `--options [ro,]ufstype=X`; the probe picks X (ufs2 / 44bsd; sun, sunx86 = Solaris,
  always ro). wsl --mount's option parser (WSL src/linux/mountutil) turns "ro" into MS_RDONLY. Read-only unless
  Settings.UfsWrite (Tools > Allow UFS writes) AND the ufs module has `modinfo -F wsl_handoff` = 1. fs_clean != 1,
  FS_NEEDSFSCK, gjournal: ro with a note. FsInfo.KernelOptions / WriteNote carry ufstype and the rw caveats
- UFS handoff patch (`ufs_handoff` in the build script, applied to a COPY of fs/ufs at 7 anchor lines that match
  in 6.6 and 6.18; if any is missing it builds read-only instead): rw mount sets fs_clean 0 on disk (stock Linux
  leaves 1, so FreeBSD would skip fsck after a crash), clean unmount sets 1; clears FS_METACKHASH when
  fs_metackhash != 0 (FreeBSD then disables its check hashes; else EINTEGRITY after the first write); with
  SU+J sets fs_mtime, so fsck_ffs never replays the old journal (it requires journal di_modrev == fs_mtime);
  refuses rw unless fs_clean == 1 (NetBSD writes 2 while mounted, stock Linux accepts 2); errors write 0 not 0xff
  (the BSDs treat any non-zero as clean). FreeBSD fields live in Linux's fs_44.fs_sparecon[23/48/49]
- Locally built modules: WSL mounts /lib/modules/<release> as an overlay whose upper layer is NOT on
  the distro disk (it is in the VM, lost on every WSL restart; the 6.6 modules vanished that way, not
  because of wsl --update). The script keeps them in /var/lib/wsl-modules/<release> and installs a
  copy in /lib/modules/<release>/extra (+ depmod, marker .restored); `FsSupport.RestoreModules` puts
  them back before the support check and before every module load.
  WSL module signing is off. Config must be EXACTLY /proc/config.gz plus the added =m drivers:
  turning DEBUG_INFO_BTF off changed struct module (DEBUG_INFO_BTF_MODULES) and 250 of 582 CRCs.
  resolve_btfids fails with new glibc: fixed with HOSTCFLAGS=-Wno-error=discarded-qualifiers (host
  tools only). Built with the gcc major that built the running kernel (6.6: gcc-11 from devel:gcc
  Factory, temporary repo; 6.18: gcc-13 from Tumbleweed). JFS needs KBUILD_EXTRA_SYMBOLS from
  fs/nls (nls_ucs2_utils is a shipped module). OpenZFS needs KERNEL_CC and a "gcc" shim.
  linux-apfs-rw needs ./genver.sh first. The script checks CRCs against btrfs.ko.
- Package managers: the script supports zypper (openSUSE, SLES) and apt (Debian, Kali, Ubuntu) via
  pkg_install ZYPPER-NAMES -- APT-NAMES and zfs_version (rpm / dpkg-query zfsutils-linux, epoch and
  Debian revision stripped). apt: --no-install-recommends, so zfsutils-linux does not pull zfs-dkms.
  DKMS packages (zfs-dkms, apfs-dkms) never work in WSL: no headers, no /lib/modules/<rel>/build.
  OpenZFS META Linux-Maximum is checked: a too-old distro zfs (Ubuntu 24.04: 2.2.2, max 6.6) skips ZFS
  instead of failing the run. Kali: gcc-13 13.4.0 pins only the 3 asm-goto probes; apfsprogs names
  its checker apfsck; Kali's fsapfsmount has no FUSE, so APFS there uses the built kernel driver
- Leap 16.0 / SLES: gcc13 is in their own repos (SLES 15 SP7: Development Tools module); zfs, jfsutils,
  apfsprogs come from download.opensuse.org/repositories/filesystems/{16.0,15.7,SLE_15_SP6}. The
  script's devel:gcc Factory fallback is Tumbleweed-only. Driver build untested on Leap/SLES
- When running the build by hand, redirect its output to a file INSIDE WSL (sh -c '... > file'):
  long output relayed through wsl.exe stdout lost lines. Don't edit the script while it runs (sh
  reads it as it goes): run a copy
- ZFS vdevs are opened from kernel threads in the VM's root mount namespace: file vdevs inside the
  distro fail (vdev.open_failed); block devices (/dev/sdX from wsl --mount --bare, loop devices) work
- `zfs` requires `zfs-kmp` (RPM dependency), so openSUSE's kernel-default/zfs-kmp-default stay installed
- Rebuild modules after `wsl --update`. `FsSupport` uses `modinfo` (no loading); modules load right before mount
- Toolchain probes (CC_HAS_*, GCC_ASM_GOTO_OUTPUT_BROKEN, ...) are recomputed from OUR compiler.
  6.18 was built with GCC 13.2.0, Tumbleweed has gcc-13 13.5.0: CC_HAS_SANE_FUNCTION_ALIGNMENT turned
  `__cold` on and changed the CRCs of _printk, panic, __fortify_panic. The script pins every probe
  that differs to /proc/config.gz by rewriting its Kconfig entry (pin_kconfig), then reruns
  olddefconfig. GCC_PLUGINS=y in Microsoft's build but off here (no plugin headers): harmless, no
  plugin is enabled and it only adds a rebuild trigger (compiler-version.h)
- Current WSL kernel (2026-09-24, after `wsl --update`): 6.18.33.2-microsoft-standard-WSL2, built
  with GCC 13. ReiserFS was removed in Linux 6.13: the script skips it (no fs/reiserfs), so on 6.18
  ReiserFS is detect-only
- Verified on 6.18.33.2 (2026-09-24): build with pinned probes, 608/608 btrfs.ko CRCs match; jfs,
  hfs, hfsplus, spl, zfs (2.4.4) and apfs load, no "disagrees about version" in dmesg. Loop-device tests
  (all pass): btrfs/ext4/xfs rw + sync -f; JFS rw, data survives remount, fsck.jfs clean; APFS kernel
  ro by default, -o readwrite write + remount, fsck.apfs clean; fsapfsmount reads it; HFS+/HFS load
  only (no mkfs.hfsplus in Tumbleweed); ZFS create/export, import by GUID -R /mnt/wsl, write, scrub,
  export. Restore after a simulated restart (extra deleted, modules unloaded): detected + loaded.
  NOT tested: the app itself (wsl --mount needs an elevated shell), real USB disks, a real wsl --shutdown
- UFS verified on 6.18.33.2 in Tumbleweed (2026-09-25) against real FreeBSD: FreeBSD 15.1 BASIC-CI image in
  QEMU/KVM inside WSL (/var/tmp/fbsdvm, driven by expect over the serial console, root disk snapshot=on).
  `newfs -U -j` UFS2 (SU+J, check hashes superblock/cg/inodes) and `newfs -O1 -U` UFS1 -> Linux ro + rw
  (100 MB file, 1500 small files, deletes, renames, symlinks incl. 200 chars, hard links, chown/chmod,
  remount ro/rw) -> FreeBSD fsck_ffs -n clean, all 3161/3160 sha256 match, FreeBSD writes after, fsck clean.
  On disk: fs_clean 0 while rw, 1 after umount/remount-ro; flags 0x20a -> 0x0a. A copy taken while rw-mounted:
  FreeBSD refuses rw ("not clean - run fsck"), fsck -p says "Journal timestamp does not match", full check,
  exit 0. Interactive fsck_ffs re-adds check hashes. fs_clean 0 and 2 images: rw request -> ro. Patch applies
  and compiles on 6.6 too. FreeBSD fsck asks "UPDATE FILESYSTEM TO TRACK DIRECTORY DEPTH" for Linux-made
  dirs (harmless). Kali (apt build, own module): NetBSD makefs UFS1 image ro + rw (incl. ENOSPC: makefs sizes
  inodes to the content) -> FreeBSD fsck clean, 1386 sums match. Debian's makefs (20190105) puts UFS2 at 8 KiB:
  neither Linux nor FreeBSD reads it (probe notes it). NOT tested: real NetBSD/OpenBSD/Solaris media, big-endian
  UFS, the app mounting a real UFS disk
- Verified on Kali 2026.2 (2026-09-24, same kernel): full apt build (script installed zfsutils-linux
  2.4.4 without zfs-dkms), 3 probes pinned, CRCs match, all 6 modules load; the same loop-device
  tests pass (fsapfsmount skipped); restore after a simulated restart with ALL built modules unloaded
  (zfs included) works. Tumbleweed re-checked after the apt refactor (jfs-only rebuild)
- Verified earlier on kernel 6.6.87.2 only (2026-09-23): all 7 modules load; JFS rw + fsck clean;
  APFS kernel ro by default, readwrite + fsck.apfs clean; ZFS pool on a loop device: import by GUID
  under /mnt/wsl, zfs get, export; real-pool ZFS detection matches blkid. HFS+/ReiserFS: load only
- Test fixtures are made with mkfs in WSL (e2fsprogs, xfsprogs, jfsutils, apfsprogs, btrfsprogs
  installed in the distro) and cross-checked with `blkid -p`. UFS fixtures: no Linux mkfs/fsck for UFS
  (Debian dropped ufsutils after wheezy); make them with newfs in the FreeBSD VM (qemu-x86 + expect installed
  in Tumbleweed) and check them with fsck_ffs there. Test scripts must restore built modules first (WSL
  idle-restarts between calls and /lib/modules/<rel>/extra is then empty)

## Logging
- `Log.Debug` = troubleshooting detail: always in mounter.log, hidden on screen unless Tools >
  Show detailed log lines / `--verbose`. `Log.Exception` = short error on screen + full stack as debug
- `Wsl.RunAsync` / `RunStreamingAsync` log every call (numbered `wsl#N`) with args, exit, duration, output
- Don't log in code that runs every 30 s (snapshot, quiet jobs) unless something changed or failed

## Rules learned the hard way (keep these)
- No double hyphen inside XML comments (app.manifest, csproj, App.config): the Windows loader
  rejects the manifest and the exe fails with a side-by-side configuration error
- The exe is WinExe, so terminals don't wait for it. CLI use goes through `XnixUsbMounter.com`
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

## Status docs (RESUME.md + STATUS.md)
Same convention as VariableDrive / FeatureRecognition: two files at the repo root, split by edit frequency.
- `RESUME.md`: the live "START HERE" resume, rewritten after every change, under about 120 lines. A fresh session
  reads it first. Banner: state (last commit, uncommitted work, built vs run), the next step; then goal, state,
  files this turn, what changed, run list, closed / do not retry, owed
- `STATUS.md`: append-only trail, one entry per commit, newest first, each naming its evidence. Prepend; never
  rewrite or truncate. An entry goes in with its own commit, headed `(this commit)`; the next change puts the hash in
- If either looks reverted or shrunk on disk: `git checkout HEAD -- RESUME.md STATUS.md`

## Verification done before handover
Compiles against the 4.8 reference assemblies; 32 core tests passed under Mono (superblock
parser on a real btrfs image, btrfs output parsers, argument quoting, PowerShell-format
state.json, flush command, cancel/timeout, job queue). The GUI and `dotnet build` itself have
not yet been run on Windows - do that first.
