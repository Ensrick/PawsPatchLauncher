"""Execute the emitted x86 hook, preserving native mask-update ABI and scope."""
import argparse,json,struct,sys,math
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--native',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE,UC_HOOK_MEM_WRITE
from unicorn.x86_const import *
from build_formation import PATTERNS
m=json.loads((a.native/'radius.json').read_text());source=(a.native/'radius.bin').read_bytes();checks=0
def check(v):
 global checks
 checks+=1;assert v,checks
def run(image,n,pattern=0,old=15,custom=False,foreign=False):
 u=Uc(UC_ARCH_X86,UC_MODE_32);cave=0x60000000;data=0x30000000;stack=0x70000000;end=stack+0xff00
 u.mem_map(image,0x700000);u.mem_map(cave,m['allocation']);u.mem_map(data,0x10000);u.mem_map(stack,0x10000)
 code=bytearray(source)
 for off,im,cv in m['fixups']:struct.pack_into('<I',code,off,(struct.unpack_from('<I',code,off)[0]+im*(image-m['image'])+cv*(cave-m['cave']))&0xffffffff)
 u.mem_write(cave,bytes(code));g=data;comp=g+0x1000;form=g+0x2000;table=form+0x100
 def put(p,v):u.mem_write(p,struct.pack('<I',v))
 def floats(p,*v):u.mem_write(p,struct.pack('<'+'f'*len(v),*v))
 def read(p):return struct.unpack('<I',u.mem_read(p,4))[0]
 cols,points=PATTERNS[pattern]
 put(g,image+0x4e7d60+(4 if foreign else 0));put(comp+4,g);put(g+0x194,n);put(g+0x1b8,form);put(form+24,table);put(form+28,6)
 floats(table,*sum(([x,y] for x,y in points),[]))
 if custom:floats(table,123)
 sp=stack+0xd000;put(sp,end);floats(sp+4,241.25,138.75,old)
 regs=[UC_X86_REG_EBX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_EDX]
 for reg in regs:u.reg_write(reg,0x12345678)
 for i in range(8):u.reg_write(UC_X86_REG_XMM0+i,0x1234567890+i)
 u.reg_write(UC_X86_REG_FPCW,0x37f);u.reg_write(UC_X86_REG_MXCSR,0x1f80);u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,comp)
 result=[];writes=[]
 def hook(uc,addr,size,_):
  if addr==image+m['target']:
   check(uc.reg_read(UC_X86_REG_ESP)==sp and read(sp)==end and uc.reg_read(UC_X86_REG_ECX)==comp)
   check(all(uc.reg_read(reg)==0x12345678 for reg in regs) and uc.reg_read(UC_X86_REG_EAX)==image+m['seh'])
   check(all(uc.reg_read(UC_X86_REG_XMM0+i)==0x1234567890+i for i in range(8)))
   check(uc.reg_read(UC_X86_REG_FPCW)==0x37f and uc.reg_read(UC_X86_REG_MXCSR)==0x1f80)
   x,y,r=struct.unpack('<fff',uc.mem_read(sp+4,12));check((x,y)==(241.25,138.75));result.append(r);uc.emu_stop()
 u.hook_add(UC_HOOK_CODE,hook);u.hook_add(UC_HOOK_MEM_WRITE,lambda uc,ac,addr,size,v,_:writes.append((addr,size)))
 u.emu_start(cave+m['offset'],end,count=2000000)
 check(len(result)==1);check(all(stack<=addr and addr+size<=sp or sp+12<=addr and addr+size<=sp+16 for addr,size in writes))
 if old<=0 or n<=6 or n>256 or custom or foreign:check(result[0]==old)
 else:
  step=points[cols][1]-points[0][1];pts=[(points[i%cols][0],points[i%cols][1]+(i//cols)*step) for i in range(n)];mx=sum(x for x,y in pts)/n;my=sum(y for x,y in pts)/n
  radius=max(math.hypot(x-mx,y-my) for x,y in pts)+4
  check(abs(result[0]-max(old,radius))<.001)
for image in (0x390000,0x460000,0x18000000):
 for pat in range(3):
  for n in (1,6,7,8,13,14,20,64,128,255,256,257):run(image,n,pat)
 for kwargs in (dict(custom=True),dict(foreign=True),dict(old=200),dict(old=-1),dict(old=0)):run(image,13,**kwargs)
print('GROUP_FORMATION_RADIUS_NATIVE_PASS',checks,'assertions')
