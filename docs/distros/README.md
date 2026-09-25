# Choosing a Linux distro for Xnix USB Mounter

Xnix USB Mounter opens Linux- and Mac-formatted USB drives through **WSL2**, the Linux system built
into Windows. WSL2 needs a Linux distribution ("distro") installed. The drives are mounted by
Microsoft's WSL kernel, which is the same for every distro. The distro supplies the tools the app
runs, like `btrfs`, `blkid` and `fsapfsmount`. That is why some features depend on the distro.

**Not sure? Pick [openSUSE Tumbleweed](opensuse-tumbleweed.md).** It is the distro the app is
developed on. The extra drivers from **Tools > Build filesystem drivers** are tested on Tumbleweed
and on [Kali](kali.md). The build works with zypper (openSUSE, SLES) and apt (Debian, Kali, Ubuntu);
on Leap, SLES, Debian and Ubuntu it is expected to work but not tested yet.

| Distro | Guide | btrfs, ext, XFS | Mac (APFS) read-only | Extra drivers (JFS, HFS+, UFS, ZFS, APFS read/write) |
|---|---|---|---|---|
| openSUSE Tumbleweed | [opensuse-tumbleweed.md](opensuse-tumbleweed.md) | Yes (tested) | Yes | Yes |
| openSUSE Leap 16.0 | [opensuse-leap.md](opensuse-leap.md) | Yes | Yes | Should work (not tested) |
| SUSE Linux Enterprise Server | [sles.md](sles.md) | Yes (needs registration for most packages) | Yes, via Package Hub | Should work (not tested) |
| Ubuntu 24.04 | [ubuntu.md](ubuntu.md) | Yes | Yes | Should work (not tested), except ZFS (package too old) |
| Ubuntu 26.04 (plain `Ubuntu`) | [ubuntu.md](ubuntu.md) | Yes | With the built APFS driver (package lacks FUSE) | Should work (not tested) |
| Fedora | [fedora.md](fedora.md) | Yes | No | No |
| Kali Linux | [kali.md](kali.md) | Yes (tested) | With the built APFS driver (tested) | Yes (tested) |
| Debian 13 | [debian.md](debian.md) | Yes | Yes | Should work (not tested) |
| CentOS Stream / AlmaLinux | [centos.md](centos.md) | Yes (EPEL, except AlmaLinux 10) | No | No |
| Arch Linux | [arch.md](arch.md) | Yes | No | No |

UFS drives from FreeBSD, NetBSD and OpenBSD open wherever the extra drivers can be built: read-only,
or read/write after **Tools > Allow UFS writes** (experimental; see the [README](../../README.md#ufs)).

## What was checked

- **openSUSE Tumbleweed**: tested with the app. UFS: disk images made by FreeBSD 15.1, written by the
  built driver, then checked by FreeBSD's `fsck_ffs` (the app itself has not mounted a real UFS disk yet).
- **Kali**: the driver build and the drivers are tested (disk-image tests for JFS, APFS, ZFS and UFS;
  HFS+ load only); the app itself was run on Tumbleweed.
- **All other distros**: package names and availability were checked against each distro's official
  package repositories in September 2026, but the app itself was not run on them. If something in a
  guide doesn't match what you see, please report it together with `mounter.log`.

## Good to know

- **You can install several distros.** They share one WSL kernel. Pick the one to use in the
  app's **WSL2 distro** box; it remembers your choice. For example, keep Ubuntu for daily work and add
  openSUSE Tumbleweed only for Mac drives.
- **The app works as root** (`wsl -u root`), so your Linux password and `sudo` don't matter to it.
  The guides use a root shell for the same reason.
- **The required packages are the same everywhere:** the btrfs tools (`btrfs-progs`, called
  `btrfsprogs` on SUSE), `util-linux` (for `blkid`) and `kmod` (for `modinfo`/`modprobe`). If they
  are missing, the app offers to install the btrfs tools itself (zypper, apt, dnf and pacman are
  supported).
- **Only WSL 2 works.** WSL 1 has no real Linux kernel. `wsl -l -v` shows the version;
  `wsl --set-version <name> 2` converts a distro.
- **After `wsl --update`** you get a new WSL kernel. Rerun **Tools > Build filesystem drivers**
  afterwards, in every distro where you built them; nothing else needs to change. Ordinary WSL or
  Windows restarts don't matter: the built drivers are kept on the distro's disk and the app puts
  them back.
