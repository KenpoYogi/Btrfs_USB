# ▶ START HERE — Linux USB Mounter resume

**⭐ LAST COMMIT (2026-09-26): VERSION 2.1.0, SETUP CHECKS WSL2 / LINUX AND CAN SET IT UP.** After copying the files, setup
checks WSL and the WSL2 distros (btrfs, blkid, modinfo). If none is ready it offers: install WSL (restart, run setup
again), update WSL, add only the missing packages to an existing distro, install openSUSE Tumbleweed (default) /
Ubuntu 26.04 / Kali with the packages, or open the guides. Builds clean: `LinuxUsbMounter-2.1.0-Setup.exe` / `-Portable.zip`.
Tested for real against WSL (no admin needed): check, fix path (Kali), fresh Ubuntu 26.04 install (then unregistered).
The user's real 2.0.0 install in `C:\Apps\Linux_USB` checked out (registry, files, Start menu, logon task).
Committed and pushed. Before it: `5e0028c` (v2 icon files deleted), `a9020bf` (installer + portable zip).

_**Next: ① (done: committed, pushed)** **② run the 2.1.0 setup over the 2.0.0 install** (Update; Linux check says
Tumbleweed is ready) **③ uninstall test** with a drive mounted **④ the earlier run list**._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4
> detect-only. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** setup makes sure WSL2 has a working Linux (user request 2026-09-26), version 2.1.0.

> ### 2. STATE
> **Committed and pushed** on `main` (previous commit `5e0028c`). Clean `dotnet build -c Release`: 0 warnings, 0 errors.
> **User's machine:** 2.0.0 installed by setup in `C:\Apps\Linux_USB` (Start menu shortcut, logon task there). WSL 2.7.14,
> distros openSUSE-Tumbleweed (default, ready) and kali-linux (test, ready). Tumbleweed's util-linux was upgraded
> 2.42.2 → 2.42.3 by a test of the first package-step version (normal repo update); since then only missing packages.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.

> ### 3. FILES THIS TURN
> `installer/Setup.cs` (WslRunner, LinuxSetup, LinuxSetupDialog, InstallForm Linux step, uninstall message) ·
> `installer/setup.manifest`, `app.manifest`, `LinuxUsbMounter.csproj` (2.1.0) · `README.md` · `docs/distros/README.md`,
> `opensuse-tumbleweed.md`, `ubuntu.md`, `kali.md` (setup tip) · `CLAUDE.md` · `STATUS.md` · this file.

> ### 4. WHAT CHANGED
> · **Check:** `wsl --version` / `--status`, `--list --verbose`, then per WSL2 distro (default first) as root: tools,
>   package manager, os-release. First ready distro wins; not the default → hint to pick it in the app.
> · **Dialog** (only when nothing is ready): problems in orange, radio choices (one line each, notes below), "make it the
>   default" box for a new distro, link to the GitHub guides. Skip = continue without.
> · **New distro:** `wsl --install -d NAME --no-launch` (launcher `install --root` fallback, WSL1 → 2), full update, then
>   btrfs-progs / util-linux / kmod (+ libfsapfs on openSUSE), as root, no Linux user. **Existing:** only missing packages.
> · **WSL install** needs a restart: setup says so and doesn't start the program.

> ### 5. ⭐ THE RUN LIST
> **① 2.1.0 setup over 2.0.0** (elevated): Update in C:\Apps\Linux_USB, app closes and restarts, Linux check finds Tumbleweed.
> **② Uninstall** with the btrfs drive mounted (warning, flush, "safe to unplug", everything removed, %TEMP% copy gone).
> **③ Linux paths not run yet:** a PC without WSL (install + restart), old inbox WSL (`wsl --update`), fresh Tumbleweed /
> Kali installs, dnf / pacman distros. **④ The app elevated** with no Linux drive (`57cc60c`) + new icon. **⑤ UFS disk**,
> **⑥ splitter check** (`369d3ff`), **⑦ real hardware**, **⑧ real `wsl --shutdown`**, **⑨ non-btrfs USB disk**, **⑩ Leap / SLES build.**

> ### 6. ⛔ CLOSED / DO NOT RETRY
> · ⛔ **Installing already-present packages in "fix" mode** — it upgrades them (zypper did); install only the missing ones.
> · ⛔ **PowerShell script blocks as callbacks for background-thread events** (Process output) — no runspace, the host
>   dies; use a compiled C# method as the delegate in test harnesses.
> · ⛔ **Backslashes / `\u` through the Bash tool (sed, perl -e) or `\uXXXX` in Write/Edit**; use script files and
>   `char.ConvertFromUtf32` in C#. ⛔ **Long RadioButton / CheckBox texts** — they don't wrap; note label underneath.
> · ⛔ **`$(IntermediateOutputPath)` in a project-level property** — empty at evaluation.
> · ⛔ **UFS rw with the stock Linux driver**; ⛔ **Debian's makefs for UFS2 fixtures**; ⛔ **test scripts that skip the
>   module restore**; ⛔ **`--` inside an XML comment**; ⛔ **module configs other than /proc/config.gz + drivers**;
>   ⛔ **unpinned toolchain probes**; ⛔ **trusting `/lib/modules/<release>`**; ⛔ **DKMS in WSL**; ⛔ **`zpool import -f`**;
>   ⛔ **ZFS file vdevs**; ⛔ **long build output through wsl.exe stdout**; ⛔ **editing the build script while it runs**;
>   ⛔ **a 32-bit process**; ⛔ **a `btrfs check --repair` feature**.

> ### 7. OWED
> **Yours:** run list ① – ⑩ (elevated session, physical drives).
> **Mine:** fixes from ① – ③; the commit's hash in STATUS.md at the next change.

> **TRAIL: [STATUS.md](STATUS.md)** · **PROJECT RULES: [CLAUDE.md](CLAUDE.md)** · **USER DOCS: [README.md](README.md)**,
> **[docs/distros/](docs/distros/README.md)**
