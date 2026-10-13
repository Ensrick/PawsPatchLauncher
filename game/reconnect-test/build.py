"""Build an opt-in 1.3.72 recovery plugin. No installation or game launch."""
import argparse, hashlib, json, struct, subprocess, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HASH = 'b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
BASE = 0x460000
# Individually located/disassembled in the verified image. Never apply a version-wide delta.
RANGES = {
    'string_ctor': (0x205de, 32), 'string_release': (0x21375, 26),
    'script_read': (0x13df06, 19), 'script_run': (0x1f9963, 34),
    'player_leave': (0x14b7c5, 146), 'leave_call': (0x157080, 18),
    'quit_request': (0x151cb5, 185), 'quit_call': (0x1503e4, 12),
    'session_read': (0x15bbe3, 22), 'world_read': (0x162ac1, 19),
    'end_mission': (0xc3efb, 0x275), 'game_type': (0x13c1a4, 7),
    'save_order_ctor': (0x121bd0, 49), 'save_order_issue': (0x122822, 95),
    'save_host_call': (0x162c30, 15), 'save_task_create': (0x16a9f6, 0xa2),
    'wanted_type': (0x150009, 0x1d6), 'write_call': (0x150271, 10),
    'schedule_call': (0x15c564, 12), 'connections': (0x15ba42, 23),
    'net_admin': (0x15c551, 23), 'vote': (0xbc599, 38),
    'task_block': (0x1ce7e9, 80),
    'join_ip_call': (0xef17e, 24), 'join_port_call': (0xef19b, 22),
    'widget_text': (0x29fc02, 4),
    'backup_flag_write': (0x14f9a1, 17), 'disconnecting_flag_write': (0x14fb31, 17),
    'active_clients': (0x14c03f, 24),
    'recovery_admin_globals': (0x150009, 0x29),
    # The save task's source walk, repeated read-only before a recovery save (issue #10).
    'save_source_walk': (0x1c1a22, 22), 'save_settings_read': (0x1c1a56, 9),
    'save_source_step': (0x1c1a99, 37), 'settings_type': (0x16bf8f, 22),
}
CALLS = {
    0x15708c: 0x14b7c5, 0x1503ea: 0x151cb5, 0x162c3a: 0x16a9f6,
    0x150273: 0x150009, 0x15c568: 0x150009,
    0xc4143: 0x8523a, 0xc414d: 0x85139,
    0x11f708: 0x121bd0, 0x11f718: 0x122822,
    0xef184: 0x29fc02, 0xef1a6: 0x29fc02,
}

def main():
    p=argparse.ArgumentParser();p.add_argument('--image', type=Path, required=True)
    p.add_argument('--deps', type=Path, required=True);p.add_argument('--compiler',type=Path,required=True)
    p.add_argument('--out',type=Path,required=True);a=p.parse_args()
    sys.path.insert(0,str(a.deps));import capstone
    raw=a.image.read_bytes();assert hashlib.sha256(raw).hexdigest()==HASH
    cs=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32);cs.detail=True
    guards=[]
    for name,(rva,size) in RANGES.items():
        instructions=[]
        for i in cs.disasm(raw[rva:rva+size+15],BASE+rva):
            instructions.append(i)
            if i.address+i.size>=BASE+rva+size:break
        assert instructions and instructions[-1].address+instructions[-1].size>=BASE+rva+size,name
        data=raw[rva:instructions[-1].address+instructions[-1].size-BASE]; fixups=[]
        for i in instructions:
            for op in i.operands:
                if op.type==capstone.x86.X86_OP_MEM and BASE <= (op.mem.disp&0xffffffff) < BASE+len(raw):
                    assert i.disp_size==4;fixups.append(i.address-BASE-rva+i.disp_offset)
                elif op.type==capstone.x86.X86_OP_IMM and not (i.group(capstone.CS_GRP_CALL) or i.group(capstone.CS_GRP_JUMP)) and BASE <= (op.imm&0xffffffff) < BASE+len(raw):
                    assert i.imm_size==4;fixups.append(i.address-BASE-rva+i.imm_offset)
        guards.append(dict(name=name,rva=rva,data=data.hex(),fixups=sorted(set(fixups))))
    for site,target in CALLS.items():
        assert raw[site]==0xe8 and site+5+struct.unpack_from('<i',raw,site+1)[0]==target, hex(site)
    a.out.mkdir(parents=True,exist_ok=True)
    lines=['/* Generated from the hash-bound 1.3.72 image; do not hand edit. */',
           'typedef struct { unsigned rva, size, count; const unsigned char *bytes; const unsigned *fixups; const char *name; } Guard;']
    for n,g in enumerate(guards):
        lines.append('static const unsigned char bytes%d[]={%s};'%(n,','.join('0x'+g['data'][j:j+2] for j in range(0,len(g['data']),2))))
        lines.append('static const unsigned fixups%d[]={%s};'%(n,','.join(map(str,g['fixups'])) or '0'))
    lines.append('static const Guard guards[]={')
    for n,g in enumerate(guards):lines.append('{%d,%d,%d,bytes%d,fixups%d,"%s"},'%(g['rva'],len(g['data'])//2,len(g['fixups']),n,n,g['name']))
    lines.append('};')
    (a.out/'guards.h').write_text('\n'.join(lines)+'\n')
    (a.out/'guards.json').write_text(json.dumps(dict(imageSha256=HASH,guards=guards,calls=CALLS),indent=2))
    common=[str(a.compiler),'-Wall','-Werror','-I'+str(a.out),'-I'+str(HERE)]
    subprocess.run(common+['-shared',str(HERE/'reconnect.c'),'-o',str(a.out/'paws_reconnect.dll'),'-lkernel32','-luser32'],check=True)
    subprocess.run(common+['-DPAWS_TEST',str(HERE/'reconnect.c'),'-o',str(a.out/'ReconnectTests.exe'),'-lkernel32','-luser32'],check=True)
    subprocess.run([str(a.out/'ReconnectTests.exe')],check=True)
    subprocess.run(common+[str(HERE/'integrated_tests.c'),'-o',str(a.out/'IntegratedTests.exe'),'-lkernel32','-luser32'],check=True)
    subprocess.run([str(a.out/'IntegratedTests.exe')],check=True)
    subprocess.run(common+['-shared','-DPAW_RECOVERY_INTEGRATED',str(HERE/'reconnect.c'),
        str(HERE.parent/'lobby-compatibility/lobby_compatibility.c'),
        '-o',str(a.out/'paws_recovery_integrated.dll'),'-lkernel32','-luser32'],check=True)
    subprocess.run(common+['-shared',str(HERE/'private_folder.c'),'-o',str(a.out/'shfolder.dll'),'-lkernel32'],check=True)
    # TCC decorates stdcall exports, but Kohan requests the documented undecorated
    # names. Rename only this DLL's two export strings, preserving their RVAs/ABI.
    import pefile
    dll=a.out/'shfolder.dll';pe=pefile.PE(str(dll));data=bytearray(dll.read_bytes())
    expected={b'_SHGetFolderPathA@20':b'SHGetFolderPathA',b'_SHGetFolderPathW@20':b'SHGetFolderPathW'}
    assert {s.name for s in pe.DIRECTORY_ENTRY_EXPORT.symbols}==set(expected)
    for s in pe.DIRECTORY_ENTRY_EXPORT.symbols:
        off=s.name_offset;old=s.name;new=expected[old]
        assert data[off:off+len(old)]==old
        data[off:off+len(old)]=new+b'\0'*(len(old)-len(new))
    pe.close();dll.write_bytes(data)
    print('Built recovery plugin; %d exact code guards; no game launched.'%len(guards))

if __name__=='__main__':main()
