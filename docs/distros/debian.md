# Xnix USB Mounter with Debian

Debian is a stable, conservative distro. It handles btrfs, ext and XFS drives, and on Debian 13
("trixie", the current stable release) also Mac (APFS) drives, read-only.

| What you want to open | Works on Debian 13? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs-utils` (not on Debian testing/unstable, see below) |
| JFS, HFS+ (Mac), ZFS, APFS read/write | Should work, not tested: **Tools > Build filesystem drivers** (tested on Kali, which is Debian-based) |
| UFS1, UFS2 (FreeBSD, NetBSD, OpenBSD) | Should work, not tested: after **Tools > Build filesystem drivers**, read-only, or read/write with **Tools > Allow UFS writes** (experimental) |

**ZFS and APFS driver packages.** Debian has them (checked September 2026; trixie: ZFS 2.3.9 in
*contrib*, 2.4.4 in backports, APFS 0.3.13): `zfs-dkms` and `zfsutils-linux` for ZFS, and `apfs-dkms`
(linux-apfs-rw) for APFS read/write. On their own they don't work in WSL, though. DKMS compiles the
driver against the headers of the running kernel, and Microsoft's WSL kernel has no headers package,
so installing `zfs-dkms` or `apfs-dkms` builds nothing WSL can load. `zfsutils-linux` still provides
`zpool`, which the app needs once a ZFS driver exists.

**Tools > Build filesystem drivers** builds these drivers against the WSL kernel's own source instead,
with apt and Debian's `gcc-13`. It is tested on Kali (Debian-based) and should work on Debian 13
the same way. For ZFS it installs `zfsutils-linux`, which is in Debian's *contrib* section: turn
*contrib* on first (see [Extra drivers](#optional-extra-drivers-jfs-hfs-ufs-zfs-apfs)). If the build
fails on Debian, please report it with `mounter.log`; [Kali](kali.md) or
[openSUSE Tumbleweed](opensuse-tumbleweed.md) next to Debian are tested alternatives.

WSL install name: **`Debian`**

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

## Step 2: Install Debian

In the administrator terminal:

```powershell
wsl --install -d Debian
```

A window opens and asks for a **user name and password** for Linux. They are separate from your
Windows account; choose anything and remember the password (nothing shows while you type it).

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version Debian 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo`:

```powershell
wsl -d Debian -u root
```

The prompt now ends with `#`. First see which Debian version you have:

```sh
grep VERSION= /etc/os-release
```

It should say `13 (trixie)`. Then update the system and install the packages:

```sh
apt update
apt upgrade -y
apt install -y btrfs-progs util-linux kmod libfsapfs-utils
```

| Package | Why |
|---|---|
| `btrfs-progs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers |
| `libfsapfs-utils` | Optional: read-only access to Mac (APFS) drives with `fsapfsmount` |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

**Debian testing or unstable** (the version line says *forky* or *sid*): leave out `libfsapfs-utils`.
The build there (20240429-2) has no FUSE support, so `fsapfsmount` can't mount anything (checked
September 2026: that package depends on no FUSE library, while the trixie one uses libfuse2). If you
have Debian 12 ("bookworm"), `libfsapfs-utils` is not verified with this app.

### Optional: extra drivers (JFS, HFS+, UFS, ZFS, APFS)

The WSL kernel from Microsoft has no drivers for these, so Xnix USB Mounter compiles them with
Debian's `gcc-13` and the WSL kernel's source code. **Tested on Kali, not yet on Debian itself.**

1. **For ZFS only: turn on *contrib*** (the ZFS tools live there). Add `contrib` after `main` in the
   file your Debian uses:

   ```sh
   ls /etc/apt/sources.list.d/debian.sources /etc/apt/sources.list 2>/dev/null
   # debian.sources (newer format):
   grep -q contrib /etc/apt/sources.list.d/debian.sources || sed -i 's/^Components: main/Components: main contrib/' /etc/apt/sources.list.d/debian.sources
   # sources.list (older format):
   grep -q contrib /etc/apt/sources.list || sed -i 's/ main$/ main contrib/' /etc/apt/sources.list
   apt update
   ```

2. In Xnix USB Mounter pick **Debian** in the **WSL2 distro** box, then choose **Tools > Build
   filesystem drivers**.
3. Wait. The first run takes 20-40 minutes and needs about 5 GB of free space in the distro.
4. The log ends with *Filesystem drivers built and installed for this WSL kernel.*

The drivers survive WSL and Windows restarts (the app puts them back when needed). **Run the build
again after every `wsl --update`**: a new WSL kernel needs its own build.

**UFS drives** (FreeBSD, NetBSD, OpenBSD) open read-only with the built driver. To write to them, tick
**Tools > Allow UFS writes (experimental)**; the [README](../../README.md#ufs) explains what that
changes on FreeBSD drives. Linux has no UFS check tool (Debian dropped `ufsutils`): check UFS drives
with `fsck_ffs` on FreeBSD.

Type `exit` to leave the root shell.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d Debian -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount zpool; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` (Mac drives) and `zpool` (ZFS, after
the driver build) only matter if you use those.

## Step 5: Use it with Xnix USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `XnixUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **Debian**. To make it the default for everything,
   run `wsl --set-default Debian` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > Debian > mnt > wsl > *drive label***, or at
   `\\wsl.localhost\Debian\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\XnixUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version Debian 2` |
| Mac drive fails with "No sub system to mount APFS format" | Your `libfsapfs-utils` lacks FUSE (testing/unstable): `apt remove -y libfsapfs-utils`, then build the APFS driver (**Tools > Build filesystem drivers**) and click **Refresh** |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfs-progs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac: `apt install -y libfsapfs-utils` (Debian 13), or build the drivers. ZFS: turn on *contrib*, then **Tools > Build filesystem drivers**. Then click **Refresh** |
| Status says *No driver* | Run **Tools > Build filesystem drivers** (see [Extra drivers](#optional-extra-drivers-jfs-hfs-ufs-zfs-apfs)) |
| A UFS drive stays *Ready (read-only)* | Tick **Tools > Allow UFS writes**, after building the drivers. Solaris UFS, and drives that were not cleanly unmounted, always open read-only (the log says which): run `fsck_ffs` on the BSD system and eject it there |
| `apt install zfs-dkms` or `apfs-dkms` builds no driver, or a guide asks for `linux-headers-$(uname -r)` | Expected in WSL: there are no headers for the WSL kernel, so DKMS builds nothing and the headers package doesn't exist. `apt remove zfs-dkms apfs-dkms`, then use **Tools > Build filesystem drivers** |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister Debian`. Your USB drives are not
touched, but eject them first.

Other distros: [overview](README.md).
