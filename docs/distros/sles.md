# Btrfs USB Mounter with SUSE Linux Enterprise Server (SLES)

SLES is SUSE's commercial distro. It works well for btrfs, ext and XFS drives. Two things to know:

- **Registration.** Most SLES packages come from SUSE's servers and need a registration code: a paid
  subscription, or a free 60-day trial from [suse.com](https://www.suse.com/download/sles/).
  Without registration, SLES 15 SP7 can still install the required packages from the free *SLE_BCI*
  repository included with the WSL image. SLES 16.0 without registration is not verified.
- **No extra drivers.** **Tools > Build filesystem drivers** needs openSUSE. If you need JFS, HFS+,
  ZFS or APFS read/write, install [openSUSE Tumbleweed](opensuse-tumbleweed.md) next to SLES.

| What you want to open | Works on SLES? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs` from SUSE Package Hub (needs registration) |
| JFS, HFS+ (Mac), ZFS, APFS read/write | No; use openSUSE Tumbleweed |
| ReiserFS, Reiser4 | No (detected only) |

WSL install names: **`SUSE-Linux-Enterprise-16.0`** or **`SUSE-Linux-Enterprise-15-SP7`**

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

## Step 2: Install SLES

In the administrator terminal (the rest of this guide uses 16.0; for 15 SP7 replace the name with
`SUSE-Linux-Enterprise-15-SP7` everywhere):

```powershell
wsl --install -d SUSE-Linux-Enterprise-16.0
```

A window opens with the first-start wizard:

1. It asks for a **user name and password** for Linux. They are separate from your Windows account;
   choose anything and remember the password.
2. It asks for your **registration e-mail and code**. Enter them if you have them, or click **Skip**
   and register later (below).

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version SUSE-Linux-Enterprise-16.0 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo` or the
root password:

```powershell
wsl -d SUSE-Linux-Enterprise-16.0 -u root
```

The prompt now ends with `#`.

### Register (if you skipped it in the wizard)

```sh
SUSEConnect -r YOUR-REGISTRATION-CODE -e you@example.com
```

(Or run `wsl-config registration` for the same dialog as the first start.)

### Required packages

```sh
zypper refresh
zypper update -y
zypper install -y btrfsprogs util-linux kmod
```

| Package | Why |
|---|---|
| `btrfsprogs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (usually already installed) |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

On SLES 15 SP7 `btrfsprogs` is in the *Basesystem* module, which a registered system has by default.

### Optional: Mac (APFS) drives, read-only

`libfsapfs` comes from **SUSE Package Hub** (community packages, needs registration). Turn it on once,
then install:

```sh
SUSEConnect -p PackageHub/16.0/x86_64      # SLES 15 SP7: PackageHub/15.7/x86_64
zypper refresh
zypper install -y libfsapfs
```

Type `exit` to leave the root shell.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d SUSE-Linux-Enterprise-16.0 -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` only matters for Mac drives.

## Step 5: Use it with Btrfs USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `BtrfsUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **SUSE-Linux-Enterprise-16.0**. To make it the default
   for everything, run `wsl --set-default SUSE-Linux-Enterprise-16.0` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > SUSE-Linux-Enterprise-16.0 > mnt > wsl > *drive label***,
   or at `\\wsl.localhost\SUSE-Linux-Enterprise-16.0\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\BtrfsUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version SUSE-Linux-Enterprise-16.0 2` |
| `zypper` finds no packages, or "No repositories defined" | Register the system (Step 3), then `zypper refresh` |
| `libfsapfs` not found | Turn on Package Hub with `SUSEConnect -p ...` (Step 3) |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfsprogs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac: install `libfsapfs` from Package Hub (Step 3), then click **Refresh**. ZFS: use openSUSE Tumbleweed |
| Status says *No driver* | That filesystem needs the extra drivers, which need openSUSE: see the [Tumbleweed guide](opensuse-tumbleweed.md) |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister SUSE-Linux-Enterprise-16.0`.
Your USB drives are not touched, but eject them first. Deregister first (`SUSEConnect -d`) to free
the subscription.

Other distros: [overview](README.md).
