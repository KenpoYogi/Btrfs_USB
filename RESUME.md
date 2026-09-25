# ▶ START HERE — Btrfs USB Mounter resume

**⭐ UNCOMMITTED (2026-09-25): UFS1 / UFS2 SUPPORT — DETECTED, MOUNTED READ-ONLY, READ/WRITE OPT-IN (TOOLS > ALLOW UFS WRITES)
WITH A PATCHED UFS DRIVER THAT KEEPS FREEBSD CONSISTENT.** Builds clean (0 warnings). On top of `369d3ff` (pushed). Driver and
disk-image tests done in Tumbleweed and Kali, checked by FreeBSD 15.1's own fsck. **The app itself has not mounted a UFS disk.**

_**Next: ① commit (user's call)**, then **② run the app elevated** (user): after Tools > Build filesystem drivers the
status check logs "UFS writable yes"; a UFS disk shows *Ready (read-only)*, and *Ready* after Tools > Allow UFS writes;
mount / Explorer / eject. **③ Earlier run list** (splitter check, real btrfs hardware; see ⑤)._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac/BSD-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS, UFS through modules built by
> `tools/build-wsl-modules.sh`; APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4
> detect-only. Safe eject. Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** UFS and UFS2, read/write if possible, all distro docs updated (user request, 2026-09-25).

> ### 2. STATE
> **Uncommitted** on `main` (last commit `369d3ff`, pushed). `dotnet build -c Release` succeeded.
> **Built modules:** Tumbleweed and Kali both have the final `ufs.ko` (`wsl_handoff=1`) in `/var/lib/wsl-modules/6.18.33.2-...`.
> **Test rig (Tumbleweed, /var/tmp/fbsdvm):** FreeBSD 15.1 BASIC-CI image + `qemu-x86` + `expect`; scripts in the session
> scratchpad (not kept). FreeBSD sources for reference in /var/tmp/fbsd. Both are disposable.

> ### 3. FILES THIS TURN
> `src/Core/FileSystems.cs` (FsKind.Ufs, `FsProbe.Ufs`, FsInfo.KernelOptions / WriteNote, `FsSupport.CanWriteUfs`) ·
> `src/Core/MountManager.cs` (UFS options, WillBeReadOnly, post-mount ro check, `LogKernelMessagesAsync`) · `src/Core/Models.cs`
> (UfsWrite, FsOptions, FsWriteNote) · `src/Core/StateStore.cs` · `src/UI/MainForm.cs` (Allow UFS writes) · `src/Program.cs` ·
> `tools/build-wsl-modules.sh` (ufs, `ufs_handoff`) · `README.md` · `docs/distros/*.md` (all 10) · `CLAUDE.md` · `STATUS.md` · this file.

> ### 4. WHAT CHANGED
> · **Detection:** UFS2 superblock at 64 KiB, UFS1 at 8 KiB, either byte order; label, blkid-style UUID, size, free space.
>   `ufstype` chosen from the superblock (ufs2, 44bsd; sun/sunx86 read-only). Not clean / needs fsck / gjournal → read-only + note.
> · **Mount:** `wsl --mount --type ufs --options [ro,]ufstype=X`. Read/write needs the setting AND the locally built driver.
>   Any kernel mount asked rw is checked in /proc/mounts afterwards; a read-only fallback is recorded and logged with dmesg.
> · **Driver patch** (copy of fs/ufs): fs_clean 0 while rw / 1 after clean unmount; FS_METACKHASH cleared (FreeBSD then drops
>   its check hashes; fsck_ffs re-adds them); fs_mtime set with SU+J (stale journal never replayed); rw only if fs_clean == 1.
> · **Docs:** README UFS section with the FreeBSD test evidence and limits; every distro guide has a UFS row, a UFS paragraph
>   in its driver section and a troubleshooting row; Fedora/CentOS/Arch list UFS under "No".

> ### 5. ⭐ THE RUN LIST
> **① Commit** (ask the user). **② The app with a UFS disk**, elevated (above); ideally a real FreeBSD USB disk, then
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
> **Yours:** the commit decision; run list ② – ⑦ (elevated session, physical drives).
> **Mine:** fixes from ②; this entry's hash in STATUS.md at the next change; CLAUDE.md "Verification done before
> handover" still says the GUI has not been run (the build has, and the user ran `a0d162d`).

> **TRAIL: [STATUS.md](STATUS.md)** · **PROJECT RULES: [CLAUDE.md](CLAUDE.md)** · **USER DOCS: [README.md](README.md)**,
> **[docs/distros/](docs/distros/README.md)**
