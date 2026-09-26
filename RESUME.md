# ▶ START HERE — Linux USB Mounter resume

**⭐ LAST COMMIT (2026-09-26): INSTALLER + UNINSTALLER, AND A PORTABLE ZIP.** Committed and pushed. The build now also makes
`bin\Release\LinuxUsbMounter-2.0.0-Setup.exe` (`installer/Setup.cs`, program embedded) and
`bin\Release\LinuxUsbMounter-2.0.0-Portable.zip`. App changes: quit message `LinuxUsbMounter.Quit`, and CLI output to a
pipe / file when redirected. Docs: README Install (installer OR portable), the 9 distro guides, CLAUDE.md. Builds clean.
Icon is now `assets/LinuxUsbMount_v3.ico` (drive + Tux; exe, .com, setup; README credits Larry Ewing); the Installed apps
entry has no Publisher (user's name removed). Don't commit the untracked v2 files (Google-sourced badge, unknown rights).
**Logic tested by reflection in a non-elevated session; a real elevated install / uninstall has NOT been run.**
Before it: `72f1f60` new app icon (v1, now replaced by v3).

_**Next: ① (done: committed, pushed)** **② elevated end-to-end test** (see ⑤ ①) **③ the earlier run list**._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4
> detect-only. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** easy install anywhere (default `C:\Program Files\Linux_USB`) and easy uninstall that closes the app and unmounts
> drives after warning the user; a portable zip as the no-install alternative (user requests 2026-09-26).

> ### 2. STATE
> **Committed and pushed** on `main` (previous commit `72f1f60`). Clean `dotnet build -c Release`: 0 warnings, 0 errors.
> The user has a hand-copied copy in `C:\Apps\Linux_USB`: the logon task starts it. Setup offers to close such a copy
> (it holds the mutex); the installed app re-points the task on its first start.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.

> ### 3. FILES THIS TURN
> new `installer/Setup.cs`, `installer/setup.manifest` · `LinuxUsbMounter.csproj` (BuildSetup, BuildPortableZip,
> `installer\**` not compiled into the app) · `src/Program.cs` (QuitMessage, redirected CLI output) · `src/UI/MainForm.cs`
> (quit handler) · `README.md` · `docs/distros/*.md` (9) · `CLAUDE.md` · `STATUS.md` · this file.

> ### 4. WHAT CHANGED
> · **Setup window:** folder (default `%ProgramFiles%\Linux_USB`, Browse adds `Linux_USB` under a non-empty folder), Start
>   menu / desktop shortcut, PATH, start when done, license link, WSL / .NET 4.8 warnings. Detects an installed copy:
>   Update (same or new folder; a move removes the old folder and re-points the logon task) or Uninstall...
> · **Closing the app:** quit message to its windows, 15 s, then Kill. Copies from other folders (Linux / Btrfs / Xnix
>   UsbMounter.exe) are closed only if the user says Yes.
> · **Uninstall** (Installed apps, `Uninstall.exe`, or setup): runs from a %TEMP% copy; warns if running / mounted;
>   closes the app; `--unmount-all` through a pipe (skipped if another copy runs elsewhere); asks before continuing with
>   drives still mounted; removes logon task (if it starts this copy), shortcuts, PATH, registry, its own files; settings
>   folder only if ticked; WSL-side drivers untouched.
> · **Portable zip:** the `net48` folder zipped; docs show a table installer vs portable.

> ### 5. ⭐ THE RUN LIST
> **① Elevated end-to-end:** run the setup → default folder, all options → Start menu entry, Installed apps entry,
> `LinuxUsbMounter --list` in a new terminal (PATH) → mount the btrfs drive → run the setup again (Update: app closes,
> drive stays mounted, app restarts) → Uninstall from Settings with the drive mounted (warning, flush, "safe to unplug",
> everything removed, `%TEMP%` copy gone) → with the `C:\Apps` copy running (other-copy prompt). Also a portable zip run.
> **② The app elevated** with no Linux drive (`57cc60c`: "Filesystem support check skipped", no VM boot) + new icon.
> **③ UFS disk**, **④ splitter check** (`369d3ff`), **⑤ real hardware** (Seagate mount / eject / cancel / unplug),
> **⑥ real `wsl --shutdown`**, **⑦ non-btrfs USB disk**, **⑧ Leap / SLES driver build.**

> ### 6. ⛔ CLOSED / DO NOT RETRY
> · ⛔ **Backslashes / `\u` through the Bash tool (sed, perl -e) or `\uXXXX` in Write/Edit** — mangled every time this session;
>   put replacements in a script file written with Write, and build characters with `char.ConvertFromUtf32` in C#.
> · ⛔ **`$(IntermediateOutputPath)` in a project-level property** — empty at evaluation (generated file lands in the root).
> · ⛔ **UFS rw with the stock Linux driver** — always require `wsl_handoff`. ⛔ **Debian's makefs for UFS2 fixtures.**
> · ⛔ **Test scripts that skip the module restore**; ⛔ **`--` inside an XML comment** (manifests, csproj, App.config).
> · ⛔ **Building modules with a config other than /proc/config.gz plus the added drivers**; ⛔ **unpinned toolchain probes**.
> · ⛔ **Trusting `/lib/modules/<release>` to keep built modules**; ⛔ **DKMS in WSL**; ⛔ **`zpool import -f`**; ⛔ **ZFS file vdevs**.
> · ⛔ **Long build output through wsl.exe stdout**; ⛔ **editing the build script while it runs**.
> · ⛔ **A 32-bit process** (Prefer32Bit); ⛔ **a `btrfs check --repair` feature**.

> ### 7. OWED
> **Yours:** run list ① – ⑧ (elevated session, physical drives).
> **Mine:** fixes from ①; the commit's hash in STATUS.md at the next change.

> **TRAIL: [STATUS.md](STATUS.md)** · **PROJECT RULES: [CLAUDE.md](CLAUDE.md)** · **USER DOCS: [README.md](README.md)**,
> **[docs/distros/](docs/distros/README.md)**
