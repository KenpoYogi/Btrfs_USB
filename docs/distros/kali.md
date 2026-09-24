# Btrfs USB Mounter with Kali Linux

Kali is a Debian-based distro for security testing. The WSL image is minimal, but it installs the
btrfs tools without any trouble.

| What you want to open | Works on Kali? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | No: Kali's `libfsapfs-utils` is built without FUSE support |
| JFS, HFS+ (Mac), ZFS, APFS read/write | No; use [openSUSE Tumbleweed](opensuse-tumbleweed.md) |
| ReiserFS, Reiser4 | No (detected only) |

**Don't install `libfsapfs-utils` on Kali.** Its `fsapfsmount` exists but only prints *"No sub system
to mount APFS format"*, and because the command is there the app would show Mac drives as ready.

**Tools > Build filesystem drivers** needs openSUSE. For Mac drives or the extra filesystems, install
openSUSE Tumbleweed next to Kali and pick it in the app.

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

Type `exit` to leave the root shell. Tip: to hide Kali's welcome message in your own user's shell,
run `touch ~/.hushlogin` there.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d kali-linux -u root`):

```sh
for c in btrfs blkid modinfo; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

All three must say **OK**.

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
| Mac drive shows *Ready* but fails with "No sub system to mount APFS format" | Remove the broken package: `apt remove -y libfsapfs-utils`, then use openSUSE Tumbleweed for Mac drives |
| `apt update` fails with a signature (key) error | Kali's archive key changed: follow the key update steps on [kali.org](https://www.kali.org/docs/), then retry |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfs-progs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac and ZFS drives need another distro: openSUSE Tumbleweed does both |
| Status says *No driver* | That filesystem needs the extra drivers, which need openSUSE: see the [Tumbleweed guide](opensuse-tumbleweed.md) |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister kali-linux`. Your USB drives are
not touched, but eject them first.

Other distros: [overview](README.md).
