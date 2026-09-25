# Btrfs USB Mounter with Kali Linux

Kali is a Debian-based distro for security testing. The WSL image is minimal, but it installs
everything Btrfs USB Mounter needs. With **Tools > Build filesystem drivers** it opens as many
filesystems as openSUSE Tumbleweed. Kali is tested with the app, together with Tumbleweed.

| What you want to open | Works on Kali? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives) | After **Tools > Build filesystem drivers**: read-only, or read/write with **Tools > Allow APFS writes** (experimental) |
| JFS, HFS+ (Mac), ZFS | Yes, after **Tools > Build filesystem drivers** (see [Extra drivers](#optional-extra-drivers-jfs-hfs-ufs-zfs-apfs)) |
| UFS1, UFS2 (FreeBSD, NetBSD, OpenBSD) | After **Tools > Build filesystem drivers**: read-only, or read/write with **Tools > Allow UFS writes** (experimental) |
| ReiserFS, Reiser4 | No (detected only) |

**Don't install `libfsapfs-utils` on Kali.** Its `fsapfsmount` exists but only prints *"No sub system
to mount APFS format"* (the Kali build has no FUSE support), and because the command is there the app
would try it instead of the APFS driver. Mac drives work with the built APFS driver instead. Online
guides that build `apfs-fuse` from source don't help either: Btrfs USB Mounter doesn't use it.

**Kali's `zfs-dkms` and `apfs-dkms` packages don't work in WSL.** DKMS compiles a driver against the
headers of the running kernel, and Microsoft's WSL kernel has no headers package, so they build
nothing WSL can load. **Tools > Build filesystem drivers** builds the same drivers against the WSL
kernel's own source instead. Kali's `zfsutils-linux` (the `zpool` tool) is used; the build installs it.

WSL install name: **`kali-linux`**

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

## Step 2: Install Kali Linux

In the administrator terminal:

```powershell
wsl --install -d kali-linux
```

A window opens and asks for a **user name and password** for Linux. They are separate from your
Windows account; choose anything and remember the password (nothing shows while you type it).

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version kali-linux 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo`:

```powershell
wsl -d kali-linux -u root
```

The prompt now ends with `#`. Update the system first (Kali is a rolling release, so this can take a
while), then install the packages:

```sh
apt update
apt full-upgrade -y
apt install -y btrfs-progs util-linux kmod
```

| Package | Why |
|---|---|
| `btrfs-progs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

Kali's usual security tools (`kali-linux-default`) are not needed for Btrfs USB Mounter.

### Optional: extra drivers (JFS, HFS+, UFS, ZFS, APFS)

The WSL kernel from Microsoft has no drivers for these, so Btrfs USB Mounter compiles them for you
with Kali's own compiler (`gcc-13`) and the WSL kernel's source code:

1. In Btrfs USB Mounter pick **kali-linux** in the **WSL2 distro** box, then choose **Tools > Build
   filesystem drivers**.
2. Wait. The first run takes 20-40 minutes and needs about 5 GB of free space in Kali. It installs the
   build tools and `zfsutils-linux` with apt, and downloads the WSL kernel source (about 250 MB),
   OpenZFS and linux-apfs-rw.
3. The log ends with *Filesystem drivers built and installed for this WSL kernel.*

The ZFS tools come from Kali's *contrib* section, which the Kali WSL image turns on by default. The
drivers survive WSL and Windows restarts: they are kept on Kali's disk and the app puts them back
when needed. **Run the build again after every `wsl --update`**: a new WSL kernel needs its own build.

Optional check tools for these filesystems: `apt install -y jfsutils apfsprogs` (`fsck.jfs`,
`apfsck`).

**UFS drives** (FreeBSD, NetBSD, OpenBSD) open read-only with the built driver. To write to them, tick
**Tools > Allow UFS writes (experimental)**; the [README](../../README.md#ufs) explains what that
changes on FreeBSD drives. Linux has no UFS check tool (Debian dropped `ufsutils`): check UFS drives
with `fsck_ffs` on FreeBSD. Kali's `makefs` package can make UFS1 test images; its UFS2 images put the
superblock where neither Linux nor FreeBSD finds it.

Type `exit` to leave the root shell. Tip: to hide Kali's welcome message in your own user's shell,
run `touch ~/.hushlogin` there.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d kali-linux -u root`):

```sh
for c in btrfs blkid modinfo zpool; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `zpool` appears after the driver build (ZFS only).

## Step 5: Use it with Btrfs USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `BtrfsUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **kali-linux**. To make it the default for everything,
   run `wsl --set-default kali-linux` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > kali-linux > mnt > wsl > *drive label***, or at
   `\\wsl.localhost\kali-linux\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\BtrfsUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version kali-linux 2` |
| Mac drive fails with "No sub system to mount APFS format" | Remove the broken package: `apt remove -y libfsapfs-utils`, then click **Refresh**. The app then uses the built APFS driver |
| `apt update` fails with a signature (key) error | Kali's archive key changed: follow the key update steps on [kali.org](https://www.kali.org/docs/), then retry |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfs-progs` |
| Status says *Needs tools* (Mac or ZFS drives) | Run **Tools > Build filesystem drivers** (it builds the APFS driver and installs `zfsutils-linux`), then click **Refresh** |
| Status says *No driver* | Run **Tools > Build filesystem drivers** (again after a `wsl --update`) |
| A UFS drive stays *Ready (read-only)* | Tick **Tools > Allow UFS writes**, after building the drivers. Solaris UFS, and drives that were not cleanly unmounted, always open read-only (the log says which): run `fsck_ffs` on the BSD system and eject it there |
| `apt install zfs-dkms` or `apfs-dkms` builds no driver, or a guide asks for `linux-headers-$(uname -r)` | Expected in WSL: there are no headers for the WSL kernel, so DKMS builds nothing and the headers package doesn't exist. `apt remove zfs-dkms apfs-dkms`, then use **Tools > Build filesystem drivers** |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

## Removing Kali

This deletes Kali **and every file inside it**, including the built drivers and the kernel source
(about 5 GB). Your USB drives and other WSL distros are not touched.

1. In Btrfs USB Mounter, **Eject** every drive that is mounted with Kali, then pick another distro in
   the **WSL2 distro** box (or close the app).
2. In a terminal (no administrator rights needed):

   ```powershell
   wsl --unregister kali-linux
   wsl -l -v
   ```

   `kali-linux` is no longer in the list.
3. If Kali was your default distro, pick a new one, for example
   `wsl --set-default openSUSE-Tumbleweed`. The `*` in `wsl -l -v` marks the default.
4. Only if you installed Kali from the Microsoft Store app (not with `wsl --install`): also remove
   **Kali Linux** under **Settings > Apps > Installed apps**.

To start over, install it again with `wsl --install -d kali-linux`.

Other distros: [overview](README.md).
