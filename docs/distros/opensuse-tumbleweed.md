# Xnix USB Mounter with openSUSE Tumbleweed

**Recommended.** Tumbleweed is the distro Xnix USB Mounter is developed and tested on, and the only
one where every feature works, including the extra drivers built by **Tools > Build filesystem drivers**.

| What you want to open | Works on Tumbleweed? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs` |
| JFS, HFS+ (Mac), ZFS, APFS read/write | Yes, after **Tools > Build filesystem drivers** |
| UFS1, UFS2 (FreeBSD, NetBSD, OpenBSD) | After **Tools > Build filesystem drivers**: read-only, or read/write with **Tools > Allow UFS writes** (experimental) |

WSL install name: **`openSUSE-Tumbleweed`**

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

## Step 2: Install openSUSE Tumbleweed

In the administrator terminal:

```powershell
wsl --install -d openSUSE-Tumbleweed
```

A window opens and the first-start wizard asks for a **user name and password** for Linux. They are
separate from your Windows account; choose anything and remember the password.

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version openSUSE-Tumbleweed 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo` or the
root password:

```powershell
wsl -d openSUSE-Tumbleweed -u root
```

The prompt now ends with `#`. Update the system first, then install the packages:

```sh
zypper refresh
zypper dup -y
zypper install -y btrfsprogs util-linux kmod libfsapfs
```

| Package | Why |
|---|---|
| `btrfsprogs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (usually already installed) |
| `libfsapfs` | Optional: read-only access to Mac (APFS) drives with `fsapfsmount` |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

### Optional: ZFS

ZFS needs the `zpool` tool from openSUSE's *filesystems* repository, plus the ZFS driver (next section):

```sh
zypper addrepo https://download.opensuse.org/repositories/filesystems/openSUSE_Tumbleweed/ filesystems
zypper --gpg-auto-import-keys refresh
zypper install -y zfs
```

`zfs` also pulls in `kernel-default` and `zfs-kmp-default`. They are built for openSUSE's own
kernel, which WSL never starts, so they do nothing, but they must stay installed because `zfs`
depends on them.

### Optional: extra drivers (JFS, HFS+, UFS, ZFS, APFS read/write)

The WSL kernel from Microsoft has no drivers for these, so Xnix USB Mounter compiles them for you:

1. In Xnix USB Mounter choose **Tools > Build filesystem drivers**.
2. Wait. The first run takes 20-40 minutes and needs about 5 GB of free space in the distro. It
   downloads the WSL kernel source (about 250 MB), OpenZFS and linux-apfs-rw.
3. The log ends with *Filesystem drivers built and installed for this WSL kernel.*

The drivers survive WSL and Windows restarts: they are kept on the distro's disk and the app puts
them back when needed. **Run the build again after every `wsl --update`**: a new WSL kernel needs its
own build. Later runs are quicker.

**UFS drives** (FreeBSD, NetBSD, OpenBSD) open read-only with the built driver. To write to them, tick
**Tools > Allow UFS writes (experimental)**; the [README](../../README.md#ufs) explains what that
changes on FreeBSD drives. Linux has no UFS check tool: check UFS drives with `fsck_ffs` on FreeBSD.

Type `exit` to leave the root shell.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d openSUSE-Tumbleweed -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount zpool; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` (APFS) and `zpool` (ZFS) only matter if
you installed those options.

## Step 5: Use it with Xnix USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `XnixUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **openSUSE-Tumbleweed**. To make it the default for
   everything, run `wsl --set-default openSUSE-Tumbleweed` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > openSUSE-Tumbleweed > mnt > wsl > *drive label***, or
   at `\\wsl.localhost\openSUSE-Tumbleweed\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\XnixUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version openSUSE-Tumbleweed 2` |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfsprogs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac: `zypper install -y libfsapfs`; ZFS: install `zfs` (Step 3). Then click **Refresh** |
| Status says *No driver* | Run **Tools > Build filesystem drivers** (again, after a `wsl --update`) |
| A UFS drive stays *Ready (read-only)* | Tick **Tools > Allow UFS writes**, after building the drivers. Solaris UFS, and drives that were not cleanly unmounted, always open read-only (the log says which): run `fsck_ffs` on the BSD system and eject it there |
| "The driver build script is missing" | Copy the `tools\` folder next to `XnixUsbMounter.exe` |
| `zypper` says a repository key is not trusted | Run `zypper --gpg-auto-import-keys refresh` |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister openSUSE-Tumbleweed`. Your USB
drives are not touched, but eject them first.

Other distros: [overview](README.md).
