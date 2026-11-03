"""Execute bytes emitted by the actual C# installer; no live game required."""
import argparse, struct, subprocess, sys, tempfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--emitter',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'lobby_colors_1372/deps_r15'),str(a.legacy/'pydeps_readable')]
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_MEM_WRITE
from unicorn.x86_const import *
checks=0
def check(v):
    global checks
    checks+=1
    assert v,checks
regs=[UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_ESP]
cases=[('same-target',{},True),('native-cohort',{'cached':1,'mode':0,'gc':0,'ai':0},True),
 ('idle',{'mode':0},False),('support',{'mode':2},False),('group-stop',{'gc':0},False),
 ('group-other-command',{'gkind':4},False),('group-ground-move',{'gf':0x10},False),
 ('group-target-move',{'gf':1},False),('group-no-target-flag',{'gf':0x40},False),
 ('group-no-target-id',{'gt':0},False),('no-ai',{'ai':0},False),('member-stop',{'mc':0},False),
 ('other-target',{'mt':73},False),('member-guard',{'mkind':6},False),
 ('member-ground-move',{'mf':0x10},False),('member-target-move',{'mf':1},False),
 ('member-no-target-flag',{'mf':0x40},False)]
with tempfile.TemporaryDirectory() as tmp:
 for image,cave in [(0x460000,0x60000000),(0xf20000,0x51000000),(0x18000000,0x28000000)]:
  payload=Path(tmp)/'gate.bin';subprocess.run([str(a.emitter),str(payload),f'{image:x}',f'{cave:x}'],check=True)
  for name,changes,expected in cases:
   u=Uc(UC_ARCH_X86,UC_MODE_32)
   for addr,size in [(cave,4096),(image+0x233000,4096),(0x30000000,0x10000),(0x70000000,0x10000)]:u.mem_map(addr,size)
   u.mem_write(cave,payload.read_bytes())
   group=0x30000100;actor=0x30001000;members=0x30002000;gc=0x30003000;mc=0x30004000;ai=0x30005000;gd=0x30006000;md=0x30007000
   d=dict(cached=0,mode=1,gc=gc,gkind=5,gf=0x51,gt=72,ai=ai,mc=mc,mkind=5,mf=0x51,mt=72);d.update(changes)
   def put(addr,value):u.mem_write(addr,struct.pack('<I',value))
   put(group+0x1c0,d['mode']);put(group+0x1ac,d['gc']);put(actor+0x70,d['ai']);put(ai+0x20,d['mc'])
   for cmd,definition,kind,flags,target in [(gc,gd,d['gkind'],d['gf'],d['gt']),(mc,md,d['mkind'],d['mf'],d['mt'])]:
    put(cmd+8,definition);put(definition+12,kind);put(cmd+12,flags);put(cmd+16,target)
   u.mem_write(members+32+13,bytes([d['cached']]))
   values=[0x12345678,actor,members,0x87654321,2,32,group,0x70008000]
   for reg,value in zip(regs,values):u.reg_write(reg,value)
   u.reg_write(UC_X86_REG_FPCW,0x37f);u.reg_write(UC_X86_REG_MXCSR,0x1f80)
   writes=[];u.hook_add(UC_HOOK_MEM_WRITE,lambda uc,access,address,size,value,data:writes.append((address,size)))
   # Both native continuation addresses stop before running native side effects.
   from unicorn import UC_HOOK_CODE
   def stop(uc,address,size,data):
    if address in (image+0x23306a,image+0x233097):uc.emu_stop()
   u.hook_add(UC_HOOK_CODE,stop);u.emu_start(cave,0,count=100)
   check(u.reg_read(UC_X86_REG_EIP)==image+(0x23306a if expected else 0x233097))
   check([u.reg_read(r) for r in regs]==values)
   check(u.reg_read(UC_X86_REG_FPCW)==0x37f and u.reg_read(UC_X86_REG_MXCSR)==0x1f80)
   check(all(0x70007ff8<=addr and addr+n<=0x70008000 for addr,n in writes))
print(f'GROUP_ATTACK_RECOVERY_NATIVE_PASS {checks} checks; same target, Stop, other orders, relocation, registers, no world writes')
