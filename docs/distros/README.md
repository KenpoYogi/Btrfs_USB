# Choosing a Linux distro for Btrfs USB Mounter

Btrfs USB Mounter opens Linux- and Mac-formatted USB drives through **WSL2**, the Linux system built
into Windows. WSL2 needs a Linux distribution ("distro") installed. The drives are mounted by
Microsoft's WSL kernel, which is the same for every distro. The distro supplies the tools the app
runs, like `btrfs`, `blkid` and `fsapfsmount`. That is why some features depend on the distro.

**Not sure? Pick [openSUSE Tumbleweed](opensuse-tumbleweed.md).** It is the distro the app is
developed and tested on, and the only one where every feature works.

| Distro | Guide | btrfs, ext, XFS | Mac (APFS) read-only | Extra drivers (JFS, HFS+, ZFS, APFS read/write) |
|---|---|---|---|---|
| openSUSE Tumbleweed | [opensuse-tumbleweed.md](opensuse-tumbleweed.md) | Yes (tested) | Yes | Yes |
| openSUSE Leap 16.0 | [opensuse-leap.md](opensuse-leap.md) | Yes | Yes | Not tested |
| SUSE Linux Enterprise Server | [sles.md](sles.md) | Yes (needs registration for most packages) | Yes, via Package Hub | No |
| Ubuntu 24.04 | [ubuntu.md](ubuntu.md) | Yes | Yes | No |
| Ubuntu 26.04 (plain `Ubuntu`) | [ubuntu.md](ubuntu.md) | Yes | No (package lacks FUSE) | No |
| Fedora | [fedora.md](fedora.md) | Yes | No | No |
| Kali Linux | [kali.md](kali.md) | Yes | No (package lacks FUSE) | No |
| Debian 13 | [debian.md](debian.md) | Yes | Yes | No |
| CentOS Stream / AlmaLinux | [centos.md](centos.md) | Yes (EPEL, except AlmaLinux 10) | No | No |
| Arch Linux | [arch.md](arch.md) | Yes | No | No |

ReiserFS and Reiser4 drives are detected on every distro but can't be opened: Linux 6.13 removed
ReiserFS, and current WSL kernels are newer than that.

## What was checked

- **openSUSE Tumbleweed**: tested with the app.
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
- **After `wsl --update`** you get a new WSL kernel. On openSUSE, rerun **Tools > Build filesystem
  drivers** afterwards; nothing else needs to change.
