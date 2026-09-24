# Btrfs USB Mounter with Arch Linux

Arch is a rolling-release distro that ships only a small base system: you add what you need. The
btrfs tools are in Arch's core repository, so btrfs, ext and XFS drives work fine.

| What you want to open | Works on Arch? |
|---|---|
| btrfs, ext2/3/4, XFS | Yes, read/write |
| APFS (Mac drives), read-only | No (`fsapfsmount` is not in Arch's repositories or the AUR) |
| JFS, HFS+ (Mac), ZFS, APFS read/write | No; use [openSUSE Tumbleweed](opensuse-tumbleweed.md) |
| ReiserFS, Reiser4 | No (detected only) |

**Tools > Build filesystem drivers** needs an openSUSE or SLES distro (it uses zypper). For Mac drives or the extra filesystems, install
openSUSE Tumbleweed next to Arch and pick it in the app. Arch's `linux-apfs-rw-dkms` and the AUR's
`zfs-utils` don't help: DKMS builds for Arch's own kernel, which WSL never starts.

WSL install name: **`archlinux`**

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

## Step 2: Install Arch Linux

In the administrator terminal:

```powershell
wsl --install -d archlinux
```

Unlike most distros, Arch **doesn't ask for a user name**: it starts you as `root` (the Linux
administrator). On the first start it sets up the package signing keys by itself. `sudo` is not
installed. That's fine for Btrfs USB Mounter, which always works as root.

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version archlinux 2`.

## Step 3: Install the extra packages

Open a **root** shell in the distro (with Arch that is also the default):

```powershell
wsl -d archlinux -u root
```

The prompt now ends with `#`. Update the whole system first. On Arch, always update everything
before installing, never only part of it:

```sh
pacman -Syu --noconfirm
pacman -S --needed --noconfirm btrfs-progs util-linux kmod
```

| Package | Why |
|---|---|
| `btrfs-progs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (already part of the base system) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (already part of the base system) |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

### Optional: your own user with sudo

Only needed if you want to use Arch yourself instead of as root. Replace `yourname`:

```sh
pacman -S --needed --noconfirm sudo
useradd -m -G wheel yourname
passwd yourname
echo '%wheel ALL=(ALL:ALL) ALL' > /etc/sudoers.d/wheel
printf '\n[user]\ndefault=yourname\n' >> /etc/wsl.conf
```

Then in the Windows terminal run `wsl --terminate archlinux`. The next start logs in as `yourname`.

Type `exit` to leave the root shell.

## Step 4: Check the setup

Paste this into the root shell (`wsl -d archlinux -u root`):

```sh
for c in btrfs blkid modinfo; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

All three must say **OK**.

## Step 5: Use it with Btrfs USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `BtrfsUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **archlinux**. To make it the default for everything,
   run `wsl --set-default archlinux` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > archlinux > mnt > wsl > *drive label***, or at
   `\\wsl.localhost\archlinux\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\BtrfsUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version archlinux 2` |
| `pacman` says "invalid or corrupted package (PGP signature)" | Refresh the keys: `pacman -Sy --noconfirm archlinux-keyring && pacman -Su --noconfirm` |
| `pacman` says "unable to lock database" | Another pacman is running. Wait, or if none is: `rm /var/lib/pacman/db.lck` |
| Drive info, scrub or check say the btrfs tools are missing | Repeat Step 3, or click **Yes** when the app offers to install `btrfs-progs` |
| Status says *Needs tools* (Mac or ZFS drives) | Mac and ZFS drives need another distro: openSUSE Tumbleweed does both |
| Status says *No driver* | That filesystem needs the extra drivers, which need an openSUSE or SLES distro: see the [Tumbleweed guide](opensuse-tumbleweed.md) |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister archlinux`. Your USB drives are
not touched, but eject them first.

Other distros: [overview](README.md).
