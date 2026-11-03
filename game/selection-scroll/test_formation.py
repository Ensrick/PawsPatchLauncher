"""Execute native GGroup::CalculateFormation, with only object lookup mocked."""
import argparse,struct,sys,json,math
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--native',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *
from capstone import Cs,CS_ARCH_X86,CS_MODE_32,CS_OP_MEM
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes();meta=json.loads((a.native/'formation.json').read_text());checks=0
def check(v):
 global checks
 checks+=1
 assert v,checks
def run(image,n,points,patched):
 u=Uc(UC_ARCH_X86,UC_MODE_32);cave=0x60000000;data=0x30000000;stack=0x70000000;end=stack+0xff00
 u.mem_map(image,0x700000);u.mem_write(image,raw);u.mem_map(cave,4096);u.mem_map(data,0x20000);u.mem_map(stack,0x10000)
 # Relocate absolute native constant references just as the PE loader does.
 md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
 for ins in md.disasm(raw[0x233718:0x2338d0],image+0x233718):
  for op in ins.operands:
   if op.type==CS_OP_MEM and not op.mem.base and not op.mem.index and 0x460000<=op.mem.disp<0xb60000:
    u.mem_write(ins.address+ins.disp_offset,struct.pack('<I',op.mem.disp+image-0x460000))
 if patched:
  b=bytearray((a.native/'formation.bin').read_bytes())
  for off,rv in meta['fixups']:struct.pack_into('<I',b,off,(image+rv-(cave+off+4))&0xffffffff)
  u.mem_write(cave,bytes(b));u.mem_write(image+meta['site'],b'\xe9'+struct.pack('<i',cave-image-meta['site']-5)+b'\x90')
 g=data;form=data+0x1000;members=data+0x2000;table=data+0x10000;origin=data+0x11000;direction=origin+8
 def put(addr,v):u.mem_write(addr,struct.pack('<I',v))
 def read(addr):return struct.unpack('<I',u.mem_read(addr,4))[0]
 put(g+0x190,members);put(g+0x194,n);put(g+0x1b8,form if points is not None else 0)
 if points is not None:
  put(form+24,table);put(form+28,len(points));u.mem_write(table,b''.join(struct.pack('<ff',*v) for v in points))
 u.mem_write(origin,struct.pack('<ffff',0,0,1,0));fallback=[]
 def hook(uc,address,size,_):
  sp=uc.reg_read(UC_X86_REG_ESP)
  if address==image+0x2338d0:
   idx=read(sp+4);uc.reg_write(UC_X86_REG_EAX,data+0x12000+idx*4);uc.reg_write(UC_X86_REG_ESP,sp+8);uc.reg_write(UC_X86_REG_EIP,read(sp))
  elif address==image+0x231d6b:
   idx=(read(sp+8)-data-0x12000)//4;fallback.append(idx)
   uc.mem_write(read(sp+4),struct.pack('<ff',idx*.5,idx*.75));uc.reg_write(UC_X86_REG_ESP,sp+4);uc.reg_write(UC_X86_REG_EIP,read(sp))
 u.hook_add(UC_HOOK_CODE,hook);sp=stack+0xe000
 for off,v in [(0,end),(4,origin),(8,direction)]:put(sp+off,v)
 saved=[UC_X86_REG_EBX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]
 for reg in saved:u.reg_write(reg,0x12345678)
 u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,g);u.reg_write(UC_X86_REG_FPCW,0x37f);u.reg_write(UC_X86_REG_MXCSR,0x1f80)
 try:u.emu_start(image+0x233718,end,count=2000000)
 except Exception:
  print(hex(image),n,hex(u.reg_read(UC_X86_REG_EIP)));raise
 check(u.reg_read(UC_X86_REG_EIP)==end and u.reg_read(UC_X86_REG_ESP)==sp+12)
 check(all(u.reg_read(reg)==0x12345678 for reg in saved))
 check(u.reg_read(UC_X86_REG_FPCW)==0x37f)
 return [struct.unpack('<ff',u.mem_read(members+16*i+4,8)) for i in range(n)],fallback
for image in (0x460000,0xf20000,0x18000000):
 for cols,points in meta['patterns']:
  for n in (1,2,6,7,8,10,14,20,64,256):
   result,fallback=run(image,n,points,True);check(not fallback)
   expected=[(points[i%cols][0],points[i%cols][1]+(i//cols)*(points[cols][1]-points[0][1])) for i in range(n)]
   mx=sum(x for x,y in expected)/n;my=sum(y for x,y in expected)/n
   check(all(abs(x-(ex-mx))<.001 and abs(y-(ey-my))<.001 for (x,y),(ex,ey) in zip(result,expected)))
   check(len(set(result))==n)
   if n<=6:check(result==run(image,n,points,False)[0])
   if n==14:check(result==run(image,n,points,True)[0])
 for points in (None,[],[(0,0)],[(i*3,i*2) for i in range(6)],[(i*3,i*2) for i in range(9)]):
  patched=run(image,14,points,True);stock=run(image,14,points,False);check(patched==stock)
print('GROUP_FORMATION_NATIVE_PASS',checks,'checks; all three formations, 1..256 members, native centering, stock <=6, custom/no formation unchanged')
