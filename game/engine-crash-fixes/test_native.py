"""Execute stock engine iteration with hooks; compare valid behavior and regressions."""
import argparse,hashlib,json,struct,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--native',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE,UcError
from unicorn.x86_const import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
meta=json.loads((a.native/'payload.json').read_text());pack=lambda v:struct.pack('<I',v&0xffffffff)
OBJ=0x50000000;STACK=0x70000000;STOP=0x71000000
def setup(image,cave,patched):
 u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(image,(len(raw)+4095)&~4095);u.mem_write(image,raw)
 u.mem_map(0,4096);u.mem_write(0,pack(0xffffffff));u.mem_map(cave,8192)
 u.mem_map(OBJ,0x20000);u.mem_map(STACK,0x10000);u.mem_map(STOP,4096)
 for index,off in meta['originalFixups']:
  site=meta['sites'][index]+off
  u.mem_write(image+site,pack(struct.unpack_from('<I',raw,site)[0]+image-0x460000))
 code=bytearray((a.native/'payload.bin').read_bytes())
 for off,kind,sign in meta['fixups']:
  d=((image-0x460000) if kind=='image' else (cave-0x10000000))*sign
  struct.pack_into('<I',code,off,(struct.unpack_from('<I',code,off)[0]+d)&0xffffffff)
 u.mem_write(cave,bytes(code))
 u.mem_write(cave+0x1000+40,pack(1)) # Windows registration tested in real fixture.
 if patched:
  for site,off,s in zip(meta['sites'],meta['offsets'],meta['originals']):u.mem_write(image+site,b'\xe9'+pack(cave+off-image-site-5)+b'\x90'*(len(s)//2-5))
 def w(addr,v):u.mem_write(addr,pack(v))
 def r(addr):return struct.unpack('<I',u.mem_read(addr,4))[0]
 return u,w,r

checks=0
for image,cave in [(0x460000,0x10000000),(0xe40000,0x21000000),(0x12000000,0x61000000)]:
 def network(entries,patched):
  u,w,r=setup(image,cave,patched);nodes=[OBJ+0x100+i*0x100 for i in range(len(entries))]
  w(OBJ+0x2cc,nodes[0] if nodes else 0)
  for i,(node,kind) in enumerate(zip(nodes,entries)):
   admin=OBJ+0x4000+i*0x100;client=OBJ+0x8000+i*0x100
   w(node,0 if kind=='null-node' else admin);w(node+4,nodes[i+1] if i+1<len(nodes) else 0)
   w(admin+0xc,0 if kind=='null-client' else client);w(client+0x28,1 if kind=='live' else 0)
  sp=STACK+0xF000;w(sp,STOP);u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,OBJ)
  saved={UC_X86_REG_EBX:0x1234,UC_X86_REG_ESI:0x5678,UC_X86_REG_EDI:0x9abc,UC_X86_REG_EBP:0xdef0}
  for reg,v in saved.items():u.reg_write(reg,v)
  u.emu_start(image+0x14c034,STOP,count=10000)
  assert u.reg_read(UC_X86_REG_ESP)==sp+4
  for reg,v in saved.items():assert u.reg_read(reg)==v
  return u.reg_read(UC_X86_REG_EAX)&255,r(cave+0x1000+20)
 for n in range(8):
  for kinds in [['live']*n,['inactive']*n,(['live','inactive']*4)[:n]]:
   assert network(kinds,False)==network(kinds,True);checks+=1
 for kinds in [['null-client'],['null-node'],['live','null-client','live'],['null-client','live','live','live'],['live','null-node','inactive','live'],['live','live','live','null-client'],['null-client']*6]:
  result,count=network(kinds,True);assert result==(kinds.count('live')>2);assert count==sum(k.startswith('null') for k in kinds);checks+=1

 def animation(kinds,patched):
  u,w,r=setup(image,cave,patched);seq=OBJ;array=OBJ+0x1000;w(seq+0x30,array);w(seq+0x38,len(kinds))
  updated=[];type_queries=[];saved_targets=[]
  table=image+0x500000;ctable=image+0x501000;stub=image+0x1080;query=image+0x1010
  w(table+4,query);w(ctable+0x3c,stub)
  u.mem_write(query,bytes.fromhex('33c0c3'));u.mem_write(stub,bytes.fromhex('c20400'))
  # Real 394e90 executes, with an empty linked controller list -> INT_MIN.
  for i,kind in enumerate(kinds):
   ctrl=OBJ+0x4000+i*0x100;target=OBJ+0x8000+i*0x100;w(array+i*4,ctrl);w(ctrl,ctable)
   w(ctrl+0xc,0x20);w(ctrl+0x34,target);w(ctrl+0x3c,0x80000000);w(target,table);w(target+4,1)
   if kind=='null':w(ctrl+0x34,0)
   elif kind=='inactive':w(ctrl+0xc,0)
   elif kind=='dead':w(target+4,0)
   elif kind=='bad-vtable':w(target,0)
   elif kind=='data-as-code':
    vt=image+0x502000;w(vt+4,image+0x57ca1c);w(target,vt)
   saved_targets.append(bytes(u.mem_read(target,0x100)))
  def on_code(uc,address,size,user):
   if address==stub:updated.append(uc.reg_read(UC_X86_REG_ECX))
   if address==query:type_queries.append(uc.reg_read(UC_X86_REG_ECX))
  u.hook_add(UC_HOOK_CODE,on_code)
  bp=STACK+0xF000;sp=bp-16;w(bp+8,0);w(bp-4,0x3f800000)
  for reg,v in {UC_X86_REG_ESP:sp,UC_X86_REG_EBP:bp,UC_X86_REG_EDI:seq,UC_X86_REG_ESI:0,UC_X86_REG_EBX:0xbadcafe}.items():u.reg_write(reg,v)
  u.emu_start(image+0x31ea96,image+0x31eb2f,count=10000)
  assert u.reg_read(UC_X86_REG_ESP)==sp and u.reg_read(UC_X86_REG_EBP)==bp
  assert u.reg_read(UC_X86_REG_EDI)==seq and u.reg_read(UC_X86_REG_EBX)==0xbadcafe
  assert r(0)==0xffffffff # No lingering SEH registration.
  for i,b in enumerate(saved_targets):assert bytes(u.mem_read(OBJ+0x8000+i*0x100,0x100))==b
  return updated,type_queries,[r(cave+0x1000+i*4) for i in range(6)]
 for kinds in [['live'],['null'],['inactive'],['live','null','inactive','live'],['live']*27]:
  assert animation(kinds,False)==animation(kinds,True);checks+=1
 for kinds in [['dead'],['bad-vtable'],['data-as-code'],['live','dead','live'],['null','data-as-code','inactive','live'],['live']*9+['dead']+['live']*17]:
  updated,queries,counts=animation(kinds,True)
  assert updated==[OBJ+0x4000+i*0x100 for i,k in enumerate(kinds) if k in ('live','null')]
  assert queries==[OBJ+0x8000+i*0x100 for i,k in enumerate(kinds) if k=='live']
  assert counts[0]==sum(k in ('dead','bad-vtable','data-as-code') for k in kinds)
  assert counts[1]==kinds.count('dead') and counts[2]==kinds.count('bad-vtable') and counts[3]==kinds.count('data-as-code')
  checks+=1

 # Real sequence-stop iteration, including log-70's material vtable -> 0x62.
 def deactivate(kinds,patched):
  u,w,r=setup(image,cave,patched);u.mem_unmap(0,4096)
  seq=OBJ;array=OBJ+0x1000;stub=image+0x1080
  w(seq+0x30,array);w(seq+0x38,len(kinds));stopped=[]
  u.mem_write(stub,bytes.fromhex('c20400'))
  for t in meta['controllers']:w(image+t['rva']+0x40,stub)
  for i,kind in enumerate(kinds):
   ctrl=OBJ+0x4000+i*0x100;w(array+i*4,ctrl)
   w(ctrl,image+meta['controllers'][i%36]['rva']);w(ctrl+4,1)
   if kind=='null':w(array+i*4,0)
   elif kind=='dead':w(ctrl+4,0)
   elif kind=='material':w(ctrl,image+0x53c3c0)
   elif kind=='fake-code-slot':w(ctrl,image+0x500000);w(image+0x500040,stub)
  def stop_call(uc,address,size,user):
   if address==stub:
    assert r(uc.reg_read(UC_X86_REG_ESP)+4)==0
    stopped.append(uc.reg_read(UC_X86_REG_ECX))
  u.hook_add(UC_HOOK_CODE,stop_call)
  sp=STACK+0xf000
  for reg,v in {UC_X86_REG_ESP:sp,UC_X86_REG_EBP:sp+16,UC_X86_REG_EDI:seq,UC_X86_REG_ESI:0,UC_X86_REG_EBX:0x1234}.items():u.reg_write(reg,v)
  if not patched and kinds==['material']:
   try:u.emu_start(image+0x31cea0,image+0x31ceb7,count=10000)
   except UcError:
    assert u.reg_read(UC_X86_REG_EIP)==0x62
    assert r(u.reg_read(UC_X86_REG_ESP))==image+0x31ceb1
    return
   raise AssertionError('log-70 signature not reproduced')
  u.emu_start(image+0x31cea0,image+0x31ceb7,count=10000)
  assert u.reg_read(UC_X86_REG_ESP)==sp and u.reg_read(UC_X86_REG_EBP)==sp+16
  assert u.reg_read(UC_X86_REG_EDI)==seq and u.reg_read(UC_X86_REG_EBX)==0x1234
  assert u.reg_read(UC_X86_REG_ESI)==len(kinds)
  assert [r(array+i*4) for i in range(len(kinds))]==[0 if k=='null' else OBJ+0x4000+i*0x100 for i,k in enumerate(kinds)]
  if all(k in ('live','null') for k in kinds):
   assert bytes(u.mem_read(cave+0x1000+56,36))==bytes(36) # No per-animation log churn.
  return stopped,[r(cave+0x1000+i) for i in (56,60,64,68,72)]
 deactivate(['material'],False);checks+=1
 for kinds in [['live'],['null'],['live']*36,['live','null','live']]:
  assert deactivate(kinds,False)==deactivate(kinds,True);checks+=1
 for kinds in [['material'],['dead'],['fake-code-slot'],['live']*9+['material']+['live']*26,['material','null','dead','live','fake-code-slot','live']]:
  stopped,counts=deactivate(kinds,True)
  assert stopped==[OBJ+0x4000+i*0x100 for i,k in enumerate(kinds) if k=='live']
  assert counts==[sum(k in ('material','dead','fake-code-slot') for k in kinds),kinds.count('dead'),kinds.count('material')+kinds.count('fake-code-slot'),0,0]
  checks+=1

 # Exercise the real native local-kingdom getter and its observer mode check.
 # Relocate the absolute operands used by these stock functions in the fixture.
 def team_setup(patched,mode=3,kingdom=OBJ+0x9000,session=True):
  u,w,r=setup(image,cave,patched)
  u.mem_unmap(0,4096) # A null access must fault, as in log-421.dmp.
  for off in (0x2be704,0x1694cd,0x15d1d9,0xb7179,0xb718e):
   w(image+off,struct.unpack_from('<I',raw,off)[0]+image-0x460000)
  w(image+0x5f3fe4,OBJ+0x1000 if session else 0)
  w(OBJ+0x10f0,mode)
  w(image+0x5f3fec,OBJ+0x2000);w(OBJ+0x200c,OBJ+0x3000)
  w(OBJ+0x3028,kingdom);w(image+0x5f3fb8,OBJ+0x4000)
  sp=STACK+0xf000;w(sp,STOP);w(sp+4,OBJ+0x6000)
  regs={UC_X86_REG_EAX:0x12345,UC_X86_REG_EBX:0x23456,
        UC_X86_REG_ECX:OBJ,UC_X86_REG_EDX:0x34567,
        UC_X86_REG_ESI:0x45678,UC_X86_REG_EDI:0x56789,
        UC_X86_REG_EBP:0x6789a,UC_X86_REG_ESP:sp,UC_X86_REG_EFLAGS:0x246}
  for reg,value in regs.items():u.reg_write(reg,value)
  return u,w,r,regs

 for mode,kingdom,session in [(3,OBJ+0x9000,True),(0,0,True),(0,0,False)]:
  # All macro stages must be no-ops, even when an observer is displaying a
  # non-null viewed kingdom. Repeat inputs and switch from player to observer.
  u,w,r,regs=team_setup(True,mode,kingdom,session)
  before=bytes(u.mem_read(OBJ,0x20000))
  for repeat in range(10):
   for index in (2,3,4,5):
    for reg,value in regs.items():u.reg_write(reg,value)
    u.emu_start(image+meta['sites'][index],STOP,count=1000)
    for reg,value in regs.items():
     assert u.reg_read(reg)==(value+8 if reg==UC_X86_REG_ESP else value)
    assert bytes(u.mem_read(OBJ,0x20000))==before
    checks+=1
  assert r(cave+0x1000+52)==40

 # Valid players execute the same displaced instructions with identical
 # registers, stack and UI state, including the relocated SEH descriptor.
 for mode in (0,1,2,4):
  for index in (2,3,4,5):
   results=[]
   for patched in (False,True):
    u,w,r,regs=team_setup(patched,mode)
    site=meta['sites'][index];end=image+site+len(bytes.fromhex(meta['originals'][index]))
    u.emu_start(image+site,end,count=1000)
    sp=u.reg_read(UC_X86_REG_ESP)
    results.append(([u.reg_read(reg) for reg in regs],bytes(u.mem_read(OBJ,0x20000)),bytes(u.mem_read(sp,STACK+0xf008-sp))))
    assert r(cave+0x1000+52)==0
   assert results[0]==results[1],(hex(image),mode,index)
   checks+=1

 # Reproduce the actual fault through GoToTeamCommands -> native ally list.
 u,w,r,regs=team_setup(False)
 for off,code in [(0xb5bc6,'c3'),(0xb5c77,'c3'),(0xb7134,'c3'),(0xb71ee,'c20800')]:
  u.mem_write(image+off,bytes.fromhex(code))
 u.mem_write(image+0xb8afb,b'\xb8'+pack(OBJ+0x9000)+bytes.fromhex('c20400'))
 w(OBJ+0x4154,2)
 try:
  u.emu_start(image+0xb764b,STOP,count=1000)
  raise AssertionError('Stock observer F failed to reproduce crash')
 except UcError:
  assert u.reg_read(UC_X86_REG_EIP)==image+0xb71a9
  assert u.reg_read(UC_X86_REG_ESI)==0
 checks+=1

print('ENGINE_CRASH_NATIVE_PASS',checks,'cases; log-70 0x62 crash reproduced; all 36 controller types; observer F; stock-loop differential; ABI; three ASLR bases')
