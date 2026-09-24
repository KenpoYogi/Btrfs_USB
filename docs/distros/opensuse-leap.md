# Btrfs USB Mounter with openSUSE Leap

openSUSE Leap is the stable, fixed-release openSUSE. Everyday mounting works well. For the extra
drivers (JFS, HFS+, ZFS, APFS read/write) use [openSUSE Tumbleweed](opensuse-tumbleweed.md) instead:
the driver build is only tested there.

| What you want to open | Works on Leap 16.0? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | Yes, with `libfsapfs` |
| JFS, HFS+ (Mac), ZFS, APFS read/write | Not tested; use Tumbleweed (see [Extra drivers](#extra-drivers)) |
| ReiserFS, Reiser4 | No (detected only) |

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

Type `exit` to leave the root shell.

### Extra drivers

**Tools > Build filesystem drivers** (JFS, HFS+, ZFS, APFS read/write) is only tested on Tumbleweed.
It needs the same GCC version that built the WSL kernel. If Leap doesn't have that version, the script
fetches one built for Tumbleweed, which may not install on Leap.

If you need these filesystems, install Tumbleweed next to Leap (`wsl --install -d openSUSE-Tumbleweed`,
see the [Tumbleweed guide](opensuse-tumbleweed.md)) and pick it in the app. Both distros can stay
installed.

For ZFS the `zpool` tool comes from the *filesystems* repository for Leap 16.0:
`https://download.opensuse.org/repositories/filesystems/16.0/`.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d openSUSE-Leap-16.0 -u root`):

```sh
for c in btrfs blkid modinfo fsapfsmount; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

`btrfs`, `blkid` and `modinfo` must say **OK**. `fsapfsmount` only matters for Mac drives.

## Step 5: Use it with Btrfs USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `BtrfsUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **openSUSE-Leap-16.0**. To make it the default for
   everything, run `wsl --set-default openSUSE-Leap-16.0` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > openSUSE-Leap-16.0 > mnt > wsl > *drive label***, or
   at `\\wsl.localhost\openSUSE-Leap-16.0\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\BtrfsUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version openSUSE-Leap-16.0 2` |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfsprogs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac: `zypper install -y libfsapfs`, then click **Refresh**. ZFS: see [Extra drivers](#extra-drivers) |
| Status says *No driver* | That filesystem needs the extra drivers: see [Extra drivers](#extra-drivers) |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister openSUSE-Leap-16.0`. Your USB
drives are not touched, but eject them first.

Other distros: [overview](README.md).
