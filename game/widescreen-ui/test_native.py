"""Emulate the unchanged stock aspect calculation with the scoped UI branch.

Only executable bytes from the verified 1.3.72 research snapshot are used.
No game is launched or accessed. Engine setter/log calls are modeled.
"""
import argparse, hashlib, struct, sys, itertools
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--emitted',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
assert hashlib.sha256(raw).hexdigest().upper()=='B865D8206990C4F055C51DE857F0B09B88AB5EE3B6AE74EE5232134001AA591C'
assert raw[0x141649:0x141654]==bytes.fromhex('8b80e80000008038007439')
source=(Path(__file__).parents[1]/'beta7/GamePresentation1372.cs').read_text()
assert 'UniformGuardSite=0x141649' in source and 'UniformPhaseCode' in source
checks=0
def phase_code(image):
 b=bytearray((a.emitted/'uniform-phase.bin').read_bytes())
 for off,rva in ((1,0x5f92f4),(13,0x5f92b4),(23,0x48d0dc)):
  struct.pack_into('<I',b,off,image+rva)
 return bytes(b)
def app_state(m,image,phase):
 # The engine remains running in menus, exactly as in the user's repro.
 for at,value in ((image+0x5f9218,2),(image+0x5f92f4,phase),
                  (image+0x5f92b4+phase*4,0x30005000),
                  (0x30005000,image+(0x48d0dc if phase==2 else 0x49d488))):
  m.mem_write(at,struct.pack('<I',value))
# Verify the type identity against the original RTTI, not a guessed enum.
col=struct.unpack_from('<I',raw,0x48d0d8)[0]-0x460000
type_desc=struct.unpack_from('<I',raw,col+12)[0]-0x460000
assert raw[type_desc+8:].split(b'\0',1)[0]==b'.?AVKKC_GameAppState@@'
for image in (0x460000,0xf20000,0x18000000):
 for patched in (False,True):
  for flag,phase in itertools.product((0,1),(0,1,2,3)):
   for width,height in ((1024,768),(1280,1024),(1280,800),(1280,720),(1600,900),(1920,1080),(2560,1080),(0,900),(1600,0)):
    m=Uc(UC_ARCH_X86,UC_MODE_32);m.mem_map(image,0x700000)
    code=bytearray(raw[0x14162d:0x1416e3])
    # Absolute operands in this verified leaf; relative CALLs need no relocation.
    for at in (0x141632,0x14163a,0x141640,0x141658,0x141663,0x1416bf,0x1416c4,0x1416ce,0x1416d3):
     off=at-0x14162d;value=struct.unpack_from('<I',code,off)[0]
     struct.pack_into('<I',code,off,value+image-0x460000)
    if patched:
     code[0x141649-0x14162d:0x141652-0x14162d]=b'\xe8'+struct.pack('<i',image+0x610100-(image+0x14164e))+b'\x84\xc0\x90\x90'
     m.mem_write(image+0x610100,phase_code(image))
     cave=image+0x610000
     code[0x141688-0x14162d:0x14168d-0x14162d]=b'\xe9'+struct.pack('<i',cave-(image+0x14168d))
     clamp=bytes.fromhex('f30f5f0d')+struct.pack('<I',image+0x4b2e38)+bytes.fromhex('f30f114dfce9')+struct.pack('<i',image+0x14168d-cave-18)
     m.mem_write(cave,clamp)
    m.mem_write(image+0x14162d,bytes(code));m.mem_map(0x30000000,0x20000)
    def word(at,value):m.mem_write(at,struct.pack('<I',value))
    def flt(at,value):m.mem_write(at,struct.pack('<f',value))
    word(image+0x5f9480,0x30000000);word(image+0x5f94d0,0x30001000)
    word(0x300000e8,0x30002000);m.mem_write(0x30002000,bytes([flag]))
    word(0x30001088,0x30003000);word(0x3000108c,0x30004000)
    flt(0x30003000,1024);flt(0x30004000,768);flt(image+0x4b2e38,1024)
    word(image+0x5f9228,width);word(image+0x5f922c,height);app_state(m,image,phase)
    stop=image+0x600000;stack=0x3001f000;word(stack,stop);m.reg_write(UC_X86_REG_ESP,stack)
    def hook(uc,address,size,unused):
     if address not in (image+0x4963f,image+0x1357af):return
     esp=uc.reg_read(UC_X86_REG_ESP);ret=struct.unpack('<I',uc.mem_read(esp,4))[0]
     if address==image+0x4963f:
      uc.mem_write(uc.reg_read(UC_X86_REG_ECX),bytes(uc.mem_read(esp+4,4)));esp+=4
     uc.reg_write(UC_X86_REG_ESP,esp+4);uc.reg_write(UC_X86_REG_EIP,ret)
    m.hook_add(UC_HOOK_CODE,hook);m.emu_start(image+0x14162d,stop,count=300)
    expected=width/height*768 if ((phase==2) if patched else flag) and width>0 and height>0 else 1024
    if patched:expected=max(1024,expected)
    actual=struct.unpack('<f',m.mem_read(0x30003000,4))[0]
    assert abs(actual-expected)<0.001,(image,patched,flag,width,height,actual,expected)
    assert m.mem_read(0x30002000,1)==bytes([flag]),'User preference modified'
    assert m.reg_read(UC_X86_REG_ESP)==stack+4,'Stack imbalance'
    checks+=3
assert raw[0x13c856:0x13c85e]==bytes.fromhex('8b80e80000008a00')
assert 'UniformPollSite,8,false)' in source
# Execute the stock main-thread change detector, including its real CALL to the
# layout function (modeled here). Once refreshed it must stay idle until needed.
for image in (0x460000,0xf20000,0x18000000):
 for patched in (False,True):
  for flag,phase in itertools.product((0,1),(0,1,2,3)):
   for cached in (0,1):
    m=Uc(UC_ARCH_X86,UC_MODE_32);m.mem_map(image,0x700000);m.mem_map(0x30000000,0x20000)
    code=bytearray(raw[0x13c851:0x13c874])
    for at in (0x13c852,0x13c860,0x13c867):
     off=at-0x13c851;value=struct.unpack_from('<I',code,off)[0]
     struct.pack_into('<I',code,off,value+image-0x460000)
    if patched:
     code[0x13c856-0x13c851:0x13c85e-0x13c851]=b'\xe8'+struct.pack('<i',image+0x610100-(image+0x13c85b))+b'\x90'*3
     m.mem_write(image+0x610100,phase_code(image))
    m.mem_write(image+0x13c851,bytes(code))
    m.mem_write(image+0x5f9480,struct.pack('<I',0x30000000))
    m.mem_write(0x300000e8,struct.pack('<I',0x30001000));m.mem_write(0x30001000,bytes([flag]))
    m.mem_write(image+0x5f9948,bytes([cached]));m.reg_write(UC_X86_REG_ESP,0x3001f000)
    calls=[]
    def poll_hook(uc,address,size,unused):
     if address in (image+0x13c874,image+0x13c898):uc.emu_stop()
     elif address==image+0x14162d:
      calls.append(address);esp=uc.reg_read(UC_X86_REG_ESP)
      ret=struct.unpack('<I',uc.mem_read(esp,4))[0]
      uc.reg_write(UC_X86_REG_ESP,esp+4);uc.reg_write(UC_X86_REG_EAX,1);uc.reg_write(UC_X86_REG_EIP,ret)
    m.hook_add(UC_HOOK_CODE,poll_hook)
    app_state(m,image,phase)
    effective=int(phase==2) if patched else flag
    for tick in range(3):
     m.emu_start(image+0x13c851,image+0x13c899,count=40)
     assert len(calls)==int(cached!=effective),'Repeated or missing native refresh'
     assert m.mem_read(image+0x5f9948,1)==bytes([effective])
     assert m.mem_read(0x30001000,1)==bytes([flag]),'Persistent preference modified'
     assert m.reg_read(UC_X86_REG_ESP)==0x3001f000
     checks+=4
print('UNIFORM_UI_NATIVE_PASS',checks,'checks; 3 ASLR bases; 9 dimension cases including 5:4; four phases; menu/movie/lobby stock grid; match aspect; one native refresh; user flag and stack preserved')
