"""Compile actual installers and execute their emitted x86 code and failure paths."""
import argparse,subprocess,sys,struct
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args();a.out.mkdir(parents=True,exist_ok=True)
here=Path(__file__).resolve().parent;game=here.parent
compiler=Path('C:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe')
sources=[game/'beta7'/n for n in ('TerrainRuntime.cs','ReleaseStartup.cs','RandomMapPatch.cs','RandomMapBundle.cs')]
sources += [here/n for n in ('GroupRetreatPriorityPatch.cs','CompanyExhaustionPatch.cs','PawCompanyOptions.cs')]
sources += [game/'exhaustion-recovery/ExhaustionRecoveryPatch.cs']
# Generate the existing enabled-mode recovery payload too.
subprocess.run([sys.executable,str(game/'exhaustion-recovery/build_native.py'),'--legacy',str(a.legacy),'--out',str(a.out/'native')],check=True)
sources += [a.out/'native/ExhaustionRecoveryPayload.cs']
for name,source in [('RetreatTests','RetreatTests.cs'),('ExhaustionTests','ExhaustionTests.cs')]:
 exe=a.out/(name+'.exe');entry='GroupRetreatPriorityTests' if name=='RetreatTests' else 'ExhaustionTests'
 subprocess.run([str(compiler),'/nologo','/target:exe','/platform:x86','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/main:'+entry,'/out:'+str(exe)]+list(map(str,sources))+[str(here/source)],check=True)
 subprocess.run([str(exe)],check=True)
for profile in ('arcane','pure'):
 exe=a.out/('Options-'+profile+'.exe')
 flags=['/define:PAW_PURE_EXTENDED'] if profile=='pure' else []
 subprocess.run([str(compiler),'/nologo','/target:exe','/platform:x86','/r:System.Web.Extensions.dll','/out:'+str(exe)]+flags+[str(here/'PawCompanyOptions.cs'),str(here/'OptionsTests.cs')],check=True)
 subprocess.run([str(exe)],check=True)
sys.path[:0]=[str(a.legacy/'lobby_colors_1372/deps_r15'),str(a.legacy/'pydeps_readable')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE,UC_HOOK_MEM_WRITE
from unicorn.x86_const import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes();checks=0
def check(v):
 global checks
 checks+=1;assert v,checks
def put(u,addr,n):u.mem_write(addr,struct.pack('<I',n))
regs=[UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]
subprocess.run([str(a.out/'ExhaustionTests.exe'),str(a.out/'exhaustion')],check=True)
for image,cave in [(0x460000,0x60000000),(0xf20000,0x51000000),(0x18000000,0x28000000)]:
 payload=a.out/'retreat.bin';subprocess.run([str(a.out/'RetreatTests.exe'),str(payload),f'{image:x}',f'{cave:x}'],check=True)
 # Full native HasCombat body; replace only actor resolution with a controlled
 # registry fixture. Real flag checks execute, not a Python mirror of the gate.
 for kind in (None,0,4,5,6,7,8):
  for count in (1,6,14,64):
   for combat in (0,0x10,0x100000):
    for patched in (False,True):
     u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(image,(len(raw)+4095)&~4095);u.mem_write(image,raw)
     u.mem_map(cave,4096);u.mem_write(cave,payload.read_bytes());u.mem_map(0x30000000,0x10000);u.mem_map(0x70000000,0x10000)
     g=0x30000100;members=0x30001000;actor=0x30002000;cmd=0x30003000;definition=0x30004000;sp=0x70008000;end=0x7000f000
     put(u,g+0x190,members);put(u,g+0x194,count);put(u,g+0x1ac,0 if kind is None else cmd);put(u,cmd+8,definition);put(u,definition+12,kind or 0)
     put(u,actor+0x100,combat)
     for i in range(count):u.mem_write(members+i*16+13,b'\1')
     def hook(uc,address,size,data):
      if address==image+0x2338d0:
       esp=uc.reg_read(UC_X86_REG_ESP);ret=struct.unpack('<I',uc.mem_read(esp,4))[0];uc.reg_write(UC_X86_REG_EAX,actor);uc.reg_write(UC_X86_REG_ESP,esp+8);uc.reg_write(UC_X86_REG_EIP,ret)
     writes=[];u.hook_add(UC_HOOK_CODE,hook);u.hook_add(UC_HOOK_MEM_WRITE,lambda uc,ac,addr,size,val,data:writes.append((addr,size)))
     for reg,value in zip(regs,[0x11111111,g,0x22222222,0x33333333,0x44444444,0x55555555]):u.reg_write(reg,value)
     u.reg_write(UC_X86_REG_ESP,sp);put(u,sp,end);u.reg_write(UC_X86_REG_FPCW,0x37f);u.reg_write(UC_X86_REG_MXCSR,0x1f80)
     u.emu_start(cave if patched else image+0x233035,end,count=50000)
     check(u.reg_read(UC_X86_REG_EIP)==end)
     check(bool(u.reg_read(UC_X86_REG_EAX)&255)==bool(combat and not(patched and kind==7)))
     check(u.reg_read(UC_X86_REG_ESP)==sp+4)
     check(all(0x70000000<=addr and addr+n<0x70010000 for addr,n in writes))
     check(u.reg_read(UC_X86_REG_FPCW)==0x37f and u.reg_read(UC_X86_REG_MXCSR)==0x1f80)
 # Entry denial changes AL only, preserves the caller and never enters the
 # strategy cancellation / Exhausted state push path.
 for initial in (0,1,0xabcdef12):
  u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(cave,4096);u.mem_map(0x70000000,0x10000)
  u.mem_write(cave,(a.out/'exhaustion.entry').read_bytes());sp=0x70008000;end=0x7000f000;put(u,sp,end);u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_EAX,initial)
  u.emu_start(cave,end);check(u.reg_read(UC_X86_REG_EAX)==initial&0xffffff00);check(u.reg_read(UC_X86_REG_ESP)==sp+4)
 # Native Exhausted::Update continuation chooses its native cleanup branch.
 u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(image,(len(raw)+4095)&~4095);u.mem_write(image,raw);u.mem_map(0x30000000,4096);u.mem_map(0x70000000,0x10000)
 u.mem_write(image+0x274a1a,(a.out/'exhaustion.update').read_bytes());u.reg_write(UC_X86_REG_ESI,0x30000100);put(u,0x30000104,0x12345678);u.reg_write(UC_X86_REG_ESP,0x70008000);put(u,0x70008000,0xabcdef)
 u.emu_start(image+0x274a1a,image+0x27792a,count=30);check(u.reg_read(UC_X86_REG_EIP)==image+0x27792a);check(u.reg_read(UC_X86_REG_ECX)==0x12345678)
print('COMPANY_BEHAVIOR_NATIVE_PASS',checks,'checks; native combat body, command kinds, 1/6/14/64 members, relocation, exhausted entry/exit')
