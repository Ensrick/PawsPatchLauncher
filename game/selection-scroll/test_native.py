"""Execute the compiled x86 paging policy with instrumented native UI services.
No Python reimplementation substitutes for the code under test."""
import argparse,json,struct,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--native',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'lobby_colors_1372/deps_r15'),str(a.legacy/'pydeps_readable')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *
meta=json.loads((a.native/'selection.json').read_text());checks=0
def check(v):
 global checks
 checks+=1
 assert v,checks
class Fixture:
 def __init__(self,image,cave,n):
  self.i=image;self.c=cave;self.d=cave+meta['dataOffset'];self.u=Uc(UC_ARCH_X86,UC_MODE_32)
  for addr,size in [(image,0x700000),(cave,meta['allocation']),(0x20000000,0x400000),(0x30000000,0x100000),(0x40000000,0x1000)]:self.u.mem_map(addr,size)
  blob=bytearray((a.native/'selection.bin').read_bytes())
  for off,im,cv in meta['fixups']:struct.pack_into('<I',blob,off,(struct.unpack_from('<I',blob,off)[0]+im*(image-meta['image'])+cv*(cave-meta['cave']))&0xffffffff)
  self.u.mem_write(cave,bytes(blob));self.alloc=0x20001000;self.cards=[];self.formations=[];self.pages=[];self.cardActors={};self.positions={};self.pool=[];self.created=0
  self.manager=self.new(0x300);self.ui=self.new(0x200);self.sel=self.new(0x80);self.world=self.new(0x100)
  self.root=self.widget();self.g=self.widget();self.form=self.widget();self.p(self.g+8,self.root);self.p(self.g+0xa8,self.form);self.p(self.form+8,self.g)
  self.app=self.new(0x100);self.logical=self.new(4);self.p(self.app+0x88,self.logical);self.u.mem_write(self.logical,struct.pack('<f',1920));self.p(image+0x5f94d0,self.app);self.p(image+0x5f9228,1920)
  self.actors=[];self.nodes=[]
  for j in range(n):
   actor=self.new(0x100);vt=self.new(0x200);node=self.new(8);self.p(actor,vt);self.p(actor+0x14,j+1);self.p(actor+0x7c,1)
   for off in (0xb4,0x134,0x34):self.p(vt+off,image+0x601000+off)
   self.p(node,actor)
   if self.nodes:self.p(self.nodes[-1]+4,node)
   self.nodes.append(node);self.actors.append(actor)
  self.head=self.nodes[0] if n else 0;self.p(self.sel+8,self.head);self.p(self.ui+0x13c,self.sel)
  for r,val in [(0x5f3fc0,self.manager),(0x5f3fbc,self.ui),(0x5f3fb8,self.world),(0x5f9218,2),(0x44d2b4,image+0x601200)]:self.p(image+r,val)
  self.u.mem_write(self.p(self.g+0x10)+0x20,struct.pack('<ffff',400,800,1600,1080));self.u.mem_write(self.p(self.form+0x10)+0x20,struct.pack('<ffff',1500,800,1600,1080))
  self.u.hook_add(UC_HOOK_CODE,self.hook)
 def new(self,n):r=self.alloc;self.alloc+=(n+15)&~15;return r
 def p(self,at,value=None):
  if value is None:return struct.unpack('<I',self.u.mem_read(at,4))[0]
  self.u.mem_write(at,struct.pack('<I',value&0xffffffff))
 def widget(self):
  w=self.new(0x100);vt=self.new(0x200);view=self.new(0x100);self.p(w,vt);self.p(vt+0xd0,self.i+0x601300);self.p(w+0x10,view);self.p(w+0x24,1);return w
 def card(self):
  c=self.widget();self.created+=1;v=self.p(c);self.p(v+0xec,self.i+0x601400);self.p(v+0x1c,self.i+0x601500)
  self.u.mem_write(self.p(c+0x10)+0x20,struct.pack('<ffff',0,0,95,180));return c
 def visible(self):
  return [a for c,a in sorted(self.cardActors.items(),key=lambda pair:self.positions.get(pair[0],(0,0))[0]) if self.p(c+8)==self.g and self.p(c+0x24)&1]
 def all_cards(self):return sorted(a for c,a in self.cardActors.items() if self.p(c+8)==self.g)
 def ret(self,n=0,value=0):
  sp=self.u.reg_read(UC_X86_REG_ESP);dest=self.p(sp);self.u.reg_write(UC_X86_REG_ESP,sp+4+n);self.u.reg_write(UC_X86_REG_EAX,value);self.u.reg_write(UC_X86_REG_EIP,dest)
 def hook(self,u,addr,size,data):
  if addr==0x40000000:u.emu_stop();return
  if not self.i<=addr<self.i+0x700000:return
  r=addr-self.i;s=u.reg_read(UC_X86_REG_ECX);sp=u.reg_read(UC_X86_REG_ESP);arg=lambda n:self.p(sp+4+4*n)
  if 0x2d9ceb<=r<0x2d9d2d or 0xed11c<=r<0xed16b:return
  if r==0x240f9:self.ret(value=len(self.accepted));return
  if r==0x601600:self.ret(4,int(arg(0) not in self.rejected));return
  if r==0xed6b5:self.ret(8,1 if arg(0) not in self.rejected else 0);return
  if r==0x2d84c5:
   self.accepted.append(arg(0));self.p(arg(0)+0x54,0x20000);self.ret(8);return
  if r==0xdacca:
   head=self.p(arg(0));self.cards=[]
   while head and len(self.cards)<6:self.cards.append(self.p(head));head=self.p(head+4)

   for c in self.cardActors:self.p(c+8,0)
   for j,actor in enumerate(self.cards):
    c=self.card();self.cardActors[c]=actor;self.p(s+0x90+4*j,c);self.p(c+8,s)
   self.p(s+0xb8,len(self.cards));self.pages.append(list(self.cards));self.formations=list(self.cards);self.ret(4)
  elif r==0x2b74f9:
   if arg(0)==self.form:self.formations=[]
   self.p(arg(0)+8,0);self.ret(8)
  elif r==0x20d4b:self.p(s,0);self.p(s+4,0);self.ret()
  elif r==0xdafe9:self.pool.append(self.p(arg(0)));self.p(arg(0),0);self.p(self.g+0x7c,1);self.ret(4)
  elif r==0xdaf89:
   c=self.pool.pop();self.p(self.g+0x7c,bool(self.pool));self.ret(4,c)
  elif r in (0xde633,0xde843,0xde967):self.ret(value=self.card())
  elif r==0x2b6c58:self.p(s+12,arg(0));self.ret(4)
  elif r==0x601500:self.ret(4)
  elif r==0x601400:self.cardActors[s]=arg(0);self.ret(4,s)
  elif r==0x20ad2:
   n=self.new(8);self.p(n,self.p(arg(0)))
   if self.p(s+4):self.p(self.p(s+4)+4,n)
   else:self.p(s,n)
   self.p(s+4,n);self.ret(4)
  elif r==0x2b7271:self.p(s+0x24,arg(0));self.ret(4)
  elif r==0x2b701c:self.positions[s]=struct.unpack('<ff',self.u.mem_read(sp+4,8));self.ret(8)
  elif r==0x601300:self.p(arg(0)+8,s);self.ret(4)
  elif r==0x2be624:self.ret(value=99)
  elif r==0x2e0fa6:self.ret(value=s)
  elif r==0x6010b4:self.ret(value=1)
  elif r in (0x601134,0x601034):self.ret(4,1)
  elif r==0xdc495:self.formations.append(arg(0));self.ret(4)
  elif r==0xdc623:self.ret(value=3)
  elif r==0xdae69:
   for j in range(self.p(s+0xb8)):self.p(self.p(s+0x90+j*4)+8,0)
   self.p(s+0xb8,0);self.ret()
  elif r==0x601200:self.p(arg(1),20);self.p(arg(1)+4,30);self.ret(8,1)
  elif r in (0x2f5043,0x2f4c40):self.route=r;u.emu_stop()
  else:raise AssertionError(hex(r))
 def call(self,name,*extra,wrapper=False):
  sp=0x300f0000
  if wrapper:
   args=extra;self.u.reg_write(UC_X86_REG_ECX,self.g)
   entry=self.c+next(h['offset'] for h in meta['hooks'] if h['name']==name)
  else:args=(self.i,self.d,*extra);entry=self.c+meta['exports'][name]
  self.u.mem_write(sp,struct.pack('<'+'I'*(1+len(args)),0x40000000,*args));self.u.reg_write(UC_X86_REG_ESP,sp)
  for reg,val in [(UC_X86_REG_EBX,0x1234),(UC_X86_REG_ESI,0x2345),(UC_X86_REG_EDI,0x3456),(UC_X86_REG_EBP,0x4567)]:self.u.reg_write(reg,val)
  self.u.emu_start(entry,0x40000000,count=1000000)
  check(self.u.reg_read(UC_X86_REG_EIP)==0x40000000)
  check(self.u.reg_read(UC_X86_REG_ESP)==sp+4+(4 if wrapper and name=='build' else 0))
  for reg,val in [(UC_X86_REG_EBX,0x1234),(UC_X86_REG_ESI,0x2345),(UC_X86_REG_EDI,0x3456),(UC_X86_REG_EBP,0x4567)]:check(self.u.reg_read(reg)==val)
  return self.u.reg_read(UC_X86_REG_EAX)
 def build(self):self.call('build',self.sel+8,wrapper=True)
 def wheel(self,delta,x=500,y=900):return self.call('wheel',123,(delta&65535)<<16,((y+30)<<16)|(x+20))
for image,cave in [(0x460000,0x10000000),(0xf20000,0x11000000),(0x18000000,0x12000000)]:
 for n in (0,1,2,6,7,12,40,200):
  f=Fixture(image,cave,n);before=bytes(f.u.mem_read(f.sel,0x80));nodes=[bytes(f.u.mem_read(x,8)) for x in f.nodes];f.build()
  page=f.p(f.d+52);check(page==11 if n else page==6);check(f.visible()==f.actors[:page]);check(f.all_cards()==f.actors);check(f.formations==f.actors);check(f.p(f.g+0xa8)==f.form)
  if n<=page:check(f.wheel(-120)==0)
  else:
   check(f.wheel(-60)==1 and f.p(f.d+24)==0);check(f.wheel(-60)==1 and f.p(f.d+24)==1);check(f.visible()==f.actors[1:1+page]);check(f.formations==f.actors)
   check(f.wheel(-30000)==1);check(f.visible()==f.actors[-page:]);check(f.formations==f.actors)
   check(f.wheel(-120)==1 and len(f.visible())==page);check(f.wheel(30000)==1 and f.visible()==f.actors[:page])
   check(f.all_cards()==f.actors);old=f.p(f.d+24);check(f.wheel(-120,100,900)==0);check(f.wheel(-120,1550,900)==0);check(f.wheel(-120,500,799)==0);check(f.p(f.d+24)==old)
   f.p(f.root+0x24,0);check(f.wheel(-120)==0);f.p(f.root+0x24,1)
   f.p(image+0x5f3fb8,0);check(f.wheel(-120)==0);f.p(image+0x5f3fb8,f.world)
   f.p(f.sel+8,f.nodes[1]);check(f.wheel(-120)==0);f.p(f.sel+8,f.head)
   f.wheel(-120);f.build();check(f.p(f.d+24)==0 and f.visible()==f.actors[:page])
  check(bytes(f.u.mem_read(f.sel,0x80))==before);check(all(bytes(f.u.mem_read(x,8))==b for x,b in zip(f.nodes,nodes)))
  f.call('reset',wrapper=True);check(f.p(f.d+4)==0);check(f.all_cards()==[]);check(f.wheel(-120)==0)
 # Execute the actual game's batch and admission functions, not a count model.
 raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
 for capped in (True,False):
  f=Fixture(image,cave,40);f.accepted=[];f.rejected={f.actors[3],f.actors[10]}
  for lo,hi in [(0x2d9ceb,0x2d9d2d),(0xed11c,0xed16b)]:f.u.mem_write(image+lo,raw[lo:hi])
  if not capped:f.u.mem_write(image+0xed12a,b'\x90\x90')
  vt=f.new(0x100);f.p(f.sel,vt);f.p(vt+4,image+0xed11c);f.p(vt+0x64,image+0x601600)
  for repeat in range(2):
   sp=0x300f0000;f.u.mem_write(sp,struct.pack('<II',0x40000000,f.sel+8));f.u.reg_write(UC_X86_REG_ESP,sp);f.u.reg_write(UC_X86_REG_ECX,f.sel)
   f.u.emu_start(cave+next(x['offset'] for x in meta['hooks'] if x['name']=='batch'),0x40000000,count=100000)
   check(f.u.reg_read(UC_X86_REG_ESP)==sp+8);check(len(f.accepted)==(6 if capped else 38));check(not set(f.accepted)&f.rejected);check(len(set(f.accepted))==len(f.accepted))
 # Enter exactly at the WndProc case, with its saved registers and HWND slot.
 f=Fixture(image,cave,40);f.build()
 for x,route in [(500,0x2f4c40),(100,0x2f5043)]:
  sp=0x300f0000;f.u.mem_write(sp,struct.pack('<9I',1,2,3,4,0x40000000,123,0x20a,0,0));f.u.reg_write(UC_X86_REG_ESP,sp)
  f.u.reg_write(UC_X86_REG_EBX,(-120&65535)<<16);f.u.reg_write(UC_X86_REG_EDI,((900+30)<<16)|(x+20));f.u.reg_write(UC_X86_REG_ESI,0x12345678)
  f.u.emu_start(cave+next(h['offset'] for h in meta['hooks'] if h['name']=='wheel'),0x40000000,count=100000)
  check(f.route==route);check(f.u.reg_read(UC_X86_REG_ESP)==sp);check(f.u.reg_read(UC_X86_REG_ESI)==0x12345678)
print('SELECTION_NATIVE_PASS',checks,'checks; relocated x86, native batch/admission, adaptive cards, hidden cards retained, full formations, WndProc ABI, wheel bounds, reset')
