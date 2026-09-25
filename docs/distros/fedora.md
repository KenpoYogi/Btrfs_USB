# Btrfs USB Mounter with Fedora

Fedora uses btrfs itself, so btrfs drives are well supported. Fedora has no package with the
`fsapfsmount` tool, so Mac (APFS) drives don't work here.

| What you want to open | Works on Fedora? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | No (`fsapfsmount` is not packaged for Fedora) |
| JFS, HFS+ (Mac), UFS (BSD), ZFS, APFS read/write | No; use [openSUSE Tumbleweed](opensuse-tumbleweed.md) |
| ReiserFS, Reiser4 | No (detected only) |

**Tools > Build filesystem drivers** needs a distro with zypper or apt (openSUSE, SLES, Debian,
Kali, Ubuntu). For Mac drives or the extra filesystems, install openSUSE Tumbleweed or Kali next to Fedora
and pick it in the app. Fedora's `apfs-fuse` package is a different
tool that Btrfs USB Mounter does not use.

WSL install names: **`FedoraLinux-44`** (used below) or **`FedoraLinux-43`**

---

## Step 1: Turn on WSL2 (once per PC)

Skip this step if you already use WSL, but still run `wsl --update`.

1. **Check that virtualization is on.** Press **Ctrl+Shift+Esc** to open Task Manager, click
   **Performance**, then **CPU**. The panel should say **Virtualization: Enabled**. If it says
   *Disabled*, turn on *Intel VT-x* / *AMD-V (SVM)* in your PC's BIOS/UEFI setup (see your PC maker's
   manual), then come back.
2. **Open an administrator terminal.** Right-click the **Start** button and choose **Terminal (Admin)**
   (on Windows 10: **Windows PowerShell (Admin)**). Click **Yes** when Windows asks.
3. **Install WSL:**

   ```powershell
   wsl --install --no-distribution
   ```

4. **Restart the PC.**
5. **Update WSL and make version 2 the default.** Open an administrator terminal again and run:

   ```powershell
   wsl --update
   wsl --set-default-version 2
   wsl --version
   ```

   `wsl --version` should print a *WSL version* of 2.x or later. If it says the command is unknown,
   your WSL is too old: run `wsl --update` again.

You need Windows 11, or Windows 10 version 22H2 with the current WSL from `wsl --update`.

## Step 2: Install Fedora

In the administrator terminal:

```powershell
wsl --install -d FedoraLinux-44
```

A window opens and asks for a Linux **user name** (no password). Fedora lets that user run `sudo`
without a password.

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version FedoraLinux-44 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo`:

```powershell
wsl -d FedoraLinux-44 -u root
```

The prompt now ends with `#`. Update the system first, then install the packages:

```sh
dnf upgrade -y
dnf install -y btrfs-progs util-linux kmod
```

| Package | Why |
|---|---|
| `btrfs-progs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (`util-linux-core` is usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (usually already installed) |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

Type `exit` to leave the root shell.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d FedoraLinux-44 -u root`):

```sh
for c in btrfs blkid modinfo; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

All three must say **OK**.

## Step 5: Use it with Btrfs USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `BtrfsUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **FedoraLinux-44**. To make it the default for
   everything, run `wsl --set-default FedoraLinux-44` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > FedoraLinux-44 > mnt > wsl > *drive label***, or at
   `\\wsl.localhost\FedoraLinux-44\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\BtrfsUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version FedoraLinux-44 2` |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfs-progs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac and ZFS drives need another distro: openSUSE Tumbleweed does both |
| Status says *No driver* | That filesystem needs the extra drivers, which need a zypper or apt distro: see the [Tumbleweed](opensuse-tumbleweed.md) or [Kali](kali.md) guide |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister FedoraLinux-44`. Your USB drives
are not touched, but eject them first.

Other distros: [overview](README.md).
