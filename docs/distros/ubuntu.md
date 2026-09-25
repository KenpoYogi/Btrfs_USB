# Xnix USB Mounter with Ubuntu

Ubuntu is the most common WSL distro and a good choice for btrfs, ext and XFS drives.

**Pick the version by what you need:**

- **`Ubuntu-24.04`** if you want to open **Mac (APFS) drives**. Its `libfsapfs-utils` package works.
- **`Ubuntu`** (currently 26.04) or **`Ubuntu-26.04`** otherwise. On 26.04 the `libfsapfs-utils`
  package is built without FUSE support, so `fsapfsmount` cannot mount anything. Online guides for
  26.04 build `apfs-fuse` from source instead; Xnix USB Mounter doesn't use `apfs-fuse` (yet). On
  26.04 the APFS kernel driver from **Tools > Build filesystem drivers** reads Mac drives instead.

| What you want to open | Ubuntu 24.04 | Ubuntu 26.04 |
|---|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs-utils` | Only with the built APFS driver (package lacks FUSE) |
| JFS, HFS+ (Mac), APFS read/write | Should work, not tested (see below) | Should work, not tested |
| UFS1, UFS2 (FreeBSD, NetBSD, OpenBSD) | Should work, not tested: read-only, or read/write with **Tools > Allow UFS writes** (experimental) | Should work, not tested |
| ZFS | No: Ubuntu's ZFS 2.2.2 is too old for the WSL kernel | Should work, not tested |
| ReiserFS, Reiser4 | No (detected only) | No |

**Extra drivers.** **Tools > Build filesystem drivers** compiles JFS, HFS+, UFS, ZFS and APFS drivers for
the WSL kernel with apt and Ubuntu's `gcc-13`. It is tested on Kali (Debian-based, same apt route),
not yet on Ubuntu. For ZFS it builds the OpenZFS version of Ubuntu's `zfsutils-linux`: 2.4.1 on 26.04
works with the current WSL kernel (6.18), but 2.2.2 on 24.04 only supports Linux up to 6.6, so on
24.04 the build skips ZFS and builds the others. Ubuntu's own `zfs-dkms` and `apfs-dkms` packages
don't help: DKMS compiles the driver against the headers of the running kernel, and Microsoft's WSL
kernel has no headers package. If the build fails on Ubuntu, please report it with `mounter.log`;
[Kali](kali.md) or [openSUSE Tumbleweed](opensuse-tumbleweed.md) next to Ubuntu are tested
alternatives.

**UFS drives** (FreeBSD, NetBSD, OpenBSD) open read-only with the built driver. To write to them, tick
**Tools > Allow UFS writes (experimental)**; the [README](../../README.md#ufs) explains what that
changes on FreeBSD drives. Linux has no UFS check tool: check UFS drives with `fsck_ffs` on FreeBSD.

This guide uses **`Ubuntu-24.04`**. For another version, replace the name everywhere.

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

## Step 2: Install Ubuntu

In the administrator terminal:

```powershell
wsl --install -d Ubuntu-24.04
```

A window opens and asks for a **user name and password** for Linux. They are separate from your
Windows account; choose anything and remember the password (nothing shows while you type it).

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version Ubuntu-24.04 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo`:

```powershell
wsl -d Ubuntu-24.04 -u root
```

The prompt now ends with `#`. Update the system first, then install the packages:

```sh
apt update
apt upgrade -y
apt install -y btrfs-progs util-linux kmod libfsapfs-utils
```

On Ubuntu 26.04 leave out `libfsapfs-utils` (it doesn't work there).

| Package | Why |
|---|---|
| `btrfs-progs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (usually already installed) |
| `libfsapfs-utils` | Optional, 24.04 only: read-only access to Mac (APFS) drives with `fsapfsmount` (from the *universe* repository, which Ubuntu turns on by default) |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

Type `exit` to leave the root shell.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d Ubuntu-24.04 -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` only matters for Mac drives.

## Step 5: Use it with Xnix USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `XnixUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **Ubuntu-24.04**. To make it the default for
   everything, run `wsl --set-default Ubuntu-24.04` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > Ubuntu-24.04 > mnt > wsl > *drive label***, or at
   `\\wsl.localhost\Ubuntu-24.04\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\XnixUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version Ubuntu-24.04 2` |
| `apt` says "Unable to locate package libfsapfs-utils" | Turn on *universe*: `add-apt-repository -y universe && apt update` |
| Mac drive fails with "No sub system to mount APFS format" | You are on Ubuntu 26.04, whose package lacks FUSE: `apt remove -y libfsapfs-utils`, build the APFS driver (**Tools > Build filesystem drivers**) and click **Refresh** |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfs-progs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac: on 24.04 `apt install -y libfsapfs-utils`, then click **Refresh**; on 26.04 build the APFS driver. ZFS: **Tools > Build filesystem drivers** (26.04; on 24.04 use Kali or openSUSE Tumbleweed) |
| Status says *No driver* | Run **Tools > Build filesystem drivers** in the app (again after a `wsl --update`) |
| A UFS drive stays *Ready (read-only)* | Tick **Tools > Allow UFS writes**, after building the drivers. Solaris UFS, and drives that were not cleanly unmounted, always open read-only (the log says which): run `fsck_ffs` on the BSD system and eject it there |
| `apt install zfs-dkms` or `apfs-dkms` builds no driver, or a guide asks for `linux-headers-$(uname -r)` | Expected in WSL: there are no headers for the WSL kernel, so DKMS builds nothing and the headers package doesn't exist. `apt remove zfs-dkms apfs-dkms`, then use **Tools > Build filesystem drivers** |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister Ubuntu-24.04`. Your USB drives
are not touched, but eject them first.

Other distros: [overview](README.md).
