# Xnix USB Mounter with openSUSE Leap

openSUSE Leap is the stable, fixed-release openSUSE. Everyday mounting works well. Leap 16.0 has
everything the extra drivers (JFS, HFS+, UFS, ZFS, APFS read/write) need: `gcc13` in its own repositories
and ZFS tools in openSUSE's *filesystems* repository. The driver build is only tested on
[openSUSE Tumbleweed](opensuse-tumbleweed.md), though.

| What you want to open | Works on Leap 16.0? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs` |
| JFS, HFS+ (Mac), ZFS, APFS read/write | Should work, not tested: see [Extra drivers](#extra-drivers) |
| UFS1, UFS2 (FreeBSD, NetBSD, OpenBSD) | Should work, not tested: read-only, or read/write with **Tools > Allow UFS writes** (experimental), after [Extra drivers](#extra-drivers) |

WSL install name: **`openSUSE-Leap-16.0`**

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

## Step 2: Install openSUSE Leap

In the administrator terminal:

```powershell
wsl --install -d openSUSE-Leap-16.0
```

A window opens and the first-start wizard asks for a **user name and password** for Linux. They are
separate from your Windows account; choose anything and remember the password.

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version openSUSE-Leap-16.0 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo` or the
root password:

```powershell
wsl -d openSUSE-Leap-16.0 -u root
```

The prompt now ends with `#`. Update the system first, then install the packages:

```sh
zypper refresh
zypper update -y
zypper install -y btrfsprogs util-linux kmod libfsapfs
```

| Package | Why |
|---|---|
| `btrfsprogs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (usually already installed) |
| `libfsapfs` | Optional: read-only access to Mac (APFS) drives with `fsapfsmount` |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

### Optional: the *filesystems* repository (ZFS and more tools)

openSUSE's *filesystems* repository has a Leap 16.0 build with the ZFS tools (`zpool`, `zfs`) and
check tools for JFS and APFS drives. It is a community repository from the openSUSE Build Service.
Add it once:

```sh
zypper addrepo https://download.opensuse.org/repositories/filesystems/16.0/ filesystems
zypper --gpg-auto-import-keys refresh
zypper install -y zfs jfsutils apfsprogs
```

| Package | Why |
|---|---|
| `zfs` | For ZFS drives: `zpool` imports and exports the pool (the ZFS driver itself comes from [Extra drivers](#extra-drivers)) |
| `jfsutils` | Optional: check or repair JFS drives by hand (`fsck.jfs`) |
| `apfsprogs` | Optional: check APFS drives by hand (`fsck.apfs`) |

`zfs` also pulls in `kernel-default` and `zfs-kmp-default`. They are built for openSUSE's own
kernel, which WSL never starts, so they do nothing, but they must stay installed because `zfs`
depends on them.

Type `exit` to leave the root shell.

### Extra drivers

The WSL kernel from Microsoft has no drivers for JFS, HFS+, UFS, ZFS or APFS read/write, so Btrfs USB
Mounter compiles them: **Tools > Build filesystem drivers**. Leap 16.0 has what the build needs.
The compiler is `gcc13` from Leap's own repositories. For ZFS, install `zfs` from the *filesystems*
repository first (above). **This is tested on Tumbleweed only.** If it fails on Leap, please report it
with `mounter.log`. As a fallback you can install Tumbleweed next to Leap
(`wsl --install -d openSUSE-Tumbleweed`, see the [Tumbleweed guide](opensuse-tumbleweed.md)) and pick
it in the app.

1. In Xnix USB Mounter choose **Tools > Build filesystem drivers**.
2. Wait. The first run takes 20-40 minutes and needs about 5 GB of free space in the distro.
3. The log ends with *Filesystem drivers built and installed for this WSL kernel.*

The drivers survive WSL restarts (the app puts them back when needed). **Run the build again after
every `wsl --update`**: a new WSL kernel needs its own build.

**UFS drives** (FreeBSD, NetBSD, OpenBSD) open read-only with the built driver. To write to them, tick
**Tools > Allow UFS writes (experimental)**; the [README](../../README.md#ufs) explains what that
changes on FreeBSD drives. Linux has no UFS check tool: check UFS drives with `fsck_ffs` on FreeBSD.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d openSUSE-Leap-16.0 -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount zpool; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` (Mac drives) and `zpool` (ZFS) only
matter if you installed those options.

## Step 5: Use it with Xnix USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `XnixUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **openSUSE-Leap-16.0**. To make it the default for
   everything, run `wsl --set-default openSUSE-Leap-16.0` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > openSUSE-Leap-16.0 > mnt > wsl > *drive label***, or
   at `\\wsl.localhost\openSUSE-Leap-16.0\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\XnixUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version openSUSE-Leap-16.0 2` |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfsprogs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac: `zypper install -y libfsapfs`; ZFS: install `zfs` from the *filesystems* repository (Step 3). Then click **Refresh** |
| `zypper` says a repository key is not trusted | Run `zypper --gpg-auto-import-keys refresh` |
| Status says *No driver* | That filesystem needs the extra drivers: see [Extra drivers](#extra-drivers) |
| A UFS drive stays *Ready (read-only)* | Tick **Tools > Allow UFS writes**, after building the drivers. Solaris UFS, and drives that were not cleanly unmounted, always open read-only (the log says which): run `fsck_ffs` on the BSD system and eject it there |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister openSUSE-Leap-16.0`. Your USB
drives are not touched, but eject them first.

Other distros: [overview](README.md).
