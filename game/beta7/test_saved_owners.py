"""Execute compiled owner hooks offline; includes the old Rakshasa save regression."""
import argparse
import json
import os
from pathlib import Path
import struct
import subprocess
import sys

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--helpers', type=Path, required=True)
p.add_argument('--legacy', type=Path, required=True)
p.add_argument('--pure', action='store_true', help='Test the four shared Vanilla/Immortals helpers')
a = p.parse_args()
sys.path.insert(0, str(a.legacy / 'lobby_colors_1372/deps_r15'))
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import *

root = Path(__file__).resolve().parent
out = a.helpers / 'saved-owner-tests'
out.mkdir(exist_ok=True)
exporter = out / 'ExportSavedOwnerFixtures.exe'
subprocess.run([str(Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'),
                '/nologo', '/target:exe', '/platform:x86', '/out:' + str(exporter),
                str(root / 'ExportSavedOwnerFixtures.cs')], check=True)
checks = 0

def check(value, label):
    global checks
    assert value, label
    checks += 1

variants = json.loads((root / 'variants.json').read_text())
if a.pure:
    variants = {path.name: '' for path in a.helpers.glob('k2_paws_pure*.exe')}
    assert len(variants) == 4, variants
tested = 0
for name, switches in variants.items():
    if 'SYNC_ONLY' in switches:
        continue  # These variants never install either ownership hook.
    tested += 1
    fixtures = out / name
    subprocess.run([str(exporter), str(a.helpers / name), str(fixtures)], check=True)
    mappings = [line.split('\t') for line in (fixtures / 'mappings.tsv').read_text().splitlines()]
    for base_index, (image, stub) in enumerate([(0x460000, 0x10000000), (0xe40000, 0x21000000), (0x12000000, 0x60000000)]):
        for site in (0, 1):
            u = Uc(UC_ARCH_X86, UC_MODE_32)
            u.mem_map(image, 0x700000)
            u.mem_map(stub, 0x10000)
            u.mem_map(0x30000000, 0x10000)
            u.mem_map(0x31000000, 0x10000)
            u.mem_map(0x32000000, 0x10000)
            u.mem_write(stub, (fixtures / (str(base_index) + '-' + str(site) + '.bin')).read_bytes())
            actor, data, text = 0x30001000, 0x30002000, 0x30003000
            world, array, session = 0x30004000, 0x30005000, 0x30006000
            kingdoms = [0x31000100 + i * 0x100 for i in range(37)]
            stack = 0x32008000
            target = image + (0x22c7cc if site == 0 else 0x22d00f)

            def w(addr, v): u.mem_write(addr, struct.pack('<I', v & 0xffffffff))
            def r(addr): return struct.unpack('<I', u.mem_read(addr, 4))[0]
            w(image + 0x5f3fb8, world)
            w(world + 0x150, array)
            for i, kingdom in enumerate(kingdoms): w(array + 4*i, kingdom)
            w(actor + 4, data)
            w(data + 8, text)
            regs = {UC_X86_REG_EBX: 0x12345678, UC_X86_REG_ECX: 0x23456789,
                    UC_X86_REG_EDX: 0x34567890, UC_X86_REG_ESI: 0x456789ab,
                    UC_X86_REG_EDI: actor, UC_X86_REG_EBP: 0x32007000, UC_X86_REG_ESP: stack}

            def run(actor_ids, owner, source, expected, count=37, state=2):
                w(image + 0x5f3fe4, session if source is not None else 0)
                w(session + 0x64, source or 0)
                w(session + 0xf0, state)
                w(world + 0x154, count)
                w(actor + 0xe8, 0xdeadbeef)
                u.mem_write(text, (actor_ids + '\0').encode('utf-16le'))
                for reg, value in regs.items(): u.reg_write(reg, value)
                u.reg_write(UC_X86_REG_EAX, owner)
                u.reg_write(UC_X86_REG_EFLAGS, 0x246)
                u.emu_start(stub, target + 6, count=50000)
                check(r(actor + 0xe8) == expected, (name, site, actor_ids, source, hex(owner), hex(r(actor + 0xe8))))
                check(u.reg_read(UC_X86_REG_EAX) == expected, 'owner in eax')
                check(all(u.reg_read(reg) == value for reg, value in regs.items()), 'registers/stack preserved')
                check(u.reg_read(UC_X86_REG_EFLAGS) == 0x246, 'flags preserved')
                check(u.reg_read(UC_X86_REG_EIP) == target + 6, 'stock continuation')

            # Exact old-save layout: slot 15 is Zahra, owner 4 is kingdom_enemy.
            run('rhaksha_settlementcamp', kingdoms[4], 2, kingdoms[4], 19)
            # Serialized snapshots must retain owners even when session metadata
            # describes a new multiplayer game (host already chose all owners).
            for actor_ids, _, target_index in mappings:
                target_owner = kingdoms[int(target_index)]
                for owner in (0, kingdoms[3], kingdoms[4], kingdoms[5], target_owner, kingdoms[36]):
                    run(actor_ids, owner, 2, owner)
                    if site == 0:
                        run(actor_ids, owner, 0, owner)
                # Films share source kind 2 with saves, but state 4 must
                # reproduce fresh-map families. Serialized owners stay exact.
                for owner in (kingdoms[3], kingdoms[4], kingdoms[5]):
                    expected = owner if site == 0 else target_owner
                    run(actor_ids, owner, 2, expected, state=4)
                for owner in (0, kingdoms[36], target_owner):
                    run(actor_ids, owner, 2, owner, state=4)
                for state in (0, 1, 2, 3, 5, 0xffffffff):
                    run(actor_ids, kingdoms[4], 2, kingdoms[4], state=state)
                # Same-process save -> film -> fresh match -> save; no latch.
                expected = kingdoms[4] if site == 0 else target_owner
                run(actor_ids, kingdoms[4], 2, expected, state=4)
                run(actor_ids, kingdoms[4], 0, expected)
                run(actor_ids, kingdoms[4], 2, kingdoms[4])
                run(actor_ids, kingdoms[36], 0, kingdoms[36])
                run(actor_ids, kingdoms[4], None, kingdoms[4])
            run('unmapped_player_building', kingdoms[36], 0, kingdoms[36])
            run('unmapped_player_building', kingdoms[4], 2, kingdoms[4], state=4)
            run('rhaksha_settlementcamp', kingdoms[4], 2, kingdoms[4], count=10, state=4)
            run('rhaksha_settlementcamp', kingdoms[4], None, kingdoms[4], state=4)
print('SAVED_OWNERS_NATIVE_PASS', checks, 'checks;', tested, 'compiled helpers; 3 image bases; no game launched')
