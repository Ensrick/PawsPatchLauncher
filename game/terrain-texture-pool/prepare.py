"""Prepare the terrain texture pool overlay from an installed 1.3.72 game; never edits the game.

prepare.py --game <Kohan II folder with Data.rwd> --out <new directory>

Reads UVars_k2.tgi from Data.rwd (bounded TGCK reader, same layout and limits as tools/RwdTool/RwdArchive.cs),
requires the retail bytes, adds one variable before the closing brace and writes
<out>/data/UVars_k2.tgi plus <out>/manifest.json (before/after SHA-256).
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

SOURCE = 'UVars_k2.tgi'
TARGET = 'data/UVars_k2.tgi'
STOCK_SHA256 = '8e6257c30cfcefcc0661917aa0d5c5966281bea9e5741baa582844d839d700ed'
ENTRIES = 1024
LINE = (b'\t\tint TerrainTexturePoolDesiredNumEntries = %d\t\t'
        b';; terrain-texture-pool overlay: gameplay zoom 2.0 and far plane 1024 need more than the stock 120\r\n' % ENTRIES)


def read_entry(archive, wanted):
    """The bytes of one entry, by name (case-insensitive). Every offset and size is checked against the file."""
    with open(archive, 'rb') as f:
        f.seek(0, 2)
        length = f.tell()
        f.seek(0)
        magic, version, count = struct.unpack('<4sII', f.read(12))
        if magic != b'TGCK' or version != 2 or not 3 <= count <= 16:
            raise ValueError('not a Kohan II TGCK version 2 archive')
        table = length - 96 * count
        if table < 12:
            raise ValueError('section table does not fit')
        f.seek(table)
        sections = {}
        for _ in range(count):
            raw = f.read(96)
            name = raw[:64].decode('utf-16-le').split('\0', 1)[0]
            offset, size, _unused, repeated = struct.unpack_from('<qqQq', raw, 64)
            if offset < 12 or size < 0 or offset > table or size > table - offset or repeated != size:
                raise ValueError('section %r lies outside the archive' % name)
            sections[name] = (offset, size)
        files, footer = sections['Files'], sections['Footer']
        f.seek(footer[0])
        end = footer[0] + footer[1]
        (entries,) = struct.unpack('<I', f.read(4))
        if entries > 100000 or entries > (footer[1] - 4) // 28:
            raise ValueError('excessive entry count')
        found = None
        for _ in range(entries):
            (chars,) = struct.unpack('<H', f.read(2))
            if not 0 < chars <= 1024 or end - f.tell() < chars * 2 + 24:
                raise ValueError('footer ends inside an entry')
            name = f.read(chars * 2).decode('utf-16-le').replace('\\', '/')
            offset, size, unknown, _index = struct.unpack('<qqIi', f.read(24))
            if unknown != 0 or offset < 0 or size < 0 or offset > files[1] or size > files[1] - offset:
                raise ValueError('entry %r lies outside the Files section' % name)
            if name.lower() == wanted.lower():
                if found:
                    raise ValueError('%s appears twice' % wanted)
                found = (offset, size)
        if f.tell() != end:
            raise ValueError('unexpected footer trailing bytes')
        if not found:
            raise ValueError('%s not found' % wanted)
        f.seek(files[0] + found[0])
        return f.read(found[1])


def overlay(stock):
    """Stock bytes plus LINE before the final closing brace; nothing else changes."""
    if hashlib.sha256(stock).hexdigest() != STOCK_SHA256:
        raise ValueError('%s is not the retail 1.3.72 file' % SOURCE)
    if b'TerrainTexturePool' in stock or not stock.endswith(b'}\r\n'):
        raise ValueError('unexpected %s layout' % SOURCE)
    return stock[:-3] + LINE + stock[-3:]


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--game', type=Path, required=True)
    p.add_argument('--out', type=Path, required=True)
    a = p.parse_args()
    if a.out.exists():
        raise SystemExit('Choose a new output directory.')
    stock = read_entry(a.game / 'Data.rwd', SOURCE)
    after = overlay(stock)
    (a.out / 'data').mkdir(parents=True)
    (a.out / TARGET).write_bytes(after)
    manifest = [{'path': TARGET, 'before': STOCK_SHA256, 'after': hashlib.sha256(after).hexdigest()}]
    (a.out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('TERRAIN_POOL_PREPARED %s entries=%d after=%s' % (TARGET, ENTRIES, manifest[0]['after']))


if __name__ == '__main__':
    main()
