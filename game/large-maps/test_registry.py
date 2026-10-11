"""Run the actual compiled registry and native ABI adapters in x86 emulation."""
import argparse,json,struct,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--native',type=Path,required=True);p.add_argument('--exe',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
import pefile
from unicorn import *
from unicorn.x86_const import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes();meta=json.loads((a.native/'registry.json').read_text());blob=(a.native/'registry.bin').read_bytes()
pe=pefile.PE(str(a.exe));reloc=[e.rva for b in pe.DIRECTORY_ENTRY_BASERELOC for e in b.entries if e.type==3]
R=0x20000000;BITS=0x21000000;OBJ=0x22000000;ST=0x23000000;EXIT=0x24000000;CTX=0x25000000;VT=0x26000000;IO_READ=VT+0x100;IO_WRITE=VT+0x200
checks=0
for image,cave in [(0x460000,0x10000000),(0x620000,0x11000000),(0xd00000,0x14000000)]:
 u=Uc(UC_ARCH_X86,UC_MODE_32);data=cave+meta['dataOffset'];native=bytearray(raw)
 for r in reloc:
  if r+4<=len(native):struct.pack_into('<I',native,r,(struct.unpack_from('<I',native,r)[0]+image-0x460000)&0xffffffff)
 u.mem_map(image,(len(native)+4095)&~4095);u.mem_write(image,bytes(native))
 code=bytearray(blob)
 for off,im,cv in meta['fixups']:struct.pack_into('<I',code,off,(struct.unpack_from('<I',code,off)[0]+im*(image-0x460000)+cv*(cave-0x10000000))&0xffffffff)
 u.mem_map(cave,(meta['allocation']+4095)&~4095);u.mem_write(cave,bytes(code))
 for addr,size in [(R,0x61000),(BITS,0x2000),(OBJ,0x20000),(ST,0x10000),(EXIT,0x1000),(CTX,0x1000),(VT,0x1000)]:u.mem_map(addr,size)
 def w(addr,v):u.mem_write(addr,struct.pack('<I',v&0xffffffff))
 def r(addr):return struct.unpack('<I',u.mem_read(addr,4))[0]
 def w16(addr,v):u.mem_write(addr,struct.pack('<H',v))
 w(data,image);w(data+4,cave+meta['call2']);w(data+8,18);w(R+0x60010,BITS);w(image+0x5ef72c,R)
 w(CTX+0x3c,VT);w(VT,IO_READ);w(VT+4,IO_WRITE)
 for h in meta['hooks']:
  old=bytearray.fromhex(h['before'])
  for rr in h['relocations']:struct.pack_into('<I',old,rr,(struct.unpack_from('<I',old,rr)[0]+image-0x460000)&0xffffffff)
  assert bytes(u.mem_read(image+h['site'],len(old)))==old
  patch=b'\xe9'+struct.pack('<I',(cave+h['offset']-image-h['site']-5)&0xffffffff)+b'\x90'*(len(old)-5)
  u.mem_write(image+h['site'],patch);checks+=1
 stream=bytearray();position=0;bits=[];errors=[]
 def return_from(uc,clean=0):
  sp=uc.reg_read(UC_X86_REG_ESP);uc.reg_write(UC_X86_REG_EIP,r(sp));uc.reg_write(UC_X86_REG_ESP,sp+4+clean)
 def hook(uc,addr,size,_):
  global position
  sp=uc.reg_read(UC_X86_REG_ESP)
  if addr in (IO_READ,IO_WRITE):
   assert uc.reg_read(UC_X86_REG_ECX)==CTX+0x3c
   ptr,n=r(sp+4),r(sp+8)
   if addr==IO_WRITE:stream.extend(uc.mem_read(ptr,n))
   else:
    assert position+n<=len(stream),(position,n,len(stream));uc.mem_write(ptr,bytes(stream[position:position+n]));position+=n
   return_from(uc,8)
  elif addr==image+0x303310:
   assert r(sp+4)==CTX+0x3c and r(sp+8)==0;return_from(uc,8)
  elif addr==image+0x1c5782:errors.append(True);raise AssertionError('native registry error')
  elif addr in (image+0x518fe,image+0x517cc):
   ptr,n=r(sp+8),r(sp+12);assert n in (16,18)
   if addr==image+0x518fe:bits.append((r(ptr),n))
   else:
    v,width=bits.pop(0);assert width==n;w(ptr,v)
   return_from(uc)
 for hooked in (IO_READ,IO_WRITE,image+0x303310,image+0x1c5782,image+0x518fe,image+0x517cc):
  u.hook_add(UC_HOOK_CODE,hook,begin=hooked,end=hooked)
 def call(site,args=(),cdecl=False):
  sp=ST+0xf000;u.mem_write(sp,struct.pack('<'+'I'*(len(args)+1),EXIT,*args));u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,R)
  regs=[UC_X86_REG_EBX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]
  for i,x in enumerate(regs):u.reg_write(x,0x12300000+i)
  u.emu_start(image+site,EXIT,count=30000000)
  assert u.reg_read(UC_X86_REG_EIP)==EXIT,hex(site)
  assert u.reg_read(UC_X86_REG_ESP)==sp+4+(0 if cdecl else len(args)*4),(hex(site),hex(u.reg_read(UC_X86_REG_ESP)))
  for i,x in enumerate(regs):assert u.reg_read(x)==0x12300000+i,hex(site)
  return u.reg_read(UC_X86_REG_EAX)
 call(0x23f5c8);assert r(R+0x60004)==0 and r(data+8)==18;checks+=1
 primary=OBJ;generic=OBJ+0x200;out=OBJ+0x1000
 w(primary,image+0x4e5c58);w(generic,image+0x4e8f80)
 call(0x23f619,[out,primary]);pid=r(out);assert pid==1
 call(0x23f619,[out,generic]);gid=r(out);assert gid==0x40000000 and r(R+0x60004)==2
 assert call(0x21117,[pid])==primary and call(0x21117,[gid])==generic;checks+=4
 cursor=out+4;w(cursor,0);assert call(0x23f868,[cursor])==primary;assert call(0x23f868,[cursor])==generic;assert call(0x23f868,[cursor])==0;checks+=3
 # Actual removal/recycling, including final index and all three extra banks.
 for i in (65536,65537,131071,131072,196607,196608,262143):
  call(0x23f5c8);w(data+16,i-65536);call(0x23f619,[out,generic]);old=r(out)
  assert (old&65535)|((old>>14)&0x30000)==i
  assert call(0x21117,[old])==generic
  call(0x23f6dc,[generic]);assert call(0x21117,[old])==0 and r(R+0x60004)==0
  w(data+16,i-65536);call(0x23f619,[out,generic]);new=r(out);assert new==old+0x10000 and call(0x21117,[new])==generic
  # Native compressed object-reference entry points use 18 bits, never
  # truncate a bank index to uint16. Reading returns the same object.
  ptr=out+16;dest=out+20;w(ptr,generic);call(0x160452,[CTX,ptr],True);assert bits==[(i,18)]
  call(0x160471,[CTX,dest],True);assert r(dest)==generic and not bits
  checks+=6
 # Fill 131,072 extra slots (twice the previous global ceiling); the next
 # real Add succeeds and actor allocation still uses the reserved low bank.
 call(0x23f5c8);u.mem_write(data+32+196608*2,struct.pack('<I',generic)*131072)
 w(data+12,131072);w(data+16,131072);w(data+20,131071);w(R+0x60004,131072)
 call(0x23f619,[out,generic]);assert r(out)==0xc0000000 and r(R+0x60004)==131073
 call(0x23f619,[out,primary]);assert r(out)==1;checks+=2
 # Save/load generations and references after recycling in both tables.
 call(0x23f5c8);call(0x23f619,[out,primary]);call(0x23f6dc,[primary]);w(R+0x60008,1);call(0x23f619,[out,primary]);pid=r(out)
 w(data+16,196607);call(0x23f619,[out,generic]);call(0x23f6dc,[generic]);w(data+16,196607);call(0x23f619,[out,generic]);gid=r(out)
 stream.clear();position=0;call(0x23f8bb,[CTX]);assert len(stream)==20+524288 and struct.unpack_from('<I',stream,8)[0]==262144
 call(0x23f5c8);call(0x23f920,[CTX]);assert position==len(stream)
 call(0x23f755,[primary,pid]);call(0x23f755,[generic,gid]);assert call(0x21117,[pid])==primary and call(0x21117,[gid])==generic;checks+=4
 # Old registry format is retained for loaded old saves, including uint16
 # reference width and full 16-bit generation values.
 call(0x23f5c8);stream=bytearray(struct.pack('<IIIHH',2,2,65536,0,0x9234));position=0;call(0x23f920,[CTX]);assert r(data+8)==16
 old=0x92340001;call(0x23f755,[primary,old]);assert call(0x21117,[old])==primary
 ptr=out+16;dest=out+20;w(ptr,primary);call(0x160452,[CTX,ptr],True);assert bits==[(1,16)];call(0x160471,[CTX,dest],True);assert r(dest)==primary
 stream.clear();position=0;call(0x23f8bb,[CTX]);assert struct.unpack_from('<I',stream,8)[0]==65536;checks+=5
 assert not errors
 # Exercise native inline adapters, including preservation of condition flags
 # and every register except the documented result. These are not functions.
 call(0x23f5c8);w(data+16,196607);call(0x23f619,[out,generic]);gid=r(out)
 for site,end,source,result in [(0x85725,0x85737,'indirect',UC_X86_REG_EAX),(0x858cd,0x858dd,'esi',UC_X86_REG_ECX),(0x85929,0x85939,'esi',UC_X86_REG_ECX),(0xc2aa5,0xc2ab7,'ebx',UC_X86_REG_ESI),(0x2344d4,0x2344e4,'ecx',UC_X86_REG_EAX),(0x234506,0x234516,'ecx',UC_X86_REG_EAX),(0x2345c9,0x2345d9,'ecx',UC_X86_REG_EDX)]:
  sp=ST+0xf000;u.reg_write(UC_X86_REG_ESP,sp)
  regs=[UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]
  for n,x in enumerate(regs):u.reg_write(x,OBJ+0x4000+0x100*n)
  if source=='indirect':w(u.reg_read(UC_X86_REG_ESI),OBJ+0x8000);w(OBJ+0x8000,gid)
  elif source=='esi':w(u.reg_read(UC_X86_REG_ESI),gid)
  else:w(u.reg_read(UC_X86_REG_EBX if source=='ebx' else UC_X86_REG_ECX)+12,gid)
  expected={x:u.reg_read(x) for x in regs};expected[result]=generic
  u.reg_write(UC_X86_REG_EFLAGS,0x247);u.emu_start(image+site,image+end,count=10000)
  assert all(u.reg_read(x)==v for x,v in expected.items()) and u.reg_read(UC_X86_REG_ESP)==sp
  assert u.reg_read(UC_X86_REG_EFLAGS)==0x247;checks+=2
 # Exact world-load generation check: no object exists yet; accept a matching
 # generation in every bank and reject stale generations. Also keep old saves.
 for mode,idx,generation in [(18,1,7),(18,65536,0),(18,131071,12),(18,131072,3),(18,196608,5),(18,262143,16383),(16,1,0x9234)]:
  w(data+8,mode)
  id=(idx&65535)|(generation<<16)|(((idx&0x30000)<<14) if mode==18 else 0)
  w16(R+4+2*idx if idx<65536 else data+32+2*(idx-65536),generation)
  for good in (True,False):
   sp=ST+0xf000;u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_EBP,ST+0x8000)
   w(ST+0x801c,id if good else id^0x10000)
   expected={x:u.reg_read(x) for x in regs}
   u.emu_start(image+0x246cdc,image+0x246cf0,count=10000)
   assert bool(u.reg_read(UC_X86_REG_EFLAGS)&0x40)==good
   assert all(u.reg_read(x)==v for x,v in expected.items()) and u.reg_read(UC_X86_REG_ESP)==sp;checks+=1
 # Save/load offset buffer allocations and cleanup pass the same full size.
 for site in (0x248040,0x2481ef,0x24821c,0x248315,0x2491d1):
  sp=ST+0xf000;u.reg_write(UC_X86_REG_ESP,sp);u.emu_start(image+site,image+site+5,count=20)
  assert u.reg_read(UC_X86_REG_ESP)==sp-4 and r(sp-4)==0x100004;checks+=1
 # Reproduce the native signed-overflow bug, then execute the actual fixed
 # instructions at the original hook with boundary and large-world indexes.
 limit=(0x7fffffff-1600)//4723
 def signed32(x):return ((x+0x80000000)&0xffffffff)-0x80000000
 def rem(x,d):return x-(abs(x)//d*(1 if x>=0 else -1))*d
 bad=next(x for x in range(limit+1,limit+10000) if rem(rem(signed32(4723*x+1600),6997),4)==-1)
 original_hash=EXIT+0x100
 u.mem_write(original_hash,raw[0x1a7860:0x1a7873])
 u.reg_write(UC_X86_REG_ECX,bad)
 u.emu_start(original_hash,original_hash+19,count=30)
 assert signed32(u.reg_read(UC_X86_REG_EDX))<0
 assert rem(signed32(u.reg_read(UC_X86_REG_EDX)),4)==-1;checks+=1
 for idx in (0,1,65535,262143,limit-1,limit,limit+1,bad,524287,1048575,4194303,16777215):
  sp=ST+0xf000;u.reg_write(UC_X86_REG_ESP,sp)
  preserved=(UC_X86_REG_EBX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP)
  before={x:u.reg_read(x) for x in preserved}
  u.reg_write(UC_X86_REG_ECX,idx)
  u.emu_start(image+0x1a7860,image+0x1a7873,count=40)
  q,v=divmod(4723*idx+1600,6997)
  assert u.reg_read(UC_X86_REG_EAX)==q and u.reg_read(UC_X86_REG_EDX)==v
  assert u.reg_read(UC_X86_REG_ECX)==6997 and u.reg_read(UC_X86_REG_ESP)==sp
  assert all(u.reg_read(x)==value for x,value in before.items())
  for count in (1,2,3,4,7,16,32):assert 0<=v%count<count
  if idx<=limit:assert v==rem(4723*idx+1600,6997)
  checks+=1
 # Run the real staging branch adapters with every language and setting.
 # Stop at native continuations; native string lookup is mocked separately.
 translations=list(json.loads(Path(__file__).with_name('warning-translations.json').read_text(encoding='utf-8')).values())
 warning_ptr=data+32+196608*6
 session,settings=OBJ+0x9000,OBJ+0xa000
 w(image+0x5f3fe4,session);w(session+0x80,settings)
 stops=[image+n for n in (0x104e93,0x10503b,0x104f8d)]
 def stop_gate(uc,addr,size,_):uc.emu_stop()
 handles=[u.hook_add(UC_HOOK_CODE,stop_gate,begin=n,end=n) for n in stops]
 def wide(ptr):
  result=bytearray()
  while True:
   b=bytes(u.mem_read(ptr,2));ptr+=2
   if b==b'\0\0':return result.decode('utf-16le')
   result.extend(b)
 for language in range(7):
  for frequency in (1,2,4):
   for mode,kind,width,height in [(1,0,1408,1408),(1,0,1500,1500),(1,0,1536,1536),(1,0,2048,2048),(1,0,1408,2048),(1,0,2048,1408),(1,0,float('nan'),2048),(1,1,2048,2048),(2,0,2048,2048),(3,0,2048,2048),(4,0,2048,2048),(5,0,2048,2048)]:
    w(data+28,frequency|(language<<8));w(session+0x6c,mode);w(session+0x64,kind)
    u.mem_write(settings+0x3c,struct.pack('<ff',width,height))
    sp=ST+0xf000;u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,session)
    before={x:u.reg_read(x) for x in regs}
    u.emu_start(image+0x104e89,EXIT,count=10000)
    expected=frequency in (2,4) and mode==1 and kind==0 and width>=1536 and height>=1536
    assert u.reg_read(UC_X86_REG_EIP)==image+(0x104f8d if expected else 0x104e93 if mode==1 else 0x10503b)
    assert all(u.reg_read(x)==v for x,v in before.items()) and u.reg_read(UC_X86_REG_ESP)==sp
    assert bool(r(warning_ptr))==expected
    if expected:assert wide(r(warning_ptr))==translations[language if language<6 else 0]
    checks+=1
 for handle in handles:u.hook_del(handle)
 localized=[]
 def lookup_mock(uc,addr,size,_):
  localized.append(True);uc.reg_write(UC_X86_REG_EAX,OBJ+0xc000);return_from(uc,8)
 handle=u.hook_add(UC_HOOK_CODE,lookup_mock,begin=image+0x71800,end=image+0x71800)
 for custom in (True,False):
  w(warning_ptr,OBJ+0xb000 if custom else 0)
  sp=ST+0xf000;u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_EAX,0x12345678)
  u.emu_start(image+0x104fbf,image+0x104fc4,count=100)
  assert u.reg_read(UC_X86_REG_ESP)==sp-4 and u.reg_read(UC_X86_REG_EAX)==0x12345678
  assert r(sp-4)==(OBJ+0xb000 if custom else image+0x4a0258)
  # Key and fallback wrappers are arguments on the original caller's stack.
  w(sp,OBJ+0xd000);w(sp+4,OBJ+0xe000);u.reg_write(UC_X86_REG_ESP,sp);localized.clear()
  u.emu_start(image+0x104fff,image+0x105004,count=100)
  assert u.reg_read(UC_X86_REG_ESP)==sp+8
  assert u.reg_read(UC_X86_REG_EAX)==(OBJ+0xd000 if custom else OBJ+0xc000)
  assert bool(localized)==(not custom);checks+=2
 u.hook_del(handle)
 # Exercise the marker resampler through the actual inline hook, checking
 # full register preservation and padded source rows, including tiny markers.
 world,mapinfo,source=OBJ+0xe000,OBJ+0xe100,OBJ+0x10000
 w(image+0x5f3fb8,world);w(world+0x30,mapinfo)
 def wf(addr,value):u.mem_write(addr,struct.pack('<f',value))
 for side,other in [(512,512),(1024,1024),(1280,1280),(1536,1536),(2048,2048),(2048,1024),(1024,2048),(4096,4096),(0,2048),(float('nan'),2048)]:
  wf(mapinfo+12,side);wf(mapinfo+16,other)
  for width,height in [(1,1),(2,2),(7,7),(9,9),(16,16),(12,8),(64,64)]:
   stride=width+1
   for y in range(height):
    for x in range(width):w(source+4*(y*stride+x),0xff000000|(y<<8)|x)
   sp=ST+0xe000;u.reg_write(UC_X86_REG_ESP,sp)
   w(sp+0x90,height);w(sp+0x80,stride);w(sp+0xc8,source);wf(sp+0x84,123.5)
   u.reg_write(UC_X86_REG_EAX,width)
   expected={x:u.reg_read(x) for x in regs if x!=UC_X86_REG_EAX}
   xmmregs=[getattr(sys.modules['unicorn.x86_const'],'UC_X86_REG_XMM'+str(n)) for n in range(8)]
   xmm_before={x:(0x112233445566778899aabbccddeeff00+n) for n,x in enumerate(xmmregs)}
   for x,v in xmm_before.items():u.reg_write(x,v)
   u.reg_write(UC_X86_REG_EFLAGS,0x247)
   u.emu_start(image+0x2d660f,image+0x2d6618,count=1000000)
   valid=side>0 and other>0 and max(side,other)>1024
   nw=min(width,max(3,int(width*1024/max(side,other)+0.5)|1)) if valid else width
   nh=min(height,max(3,int(height*1024/max(side,other)+0.5)|1)) if valid else height
   assert u.reg_read(UC_X86_REG_EAX)==nw and r(sp+0x90)==nh
   assert all(u.reg_read(x)==v for x,v in expected.items()) and u.reg_read(UC_X86_REG_ESP)==sp
   assert u.reg_read(UC_X86_REG_EFLAGS)==0x247
   assert all(u.reg_read(x)==v for x,v in xmm_before.items() if x!=UC_X86_REG_XMM0)
   assert u.reg_read(UC_X86_REG_XMM0)==struct.unpack('<I',struct.pack('<f',123.5))[0]
   changed=nw!=width or nh!=height
   assert r(sp+0x80)==(nw if changed else stride)
   if not changed:assert r(sp+0xc8)==source
   else:
    for y in range(nh):
     for x in range(nw):
      sx=(2*x+1)*width//(2*nw);sy=(2*y+1)*height//(2*nh)
      assert r(r(sp+0xc8)+4*(y*nw+x))==r(source+4*(sy*stride+sx))
   checks+=1
 # Continue through the real native textured-marker raster loop to verify
 # centering, bounds, tint and source stride, rather than just helper output.
 def round_mock(uc,addr,size,_):
  value=struct.unpack('<f',uc.mem_read(uc.reg_read(UC_X86_REG_ESP)+4,4))[0]
  uc.reg_write(UC_X86_REG_EAX,int(value+0.5));return_from(uc)
 handle=u.hook_add(UC_HOOK_CODE,round_mock,begin=image+0x306619,end=image+0x306619)
 for side,size in [(1024,9),(1536,7),(2048,5)]:
  wf(mapinfo+12,side);wf(mapinfo+16,side)
  sp=ST+0xe000;u.mem_write(sp,bytes(512));u.reg_write(UC_X86_REG_ESP,sp)
  u.mem_write(source,b'\xff\xff\xff\xff'*81)
  pixels,mask=OBJ+0x5000,OBJ+0x6000;u.mem_write(pixels,bytes(4096));u.mem_write(mask,bytes(1024))
  w(OBJ+0x7000,pixels);w(OBJ+0x7004,mask);w(OBJ+0x7010,0xff0000ff)
  for off,v in [(0x90,9),(0x80,9),(0xc8,source),(0xa4,OBJ+0x7000),(0xa8,OBJ+0x7004),(0xc0,2),(0xc4,32),(0xbc,32),(0xd4,OBJ+0x7010)]:w(sp+off,v)
  wf(sp+0x84,side/2);wf(sp+0x88,side/2);wf(sp+0x8c,32/side)
  u.reg_write(UC_X86_REG_EAX,9);u.reg_write(UC_X86_REG_EDI,32)
  u.emu_start(image+0x2d660f,image+0x2d759c,count=1000000)
  assert u.reg_read(UC_X86_REG_EIP)==image+0x2d759c
  drawn=[(x,y) for y in range(32) for x in range(32) if r(pixels+4*(y*32+x))]
  assert len(drawn)==size*size,(side,len(drawn),size,drawn[:12],drawn[-12:])
  assert min(x for x,y in drawn)==16-size//2 and max(x for x,y in drawn)==16-size//2+size-1
  assert min(y for x,y in drawn)==16-size//2 and max(y for x,y in drawn)==16-size//2+size-1
  assert all(r(pixels+4*(y*32+x))==0xff0000fe for x,y in drawn)
  checks+=1
 u.hook_del(handle)
 # Stock city art is 7x7: black edge and one black center pixel inside white.
 # R9 selected source columns 0,2,4,6 on 2048 maps, losing the center dot and
 # placing the resulting 4x4 shape half a pixel off its native anchor.
 handle=u.hook_add(UC_HOOK_CODE,stop_gate,begin=image+0x2d6618,end=image+0x2d6618)
 for side in (1280,1408,1536,1664,1792,1920,2048,4096):
  wf(mapinfo+12,side);wf(mapinfo+16,side)
  for y in range(7):
   for x in range(7):
    pixel=0 if x in (0,6) and y in (0,6) else 0xff000000 if x in (0,6) or y in (0,6) or (x,y)==(3,3) else 0xffffffff
    w(source+4*(y*7+x),pixel)
  sp=ST+0xe000;u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_EAX,7)
  w(sp+0x90,7);w(sp+0x80,7);w(sp+0xc8,source)
  u.emu_start(image+0x2d660f,image+0x2d6618,count=1000000)
  n=u.reg_read(UC_X86_REG_EAX);ptr=r(sp+0xc8)
  assert n in (3,5,7) and r(sp+0x90)==n
  assert r(ptr+4*((n//2)*n+n//2))==0xff000000
  for y in range(n):
   for x in range(n):
    assert r(ptr+4*(y*n+x))==r(ptr+4*(y*n+n-1-x))==r(ptr+4*((n-1-y)*n+x))
  assert -(n//2)+(n-1)/2==0 # Native placement retains the exact center.
  checks+=1
 u.hook_del(handle)
print('EXTENDED_REGISTRY_NATIVE_PASS',checks,'checks; 3 ASLR bases; registry/save/terrain; warning languages and staging ABI; minimap scaling and actual native raster loop; stream/UI services mocked; no game launched')
