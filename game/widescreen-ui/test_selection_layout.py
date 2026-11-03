"""Validate shipped selection layouts and execute the emitted spacing hook.

Uses the original six-card division/rounding and actual generated TGI anchors;
keeps banner, its two bars and formation buttons inside their input containers.
Does not launch or modify the game.
"""
import argparse, math, re, struct, sys
from pathlib import Path
p=argparse.ArgumentParser()
p.add_argument('--assets',type=Path,required=True)
p.add_argument('--emitted',type=Path,required=True)
p.add_argument('--legacy',type=Path,required=True)
a=p.parse_args()
sys.path.insert(0,str(a.legacy/'lobby_colors_1372/deps_r15'))
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32
from unicorn.x86_const import *
checks=0
def check(ok,why):
 global checks
 assert ok,why
 checks+=1
def block(text,name):
 m=re.search(r'\['+re.escape(name)+r'(?:\s[^\]]*)?\]\s*\{',text)
 assert m,name
 start=m.end();depth=1
 for n in range(start,len(text)):
  if text[n]=='{':depth+=1
  elif text[n]=='}':
   depth-=1
   if not depth:return text[start:n]
 raise AssertionError(name)
def anchor(text,key):
 return re.search(r'^\s*'+key+r'\s*=\s*(\S+)',text,re.M)[1]
def x(value,parent,left=0):
 if value.endswith('r'):return parent-float(value[:-1])
 if value.endswith('%'):return parent*float(value[:-1])/100
 if value.startswith('+'):return left+float(value[1:])
 return float(value)
original=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
check(original[0xdad14:0xdad1a]==bytes.fromhex('f30f5c44240c'),'exact native spacing guard')
check(struct.unpack_from('<f',original,0x8f4a7c-0x460000)[0]==6,'native six-card divisor')
u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(0x10000000,0x3000)
code=(a.emitted/'hud-layout.bin').read_bytes()[0x600:0x700]
u.mem_write(0x10000000,code);stack=0x10002000;stop=0x10001000
for scope in ('pure','arcane'):
 raw=(a.assets/scope/'payload/data/ui/game/object_display.tgi').read_bytes()
 text=raw.decode('utf-16' if raw.startswith(b'\xff\xfe') else 'utf-8')
 root=block(text,'ObjectDisplay');rootview=block(root,'View')
 company=block(root,'CompanyDisplay');group=block(root,'GroupDisplay')
 banner=block(block(company,'BannerButton'),'View')
 formations=[block(part,'FormationButtons').split('[View]',1)[1] for part in (company,group)]
 check(anchor(rootview,'L')=='295','portraits stay beside minimap')
 check(anchor(rootview,'R')=='273r','container includes right-side controls')
 for w,h in ((1024,768),(1280,1024),(1280,720),(1920,1080),(2560,1440),(3440,1440),(3840,2160)):
  logical=max(1024,w/h*768);width=logical-273-295
  # Execute the hook with a real CALL's stack layout. Widths here are logical:
  # native TGC_Widget::GetWidth (0x716fc8) already divides by UI scale.
  regs={UC_X86_REG_EAX:11,UC_X86_REG_ECX:22,UC_X86_REG_EDX:33,UC_X86_REG_EBX:44,UC_X86_REG_ESI:55,UC_X86_REG_EDI:66,UC_X86_REG_EBP:77}
  for reg,v in regs.items():u.reg_write(reg,v)
  for i in range(8):u.reg_write(UC_X86_REG_XMM0+i,(i+1)*0x123456789abcdef0123456789)
  hi=u.reg_read(UC_X86_REG_XMM0)&~0xffffffff
  u.reg_write(UC_X86_REG_XMM0,hi|struct.unpack('<I',struct.pack('<f',width))[0])
  u.reg_write(UC_X86_REG_EFLAGS,0x246);u.reg_write(UC_X86_REG_ESP,stack)
  u.mem_write(stack,struct.pack('<4If',stop,0x1234,0x2345,0x3456,44.))
  u.emu_start(0x10000000,stop,count=50)
  available=struct.unpack('<f',struct.pack('<I',u.reg_read(UC_X86_REG_XMM0)&0xffffffff))[0]
  check(abs(available-412)<.001,'stock compact card area')
  check(u.reg_read(UC_X86_REG_XMM0)&~0xffffffff==hi,'upper SSE lanes')
  for reg,v in regs.items():check(u.reg_read(reg)==v,'general registers preserved')
  for i in range(1,8):check(u.reg_read(UC_X86_REG_XMM0+i)==(i+1)*0x123456789abcdef0123456789,'other SSE registers preserved')
  check(u.reg_read(UC_X86_REG_ESP)==stack+4 and u.reg_read(UC_X86_REG_EFLAGS)==0x246,'stack and flags preserved')
  bl=x(anchor(banner,'L'),width);br=x(anchor(banner,'R'),width,bl)
  check(0<=bl<br<=width,'banner within clickable parent')
  for barname in ('Morale','AttackEfficiency'):
   view=block(block(company,barname),'View');l=x(anchor(view,'L'),width);r=x(anchor(view,'R'),width,l)
   check(bl<l<r<br,'bars follow banner')
  for form in formations:
   fl=x(anchor(form,'L'),width)
   check(fl==width-44 and br<fl and fl+44==width,'formations against right edge')
   # Native floors the six-slot step. Mini-card width is <=73 logical units;
   # the stock engine may place narrower cards in this same grid.
   for count in range(1,7):
    last=(count-1)*math.floor(available/6)
    check(last+73<=fl+1,'cards never cover formation buttons')
print('SELECTION_LAYOUT_PASS',checks,'checks; both families, 7 resolutions, 1-6 cards; emitted native hook')
