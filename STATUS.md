# Linux USB Mounter — where we are (status trail)

> **▶ THE LIVE "START HERE" RESUME IS [RESUME.md](RESUME.md)** (small, rewritten each change). This file is the
> **append-only trail**: one entry per commit (or per uncommitted change, until it is committed), newest first, each
> naming what changed and the evidence behind it. **Prepend; never rewrite or truncate.** An entry goes in with
> its own commit, so it cannot name its own hash: the next change adds it (`(this commit)` → the hash). Detailed technical findings stay in
> [CLAUDE.md](CLAUDE.md); logs are in `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log`.

---

## ⭐ (this commit) — **RENAMED AGAIN: XNIX USB MOUNTER → LINUX USB MOUNTER, OUTPUT FILES LinuxUsbMounter.exe / .exe.config / .com.** _2026-09-25. Evidence: user request ("from XnixUsbMounter to LinuxUsbMounter to be a bit more descriptive ... all the code and documentation changes"). Clean `obj`, `dotnet build -c Release`: 0 warnings, 0 errors; bin\Release
et48 has LinuxUsbMounter.exe / .exe.config / .com / .pdb, ProductName "Linux USB Mounter", OriginalFilename LinuxUsbMounter.exe. `git grep -i xnix` outside the status docs: only the README upgrade note and CLAUDE.md "Name". Not run (elevated GUI)._

- **Renamed** everything `29f15c5` renamed: `XnixUsbMounter.csproj` → `LinuxUsbMounter.csproj` (git mv), assembly, namespaces,
  display name, file headers, LICENSE, app.manifest, launcher, .vscode, build script comments, README, distro guides, CLAUDE.md.
- **Still the old BtrfsUsbMounter name on purpose** (unchanged): data folder, mutex / ack event / ShowWindow message,
  logon task, the `.ps1` check. Nothing kept the Xnix name.
- README upgrade section now covers `BtrfsUsbMounter.*` and `XnixUsbMounter.*`. STATUS.md history left as written, title only.
- **Files.** all of the above, `RESUME.md`, this file.

---

## `159c44d` — **REISERFS AND REISER4 REMOVED FROM ALL USER DOCUMENTATION.** _2026-09-25. Evidence: user request ("Since Reiser4 and ReiserFS cannot be mounted, remove all mention of them from all documentation"). `dotnet build -c Release`: 0 warnings, 0 errors. `git grep -i reiser` outside the status docs and CLAUDE.md: only src/Core/FileSystems.cs and tools/build-wsl-modules.sh (code). Not run in the GUI._

- **README:** both table rows, the "(before Linux 6.13) ReiserFS" in the build steps, "Reiser" in the file list.
- **docs/distros:** the ReiserFS / Reiser4 row in all 9 guides, the paragraph in docs/distros/README.md.
- **User-facing texts:** `--help` (`src/Program.cs`, now "... JFS, ZFS, HFS+, APFS, UFS/UFS2"), the Tools menu item and the
  build confirmation dialog (`src/UI/MainForm.cs`).
- **Unchanged (code):** the probe still detects both and explains why they can't be mounted; the build script still
  builds reiserfs on pre-6.13 kernels. CLAUDE.md "Filesystems" records the decision.
- **Files.** `README.md`, `docs/distros/*.md` (10), `src/Program.cs`, `src/UI/MainForm.cs`, `CLAUDE.md`, `RESUME.md`, this file.

---

## `29f15c5` — **RENAMED TO XNIX USB MOUNTER: OUTPUT FILES XnixUsbMounter.exe / .exe.config / .com.** _2026-09-25. Evidence: user request ("make the name of the output files from BtrfsUsbMounter to XnixUsbMounter ... all the code and documentation changes"). Clean `obj`, `dotnet build -c Release`: 0 warnings, 0 errors; bin\Release
et48 has XnixUsbMounter.exe / .exe.config / .com / .pdb, exe ProductName "Xnix USB Mounter", OriginalFilename XnixUsbMounter.exe. Not run (elevated GUI)._

- **Renamed:** `BtrfsUsbMounter.csproj` → `XnixUsbMounter.csproj` (git mv), AssemblyName / RootNamespace / every namespace,
  Product and every "Btrfs USB Mounter" display string (window title, --help, log start line, task description, SPDX
  headers, LICENSE "Software:" line, build script comments), app.manifest identity, launcher target exe, .vscode tasks.
- **Kept on purpose** (older copies must still see the new one): `%LOCALAPPDATA%\BtrfsUsbMounter` (state + log carry
  over), mutex / ack event / ShowWindow message `BtrfsUsbMounter.*`, logon task name `BtrfsUsbMounter` (re-pointed to the
  new exe by `PointsElsewhere`), the `BtrfsUsbMounter.ps1` check. Commented in code; listed in CLAUDE.md "Name".
- **Docs:** README (names, install folder, new "Upgrading from BtrfsUsbMounter.exe"), all distro guides, CLAUDE.md.
  STATUS.md history left as written (append-only), title only.
- **Files.** all of the above, `RESUME.md`, this file.

---

## `a874ec1` — **EMPTY DRIVE LIST TEXT: REISERFS AND REISER4 REMOVED, "UFS" → "UFS/UFS2".** _2026-09-25. Evidence: user request ("In the dialogue background remove the words ReiserFS and Reiser4 please. Also change UFS to UFS/UFS2"). `dotnet build -c Release`: 0 errors. Not run in the GUI._

- `MainForm.emptyLabel` now reads "...btrfs, ext2/3/4, XFS, JFS, ZFS, HFS+, APFS or UFS/UFS2 - it will appear here
  automatically." Detection unchanged. `--help` (`src/Program.cs`) still lists ReiserFS and Reiser4 (asked the user).
- **Files.** `src/UI/MainForm.cs`, `RESUME.md`, this file.

---

## `238c76f` — **UFS1 / UFS2 (FREEBSD, NETBSD, OPENBSD): DETECTED, MOUNTED READ-ONLY, READ/WRITE OPT-IN WITH A PATCHED DRIVER THAT FREEBSD ACCEPTS.** _2026-09-25. Evidence: user request ("add UFS and UFS2 support", then "read/write if possible, and update all the distro docs"). Driver built on 6.18.33.2 in Tumbleweed and Kali (CRCs match, 7 modules load, `wsl_handoff=1`); patch applies and compiles on the 6.6 tree too. **FreeBSD 15.1 in QEMU/KVM inside WSL:** `newfs -U -j` UFS2 (SU+J + check hashes) and `newfs -O1 -U` UFS1 → Linux ro and rw writes → FreeBSD `fsck_ffs -n` clean, 3161 / 3160 checksums match, FreeBSD writes after, clean; crash copy (taken while rw-mounted) refused rw by FreeBSD, `fsck -p` skips the stale journal and does a full check. Kali: NetBSD `makefs` UFS1 written (incl. ENOSPC) → FreeBSD fsck clean, 1386 checksums match. Probe run on 8 real superblocks (FreeBSD, makefs, crafted unclean). `dotnet build -c Release`: 0 warnings. **The app itself has not mounted a UFS disk** (wsl --mount needs an elevated shell)._

- **Probe** (`FsProbe.Ufs`): UFS2 at 64 KiB, UFS1 (or makefs UFS2, not mountable, noted) at 8 KiB, either byte order;
  label (fs_volname, new layout only), UUID as blkid (`%08x%08x` of fs_id), size and free space; picks `ufstype`
  (ufs2, 44bsd; sun / sunx86 by the Solaris state stamp, read-only). fs_clean != 1, FS_NEEDSFSCK, gjournal → read-only
  with a note; `WriteNote` for check hashes / SU+J.
- **Mount:** kernel method, `--options [ro,]ufstype=X`. Read/write only with **Tools > Allow UFS writes (experimental)**
  (`AppSettings.UfsWrite`) and a `ufs` module with `modinfo -F wsl_handoff` = 1 (`FsSupport.CanWriteUfs`). New for every
  kernel mount asked rw: `/proc/mounts` is checked and a driver's ro fallback is recorded (no flush on eject).
- **Build script:** `ufs` in the default set, `UFS_FS=m` + `UFS_FS_WRITE=y` (stamp `... ufs` → one reconfigure),
  `ufs_handoff` patches a copy of fs/ufs: fs_clean 0 while rw, 1 on clean unmount, 0 (not 0xff) on error; clears
  FS_METACKHASH; sets fs_mtime with SU+J; rw only when fs_clean == 1. Falls back to a read-only build if an anchor moves.
- **Docs:** README (table, build steps, UFS section), all 10 distro guides (tables, driver sections, troubleshooting;
  Debian/Kali anchors renamed), CLAUDE.md.
- **Files.** `src/Core/FileSystems.cs`, `src/Core/MountManager.cs`, `src/Core/Models.cs`, `src/Core/StateStore.cs`,
  `src/UI/MainForm.cs`, `src/Program.cs`, `tools/build-wsl-modules.sh`, `README.md`, `docs/distros/*.md`, `CLAUDE.md`,
  `RESUME.md`, this file.

---

## ⭐ 369d3ff — **THE SPLITTER CAN BE SEEN, AND THE LOG STARTS BIG: THE UPPER PANE AT HALF ITS OLD HEIGHT.** _2026-09-24. Evidence: the user ran `a0d162d` at 16:26 — the log shows X → tray (16:27:00, `exit requested False`) and Close → exit (16:27:03, `True`) working — but `state.json` kept `LogHeight: 0`: the splitter was never dragged. It was an unmarked 6 px strip in the window colour. User: "vertically resize the upper and lower panes; the upper pane initially 50% smaller, the lower gets the extra space". `dotnet build -c Release`: succeeded. **Not yet run.**_

- **Visible splitter:** 8 px (96 dpi), a line along each edge and nine grip dots in the middle (`OnPaintSplitter`,
  repainted on move and resize); tooltip "Drag to resize the drive list and the log." over the bar.
- **Default split:** the drive list + buttons get half of what a 170 px log left them; the log gets the rest (about
  190 / 360 px in the default 1100 × 680 window). A dragged height (`LogHeight` > 0) still wins.
- **Files.** `src/UI/MainForm.cs`.

---

## ⭐ a0d162d — **MAIN WINDOW: DRAGGABLE SPLITTER, X MINIMIZES TO THE TRAY, CLOSE BUTTON EXITS.** _2026-09-24. Evidence: user request ("both upper and lower panes resizeable", "the top right x also minimizes the window, not closes", "add a Close button on the bottom right"). `dotnet build -c Release`: succeeded, 0 warnings. **Not yet run** (needs an elevated session)._

- **SplitContainer** between the drive list (+ button bar) and the log; `FixedPanel = Panel2`, minimums 140 / 60 px at
  96 dpi, default log 170 px. `ApplySplitLayout()` in `OnLoad` (fires on first show, also when started in the tray).
- **`AppSettings.LogHeight`** (96-dpi px, 0 = default), saved only after a user drag (`SplitterMoving` flag).
- **X / Alt+F4 / taskbar close → minimize to the tray** (`OnFormClosing`, `CloseReason.UserClosing` without
  `exitRequested`). First-hide balloon mentions tray > Exit.
- **Close button** (bottom right, `Dock = Right` host panel) and tray **Exit** → `RequestExit()` → the existing close
  path with its job and mounted-drive prompts. The other buttons wrap to a second row on narrow windows (bar height
  follows `GetPreferredSize`).
- **Files.** `src/UI/MainForm.cs`, `src/Core/Models.cs`; `RESUME.md` and this file created; CLAUDE.md status-docs section.

---

## ⭐ 876d770 — **FILESYSTEM DRIVERS BUILD WITH APT TOO; TESTED ON KALI.** _2026-09-24 11:07. Evidence: Kali 2026.2 on kernel 6.18.33.2 — full apt build, 3 probes pinned, CRCs match, all 6 modules load, JFS / APFS / ZFS loop-device tests pass, restore after a simulated restart with all built modules unloaded works. Tumbleweed zypper path re-checked (jfs-only rebuild)._

- `build-wsl-modules.sh`: `pkg_install ZYPPER -- APT` names; ZFS version from `dpkg-query zfsutils-linux` (no recommends,
  so no zfs-dkms); gcc-NN from the distro; ZFS skipped with a message when the distro's OpenZFS is older than the kernel
  (META `Linux-Maximum`) instead of failing the run.
- App hints mention apt distros. Guides: Kali (APFS through the built kernel driver, Kali's fsapfsmount has no FUSE;
  removing Kali), Debian (contrib for ZFS), Ubuntu (24.04 ZFS too old), DKMS explained.

---

## be5a3b8 — **DISTRO GUIDES: FILESYSTEMS REPOSITORY FOR LEAP AND SLES.** _2026-09-24 10:29. Evidence: download.opensuse.org/repositories/filesystems has Leap 16.0, 15.7 and SLE_15_SP6 builds of zfs, jfsutils, apfsprogs; gcc13 is in Leap/SLES repositories. **Driver build on Leap/SLES untested.**_

- Leap and SLES guides: the repository, ZFS, the driver build. Other distros told they need an openSUSE / SLES (later:
  apt) distro for the build. Built drivers survive WSL restarts.

---

## ⭐⭐ 9ea658f — **DRIVER BUILD FIXED ON 6.18; BUILT DRIVERS SURVIVE WSL RESTARTS.** _2026-09-24 10:29. Evidence: after `wsl --update` to 6.18.33.2 nothing installed — GCC 13.5 vs Microsoft's 13.2.0 turned `__cold` on through CC_HAS_SANE_FUNCTION_ALIGNMENT and changed the CRCs of `_printk`, `panic`, `__fortify_panic`; and the 6.6 modules had vanished because `/lib/modules/<release>` is an overlay whose upper layer is in the VM. Verified: 608/608 btrfs.ko CRCs match; jfs, hfs, hfsplus, spl, zfs, apfs load; loop-device tests pass; restore after a simulated restart works._

- `pin_kconfig`: every toolchain probe that differs from `/proc/config.gz` is pinned, then `olddefconfig` again.
- Modules kept in `/var/lib/wsl-modules/<release>`, copied to `/lib/modules/<release>/extra` + depmod;
  `FsSupport.RestoreModules` before the support check and every module load.
- devel:gcc Factory fallback on Tumbleweed only.

---

## f4c480d — **DISTRO SETUP GUIDES; REISERFS SKIPPED ON 6.13+ KERNELS.** _2026-09-24 09:56. Evidence: WSL kernel now 6.18.33.2; ReiserFS was removed in Linux 6.13 (no `fs/reiserfs`), so the build failed there._

- `docs/distros/`: guides for Tumbleweed, Leap, SLES, Ubuntu, Fedora, Kali, Debian, CentOS Stream / AlmaLinux, Arch.
- Build script skips ReiserFS without `fs/reiserfs`; stops early without zypper. App hints corrected (ReiserFS on 6.13+,
  fsapfsmount availability). README install step copies `tools\` and `LICENSE`.

---

## 9235484 — **LICENSE: MIT + COMMONS CLAUSE v1.0.** _2026-09-24 09:24. Evidence: user decision — no selling; GPL cannot forbid it._

- SPDX `LicenseRef-MIT-Commons-Clause` in every source file; `AppInfo` notices, assembly Copyright, README, CLAUDE.md.

---

## ⭐⭐⭐ fb07a19 — **MULTI-FILESYSTEM SUPPORT, WSL DRIVER BUILDS, LOGGING, LICENSING.** _2026-09-24 00:11. Evidence: the first launch on Windows failed with a side-by-side configuration error — `--` inside an XML comment in app.manifest. 6.6.87.2: all 7 built modules loaded; JFS rw + fsck clean; APFS kernel ro / readwrite + fsck.apfs clean; ZFS pool on a loop device imported by GUID under /mnt/wsl._

- Manifest fix; console front end `BtrfsUsbMounter.com` (terminals wait, real exit code).
- DEBUG log level, startup header, every wsl.exe call numbered; Tools toggle and `--verbose`.
- Detection of btrfs, ext2/3/4, XFS, JFS, ReiserFS, Reiser4, ZFS, HFS+, APFS; runtime kernel support check.
- ext/XFS rw; APFS ro (fsapfsmount) or kernel driver (writes experimental, opt-in); ZFS through `zpool import/export`.
- `tools/build-wsl-modules.sh` with symbol-version check; Tools > Build filesystem drivers.

---

## bdce371 — **INITIAL COMMIT: BTRFS USB MOUNTER.** _2026-09-23 20:36. Evidence: 32 core tests under Mono (superblock parser on a real btrfs image, output parsers, quoting, PowerShell-format state.json, flush command, cancel / timeout, job queue)._

- WinForms (.NET Framework 4.8) tool mounting btrfs USB drives through WSL2: tray UI, drive info, scrub, safe eject.
