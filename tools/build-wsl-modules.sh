#!/bin/sh
# Xnix USB Mounter
# Copyright (c) 2026 Jay Weiner
# SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
#
# Licensed under the MIT License with the Commons Clause License Condition v1.0:
# you may use, copy, modify and distribute it, but not sell it or a product or service
# whose value derives substantially from it. See the LICENSE file.
#
# Builds filesystem kernel modules for the RUNNING WSL2 kernel (Microsoft's kernel, not the
# distro's kernel-default package, which WSL never boots) and installs them where modprobe finds
# them. Run as root inside the WSL distro: openSUSE or SLES (zypper), or Debian, Kali or Ubuntu (apt).
# Tested on openSUSE Tumbleweed and Kali.
#
#   sh build-wsl-modules.sh [jfs] [reiserfs] [hfsplus] [ufs] [zfs] [apfs]    (no arguments = all)
#
# Rerun after "wsl --update": a new WSL kernel needs modules built against its own source.
# Built from:
#   - in-tree drivers (JFS, HFS+, HFS, UFS, and ReiserFS on kernels before 6.13, which removed it) from
#     github.com/microsoft/WSL2-Linux-Kernel at the tag matching uname -r, configured with the
#     running kernel's own /proc/config.gz. UFS gets write support and a small change (ufs_handoff
#     below) so that FreeBSD notices when Linux wrote to the filesystem
#   - OpenZFS (github.com/openzfs/zfs), same version as the installed zfs userspace package
#   - linux-apfs-rw (github.com/linux-apfs/linux-apfs-rw); its write support is experimental
set -eu

KVER=$(uname -r)                      # e.g. 6.6.87.2-microsoft-standard-WSL2
BASE=${KVER%%-*}                      # e.g. 6.6.87.2
WORK=/usr/src/wsl-modules
KSRC=$WORK/WSL2-Linux-Kernel-linux-msft-wsl-$BASE
DEST=/lib/modules/$KVER/extra
# WSL keeps /lib/modules/<release> in an overlay whose writable layer is in memory, so anything in DEST
# is gone after a WSL restart. The built modules are kept here, on the distro's disk, and Btrfs USB
# Mounter copies them back into DEST (plus depmod) when they are missing.
STORE=/var/lib/wsl-modules/$KVER
JOBS=$(nproc)
WANT=${*:-jfs reiserfs hfsplus ufs zfs apfs}

log() { printf '\n==== %s\n' "$*"; }
want() { case " $WANT " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

# Kconfig symbols that probe the toolchain (no prompt, so .config can't set them): the ones whose value
# differs between the running kernel's config and ours
PROBES='^CONFIG_(CC_HAS_|CC_CAN_|CC_NO_|GCC_ASM_|AS_HAS_|AS_WRITES_|LD_HAS_|LD_CAN_|TOOLCHAIN_HAS_|TOOLS_SUPPORT_)[A-Z0-9_]*='
probe_diff() {
    { zcat /proc/config.gz | grep -E "$PROBES"; grep -E "$PROBES" .config; } |
        sort | uniq -u | sed 's/^CONFIG_//; s/=.*//' | sort -u
}
# Rewrite "config SYM" in the tree's Kconfig so it is always $2 (y/n): drops its own type, default and
# depends lines and puts "def_bool $2" in their place. Safe to repeat.
pin_kconfig() {
    f=$(grep -rlx --include='Kconfig*' "\(menu\)\?config $1" . | head -1)
    [ -n "$f" ] || return 1
    awk -v sym="$1" -v val="$2" '
        /^(menu)?config / { blk = ($2 == sym); print; if (blk) print "\tdef_bool " val; next }
        /^(choice|endchoice|menu|endmenu|if|endif|source|comment)([ \t]|$)/ { blk = 0 }
        blk && /^[ \t]+(def_bool|default|depends on|bool[ \t]*$)/ { next }
        { print }' "$f" > "$f.pin" && mv "$f.pin" "$f"
}

# ufs_handoff DIR: changes a copy of fs/ufs so that read/write mounts hand the filesystem back to the BSDs
# the way FreeBSD expects from a kernel that does not know its newer features:
#   - while mounted read/write, fs_clean is 0 on disk, as FreeBSD does itself. Linux leaves it at 1, so after
#     a crash or an unplugged drive FreeBSD would take the filesystem for clean and skip fsck. A clean
#     unmount sets it back to 1; an error leaves 0 (the BSDs treat Linux's "bad" value 0xff as clean).
#     Only fs_clean 1 allows a read/write mount: Linux also takes 2 as clean, which NetBSD writes while mounted.
#   - metadata check hashes (FreeBSD 12+ newfs default) are switched off by clearing FS_METACKHASH: Linux
#     does not update them, and FreeBSD disables them on its next mount when that flag is gone (fsck_ffs can
#     add them back). Otherwise FreeBSD rejects the superblock after the first write.
#   - with journaled soft updates, fs_mtime is set to the mount time, so fsck_ffs never replays a journal
#     left by an earlier FreeBSD mount over the changes made here (it only uses a journal whose time
#     matches fs_mtime), and does a full check instead.
# Only for the 4.4BSD flavours (ufstype=44bsd and ufs2); Solaris flavours are left alone. The module gets
# modinfo field wsl_handoff=1, which Xnix USB Mounter requires before it mounts UFS read/write.
ufs_handoff() {
    cat > "$1/wsl-handoff.h" <<'EOF'
/* Added by tools/build-wsl-modules.sh (Xnix USB Mounter): see ufs_handoff there */
#define WSL_FS_DOSOFTDEP	0x00000002
#define WSL_FS_SUJ		0x00000008
#define WSL_FS_FLAGS_UPDATED	0x80		/* in the old 8-bit flags: 32-bit fs_flags in use */
#define WSL_FS_METACKHASH	0x00000200
/* FreeBSD struct fs fields that sit in Linux's fs_44.fs_sparecon[] */
#define WSL_SPARE_MTIME		23		/* fs_mtime, 64 bits (offset 1208) */
#define WSL_SPARE_METACKHASH	48		/* fs_metackhash (1308) */
#define WSL_SPARE_FLAGS		49		/* fs_flags (1312) */

static bool ufs_wsl_bsd(struct super_block *sb)
{
	return (UFS_SB(sb)->s_flags & UFS_ST_MASK) == UFS_ST_44BSD;
}

static __s8 ufs_wsl_bad_state(struct super_block *sb)
{
	return ufs_wsl_bsd(sb) ? 0 : UFS_FSBAD;
}

/*
 * Linux also accepts fs_clean 2 (Solaris "stable") as clean, but NetBSD writes 2 while it has the
 * filesystem mounted read/write, so for the BSDs only 1 is clean.
 */
static void ufs_wsl_check_clean(struct super_block *sb)
{
	struct ufs_super_block_first *usb1 = ubh_get_usb_first(UFS_SB(sb)->s_uspi);

	if (ufs_wsl_bsd(sb) && !sb_rdonly(sb) && usb1->fs_clean != UFS_FSCLEAN) {
		pr_err("%s: not cleanly unmounted (fs_clean %d), mounting read-only; run fsck on BSD\n",
		       sb->s_id, usb1->fs_clean);
		sb->s_flags |= SB_RDONLY;
	}
}

static void ufs_wsl_mark_in_use(struct super_block *sb)
{
	struct ufs_sb_private_info *uspi = UFS_SB(sb)->s_uspi;
	struct ufs_super_block_first *usb1 = ubh_get_usb_first(uspi);
	struct ufs_super_block_third *usb3 = ubh_get_usb_third(uspi);
	__fs32 *spare = usb3->fs_un2.fs_44.fs_sparecon;
	u32 flags;

	if (!ufs_wsl_bsd(sb))
		return;
	if (uspi->fs_magic == UFS2_MAGIC || (usb1->fs_flags & WSL_FS_FLAGS_UPDATED)) {
		flags = fs32_to_cpu(sb, spare[WSL_SPARE_FLAGS]);
		/* NetBSD uses this flag bit for something else, but never sets fs_metackhash */
		if (spare[WSL_SPARE_METACKHASH] && (flags & WSL_FS_METACKHASH)) {
			flags &= ~WSL_FS_METACKHASH;
			spare[WSL_SPARE_FLAGS] = cpu_to_fs32(sb, flags);
			pr_info("%s: metadata check hashes switched off (fsck_ffs on FreeBSD can add them back)\n",
				sb->s_id);
		}
		if ((flags & (WSL_FS_SUJ | WSL_FS_DOSOFTDEP)) == (WSL_FS_SUJ | WSL_FS_DOSOFTDEP)) {
			__fs64 now = cpu_to_fs64(sb, ktime_get_real_seconds());

			memcpy(&spare[WSL_SPARE_MTIME], &now, sizeof(now));
		}
	}
	usb1->fs_clean = 0;
	ubh_mark_buffer_dirty(USPI_UBH(uspi));
	ubh_sync_block(USPI_UBH(uspi));
}

static void ufs_wsl_mark_clean(struct super_block *sb)
{
	struct ufs_super_block_first *usb1 = ubh_get_usb_first(UFS_SB(sb)->s_uspi);

	if (ufs_wsl_bsd(sb) && usb1->fs_clean == 0)
		usb1->fs_clean = UFS_FSCLEAN;
}
EOF
    # every change goes at a line that is the same in the 6.6 and 6.18 trees; fail unless all seven match
    awk '
        /^static const struct super_operations ufs_super_ops;$/ { print; print "#include \"wsl-handoff.h\""; n++; next }
        /^\tsb->s_op = &ufs_super_ops;$/ { print "\tufs_wsl_check_clean(sb);"; n++ }
        /usb1->fs_clean = UFS_FSBAD;/ { sub(/UFS_FSBAD/, "ufs_wsl_bad_state(sb)"); n++ }
        /^static void ufs_put_super_internal\(/ { psi = 1 }
        psi && /^\tufs_put_cstotal\(sb\);$/ { print "\tufs_wsl_mark_clean(sb);"; psi = 0; n++ }
        /^\t\tsb->s_flags &= ~SB_RDONLY;$/ { print; print "\t\tufs_wsl_mark_in_use(sb);"; n++; next }
        /^\t\tif \(!ufs_read_cylinder_structures\(sb\)\)$/ { cyl = 1; print; next }
        cyl && /^\t\t\tgoto failed;$/ { print; print "\tif (!sb_rdonly(sb))"; print "\t\tufs_wsl_mark_in_use(sb);"; cyl = 0; n++; next }
        { cyl = 0; print }
        END { exit n == 7 ? 0 : 1 }' "$1/super.c" > "$1/super.c.new" || return 1
    mv "$1/super.c.new" "$1/super.c"
    printf '\nMODULE_INFO(wsl_handoff, "1");\n' >> "$1/super.c"
}

[ "$(id -u)" = 0 ] || { echo "Run as root (wsl -u root)."; exit 1; }
case "$KVER" in *microsoft*WSL2*) ;; *) echo "Not a WSL2 kernel: $KVER"; exit 1 ;; esac
if command -v zypper >/dev/null 2>&1; then PM=zypper
elif command -v apt-get >/dev/null 2>&1 && command -v dpkg-query >/dev/null 2>&1; then PM=apt
else
    echo "This script needs zypper (openSUSE, SLES) or apt (Debian, Kali, Ubuntu). Install one of those"
    echo "distros in WSL and build there, e.g.: wsl --install -d openSUSE-Tumbleweed"
    exit 1
fi

# pkg_install ZYPPER-NAMES -- APT-NAMES: installs the names for this distro's package manager
pkg_install() {
    z=""; a=""; side=z
    for p in "$@"; do
        if [ "$p" = -- ]; then side=a; elif [ $side = z ]; then z="$z $p"; else a="$a $p"; fi
    done
    case $PM in
        zypper) zypper --non-interactive --quiet install --no-recommends $z >/dev/null ;;
        apt)    DEBIAN_FRONTEND=noninteractive apt-get install -y -q --no-install-recommends $a >/dev/null ;;
    esac
}
# Version of the installed ZFS userspace tools (upstream part only, e.g. 2.4.4), empty if not installed
zfs_version() {
    case $PM in
        zypper) rpm -q --qf '%{VERSION}' zfs 2>/dev/null || true ;;
        apt)    dpkg-query -W -f='${Status} ${Version}\n' zfsutils-linux 2>/dev/null |
                    sed -n 's/^install ok installed \([0-9]*:\)\{0,1\}\([^-~+]*\).*/\2/p' ;;
    esac
}

log "Kernel $KVER, building: $WANT"
# Use the compiler major version that built the running kernel, to stay as close to Microsoft's build as
# possible (6.6 kernels: gcc-11, which Tumbleweed no longer ships, so it comes from openSUSE's devel:gcc
# project; 6.18: gcc-13). Point releases still differ, so the toolchain probes are pinned below.
KGCC=$(sed -n 's/.*gcc (GCC) \([0-9][0-9]*\)\..*/\1/p' /proc/version)
CC_BIN=gcc-${KGCC:-13}
# host tools only (resolve_btfids/libbpf): newer glibc headers turn a const warning into -Werror
HOSTFIX="-Wno-error=discarded-qualifiers"
log "Installing build tools (the running kernel was built with GCC ${KGCC:-?}, using $CC_BIN)"
[ $PM = apt ] && apt-get update -q >/dev/null
pkg_install make flex bison bc libelf-devel openssl-devel dwarves python3 perl rsync tar gzip xz curl git kmod \
         -- make flex bison bc libelf-dev libssl-dev dwarves python3 perl rsync tar gzip xz-utils curl git kmod \
            ca-certificates libc6-dev
if ! command -v "$CC_BIN" >/dev/null && [ $PM = apt ]; then
    pkg_install -- "gcc-$KGCC" || {
        echo "gcc-$KGCC is not in this distro's repositories, and the WSL kernel was built with it."
        echo "Use a distro that has it (Debian 13+, Kali, Ubuntu 24.04+ have gcc-13) or openSUSE Tumbleweed."
        exit 1
    }
fi
if ! command -v "$CC_BIN" >/dev/null; then
    if ! zypper --non-interactive --quiet install --no-recommends "gcc$KGCC" >/dev/null 2>&1; then
        # the devel:gcc fallback below is built for Tumbleweed; Leap and SLES have gcc13 in their own
        # repositories (SLES 15: Development Tools module)
        if ! grep -q '^ID="\?opensuse-tumbleweed' /etc/os-release; then
            echo "gcc$KGCC is not in the configured repositories. Install it first (zypper install gcc$KGCC;"
            echo "on SLES 15 add the Development Tools module: SUSEConnect -p sle-module-development-tools/15.7/x86_64)."
            exit 1
        fi
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

mkdir -p "$WORK" "$STORE"
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
STAMP="$CC_BIN keep-btf pin-probes ufs"
if [ ! -f vmlinux.symvers ] || ! cmp -s /proc/config.gz .running-config.gz || [ "$(cat .built-with 2>/dev/null)" != "$STAMP" ]; then
    log "Configuring from /proc/config.gz"
    zcat /proc/config.gz > .config
    cp /proc/config.gz .running-config.gz
    # drivers that exist in this tree but are off in the WSL build; everything they select is built in
    scripts/config --module JFS_FS --enable JFS_POSIX_ACL --enable JFS_SECURITY \
                   --module REISERFS_FS --enable REISERFS_FS_XATTR --enable REISERFS_FS_POSIX_ACL \
                   --enable REISERFS_FS_SECURITY \
                   --module HFSPLUS_FS --module HFS_FS \
                   --module UFS_FS --enable UFS_FS_WRITE \
                   --disable LOCALVERSION_AUTO
    # Everything else stays exactly as Microsoft configured it, debug info and BTF included:
    # DEBUG_INFO_BTF_MODULES adds fields to struct module, so turning BTF off changes the module ABI.
    make -s CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" olddefconfig
    # Toolchain probes are answered by OUR compiler, and a newer point release can answer differently
    # (GCC 13.5 vs Microsoft's 13.2.0: CC_HAS_SANE_FUNCTION_ALIGNMENT turns __cold on, which changes the
    # symbol CRCs of _printk and panic). Pin every probe that differs to the running kernel's value.
    pinned=""
    for sym in $(probe_diff); do
        val=n
        zcat /proc/config.gz | grep -qx "CONFIG_$sym=y" && val=y
        pin_kconfig "$sym" "$val" || { echo "Cannot find config $sym in the kernel tree"; exit 1; }
        pinned="$pinned $sym=$val"
    done
    if [ -n "$pinned" ]; then
        echo "Pinned toolchain checks to the running kernel's values:$pinned"
        make -s CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" olddefconfig
        left=$(probe_diff)
        [ -z "$left" ] || { echo "Toolchain checks still differ from the running kernel:" $left; exit 1; }
    fi
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
        cp "$d"/*.ko "$STORE"/
    done
done

# ---- UFS (FreeBSD, NetBSD, OpenBSD; Solaris read-only) -------------------------------------------
if want ufs; then
    # built from a copy, so the kernel tree stays as downloaded
    UFS=$WORK/ufs
    rm -rf "$UFS"
    cp -r fs/ufs "$UFS"
    if ufs_handoff "$UFS"; then
        log "Building fs/ufs with write support"
    else
        # without the handoff changes, writing could leave a FreeBSD filesystem that FreeBSD rejects
        log "Building fs/ufs read-only: this kernel's fs/ufs/super.c differs from what ufs_handoff expects"
        rm -rf "$UFS"
        cp -r fs/ufs "$UFS"
        sed -i '1i #undef CONFIG_UFS_FS_WRITE' "$UFS/super.c"
    fi
    make -s -j"$JOBS" CC="$CC_BIN" HOSTCC="$CC_BIN" HOSTCFLAGS="$HOSTFIX" M="$UFS" modules
    cp "$UFS/ufs.ko" "$STORE"/
fi

# ---- OpenZFS ----------------------------------------------------------------------------------
if want zfs; then
    # the module must match the userspace tools (zpool), so build the version the distro ships.
    # Debian and Kali: zfsutils-linux is in "contrib"; without recommends so zfs-dkms is not pulled in
    ZVER=$(zfs_version)
    case "$ZVER" in [0-9]*) ;; *)
        pkg_install zfs -- zfsutils-linux || {
            echo "Installing the ZFS tools failed. openSUSE/SLES: add the filesystems repository;"
            echo "Debian/Kali: enable the contrib component in /etc/apt/sources.list."
            exit 1
        }
        ZVER=$(zfs_version) ;;
    esac
    case "$ZVER" in [0-9]*) ;; *) echo "Cannot tell which ZFS version is installed"; exit 1 ;; esac
    cd "$WORK"
    if [ ! -d "zfs-$ZVER" ]; then
        log "Downloading OpenZFS $ZVER"
        curl -fL --retry 3 -o "zfs-$ZVER.tar.gz" "https://github.com/openzfs/zfs/releases/download/zfs-$ZVER/zfs-$ZVER.tar.gz"
        tar -xzf "zfs-$ZVER.tar.gz"
        rm -f "zfs-$ZVER.tar.gz"
    fi
    cd "zfs-$ZVER"
    # an older distro package can be too old for the WSL kernel (Ubuntu 24.04: 2.2.2, Linux 6.6 at most)
    zmax=$(sed -n 's/^Linux-Maximum:[[:space:]]*//p' META)
    kmm=$(echo "$BASE" | cut -d. -f1,2)
    newest=$(printf '%s\n%s\n' "$kmm" "${zmax:-$kmm}" | sort -t. -k1,1n -k2,2n | tail -1)
    if [ "$newest" != "${zmax:-$kmm}" ]; then
        log "Skipping zfs: OpenZFS $ZVER supports Linux up to $zmax, the WSL kernel is $kmm"
        echo "Install a newer ZFS package if the distro has one and build again,"
        echo "or build in openSUSE Tumbleweed or Kali."
    else
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
    find module -name '*.ko' -exec cp {} "$STORE"/ \;
    fi
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
    cp apfs.ko "$STORE"/
fi

log "Installing into $DEST (kept in $STORE)"
rm -rf "$DEST"
mkdir -p "$DEST"
cp "$STORE"/*.ko "$DEST"/
depmod -a "$KVER"
touch "$DEST/.restored"
ls -l "$DEST"
log "Loading to verify"
status=0
for m in $(ls "$DEST" | sed 's/\.ko$//'); do
    if modprobe "$m"; then echo "  $m: loaded"; else echo "  $m: FAILED"; status=1; fi
done
grep -wE 'jfs|reiserfs|hfsplus|hfs|ufs|zfs|apfs' /proc/filesystems || true
exit $status
