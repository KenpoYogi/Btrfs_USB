#!/bin/sh
# Btrfs USB Mounter
# Copyright (c) 2026 Jay Weiner
# SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
#
# Licensed under the MIT License with the Commons Clause License Condition v1.0:
# you may use, copy, modify and distribute it, but not sell it or a product or service
# whose value derives substantially from it. See the LICENSE file.
#
# Builds filesystem kernel modules for the RUNNING WSL2 kernel (Microsoft's kernel, not the
# distro's kernel-default package, which WSL never boots) and installs them where modprobe finds
# them. Run as root inside the WSL distro (openSUSE Tumbleweed; package names are zypper's):
#
#   sh build-wsl-modules.sh [jfs] [reiserfs] [hfsplus] [zfs] [apfs]    (no arguments = all)
#
# Rerun after "wsl --update": a new WSL kernel needs modules built against its own source.
# Built from:
#   - in-tree drivers (JFS, HFS+, HFS, and ReiserFS on kernels before 6.13, which removed it) from
#     github.com/microsoft/WSL2-Linux-Kernel at the tag matching uname -r, configured with the
#     running kernel's own /proc/config.gz
#   - OpenZFS (github.com/openzfs/zfs), same version as the installed zfs userspace package
#   - linux-apfs-rw (github.com/linux-apfs/linux-apfs-rw); its write support is experimental
set -eu

KVER=$(uname -r)                      # e.g. 6.6.87.2-microsoft-standard-WSL2
BASE=${KVER%%-*}                      # e.g. 6.6.87.2
WORK=/usr/src/wsl-modules
KSRC=$WORK/WSL2-Linux-Kernel-linux-msft-wsl-$BASE
DEST=/lib/modules/$KVER/extra
JOBS=$(nproc)
WANT=${*:-jfs reiserfs hfsplus zfs apfs}

log() { printf '\n==== %s\n' "$*"; }
want() { case " $WANT " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

[ "$(id -u)" = 0 ] || { echo "Run as root (wsl -u root)."; exit 1; }
case "$KVER" in *microsoft*WSL2*) ;; *) echo "Not a WSL2 kernel: $KVER"; exit 1 ;; esac
command -v zypper >/dev/null 2>&1 || {
    echo "This script needs openSUSE (zypper and rpm). Install openSUSE-Tumbleweed in WSL and build there:"
    echo "  wsl --install -d openSUSE-Tumbleweed"
    exit 1
}

log "Kernel $KVER, building: $WANT"
# Use the compiler major version that built the running kernel, to stay as close to Microsoft's build as
# possible (compiler-dependent config options such as CC_HAS_* follow it). Tumbleweed no longer ships
# gcc11; openSUSE's devel:gcc project still builds it for Factory.
KGCC=$(sed -n 's/.*gcc (GCC) \([0-9][0-9]*\)\..*/\1/p' /proc/version)
CC_BIN=gcc-${KGCC:-13}
# host tools only (resolve_btfids/libbpf): newer glibc headers turn a const warning into -Werror
HOSTFIX="-Wno-error=discarded-qualifiers"
log "Installing build tools (the running kernel was built with GCC ${KGCC:-?}, using $CC_BIN)"
zypper --non-interactive --quiet install --no-recommends make flex bison bc libelf-devel openssl-devel \
    dwarves python3 perl rsync tar gzip xz curl git kmod >/dev/null
if ! command -v "$CC_BIN" >/dev/null; then
    if ! zypper --non-interactive --quiet install --no-recommends "gcc$KGCC" >/dev/null 2>&1; then
        # add devel:gcc just for this install (low priority, so it never replaces distro packages), then remove it
        echo "gcc$KGCC is not in the configured repositories; installing it from openSUSE devel:gcc (Factory)"
        zypper --non-interactive --quiet --gpg-auto-import-keys addrepo --refresh --priority 150 \
            https://download.opensuse.org/repositories/devel:/gcc/openSUSE_Factory/ wsl-modules-devel-gcc
        rc=0
        zypper --non-interactive --quiet --gpg-auto-import-keys install --no-recommends "gcc$KGCC" >/dev/null || rc=$?
        zypper --non-interactive --quiet removerepo wsl-modules-devel-gcc
        [ "$rc" = 0 ] || { echo "Installing gcc$KGCC failed (zypper exit $rc)"; exit 1; }
    fi
fi
command -v "$CC_BIN" >/dev/null || { echo "$CC_BIN not found"; exit 1; }

mkdir -p "$WORK" "$DEST"
cd "$WORK"

# ---- WSL kernel source, configured exactly like the running kernel ----------------------------
if [ ! -f "$KSRC/Makefile" ]; then
    log "Downloading WSL kernel source linux-msft-wsl-$BASE"
    curl -fL --retry 3 -o "linux-msft-wsl-$BASE.tar.gz" \
        "https://github.com/microsoft/WSL2-Linux-Kernel/archive/refs/tags/linux-msft-wsl-$BASE.tar.gz"
    tar -xzf "linux-msft-wsl-$BASE.tar.gz"
    rm -f "linux-msft-wsl-$BASE.tar.gz"
fi
cd "$KSRC"
STAMP="$CC_BIN keep-btf"
if [ ! -f vmlinux.symvers ] || ! cmp -s /proc/config.gz .running-config.gz || [ "$(cat .built-with 2>/dev/null)" != "$STAMP" ]; then
    log "Configuring from /proc/config.gz"
    zcat /proc/config.gz > .config
    cp /proc/config.gz .running-config.gz
    # drivers that exist in this tree but are off in the WSL build; everything they select is built in
    scripts/config --module JFS_FS --enable JFS_POSIX_ACL --enable JFS_SECURITY \
                   --module REISERFS_FS --enable REISERFS_FS_XATTR --enable REISERFS_FS_POSIX_ACL \
                   --enable REISERFS_FS_SECURITY \
                   --module HFSPLUS_FS --module HFS_FS \
                   --disable LOCALVERSION_AUTO
    # Everything else stays exactly as Microsoft configured it, debug info and BTF included:
    # DEBUG_INFO_BTF_MODULES adds fields to struct module, so turning BTF off changes the module ABI.
    make -s CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" olddefconfig
    # the release string must match uname -r exactly, or modprobe refuses the modules
    built=$(make -s CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" kernelrelease)
    [ "$built" = "$KVER" ] || { echo "Kernel release mismatch: built $built, running $KVER"; exit 1; }
    log "Building vmlinux for symbol versions (20-40 minutes on first run)"
    make -s -j"$JOBS" CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" vmlinux
    make -s CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" modules_prepare
    echo "$STAMP" > .built-with
fi
cp vmlinux.symvers Module.symvers

# symbol CRCs must match the running kernel: compare with a module Microsoft shipped (btrfs.ko)
ref=$(modinfo -n btrfs 2>/dev/null || true)
if [ -n "$ref" ] && [ -f "$ref" ]; then
    bad=$(modprobe --dump-modversions "$ref" | while read -r crc sym; do
        mine=$(awk -v s="$sym" '$2 == s { print $1; exit }' Module.symvers)
        [ -z "$mine" ] || [ "$mine" = "$crc" ] || echo "$sym"
    done | head -5)
    if [ -n "$bad" ]; then
        echo "Symbol versions differ from the running kernel (e.g. $bad). Not installing."; exit 1
    fi
    echo "Symbol versions match the running kernel (checked against $ref)."
fi

# ---- in-tree filesystem drivers ---------------------------------------------------------------
for fs in jfs reiserfs hfsplus; do
    want "$fs" || continue
    if [ ! -d "fs/$fs" ]; then
        # ReiserFS was removed from mainline Linux in 6.13, so newer WSL kernels have no source for it
        log "Skipping $fs: this kernel tree ($BASE) has no fs/$fs (ReiserFS was removed in Linux 6.13)"
        continue
    fi
    dirs="fs/$fs"
    [ "$fs" = hfsplus ] && dirs="fs/hfsplus fs/hfs"
    extra=""
    if [ "$fs" = jfs ]; then
        # JFS uses NlsUniUpper* from nls_ucs2_utils, which WSL ships as a module: build that directory for
        # its symbol versions only (the shipped nls_ucs2_utils.ko keeps being used)
        make -s -j"$JOBS" CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" M=fs/nls modules
        extra="KBUILD_EXTRA_SYMBOLS=$KSRC/fs/nls/Module.symvers"
    fi
    for d in $dirs; do
        log "Building $d"
        make -s -j"$JOBS" CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" M="$d" $extra modules
        cp "$d"/*.ko "$DEST"/
    done
done

# ---- OpenZFS ----------------------------------------------------------------------------------
if want zfs; then
    ZVER=$(rpm -q --qf '%{VERSION}' zfs 2>/dev/null || true)
    [ -n "$ZVER" ] && [ "${ZVER#package}" = "$ZVER" ] || { zypper --non-interactive --quiet install zfs >/dev/null; ZVER=$(rpm -q --qf '%{VERSION}' zfs); }
    cd "$WORK"
    if [ ! -d "zfs-$ZVER" ]; then
        log "Downloading OpenZFS $ZVER"
        curl -fL --retry 3 -o "zfs-$ZVER.tar.gz" "https://github.com/openzfs/zfs/releases/download/zfs-$ZVER/zfs-$ZVER.tar.gz"
        tar -xzf "zfs-$ZVER.tar.gz"
        rm -f "zfs-$ZVER.tar.gz"
    fi
    cd "zfs-$ZVER"
    log "Building OpenZFS $ZVER kernel modules"
    make -s distclean >/dev/null 2>&1 || true
    # OpenZFS runs kbuild itself; give it the kernel's compiler (KERNEL_CC) and a "gcc" that is that
    # compiler, for anything kbuild calls by the default name (only gcc-NN is installed)
    SHIM=$WORK/bin
    mkdir -p "$SHIM"
    ln -sf "$(command -v "$CC_BIN")" "$SHIM/gcc"
    ln -sf "$(command -v "$CC_BIN")" "$SHIM/cc"
    PATH="$SHIM:$PATH" ./configure --quiet --with-config=kernel --with-linux="$KSRC" --with-linux-obj="$KSRC" \
        CC="$CC_BIN" KERNEL_CC="$CC_BIN"
    PATH="$SHIM:$PATH" make -s -j"$JOBS"
    find module -name '*.ko' -exec cp {} "$DEST"/ \;
fi

# ---- APFS (linux-apfs-rw) ---------------------------------------------------------------------
if want apfs; then
    cd "$WORK"
    if [ ! -d linux-apfs-rw ]; then
        log "Downloading linux-apfs-rw"
        git clone --quiet --depth 1 https://github.com/linux-apfs/linux-apfs-rw.git
    fi
    cd linux-apfs-rw
    log "Building apfs.ko"
    make -s -C "$KSRC" CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" M="$PWD" clean >/dev/null 2>&1 || true
    ./genver.sh   # writes version.h, as the project's own Makefile does
    # mounts stay read-only unless mounted with -o readwrite (not built with CONFIG_APFS_RW_ALWAYS)
    make -s -j"$JOBS" -C "$KSRC" CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" M="$PWD" modules
    cp apfs.ko "$DEST"/
fi

log "Installing into $DEST"
depmod -a "$KVER"
ls -l "$DEST"
log "Loading to verify"
status=0
for m in $(ls "$DEST" | sed 's/\.ko$//'); do
    if modprobe "$m"; then echo "  $m: loaded"; else echo "  $m: FAILED"; status=1; fi
done
grep -wE 'jfs|reiserfs|hfsplus|hfs|zfs|apfs' /proc/filesystems || true
exit $status
