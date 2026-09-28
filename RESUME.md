# ▶ START HERE — Linux USB Mounter resume

**⭐ LAST COMMIT (2026-09-26): VERSION 2.2.0, UNINSTALL CAN ALSO REMOVE THE WSL DISTRO SETUP INSTALLED.** Setup records
each distro it installs (`LumSetupDistros`, per user SID); the uninstall window then shows "What to remove": the Windows
app (always) and that distro (unticked; deletes it with `wsl --unregister`, after the drives are unmounted). Builds clean:
`LinuxUsbMounter-2.2.0-Setup.exe` / `-Portable.zip`. Tested: window layout, InstallDistro + RemoveDistro on a throwaway
Ubuntu 26.04. Not elevated-tested (recording needs HKLM write).
Committed and pushed. Before it: `c676eba` 2.1.0 (setup checks WSL2 / Linux and can set it up).
**Then (2026-09-28, committed and pushed): README only.** Removed "Migrating from the PowerShell version" and "Upgrading from
BtrfsUsbMounter.exe or XnixUsbMounter.exe" (user request); the portable version now says clearly (intro, Install table
row, note at the top of its section) that WSL2 and the distro must be installed and set up by hand (no Linux check).
**Then `b3b1aee`: README only.** Also removed "What changed compared to the PowerShell version" (user request).
**Then (committed and pushed): README only.** Intro no longer calls it a port of the PowerShell tool; the "same folder as the
PowerShell version" bullet is gone (user request). The README now mentions PowerShell only as the shell.
**LAST COMMIT (2026-09-28, pushed): VERSION 2.2.1, ReiserFS / Reiser4 removed from the code** (user request). FileSystems.cs: no
FsKind, probe, NoDriverHint text or KernelKinds entry; build script: no reiserfs argument, config options or skip path.
Clean Release build (0 warnings), `LinuxUsbMounter-2.2.1-Setup.exe` / `-Portable.zip`; reflection: FsKind lists no Reiser
kind, a ReiserFS magic at 64 KiB now probes as nothing; `sh -n` on the script OK. Script not run in WSL (the lines removed
were no-ops on 6.18). CLAUDE.md updated.
**Then (committed and pushed): main window title shows the version**, "Linux USB Mounter (2.2.1)" (user request; MainForm
BuildUi, assembly version to 3 parts). Clean Release build; not seen on screen yet (the app needs elevation). Version stays 2.2.1.

_**Next: ① (done: committed, pushed)** **② run the 2.2.0 setup over the 2.0.0 install** (Update; Linux check says
Tumbleweed is ready) **③ uninstall test** with a drive mounted (and, on a test PC, with a setup-installed distro) **④ the
earlier run list**._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** the uninstaller offers to remove the WSL distro setup installed (user request 2026-09-26), version 2.2.0.

> ### 2. STATE
> **Committed and pushed** on `main` (previous commit `c676eba`). Clean `dotnet build -c Release`: 0 warnings, 0 errors.
> **User's machine:** 2.0.0 installed by setup in `C:\Apps\Linux_USB` (Start menu shortcut, logon task there). WSL 2.7.14,
> distros openSUSE-Tumbleweed (default, ready) and kali-linux (test, ready). Tumbleweed's util-linux was upgraded
> 2.42.2 → 2.42.3 by a test of the first package-step version (normal repo update); since then only missing packages.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.

> ### 3. FILES THIS TURN
> `installer/Setup.cs` (Installation.AddSetupDistro / SetupDistros, LinuxSetup.InstalledNames / RemoveDistro, Uninstaller
> step 2b, UninstallForm "What to remove") · `installer/setup.manifest`, `app.manifest`, `LinuxUsbMounter.csproj` (2.2.0) ·
> `README.md` · `CLAUDE.md` · `STATUS.md` · this file. (2.1.0 before it: the Linux check; see STATUS.md.)

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
