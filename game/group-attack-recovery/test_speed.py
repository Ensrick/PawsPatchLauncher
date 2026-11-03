"""Exercise emitted x86 gates, including native wait branches and bad inputs."""
import argparse,struct,subprocess,sys,tempfile,math
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--emitter',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_MEM_WRITE
from unicorn.x86_const import *
checks=0
def check(v):
 global checks
 checks+=1;assert v,checks
regs=[UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_ESP]
cases=[('slow leader',.9,.9,1.,False),('slow follower',.9,.4,1.,True),('normal leader',1.3,1.3,1.,False),('normal wait',1.3,.8,1.,True),('equal threshold',1.,1.,1.,False),('zero request',.9,0.,1.,True),('negative request',.9,-1.,1.,True),('zero cap',0.,.9,1.,True),('negative cap',-1.,.9,1.,True),('nan cap',float('nan'),.9,1.,True),('infinite cap',float('inf'),.9,1.,True),('lower configured threshold',.9,.5,.4,False)]
with tempfile.TemporaryDirectory() as td:
 for image,cave in [(0x460000,0x60000000),(0x390000,0x51000000),(0x18000000,0x28000000)]:
  for index,site in enumerate([0x26f69e,0x27dc2c,0x27f499]):
   file=Path(td)/'gate.bin';subprocess.run([str(a.emitter),str(file),str(index),f'{image:x}',f'{cave:x}'],check=True)
   for name,cap,requested,threshold,wait in cases:
    for mode in ['company','unit','no-membership','no-owner','no-org','no-ai','no-actor']:
     u=Uc(UC_ARCH_X86,UC_MODE_32)
     for at,size in [(cave,4096),(image+(site&~4095),8192),(0x30000000,0x10000),(0x70000000,0x10000)]:u.mem_map(at,size)
     u.mem_write(cave,file.read_bytes());state=0x30000100;ai=0x30001000;actor=0x30002000;member=0x30003000;owner=0x30004000;org=0x30005000;param=0x30006000
     def put(at,v):u.mem_write(at,struct.pack('<I',v))
     def flt(at,v):u.mem_write(at,struct.pack('<f',v))
     put(state+4,0 if mode=='no-ai' else ai);put(ai+4,0 if mode=='no-actor' else actor)
     put(actor+0x7c,org if mode=='company' else 0);put(actor+0x80,0 if mode=='no-membership' else member);put(member+0x10,0 if mode=='no-owner' else owner);put(owner+0x7c,0 if mode=='no-org' else org)
     flt(org+0xd8,cap);flt(param,threshold);flt(state+(0x2c if index==0 else 0x24),requested)
     values=[param,0x12345678,0x87654321,0x456789ab,state,state,0x56789abc,0x70008000]
     for reg,value in zip(regs,values):u.reg_write(reg,value)
     for i in range(8):u.reg_write(UC_X86_REG_XMM0+i,0x123456789abcdef00123456789abcdef0+i)
     initial_xmm=[u.reg_read(UC_X86_REG_XMM0+i) for i in range(8)]
     u.reg_write(UC_X86_REG_MXCSR,0x1f80);u.reg_write(UC_X86_REG_FPCW,0x37f)
     writes=[];u.hook_add(UC_HOOK_MEM_WRITE,lambda uc,access,at,n,v,data:writes.append((at,n)))
     u.emu_start(cave,image+site+8,count=100)
     check(u.reg_read(UC_X86_REG_EIP)==image+site+8);check([u.reg_read(reg) for reg in regs]==values)
     flags=u.reg_read(UC_X86_REG_EFLAGS);actual_wait=not(flags&1 or flags&64) # ja / inverse jbe
     effective=wait if mode in ('company','unit') else threshold>requested
     check(actual_wait==effective)
     expected=min(cap,threshold) if mode in ('company','unit') and math.isfinite(cap) and cap>0 else threshold
     check(u.reg_read(UC_X86_REG_XMM0)==struct.unpack('<I',struct.pack('<f',expected))[0])
     check(all(u.reg_read(UC_X86_REG_XMM0+i)==initial_xmm[i] for i in range(1,8)))
     check(u.reg_read(UC_X86_REG_FPCW)==0x37f and u.reg_read(UC_X86_REG_MXCSR)==0x1f80)
     check(all(0x70007ff8<=at and at+n<=0x70008000 for at,n in writes))
# Execute the real native registry lookup, including its stale-generation
# rejection, rather than substituting a resolver that always returns a group.
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
with tempfile.TemporaryDirectory() as td:
 for image,cave in [(0x460000,0x60000000),(0x390000,0x51000000),(0x18000000,0x28000000)]:
  for index,site in enumerate([0x26f68a,0x27dc18,0x27f485],3):
   file=Path(td)/'active.bin';subprocess.run([str(a.emitter),str(file),str(index),f'{image:x}',f'{cave:x}'],check=True)
   for mode in [0,1,2,3,0xffffffff]:
    for command in [0,0x30006000]:
     for kind in ['group','other','null','stale']:
      u=Uc(UC_ARCH_X86,UC_MODE_32)
      for at,size in [(image,0x700000),(cave,4096),(0x30000000,0x100000),(0x70000000,0x10000)]:u.mem_map(at,size)
      u.mem_write(cave,file.read_bytes());u.mem_write(image+0x21117,raw[0x21117:0x21136])
      # Run through a real CALL at the patched site, not directly into the gate.
      u.mem_write(image+site,b'\xe8'+struct.pack('<I',(cave-(image+site+5))&0xffffffff))
      group=0x30000200;handle=0x30001000;registry=0x30010000;oid=0x7823abcd
      def put(at,v):u.mem_write(at,struct.pack('<I',v))
      put(image+0x5ef72c,registry);put(handle,oid);put(group,image+(0x4e7d60 if kind!='other' else 0x4e0000))
      put(group+0x1c0,mode);put(group+0x1ac,command)
      u.mem_write(registry+4+2*(oid&65535),struct.pack('<H',(oid>>16)+(kind=='stale')))
      put(registry+0x20004+4*(oid&65535),0 if kind=='null' else group)
      values=[0x12345678,0x12345678,handle,0x456789ab,0x12345678,0x12345678,0x56789abc,0x70008000]
      for reg,value in zip(regs,values):u.reg_write(reg,value)
      for i in range(8):u.reg_write(UC_X86_REG_XMM0+i,0x123456789abcdef0+i)
      before=[u.reg_read(UC_X86_REG_XMM0+i) for i in range(8)]
      writes=[];u.hook_add(UC_HOOK_MEM_WRITE,lambda uc,access,at,n,v,data:writes.append((at,n)))
      u.emu_start(image+site,image+site+5,count=100)
      check(u.reg_read(UC_X86_REG_EIP)==image+site+5)
      expected=kind=='other' or (kind=='group' and mode==1 and command!=0)
      check(bool(u.reg_read(UC_X86_REG_EAX)&255)==expected)
      check(all(u.reg_read(regs[i])==values[i] for i in [1,4,5,6,7]))
      check([u.reg_read(UC_X86_REG_XMM0+i) for i in range(8)]==before)
      check(all(0x70007ff4<=at and at+n<=0x70008000 for at,n in writes))
print('GROUP_MOVEMENT_SPEED_NATIVE_PASS',checks,'checks')
