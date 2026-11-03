"""Execute the pinned native status producer, serializer, reader and constructor.

No game starts. Only ancillary subobject constructors, logging/allocation and
stream capacity allocation are stubbed; the status instructions and bit IO run.
The candidate bytes come from TaskStatusPatch.Build via TaskStatusTests.exe.
"""
import argparse,hashlib,json,struct,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--candidate',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
I,C,O,S,T=0x460000,0x10000000,0x50000000,0x70000000,0x71000000
pack=lambda v:struct.pack('<I',v&0xffffffff)
results=[]
def machine():
 u=Uc(UC_ARCH_X86,UC_MODE_32)
 for b,n in [(I,(len(raw)+4095)&~4095),(C,4096),(O,0x10000),(S,0x10000),(T,4096),(0,4096)]:u.mem_map(b,n)
 u.mem_write(I,raw);u.mem_write(0,pack(0xffffffff))
 return u
def r(u,at):return struct.unpack('<I',u.mem_read(at,4))[0]
def w(u,at,v):u.mem_write(at,pack(v))
def ret(u,pop=0,value=None):
 sp=u.reg_read(UC_X86_REG_ESP);u.reg_write(UC_X86_REG_EIP,r(u,sp));u.reg_write(UC_X86_REG_ESP,sp+4+pop)
 if value is not None:u.reg_write(UC_X86_REG_EAX,value)
def run(u,at,args=(),ecx=0):
 sp=S+0x8000;w(u,sp,T)
 for i,v in enumerate(args):w(u,sp+4+i*4,v)
 u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,ecx);u.emu_start(I+at,T,count=20000)
 assert u.reg_read(UC_X86_REG_EIP)==T

# Prove the native constructor preserves dirty +0xa0 until the candidate hook.
# Include the real base constructor and its reset; stub only subobjects.
stubs={0x152ea7:4,0x16d05a:0,0x16d0fa:0,0x20d4b:0,0x15589f:0,
       0x152e46:4,0x148f82:0,0x16d19d:0,0x75ed2:0,0x2fe9eb:4,
       0x158202:0,0x158320:0,0x1531c6:0,0x152e78:4,0x23f57:8,0x161d0a:0}
for poison in [0,1,2,3,4,5,6,7,0x73756a2d,0xcccccccc,0xffffffff]:
 baseline=None;baseline_registers=None
 for patched in [False,True]:
  u=machine();u.mem_write(O,b'\xcc'*0x310);w(u,O+0xa0,poison)
  if patched:
   u.mem_write(C,a.candidate.read_bytes());u.mem_write(I+0x149760,b'\xe9'+pack(C-(I+0x149760+5)))
  def hook(uc,at,size,_):
   rv=at-I;sp=uc.reg_read(UC_X86_REG_ESP)
   if rv in stubs:ret(uc,stubs[rv],uc.reg_read(UC_X86_REG_ECX))
   elif rv==0x1357af:ret(uc)
   elif rv==0x2ef03c:ret(uc,value=O+0x2000)
   elif rv==0x3e34e0:
    dest,val,n=[r(uc,sp+4+i*4) for i in range(3)];uc.mem_write(dest,bytes([val&255])*n);ret(uc,value=dest)
  u.hook_add(UC_HOOK_CODE,hook);run(u,0x14954c,ecx=O)
  assert r(u,O+0xa0)==(0 if patched else poison)
  assert u.reg_read(UC_X86_REG_EAX)==O and u.reg_read(UC_X86_REG_ESP)==S+0x8004
  registers=[u.reg_read(reg) for reg in [UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_ESP,UC_X86_REG_EFLAGS]]
  if not patched:baseline=bytearray(u.mem_read(O,0x310));baseline_registers=registers
  else:
   baseline[0xa0:0xa4]=bytes(4)
   assert bytes(u.mem_read(O,0x310))==baseline and registers==baseline_registers
  results.append(dict(test='constructor',poison=hex(poison),patched=patched,status=r(u,O+0xa0)))

# Actual local status getter, then actual native three-bit output/input.
# Test all bit alignments, including reads crossing a byte boundary.
for status in [0,1,2,3,4,5,6,7,0x73756a2d]:
 for align in range(8):
  u=machine();w(u,O+0xa0,status);observed=[];assertions=[]
  def hook(uc,at,size,_):
   rv=at-I;sp=uc.reg_read(UC_X86_REG_ESP)
   if rv==0x155dc4:
    observed.append(r(uc,uc.reg_read(UC_X86_REG_ECX)+4));ret(uc,4)
   elif rv==0x51add:ret(uc,4) # fixed test buffer has sufficient room
   elif rv==0x3e2f60:
    dst,src,n=[r(uc,sp+4+i*4) for i in range(3)];uc.mem_write(dst,bytes(uc.mem_read(src,n)));ret(uc,value=dst)
   elif rv==0x30154e:assertions.append(True);uc.emu_stop()
  u.hook_add(UC_HOOK_CODE,hook);run(u,0x14af6e,ecx=O);assert observed==[status]
  stream=O+0x1000;buf=O+0x2000;w(u,stream+4,buf);w(u,stream+8,0);w(u,stream+12,align)
  run(u,0x148d89,[stream,status]);w(u,stream+8,0);w(u,stream+12,align)
  try:run(u,0x148d9c,[stream,O+0x3000])
  except AssertionError:assert assertions
  assert bool(assertions)==((status&7)>=5)
  if not assertions:assert r(u,O+0x3000)==(status&7)
  results.append(dict(test='wire',status=hex(status),align=align,asserted=bool(assertions)))
a.out.write_text(json.dumps(dict(passed=True,cases=len(results),results=results),indent=2));print('TASK_STATUS_NATIVE_PASS',len(results),'cases')
