"""Execute actual GTID Add/Remove/lookup at the old and new capacity boundaries."""
import argparse, struct, sys
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--legacy',type=Path,required=True)
p.add_argument('--exe',type=Path,required=True)
a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *

raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
pe=pefile.PE(str(a.exe))
relocs=[e.rva for block in pe.DIRECTORY_ENTRY_BASERELOC for e in block.entries if e.type==3]
before=bytes.fromhex('81be0400060000800000722d')
after=bytes.fromhex('81be04000600ffff0000722d')
assert raw[0x23f61f:0x23f62b]==before
assert raw[0x23f660:0x23f668]==bytes.fromhex('bd0000010043eb13')
assert raw[0x23f685:0x23f696]==bytes.fromhex('0fb74c56048b7c2418518bc4c1e1100bca')
checks=3
REG=0x20000000; BITS=0x21000000; OBJ=0x22000000; STACK=0x23000000; EXIT=0x24000000
for image in (0x460000,0x620000,0xd00000):
 for patched in (False,True):
  for count in (32767,32768,65533,65534,65535):
   u=Uc(UC_ARCH_X86,UC_MODE_32)
   relocated=bytearray(raw)
   for r in relocs:
    if r+4<=len(relocated):struct.pack_into('<I',relocated,r,(struct.unpack_from('<I',relocated,r)[0]+image-0x460000)&0xffffffff)
   u.mem_map(image,(len(raw)+4095)&~4095);u.mem_write(image,bytes(relocated))
   if patched:u.mem_write(image+0x23f61f,after)
   u.mem_map(REG,0x61000);u.mem_map(BITS,0x2000);u.mem_map(OBJ,0x1000);u.mem_map(STACK,0x10000);u.mem_map(EXIT,0x1000)
   def w(addr,val):u.mem_write(addr,struct.pack('<I',val))
   def r(addr):return struct.unpack('<I',u.mem_read(addr,4))[0]
   # Occupy 1..count; slot zero remains permanently invalid.
   u.mem_write(REG+0x20008,struct.pack('<I',OBJ+0x100)*count)
   w(REG+0x60004,count);w(REG+0x60008,count+1 if count<65535 else 1);w(REG+0x6000c,count);w(REG+0x60010,BITS)
   u.mem_write(BITS,b'\xff'*8192)
   errors=[]
   def hook(uc,addr,size,data):
    if addr==image+0x1357af:
     errors.append(True);sp=uc.reg_read(UC_X86_REG_ESP);uc.reg_write(UC_X86_REG_EIP,r(sp));uc.reg_write(UC_X86_REG_ESP,sp+4)
   u.hook_add(UC_HOOK_CODE,hook)
   def call(rva,args):
    sp=STACK+0xf000
    u.mem_write(sp,struct.pack('<'+'I'*(len(args)+1),EXIT,*args))
    u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,REG)
    u.emu_start(image+rva,EXIT,count=1000000)
    assert u.reg_read(UC_X86_REG_EIP)==EXIT
    assert u.reg_read(UC_X86_REG_ESP)==sp+4+len(args)*4
    return u.reg_read(UC_X86_REG_EAX)
   call(0x23f619,[OBJ+0x200,OBJ])
   success=count<(65535 if patched else 32768)
   assert bool(errors)!=success
   assert r(REG+0x60004)==count+int(success)
   if success:
    index=count+1
    assert r(OBJ+0x14)==index==r(OBJ+0x200)
    assert r(REG+0x20004+index*4)==OBJ
    assert r(REG+0x60008)==(1 if index==65535 else index+1)
    assert call(0x21117,[index])==OBJ
    call(0x23f6dc,[OBJ])
    assert r(REG+0x60004)==count
    assert r(REG+0x20004+index*4)==0
    assert call(0x21117,[index])==0 # old ID cannot resolve after recycling
    w(REG+0x60008,index)
    call(0x23f619,[OBJ+0x200,OBJ])
    assert r(OBJ+0x14)==0x10000+index
    assert call(0x21117,[0x10000+index])==OBJ
    checks+=10
   else:
    assert r(OBJ+0x200)==0
    checks+=1
   assert r(REG+0x20004)==0
   checks+=5
print('LARGE_MAPS_NATIVE_PASS',checks,'checks; actual Add/Remove/lookup; 3 ASLR bases; rejection, final free slot, wrap, recycling and stale IDs; no game launched')
