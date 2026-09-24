# Btrfs USB Mounter - project context

Standalone Windows desktop tool (NOT a Cimatron plugin; no Cimatron/ACIS references).
Mounts btrfs-formatted USB drives through WSL2 (`wsl --mount --type btrfs`), because the
WinBtrfs driver is blocked by the Windows "Cross Certificates for Code Integrity Exceptions"
policy (event 3077, policy 8f9cb695-5d48-48d6-a329-7202b44607e3).

## Constraints
- .NET Framework 4.8, C# 7.3 (`<LangVersion>7.3</LangVersion>`), WinForms, UI built in code (no designer files)
- No NuGet packages. Framework assemblies only (System.Management, System.Runtime.Serialization, ...)
- SDK-style csproj, built with `dotnet build`; needs the .NET Framework 4.8 Developer Pack
- `requireAdministrator` manifest: running or debugging needs an elevated VS Code
- AnyCPU, Prefer32Bit=false (a 32-bit process would be redirected away from the real wsl.exe)

## Commands
- Build: `dotnet build -c Release` -> `bin\Release\net48\BtrfsUsbMounter.exe` (+ `.exe.config`)
- CLI check without the GUI: `BtrfsUsbMounter.exe --list` (elevated terminal)
- Logs: `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log`; state: `state.json` in the same folder

## Environment (developer machine)
- WSL2 distro: openSUSE-Tumbleweed (default, only distro), user `chippy`
- btrfsprogs and util-linux installed in the distro
- Test drive: 2 TB Seagate ST32000641AS in a USB enclosure, disk 2, GPT, btrfs partition 1,
  label `ExtDrive`, mounted at `/mnt/wsl/ExtDrive`, top folder owned by chippy

## Architecture
- `src/Core`: no UI dependencies. `Engine` wires StateStore, SuperblockCache, MountManager,
  VolumeScanner, Maintenance. `JobQueue` runs one background task at a time
  (flags: Mutating, Quiet, Cancellable, Unique) and raises `Changed` on the UI thread
- `src/UI`: MainForm (tray, owner-drawn Space column, WM_DEVICECHANGE in WndProc, tools menu),
  DriveInfoForm (usage bar, allocation, error counters, scrub)
- `src/Program.cs`: single instance (mutex `Local\BtrfsUsbMounter.GUI`, shared with the old
  PowerShell version), show-window broadcast + ack event, CLI mode

## Rules learned the hard way (keep these)
- Never block the UI thread: all wsl.exe, WMI and raw disk I/O go through JobQueue
- Every wsl.exe call needs a timeout AND must honour cancellation
- Eject = `sync -f /mnt/wsl/<name>` (this filesystem only) streamed with progress; cancelling
  must leave the drive mounted. If the disk is already gone, skip the flush and just release it
- Keep-alive process (`wsl -u root --exec sleep infinity`) while anything is mounted, or WSL's
  idle shutdown silently detaches the disk
- Superblock reads are cached per partition so rescans don't spin up sleeping drives
- A hidden/hung instance must never silently block new launches
- No `btrfs check --repair` feature by design
- state.json must stay compatible with the PowerShell version's field names

## Verification done before handover
Compiles against the 4.8 reference assemblies; 32 core tests passed under Mono (superblock
parser on a real btrfs image, btrfs output parsers, argument quoting, PowerShell-format
state.json, flush command, cancel/timeout, job queue). The GUI and `dotnet build` itself have
not yet been run on Windows - do that first.
