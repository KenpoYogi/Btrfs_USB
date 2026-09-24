# Btrfs USB Mounter — where we are (status trail)

> **▶ THE LIVE "START HERE" RESUME IS [RESUME.md](RESUME.md)** (small, rewritten each change). This file is the
> **append-only trail**: one entry per commit (or per uncommitted change, until it is committed), newest first, each
> naming what changed and the evidence behind it. **Prepend; never rewrite or truncate.** An entry goes in with
> its own commit, so it cannot name its own hash: the next change adds it (`(this commit)` → the hash). Detailed technical findings stay in
> [CLAUDE.md](CLAUDE.md); logs are in `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log`.

---

## ⭐ (this commit) — **MAIN WINDOW: DRAGGABLE SPLITTER, X MINIMIZES TO THE TRAY, CLOSE BUTTON EXITS.** _2026-09-24. Evidence: user request ("both upper and lower panes resizeable", "the top right x also minimizes the window, not closes", "add a Close button on the bottom right"). `dotnet build -c Release`: succeeded, 0 warnings. **Not yet run** (needs an elevated session)._

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
