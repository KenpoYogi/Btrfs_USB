# ▶ START HERE — Xnix USB Mounter resume

**⭐ LAST COMMIT (2026-09-25): REISERFS AND REISER4 REMOVED FROM ALL USER DOCUMENTATION** (README, distro guides,
`--help`, Tools menu, build dialog). Builds clean. On top of `29f15c5` (rename to Xnix USB Mounter, pushed). Detection code and
the build script's pre-6.13 reiserfs path are unchanged. **Not run yet.**

_**Next: ① (done: committed, pushed)** **② run the app elevated** (user): starts as "Xnix USB Mounter", reads the existing
state.json, re-points the logon task; `XnixUsbMounter --list` from a terminal. Delete the stale BtrfsUsbMounter.* files in
bin\ and in any install folder. **③ UFS disk + earlier run list** (see ⑤)._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4
> detect-only. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** no mention of ReiserFS / Reiser4 in any user documentation (user request, 2026-09-25).

> ### 2. STATE
> **Committed and pushed** on `main` (previous commit `29f15c5`). Clean `dotnet build -c Release`: 0 warnings, 0 errors.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.
> **Test rig (Tumbleweed, /var/tmp/fbsdvm):** FreeBSD 15.1 BASIC-CI image + `qemu-x86` + `expect`; scripts in the session
> scratchpad (not kept). FreeBSD sources for reference in /var/tmp/fbsd. Both are disposable.

> ### 3. FILES THIS TURN
> `README.md` · `docs/distros/*.md` (10) · `src/Program.cs` (--help) · `src/UI/MainForm.cs` (menu, build dialog) · `CLAUDE.md` ·
> `STATUS.md` · this file.

> ### 4. WHAT CHANGED
> · **Docs and UI texts:** ReiserFS / Reiser4 rows, paragraphs and list entries removed; `--help` says UFS/UFS2.
> · **Kept in code:** FsProbe still detects both (with a "can't mount" note); the build script still has its reiserfs path.
> · Before: `29f15c5` rename to Xnix USB Mounter (old name kept for data folder, mutex, logon task); `a874ec1` empty-list text.

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
> **Yours:** run list ② – ⑦ (elevated session, physical drives).
> **Mine:** fixes from ②; this commit's hash in STATUS.md at the next change; CLAUDE.md "Verification done before
> handover" still says the GUI has not been run (the build has, and the user ran `a0d162d`).

> **TRAIL: [STATUS.md](STATUS.md)** · **PROJECT RULES: [CLAUDE.md](CLAUDE.md)** · **USER DOCS: [README.md](README.md)**,
> **[docs/distros/](docs/distros/README.md)**
