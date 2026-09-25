# Linux USB Mounter with CentOS (and AlmaLinux)

**CentOS Linux has reached end of life** (CentOS 8 in 2021, CentOS 7 in 2024) and gets no more
updates. Its successor, **CentOS Stream**, has official WSL images, but they are not in
`wsl --install`: you download a file first. That is route A below.

If you just want a RHEL-compatible distro with the least effort, use **AlmaLinux 10** (route B). It
installs with one command and has `btrfs-progs` in its own repositories.

| What you want to open | CentOS Stream 9/10 | AlmaLinux 10 | AlmaLinux 9 |
|---|---|---|---|
| btrfs, ext2/3/4, XFS | Yes (btrfs tools from EPEL) | Yes | Yes (btrfs tools from EPEL) |
| APFS (Mac drives), read-only | No (`fsapfsmount` not packaged) | No | No |
| JFS, HFS+ (Mac), UFS (BSD), ZFS, APFS read/write | No; use [openSUSE Tumbleweed](opensuse-tumbleweed.md) | No | No |

Red Hat-family distros don't ship `btrfs-progs` themselves (AlmaLinux 10 is the exception). On the
others it comes from **EPEL** (Extra Packages for Enterprise Linux, a Fedora project repository).

**Tools > Build filesystem drivers** needs a distro with zypper or apt (openSUSE, SLES, Debian,
Kali, Ubuntu). For Mac drives or the extra filesystems, install openSUSE Tumbleweed or Kali as well
and pick it in the app.

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
   your WSL is too old: run `wsl --update` again. Installing from a `.wsl` file (route A) needs a
   current WSL.

You need Windows 11, or Windows 10 version 22H2 with the current WSL from `wsl --update`.

---

## Route A: CentOS Stream 10

### A2: Install CentOS Stream

1. Open the CentOS WSL images page, [sigs.centos.org/altimages/wsl-images](https://sigs.centos.org/altimages/wsl-images/),
   and download the **CentOS Stream 10** image for x86_64 (a file ending in `.wsl`) into your
   **Downloads** folder. (The files are also at
   `https://mirror.stream.centos.org/SIGs/10-stream/altimages/images/wsl/`.)
2. In the administrator terminal, install it under the name `CentOS-Stream-10` (replace the file
   name with the one you downloaded):

   ```powershell
   wsl --install --from-file "$HOME\Downloads\CentOS-Stream-10-WSL.x86_64.wsl" --name CentOS-Stream-10
   ```

   Double-clicking the `.wsl` file also works, but then WSL picks the name.
3. Start it with `wsl -d CentOS-Stream-10`. The first start asks for a Linux **user name** (no
   password); that user gets full `sudo` rights.

Check that it runs as WSL **2**:

```powershell
wsl -l -v
```

The `VERSION` column must say `2`. If it says `1`, run `wsl --set-version CentOS-Stream-10 2`.

### A3: Install the extra packages

Open a **root** shell in the distro. Root is the Linux administrator, so you don't need `sudo`:

```powershell
wsl -d CentOS-Stream-10 -u root
```

The prompt now ends with `#`. Update the system, turn on EPEL, then install the packages:

```sh
dnf upgrade -y
dnf install -y dnf-plugins-core
dnf config-manager --set-enabled crb
dnf install -y epel-release
dnf install -y btrfs-progs util-linux kmod
```

If `epel-release` is not found, install it straight from the EPEL project:
`dnf install -y https://dl.fedoraproject.org/pub/epel/epel-release-latest-10.noarch.rpm`
(use `-9` instead of `-10` on CentOS Stream 9).

| Package | Why |
|---|---|
| `epel-release` | Turns on the EPEL repository, which has `btrfs-progs` |
| `btrfs-progs` | **Required.** The `btrfs` command: drive info, scrub, offline check |
| `util-linux` | **Required.** `blkid` finds the mounted partition inside WSL (`util-linux-core` is usually already installed) |
| `kmod` | **Required.** `modinfo` / `modprobe` check and load drivers (usually already installed) |
| `e2fsprogs`, `xfsprogs` | Optional: check or repair ext and XFS drives by hand (`fsck.ext4`, `xfs_repair`) |

Type `exit` to leave the root shell. Continue with [Step 4](#step-4-check-the-setup), using the
name `CentOS-Stream-10`.

---

## Route B: AlmaLinux 10 (easiest)

### B2: Install AlmaLinux

In the administrator terminal:

```powershell
wsl --install -d AlmaLinux-10
```

A window opens and asks for a Linux **user name**, then a **password**. They are separate from your
Windows account; choose anything and remember the password.

Check that it runs as WSL **2** with `wsl -l -v` (the `VERSION` column must say `2`; if it says `1`,
run `wsl --set-version AlmaLinux-10 2`).

### B3: Install the extra packages

Open a **root** shell in the distro:

```powershell
wsl -d AlmaLinux-10 -u root
```

The prompt now ends with `#`. On AlmaLinux 10 `btrfs-progs` is in AlmaLinux's own repositories:

```sh
dnf upgrade -y
dnf install -y btrfs-progs util-linux kmod
```

**AlmaLinux 9** (`wsl --install -d AlmaLinux-9`) needs EPEL first, as in route A:

```sh
dnf upgrade -y
dnf install -y dnf-plugins-core
dnf config-manager --set-enabled crb
dnf install -y epel-release
dnf install -y btrfs-progs util-linux kmod
```

The packages are the same as in the table in route A. Type `exit` to leave the root shell.

---

## Step 4: Check the setup

Paste this into the root shell (`wsl -d CentOS-Stream-10 -u root`, or your AlmaLinux name):

```sh
for c in btrfs blkid modinfo; do
  command -v $c >/dev/null && echo "OK       $c" || echo "missing  $c"
done
```

All three must say **OK**.

## Step 5: Use it with Linux USB Mounter

1. Copy the whole program folder (with `tools\` and `LICENSE`) somewhere permanent and start
   `LinuxUsbMounter.exe`. Click **Yes** when Windows asks for administrator rights.
2. In the **WSL2 distro** box at the top, pick **CentOS-Stream-10** (or **AlmaLinux-10**). To make it
   the default for everything, run `wsl --set-default CentOS-Stream-10` once.
3. Plug in the USB drive. It appears in the list; select it and click **Mount**.
4. The files are in Explorer under **Linux > CentOS-Stream-10 > mnt > wsl > *drive label***, or at
   `\\wsl.localhost\CentOS-Stream-10\mnt\wsl\<label>`.
5. Always **Eject** in the app before unplugging, so all data is written to the drive.

To test from the command line, open an administrator terminal in the program folder and run
`.\LinuxUsbMounter --list`.

## Troubleshooting

| Problem | Fix |
|---|---|
| `wsl --install --from-file` is not recognized | Your WSL is too old: run `wsl --update` |
| The distro box is empty | Run `wsl -l -v`. The distro must show `VERSION 2`; run `wsl --set-version <name> 2` |
| `No match for argument: btrfs-progs` | EPEL is not on (route A, or AlmaLinux 9): install `epel-release` as shown above |
| Drive info, scrub or check say the btrfs tools are missing | Repeat step A3 or B3, or click **Yes** when the app offers to install `btrfs-progs` (turn on EPEL first, except on AlmaLinux 10) |
| Status says *Needs tools* (Mac or ZFS drives) | Mac and ZFS drives need another distro: openSUSE Tumbleweed does both |
| Status says *No driver* | That filesystem needs the extra drivers, which need a zypper or apt distro: see the [Tumbleweed](opensuse-tumbleweed.md) or [Kali](kali.md) guide |
| Anything else | **Tools > Open log file**, or `%LOCALAPPDATA%\BtrfsUsbMounter\mounter.log` |

To remove the distro **and every file inside it**: `wsl --unregister CentOS-Stream-10` (or your
AlmaLinux name). Your USB drives are not touched, but eject them first.

Other distros: [overview](README.md).
