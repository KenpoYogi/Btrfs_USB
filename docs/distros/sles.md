# Btrfs USB Mounter with SUSE Linux Enterprise Server (SLES)

SLES is SUSE's commercial distro. It works well for btrfs, ext and XFS drives. Two things to know:

- **Registration.** Most SLES packages come from SUSE's servers and need a registration code: a paid
  subscription, or a free 60-day trial from [suse.com](https://www.suse.com/download/sles/).
  Without registration, SLES 15 SP7 can still install the required packages from the free *SLE_BCI*
  repository included with the WSL image. SLES 16.0 without registration is not verified.
- **Extra drivers: possible, not tested.** SLES has `gcc13`, which the driver build needs, and the ZFS
  tools come from openSUSE's *filesystems* repository, which also has SLES-compatible builds. The
  build is only tested on [openSUSE Tumbleweed](opensuse-tumbleweed.md), though.

| What you want to open | Works on SLES? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs` from SUSE Package Hub (needs registration) |
| JFS, HFS+ (Mac), ZFS, APFS read/write | Should work, not tested: see [Extra drivers](#extra-drivers) |
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

### Optional: the *filesystems* repository (ZFS and more tools)

openSUSE's *filesystems* repository (a community repository from the openSUSE Build Service, not
supported by SUSE) has builds for SLES too. It provides the ZFS tools (`zpool`, `zfs`) and check
tools for JFS and APFS drives. Pick the folder for your SLES version:

| SLES version | Repository |
|---|---|
| 16.0 | `https://download.opensuse.org/repositories/filesystems/16.0/` |
| 15 SP7 | `https://download.opensuse.org/repositories/filesystems/15.7/` (the Leap 15.7 build, same code base as SLES 15 SP7) |

For SLES 15 SP7 replace `16.0` with `15.7` in the first command:

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

`zfs` also pulls in `kernel-default` and `zfs-kmp-default`. They are for a normal SLES kernel, which
WSL never starts, so they do nothing, but they must stay installed because `zfs` depends on them.

Type `exit` to leave the root shell.

### Extra drivers

The WSL kernel from Microsoft has no drivers for JFS, HFS+, ZFS or APFS read/write, so Btrfs USB
Mounter compiles them: **Tools > Build filesystem drivers**. **This is tested on openSUSE Tumbleweed
only.** It should work on SLES because the pieces it needs exist there:

- **A registered system.** The build installs compilers and development packages with `zypper`.
- **`gcc13`**, the compiler version that built the WSL kernel. On SLES 16.0 it is in the base
  product. On SLES 15 SP7 turn on the free *Development Tools* module first:
  `SUSEConnect -p sle-module-development-tools/15.7/x86_64`.
- **For ZFS:** the `zfs` package from the *filesystems* repository (above).

Then:

1. In Btrfs USB Mounter choose **Tools > Build filesystem drivers**.
2. Wait. The first run takes 20-40 minutes and needs about 5 GB of free space in the distro.
3. The log ends with *Filesystem drivers built and installed for this WSL kernel.* If a package is
   missing, the log says which one.

The drivers survive WSL restarts (the app puts them back when needed). **Run the build again after
every `wsl --update`**: a new WSL kernel needs its own build. If the build fails on SLES, please report
it with `mounter.log`. As a fallback you can install [openSUSE Tumbleweed](opensuse-tumbleweed.md) next
to SLES and pick it in the app.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d SUSE-Linux-Enterprise-16.0 -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount zpool; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` (Mac drives) and `zpool` (ZFS) only
matter if you installed those options.

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
| Status says *Needs tools* (Mac or ZFS drives) | Mac: install `libfsapfs` from Package Hub (Step 3), then click **Refresh**. ZFS: install `zfs` from the *filesystems* repository (Step 3) |
| Status says *No driver* | That filesystem needs the extra drivers: see [Extra drivers](#extra-drivers) |
| `zypper` says a repository key is not trusted | Run `zypper --gpg-auto-import-keys refresh` |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister SUSE-Linux-Enterprise-16.0`.
Your USB drives are not touched, but eject them first. Deregister first (`SUSEConnect -d`) to free
the subscription.

Other distros: [overview](README.md).
