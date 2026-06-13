#!/usr/bin/env python3
"""
Globalize hidden symbols in a Mach-O or ELF static archive.

The curl-impersonate static archives are built with -fvisibility=hidden:
Mach-O objects mark symbols "private external" (N_PEXT) and ELF objects mark
them STV_HIDDEN. Neither macOS's -exported_symbols_list nor a GNU linker
version script can promote such symbols to dynamic exports, so we patch the
object symbol tables (clear N_PEXT / clear st_other visibility) for the
symbols we need to export before linking.

This script parses the ar format directly to avoid name collision issues
with `ar -x` when multiple members share the same filename. Both BSD ar
(Mach-O, "#1/N" extended names) and GNU ar (ELF, "//" long-name table) are
handled; member sizes never change so patching is done in place.

Usage:
    python globalize_symbols.py <archive.a> <symbols_file> <output.a>

The symbols file uses Mach-O spelling (leading underscore); the underscore is
stripped automatically when matching ELF symbol names.
"""

import shutil
import struct
import sys
from pathlib import Path

# Mach-O constants
MH_MAGIC_64 = 0xFEEDFACF
LC_SYMTAB = 0x02
N_PEXT = 0x10  # private external bit
N_EXT = 0x01   # external bit

# ELF constants
ELF_MAGIC = b"\x7fELF"
SHT_SYMTAB = 2
STV_VISIBILITY_MASK = 0x3  # low two bits of st_other

AR_MAGIC = b"!<arch>\n"
AR_HEADER_SIZE = 60


def parse_symbols_file(path: Path) -> set:
    """Read exported symbol names from a file (one per line, with leading _)."""
    symbols = set()
    for line in path.read_text().splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            symbols.add(line.encode("utf-8"))
    return symbols


def globalize_macho(data: bytearray, symbols_to_globalize: set) -> int:
    """Patch a 64-bit Mach-O object to clear N_PEXT for specified symbols.

    Returns the number of symbols patched.
    """
    if len(data) < 4:
        return 0

    magic = struct.unpack_from("<I", data, 0)[0]
    if magic != MH_MAGIC_64:
        return 0  # Not a 64-bit Mach-O

    # Parse Mach-O header: magic, cputype, cpusubtype, filetype, ncmds,
    # sizeofcmds, flags, reserved
    ncmds = struct.unpack_from("<I", data, 16)[0]

    # Find LC_SYMTAB
    offset = 32  # size of mach_header_64
    symtab_offset = None
    for _ in range(ncmds):
        cmd, cmdsize = struct.unpack_from("<II", data, offset)
        if cmd == LC_SYMTAB:
            # struct symtab_command: cmd, cmdsize, symoff, nsyms, stroff, strsize
            symoff, nsyms, stroff, strsize = struct.unpack_from(
                "<IIII", data, offset + 8
            )
            symtab_offset = (symoff, nsyms, stroff, strsize)
            break
        offset += cmdsize

    if symtab_offset is None:
        return 0

    symoff, nsyms, stroff, strsize = symtab_offset
    patched = 0
    nlist_size = 16  # sizeof(nlist_64)

    for i in range(nsyms):
        entry_off = symoff + i * nlist_size
        # struct nlist_64: n_strx(4), n_type(1), n_sect(1), n_desc(2), n_value(8)
        n_strx, n_type = struct.unpack_from("<IB", data, entry_off)

        # Check if this symbol has N_PEXT set
        if not (n_type & N_PEXT):
            continue

        # Get symbol name from string table
        str_start = stroff + n_strx
        str_end = data.index(b"\x00", str_start)
        sym_name = bytes(data[str_start:str_end])

        if sym_name in symbols_to_globalize:
            # Clear N_PEXT, ensure N_EXT is set
            new_type = (n_type & ~N_PEXT) | N_EXT
            struct.pack_into("B", data, entry_off + 4, new_type)
            patched += 1

    return patched


def globalize_elf(data: bytearray, symbols_to_globalize: set) -> int:
    """Patch a 64-bit little-endian ELF object to clear st_other visibility
    (STV_HIDDEN -> STV_DEFAULT) for specified symbols.

    Returns the number of symbols patched.
    """
    if len(data) < 64 or data[:4] != ELF_MAGIC:
        return 0
    if data[4] != 2 or data[5] != 1:  # ELFCLASS64, ELFDATA2LSB only
        return 0

    e_shoff = struct.unpack_from("<Q", data, 0x28)[0]
    e_shentsize, e_shnum = struct.unpack_from("<HH", data, 0x3A)
    if e_shoff == 0:
        return 0
    if e_shnum == 0:
        # Extended section count lives in sh_size of section header 0
        e_shnum = struct.unpack_from("<Q", data, e_shoff + 32)[0]

    patched = 0
    sym_entry_size = 24  # sizeof(Elf64_Sym)

    for index in range(e_shnum):
        sh_off = e_shoff + index * e_shentsize
        sh_type = struct.unpack_from("<I", data, sh_off + 4)[0]
        if sh_type != SHT_SYMTAB:
            continue

        # Section header: sh_offset(24), sh_size(32), sh_link(40)
        sym_off, sym_size = struct.unpack_from("<QQ", data, sh_off + 24)
        sh_link = struct.unpack_from("<I", data, sh_off + 40)[0]
        str_off = struct.unpack_from(
            "<Q", data, e_shoff + sh_link * e_shentsize + 24
        )[0]

        for entry_off in range(sym_off, sym_off + sym_size, sym_entry_size):
            # Elf64_Sym: st_name(4), st_info(1), st_other(1), ...
            st_name = struct.unpack_from("<I", data, entry_off)[0]
            st_other = data[entry_off + 5]
            if not (st_other & STV_VISIBILITY_MASK):
                continue

            name_start = str_off + st_name
            name_end = data.index(b"\x00", name_start)
            sym_name = bytes(data[name_start:name_end])

            if sym_name in symbols_to_globalize:
                data[entry_off + 5] = st_other & ~STV_VISIBILITY_MASK
                patched += 1

    return patched


def process_archive(archive_path: Path, symbols_file: Path, output_path: Path):
    """Patch the archive in-place by parsing BSD ar format directly.

    This avoids `ar -x` which loses members when filenames collide.
    """
    symbols = parse_symbols_file(symbols_file)
    # ELF symbol names have no Mach-O underscore prefix
    elf_symbols = {s[1:] if s.startswith(b"_") else s for s in symbols}
    print(f"Symbols to globalize: {len(symbols)}")

    data = bytearray(archive_path.read_bytes())

    if data[:8] != AR_MAGIC:
        raise RuntimeError("Not a valid ar archive")

    total_patched = 0
    members_scanned = 0
    pos = 8  # skip ar magic

    while pos < len(data):
        if pos + AR_HEADER_SIZE > len(data):
            break

        # Parse ar member header (60 bytes):
        # name[16] date[12] uid[6] gid[6] mode[8] size[10] fmag[2]
        header = data[pos:pos + AR_HEADER_SIZE]
        size_str = header[48:58].decode("ascii").strip()
        fmag = header[58:60]

        if fmag != b"`\n":
            break  # Invalid header

        member_size = int(size_str)
        member_start = pos + AR_HEADER_SIZE
        member_end = member_start + member_size

        # Get the member name for diagnostics
        name_field = header[0:16].decode("ascii").strip()

        # Skip ar metadata: BSD "__.SYMDEF*" index, GNU "/" symbol index and
        # "//" long-name table. GNU "/NN" entries are real members whose name
        # lives in the long-name table.
        if name_field in ("/", "//") or name_field.startswith("__.SYMDEF"):
            pass  # ar metadata, skip patching
        else:
            # BSD ar may have extended names: "#1/NN" means NN bytes of name
            # prepended to the member data
            obj_offset = member_start
            if name_field.startswith("#1/"):
                name_len = int(name_field[3:])
                obj_offset = member_start + name_len

            # Try to patch this member's object data (Mach-O or ELF)
            member_data = data[obj_offset:member_end]
            if len(member_data) >= 4:
                member_buf = bytearray(member_data)
                if member_data[:4] == ELF_MAGIC:
                    patched = globalize_elf(member_buf, elf_symbols)
                else:
                    patched = globalize_macho(member_buf, symbols)
                if patched > 0:
                    data[obj_offset:member_end] = member_buf
                    total_patched += patched

            members_scanned += 1

        # Advance to next member (2-byte aligned)
        pos = member_end
        if pos % 2 != 0:
            pos += 1

    print(f"Patched {total_patched} symbol(s) across {members_scanned} member(s)")

    output_path.write_bytes(data)

    # Regenerate the symbol table so the linker can find the now-global symbols
    # ranlib updates __.SYMDEF which the linker uses for symbol lookup
    import subprocess
    subprocess.run(["ranlib", str(output_path)], check=True)

    print(f"Output: {output_path}")


def main():
    if len(sys.argv) != 4:
        print(f"Usage: {sys.argv[0]} <archive.a> <symbols_file> <output.a>")
        return 1

    archive_path = Path(sys.argv[1])
    symbols_file = Path(sys.argv[2])
    output_path = Path(sys.argv[3])

    if not archive_path.exists():
        print(f"Error: archive not found: {archive_path}")
        return 1
    if not symbols_file.exists():
        print(f"Error: symbols file not found: {symbols_file}")
        return 1

    process_archive(archive_path, symbols_file, output_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
