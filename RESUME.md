# ▶ START HERE — Btrfs USB Mounter resume

**⭐ COMMITTED AND PUSHED TO `origin/main` (2026-09-24): MAIN WINDOW LAYOUT — DRAGGABLE SPLITTER BETWEEN THE DRIVE LIST AND THE LOG, TITLE-BAR X
MINIMIZES TO THE TRAY, NEW "Close" BUTTON BOTTOM RIGHT THAT REALLY EXITS.** `dotnet build -c Release` succeeds, 0 warnings.
**Not yet run** — the GUI needs an elevated session. It is the commit after `876d770` (apt driver build, tested on Kali).

_**Next: ① run `bin\Release\net48\BtrfsUsbMounter.exe` elevated and check the three changes** (user): drag the splitter both
ways, restart and see the log height remembered; X, Alt+F4 and taskbar "Close window" go to the tray; Close with a drive
mounted asks Yes/No/Cancel, and Cancel then X still only minimizes; narrow the window to ~880 px and see the buttons wrap
to a second row. **② Fix what ① finds.** **③ The real-hardware run** (run list ③)._

> ### 1. GOAL
> **Enduring:** a standalone Windows tool that mounts Linux/Mac-formatted USB drives through WSL2 (`wsl --mount`): btrfs,
> ext2/3/4, XFS read/write with the stock kernel; JFS, HFS+, ZFS, APFS through modules built by `tools/build-wsl-modules.sh`;
> APFS read-only through fsapfsmount otherwise; ReiserFS on kernels before 6.13; Reiser4 detect-only. Safe eject (flush this
> filesystem, then detach). Never blocks the UI thread; every wsl.exe call has a timeout and honours cancellation.
> **Now:** usability of the main window (user requests, 2026-09-24).

> ### 2. STATE
> **Committed:** the window-layout change, on top of `876d770`; pushed to `origin/main` (github.com/KenpoYogi/Btrfs_USB). Builds clean.
> **Build output:** `bin\Release\net48\` — `BtrfsUsbMounter.exe` (+ `.exe.config`), `BtrfsUsbMounter.com` for terminals,
> `LICENSE`, `tools\build-wsl-modules.sh`. Runs elevated only (`requireAdministrator`).
> **WSL kernel:** 6.18.33.2-microsoft-standard-WSL2. Drivers built and loading in openSUSE-Tumbleweed and kali-linux.
> **Verified:** driver build and loop-device tests on 6.18 in both distros (see CLAUDE.md). **Never verified:** the app
> mounting a real USB disk end to end, a real `wsl --shutdown` with drives mounted, the new window layout.

> ### 3. FILES THIS TURN
> `src/UI/MainForm.cs` (SplitContainer, button bar as Panel + wrapping FlowLayoutPanel + Close, `RequestExit`, `ApplySplitLayout`
> in `OnLoad`, X → minimize in `OnFormClosing`) · `src/Core/Models.cs` (`AppSettings.LogHeight`) · `RESUME.md`, `STATUS.md` (new) ·
> `CLAUDE.md` (status docs section).

> ### 4. WHAT CHANGED
> · **Splitter.** `SplitContainer`, horizontal, `FixedPanel = Panel2`: a window resize changes the drive list, the log keeps its
>   height. Panel1 = drive list + button bar, Panel2 = log. Minimums 140 / 60 px (96 dpi, scaled). Default log 170 px.
> · **Remembered log height.** Saved on a user drag only (`SplitterMoving` sets a flag, `SplitterMoved` saves), in 96-dpi pixels,
>   as `state.json` `Settings.LogHeight` (0 = default). A new field; older state files and the PowerShell version still load.
> · **X = minimize.** `CloseReason.UserClosing` without `exitRequested` → cancel + minimize (the Resize handler hides to the tray).
>   The first-hide balloon now says "right-click it > Exit to quit". Windows shutdown and `reallyExit` still close.
> · **Close button** and tray **Exit** both call `RequestExit()` → the old close path (running-job wait / force quit, mounted-drives
>   Yes/No/Cancel). Cancel clears `exitRequested`. The job hint reads "Click Close again to force quit".

> ### 5. ⭐ THE RUN LIST
> **① The layout change**, elevated (above). **② Fix what ① finds.** **③ Real hardware:** the 2 TB Seagate (disk 2, btrfs `ExtDrive`) —
> mount, Explorer, eject with progress, cancel an eject (must stay mounted), unplug during eject. **④ A real `wsl --shutdown`**
> with a drive mounted, then remount (built modules restored). **⑤ A non-btrfs USB disk** (ext4 or XFS) end to end.
> **⑥ Leap / SLES driver build** (documented, untested).

> ### 6. ⛔ CLOSED / DO NOT RETRY
> · ⛔ **`--` inside an XML comment** (app.manifest, csproj, App.config): the exe fails with a side-by-side configuration error.
> · ⛔ **Building modules with a config other than exactly /proc/config.gz plus the added `=m` drivers** — turning DEBUG_INFO_BTF off
>   changed 250 of 582 CRCs. ⛔ **Leaving toolchain probes to our gcc** — pin every probe that differs (CC_HAS_SANE_FUNCTION_ALIGNMENT
>   changed `_printk`, `panic`, `__fortify_panic`).
> · ⛔ **Trusting `/lib/modules/<release>` to keep built modules** — its upper layer lives in the VM; keep them in
>   `/var/lib/wsl-modules/<release>` and restore.
> · ⛔ **DKMS packages (zfs-dkms, apfs-dkms) in WSL** — no headers, no `/lib/modules/<rel>/build`. ⛔ **`zpool import -f`**.
> · ⛔ **ZFS file vdevs inside the distro** — opened from the VM's root namespace; use block devices.
> · ⛔ **Long build output relayed through wsl.exe stdout** — lines were lost; redirect to a file inside WSL. ⛔ **Editing the build
>   script while it runs** — run a copy.
> · ⛔ **A 32-bit process** (Prefer32Bit) — redirected away from the real wsl.exe. ⛔ **A `btrfs check --repair` feature** — by design.

> ### 7. OWED
> **Yours:** run list ① and ③–⑤ (they need an elevated session and the physical drive).
> **Mine:** fixes from ①; the layout commit's hash in STATUS.md at the next change; update CLAUDE.md's "Verification done before handover" once the GUI has been run (it still says the GUI and
> `dotnet build` have not been run on Windows — the build now has, 2026-09-24).

> **TRAIL: [STATUS.md](STATUS.md)** · **PROJECT RULES: [CLAUDE.md](CLAUDE.md)** · **USER DOCS: [README.md](README.md)**,
> **[docs/distros/](docs/distros/README.md)**
