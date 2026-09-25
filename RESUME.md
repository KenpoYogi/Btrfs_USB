# ▶ START HERE — Linux USB Mounter resume

**⭐ LAST COMMIT (2026-09-25): THE WSL VM IS NO LONGER BOOTED WHEN NO DRIVE CAN BE MOUNTED** (user saw fans spike with the
app at 0 % CPU: the VM boot costs ~5 host + ~2 guest cores for 2-3 s and Task Manager doesn't show guest time). Builds clean.
On top of `e2b0d11` (rename to Linux USB Mounter, pushed). **Not run yet.**

_**Next: ① (done: committed, pushed)** **② run the app elevated** with no Linux drive plugged in: the log shows
"Filesystem support check skipped" and no `wsl#3 ... sh -c` call, and `vmmemWSL` doesn't appear; then plug in the btrfs
drive: support check + mount as before. **③ UFS disk + earlier run list** (see ⑤)._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4
> detect-only. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** don't start the WSL VM when there is nothing to mount (fan spikes, user report 2026-09-25).

> ### 2. STATE
> **Committed and pushed** on `main` (previous commit `e2b0d11`). Clean `dotnet build -c Release`: 0 warnings, 0 errors.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.
> **Test rig (Tumbleweed, /var/tmp/fbsdvm):** FreeBSD 15.1 BASIC-CI image + `qemu-x86` + `expect`; scripts in the session
> scratchpad (not kept). FreeBSD sources for reference in /var/tmp/fbsd. Both are disposable.

> ### 3. FILES THIS TURN
> `src/UI/MainForm.cs` (RequestScan) · `src/Program.cs` (CLI) · `CLAUDE.md` · `STATUS.md` · this file.

> ### 4. WHAT CHANGED
> · **Cause found:** the first scan's support check (`wsl -d ... sh -c`) booted the VM even with no Linux drive attached.
>   UI timers, the 30 s MSFT_Disk check (~35 ms), the idle VM and the keep-alive cost nothing measurable.
> · **Fix:** scan first; the support check runs only when a volume is mountable. Same for the CLI. Mount checks support itself.
> · Before: `e2b0d11` rename to Linux USB Mounter; `159c44d` ReiserFS / Reiser4 out of the docs.

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
