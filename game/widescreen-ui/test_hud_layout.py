"""Execute emitted presentation hooks across aspect ratios and app states.

Checks full register/flag/stack preservation, exact widget scope, independent
hit rectangles and native tooltip arguments. No running game is accessed.
"""
import argparse,struct,json,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--emitted',type=Path,required=True);p.add_argument('--fixups',type=Path,required=True);a=p.parse_args()
sys.path.insert(0,str(a.legacy/'lobby_colors_1372/deps_r15'))
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *
checks=0
def check(ok,message):
 global checks
 assert ok,message
 checks+=1
sizes=[(1024,768),(1280,1024),(1280,720),(1920,1080),(1920,1200),(2560,1440),(2560,1080),(3440,1440),(3840,2160)]
for image,cave in [(0x460000,0x10000000),(0xb10000,0x20000000),(0x18000000,0x08000000)]:
 for w,h in sizes:
  logical=max(1024,w/h*768);scale=w/logical
  for phase in [0,2]:
   u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(image,0x700000);u.mem_map(cave,0x2000);heap=0x30000000;u.mem_map(heap,0x20000)
   def word(at,v):u.mem_write(at,struct.pack('<I',v&0xffffffff))
   def flt(at,v):u.mem_write(at,struct.pack('<f',v))
   code=bytearray((a.emitted/'hud-layout.bin').read_bytes())
   for kind,off,rva in json.loads(a.fixups.read_text()):wordval=image+rva if kind==1 else image+rva-(cave+0x1000+off+4);struct.pack_into('<I',code,off,wordval&0xffffffff)
   u.mem_write(cave+0x1000,bytes(code))
   pc=bytearray((a.emitted/'uniform-phase.bin').read_bytes())
   for off,rva in [(1,0x5f92f4),(13,0x5f92b4),(23,0x48d0dc)]:struct.pack_into('<I',pc,off,image+rva)
   u.mem_write(cave+0xd00,bytes(pc))
   word(image+0x5f92f4,phase);word(image+0x5f92b4+phase*4,heap+0x100);word(heap+0x100,image+(0x48d0dc if phase==2 else 0x49d488))
   word(image+0x5f9228,w);word(image+0x5f94d0,heap+0x200);word(heap+0x288,heap+0x300);flt(heap+0x300,logical)
   ve=heap+0x1000;owner=heap+0x2000;name=heap+0x3000;stack=heap+0x18000;stop=heap+0x19000
   for key,left,width,offset in [('ObjectDisplay',295,456,None),('FavoritesDisplay',304,433,8.5),('ConnectionStatusDialog',176,675,1.5),('EconomicImpact',544,480,None),('OrganizationEditor',226,798,0),('GameOrganizationEditor',226,798,0),('Other',100,456,None),('ObjectDisplayExtra',295,456,None)]:
    for own_view in [True,False]:
     word(ve+0xc,owner);word(owner+0x10,ve if own_view else ve+0x100);word(owner+4,name);u.mem_write(name,(key+'\0').encode('utf-16le'))
     box=[left*scale,582*h/768,(left+width)*scale,762*h/768];u.mem_write(ve+0x20,struct.pack('<4f',*box));word(ve+0x88,0x12345678)
     regs={UC_X86_REG_EAX:0x101,UC_X86_REG_EBX:0x202,UC_X86_REG_ECX:0x303,UC_X86_REG_EDX:0x404,UC_X86_REG_EBP:0x505,UC_X86_REG_EDI:0x606,UC_X86_REG_ESI:ve}
     for reg,value in regs.items():u.reg_write(reg,value)
     for n in range(8):u.reg_write(UC_X86_REG_XMM0+n,(n+1)*0x123456789abcdef0123456789)
     u.reg_write(UC_X86_REG_EFLAGS,0x246);word(stack,stop);u.reg_write(UC_X86_REG_ESP,stack)
     u.emu_start(cave+0x1000,stop,count=1000)
     actual=struct.unpack('<4f',u.mem_read(ve+0x20,16));expected=box.copy()
     if phase==2 and own_view:
      if offset is not None:expected[0]=(w-width*scale)/2+offset*scale;expected[2]=expected[0]+width*scale
      elif key=='EconomicImpact':expected[0]=w-width*scale;expected[2]=w
     check(all(abs(x-y)<.001 for x,y in zip(actual,expected)),(key,phase,w,own_view,actual,expected))
     for reg,value in regs.items():check(u.reg_read(reg)==(0x12345678 if reg==UC_X86_REG_EAX else value),'general register preservation')
     for n in range(8):check(u.reg_read(UC_X86_REG_XMM0+n)==(n+1)*0x123456789abcdef0123456789,'full SSE preservation')
     check(u.reg_read(UC_X86_REG_EFLAGS)==0x246,'flags preserved');check(u.reg_read(UC_X86_REG_ESP)==stack+4,'balanced stack')
   captured=[]
   def native(uc,addr,size,data):
    if addr!=image+0x2b8185:return
    sp=uc.reg_read(UC_X86_REG_ESP);ptr=uc.reg_read(UC_X86_REG_ECX)
    captured.append((struct.unpack('<f',uc.mem_read(ptr+0x94,4))[0],struct.unpack('<3I',uc.mem_read(sp+4,12))))
    uc.reg_write(UC_X86_REG_ESP,sp+16);uc.reg_write(UC_X86_REG_EIP,struct.unpack('<I',uc.mem_read(sp,4))[0])
   u.hook_add(UC_HOOK_CODE,native)
   for anchor in [1024.,1020.,555.]:
    flt(owner+0x94,anchor);u.mem_write(stack,struct.pack('<4I',stop,11,22,33));u.reg_write(UC_X86_REG_ESP,stack);u.reg_write(UC_X86_REG_ECX,owner);u.reg_write(UC_X86_REG_ESI,0x123456)
    u.emu_start(cave+0x1500,stop,count=1000)
    check(abs(captured[-1][0]-(logical if phase==2 and anchor==1024 else anchor))<.001,'ordinary tooltip anchor')
    check(captured[-1][1]==(11,22,33),'native text/font/format arguments preserved')
    check(struct.unpack('<f',u.mem_read(owner+0x94,4))[0]==anchor,'interface restored after tooltip')
    check(u.reg_read(UC_X86_REG_ESP)==stack+16 and u.reg_read(UC_X86_REG_ESI)==0x123456,'tooltip calling convention')
print('HUD_LAYOUT_PASS',checks,'checks; 3 relocations, 9 resolutions, menus and match; actual emitted code')
