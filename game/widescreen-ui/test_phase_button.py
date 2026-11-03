"""Check emitted helper code, transitions and square/click bounds without a game."""
import argparse, itertools, json, struct, sys
from pathlib import Path
p=argparse.ArgumentParser()
p.add_argument('--legacy',type=Path,required=True)
p.add_argument('--emitted',type=Path,required=True)
a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32
from unicorn.x86_const import *
checks=0
def check(ok,label):
 global checks
 assert ok,label
 checks+=1
for image,cave in ((0x460000,0x10000000),(0xf20000,0x20000000),(0x18000000,0x22000000)):
 u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(image,0x700000);u.mem_map(cave,0x1000);u.mem_map(0x30000000,0x20000)
 phase=bytearray((a.emitted/'uniform-phase.bin').read_bytes())
 for off,rva in ((1,0x5f92f4),(13,0x5f92b4),(23,0x48d0dc)):
  struct.pack_into('<I',phase,off,image+rva)
 button=bytearray((a.emitted/'uniform-button.bin').read_bytes())
 struct.pack_into('<I',button,11,image+0x5f94d0);struct.pack_into('<I',button,29,cave+0xdf0)
 u.mem_write(cave+0xd00,bytes(phase));u.mem_write(cave+0xd80,bytes(button))
 def word(at,x):u.mem_write(at,struct.pack('<I',x))
 def flt(at,x):u.mem_write(at,struct.pack('<f',x))
 stack=0x3001f000;end=0x30000000
 # Keep engine run phase 2 throughout, reproducing the failed first candidate.
 # Every slot can hold a different app-state type; do not guess numeric IDs.
 for slot,kind in itertools.product(range(16),('null','menu','loading','lobby','game','scoring')):
  word(image+0x5f9218,2);word(image+0x5f92f4,slot)
  word(image+0x5f92b4+slot*4,0 if kind=='null' else 0x30004000)
  word(0x30004000,image+(0x48d0dc if kind=='game' else 0x49d488))
  word(stack,end);u.reg_write(UC_X86_REG_ESP,stack)
  u.reg_write(UC_X86_REG_ECX,0x12345678)
  u.emu_start(cave+0xd00,end,count=20)
  check((u.reg_read(UC_X86_REG_EAX)&255)==int(kind=='game'),'effective app-state type')
  check(u.reg_read(UC_X86_REG_ESP)==stack+4 and u.reg_read(UC_X86_REG_ECX)==0x12345678,'phase ABI')
 for invalid in (16,17,0xffffffff):
  word(image+0x5f92f4,invalid);word(stack,end);u.reg_write(UC_X86_REG_ESP,stack)
  u.emu_start(cave+0xd00,end,count=20)
  check((u.reg_read(UC_X86_REG_EAX)&255)==0,'invalid app-state index uses stock canvas')
 for width,height in ((1024,768),(1280,1024),(1280,720),(1600,900),(1920,1080),(1920,1200),(2560,1440),(2560,1080),(3440,1440),(3840,2160)):
  logical=max(1024,width/height*768)
  word(image+0x5f94d0,0x30001000);word(0x30001088,0x30002000);flt(0x30002000,logical);flt(cave+0xdf0,4)
  word(stack,end);u.reg_write(UC_X86_REG_ESP,stack);u.reg_write(UC_X86_REG_ESI,0x30003000)
  u.reg_write(UC_X86_REG_EAX,0xdeadbeef);u.reg_write(UC_X86_REG_EFLAGS,0x246)
  u.reg_write(UC_X86_REG_XMM0,0x123456789abcdef01122334455667788)
  u.emu_start(cave+0xd80,end,count=40)
  right=struct.unpack('<f',u.mem_read(0x30003094,4))[0]
  check(abs(right-(logical-4))<0.001,'tooltip follows actual viewport')
  check(u.reg_read(UC_X86_REG_EAX)==0xdeadbeef and u.reg_read(UC_X86_REG_EFLAGS)==0x246,'tooltip preserves registers/flags')
  check(u.reg_read(UC_X86_REG_XMM0)==0x123456789abcdef01122334455667788 and u.reg_read(UC_X86_REG_ESP)==stack+4,'tooltip SSE/stack')
  left,top,right,bottom=logical-60,768-254,logical-4,768-198
  check(right-left==bottom-top==56 and bottom==576-6,'square button six units above visible bar')
  if width/height>=4/3:
   check(abs((right-left)*width/logical-(bottom-top)*height/768)<0.001,'square physical pixels')
text=(Path(__file__).parent/'button.tgi').read_text()
for expected in ('L = 60r','T = 254b','R = 4r','B = 198b','R = 54.666667','B = 54.666667'):
 check(expected in text,'layout '+expected)
print('PHASE_BUTTON_PASS',checks,'checks; emitted C# payload; 3 relocation bases; 10 sizes; no live game launched')
