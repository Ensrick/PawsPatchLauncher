"""Disassemble GTID and enumerate direct references for the local capacity audit."""
import argparse,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from capstone import *
from capstone.x86 import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes();base=0x460000
cs=Cs(CS_ARCH_X86,CS_MODE_32);cs.detail=True
lines=[]
def section(start,end):
 lines.append('\nSECTION '+hex(start))
 for x in cs.disasm(raw[start-base:end-base],start):lines.append(f'{x.address:08x} {x.bytes.hex():26} {x.mnemonic} {x.op_str}')
section(0x69f4cb,0x69fa20)
for s,e in [(0x4810f0,0x481180),(0x4e56d0,0x4e5770),(0x4e5850,0x4e59a0),(0x522a50,0x522b00),(0x5c03d0,0x5c0580),(0x694440,0x694660)]:section(s,e)
lines.append('\nREFERENCES (linear disassembly; candidates require function-boundary review)')
cs.skipdata=True
for x in cs.disasm(raw[0x1000:0x483000],base+0x1000):
 if x.id==0:continue
 hits=[]
 for o in x.operands:
  if o.type==X86_OP_MEM and o.mem.disp in (0x20004,0x60004,0x60008,0x6000c,0x60010,0xa4f72c):hits.append(hex(o.mem.disp))
  if o.type==X86_OP_IMM and (0x69f4cb<=o.imm<0x69fa20 or o.imm==0x481117):hits.append(hex(o.imm))
 if hits:lines.append(f'{x.address:08x} {x.bytes.hex():26} {x.mnemonic} {x.op_str} ; {hits}')
a.out.write_text('\n'.join(lines));print(a.out)
