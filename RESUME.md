# ▶ START HERE — Xnix USB Mounter resume

**⭐ LAST COMMIT (2026-09-25): RENAMED TO XNIX USB MOUNTER — OUTPUT FILES XnixUsbMounter.exe / .exe.config / .com.**
Builds clean (0 warnings). On top of `a874ec1` (pushed). Data folder, mutex / window message and logon task keep the old
`BtrfsUsbMounter` name on purpose (see CLAUDE.md "Name"). **Not run yet.**

_**Next: ① (done: committed, pushed)** **② run the app elevated** (user): starts as "Xnix USB Mounter", reads the existing
state.json, re-points the logon task (log: "Updated the logon task..."); `XnixUsbMounter --list` from a terminal.
Delete the stale BtrfsUsbMounter.* files in bin\ and in any install folder. **③ UFS disk + earlier run list** (see ⑤).
**Open:** `--help` in `src/Program.cs` still lists ReiserFS and Reiser4 (asked the user)._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4
> detect-only. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** rename the output files (and the product) to XnixUsbMounter / Xnix USB Mounter (user request, 2026-09-25).

> ### 2. STATE
> **Committed and pushed** on `main` (previous commit `a874ec1`). Clean `dotnet build -c Release`: 0 warnings, 0 errors.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.
> **Test rig (Tumbleweed, /var/tmp/fbsdvm):** FreeBSD 15.1 BASIC-CI image + `qemu-x86` + `expect`; scripts in the session
> scratchpad (not kept). FreeBSD sources for reference in /var/tmp/fbsd. Both are disposable.

> ### 3. FILES THIS TURN
> `XnixUsbMounter.csproj` (renamed) · every `src/**/*.cs` · `launcher/Launcher.cs` · `app.manifest` · `.vscode/*.json` · `LICENSE` ·
> `tools/build-wsl-modules.sh` (comments) · `README.md` · `docs/distros/*.md` · `CLAUDE.md` · `STATUS.md` · this file.

> ### 4. WHAT CHANGED
> · **Name:** assembly, namespaces, project file, display name, SPDX headers, docs → XnixUsbMounter / Xnix USB Mounter.
> · **Kept old name:** `%LOCALAPPDATA%\BtrfsUsbMounter`, `Local\BtrfsUsbMounter.GUI` / `.Ack`, `BtrfsUsbMounter.ShowWindow`,
>   logon task `BtrfsUsbMounter`, the `BtrfsUsbMounter.ps1` check. README has an "Upgrading from BtrfsUsbMounter.exe" section.
> · Before: `a874ec1` empty drive list text; `238c76f` UFS1 / UFS2 support.

> ### 5. ⭐ THE RUN LIST
> **① (done)** Run it elevated (above). **② The app with a UFS disk**, elevated (above); ideally a real FreeBSD USB disk, then
> `fsck_ffs` on FreeBSD. **③ The splitter check** from `369d3ff` (drag both ways, height remembered, X → tray, Close prompts).
> **④ Real hardware:** the 2 TB Seagate (btrfs `ExtDrive`) mount / eject / cancel eject / unplug during eject. **⑤ A real
> `wsl --shutdown`** with a drive mounted, then remount. **⑥ A non-btrfs USB disk** end to end. **⑦ Leap / SLES driver build.**

> ### 6. ⛔ CLOSED / DO NOT RETRY
> · ⛔ **UFS rw with the stock Linux driver** — it leaves fs_clean 1 while mounted, keeps FreeBSD check hashes stale
>   (EINTEGRITY on FreeBSD) and accepts NetBSD's in-use value 2; always require `wsl_handoff`.
> · ⛔ **Debian's makefs for UFS2 fixtures** — superblock at 8 KiB, unreadable by Linux and FreeBSD; use newfs in the FreeBSD VM.
> · ⛔ **Test scripts that skip the module restore** — WSL idle-restarts between calls and /lib/modules/<rel>/extra is empty.
> · ⛔ **`--` inside an XML comment** (app.manifest, csproj, App.config): side-by-side configuration error.
> · ⛔ **Building modules with a config other than /proc/config.gz plus the added drivers**; ⛔ **unpinned toolchain probes**.
> · ⛔ **Trusting `/lib/modules/<release>` to keep built modules** — keep them in `/var/lib/wsl-modules/<release>`.
> · ⛔ **DKMS packages in WSL**; ⛔ **`zpool import -f`**; ⛔ **ZFS file vdevs inside the distro**.
> · ⛔ **Long build output through wsl.exe stdout**; ⛔ **editing the build script while it runs** — run a copy.
> · ⛔ **A 32-bit process** (Prefer32Bit); ⛔ **a `btrfs check --repair` feature**.

> ### 7. OWED
> **Yours:** the `--help` question; run list ② – ⑦ (elevated session, physical drives).
> **Mine:** fixes from ②; this commit's hash in STATUS.md at the next change; CLAUDE.md "Verification done before
> handover" still says the GUI has not been run (the build has, and the user ran `a0d162d`).

> **TRAIL: [STATUS.md](STATUS.md)** · **PROJECT RULES: [CLAUDE.md](CLAUDE.md)** · **USER DOCS: [README.md](README.md)**,
> **[docs/distros/](docs/distros/README.md)**
