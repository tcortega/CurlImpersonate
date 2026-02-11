#!/usr/bin/env python3
"""
Globalize private-external symbols in a Mach-O static archive.

The curl-impersonate static archive is built with -fvisibility=hidden, marking
all symbols as "private external" (N_PEXT). macOS's -exported_symbols_list
cannot promote private externals to global exports, so we patch the nlist
entries to clear the N_PEXT bit for symbols we need to export.

This script parses the BSD ar format directly to avoid name collision issues
with `ar -x` when multiple members share the same filename.

Usage:
    python globalize_symbols.py <archive.a> <symbols_file> <output.a>
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


def process_archive(archive_path: Path, symbols_file: Path, output_path: Path):
    """Patch the archive in-place by parsing BSD ar format directly.

    This avoids `ar -x` which loses members when filenames collide.
    """
    symbols = parse_symbols_file(symbols_file)
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

        # Skip symbol table and string table entries
        if not name_field.startswith("#1/") and (
            name_field.startswith("/") or name_field.startswith("__.SYMDEF")
        ):
            pass  # ar metadata, skip patching
        else:
            # BSD ar may have extended names: "#1/NN" means NN bytes of name
            # prepended to the member data
            obj_offset = member_start
            if name_field.startswith("#1/"):
                name_len = int(name_field[3:])
                obj_offset = member_start + name_len

            # Try to patch this member's Mach-O data
            member_data = data[obj_offset:member_end]
            if len(member_data) >= 4:
                member_buf = bytearray(member_data)
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
