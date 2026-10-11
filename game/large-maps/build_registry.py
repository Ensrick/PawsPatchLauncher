"""Build guarded, local-only extended GTID registry and exact ABI adapters."""
import argparse,hashlib,json,struct,subprocess,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('--compiler',required=True);p.add_argument('--exe',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
import pefile
from capstone import *
from keystone import *
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes();IMAGE=0x460000;BASE=0x10000000
assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
a.out.mkdir(parents=True,exist_ok=True)
translations=json.loads(Path(__file__).with_name('warning-translations.json').read_text(encoding='utf-8'))
assert list(translations)==['en','ru','de','fr','cs','uk']
header=''
for language,message in translations.items():
 words=struct.unpack('<'+'H'*(len(message.encode('utf-16le'))//2),message.encode('utf-16le'))
 header+='static const W warning_'+language+'[]={'+','.join(hex(w) for w in words)+',0};\n'
header+='static const W *warning_translations[]={'+','.join('warning_'+s for s in translations)+'};\n'
(a.out/'large_map_warning_text.h').write_text(header)
subprocess.run([a.compiler,'-shared','-nostdlib','-Wall','-Werror','-I'+str(a.out),'-Wl,--image-base=0x10000000',str(Path(__file__).with_name('registry.c')),'-o',str(a.out/'registry.dll')],check=True)
pe=pefile.PE(str(a.out/'registry.dll'));assert not hasattr(pe,'DIRECTORY_ENTRY_IMPORT')
code=bytearray(pe.get_memory_mapped_image());exports={s.name.decode():s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols}
state_size=struct.unpack_from('<I',code,exports['registry_state_size'])[0]
fix=[(e.rva,0,1) for b in pe.DIRECTORY_ENTRY_BASERELOC for e in b.entries if e.type==3]
native=pefile.PE(str(a.exe));native_reloc={e.rva for b in native.DIRECTORY_ENTRY_BASERELOC for e in b.entries if e.type==3}
ks=Ks(KS_ARCH_X86,KS_MODE_32);md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
wrapper_start=(len(code)+4095)&~4095;data_offset=wrapper_start+0x4000;data=BASE+data_offset;pos=wrapper_start;hooks=[]
def put(asm):
 global pos
 off=pos;blob=bytes(ks.asm(asm,BASE+off)[0]);pos=(off+len(blob)+15)&~15
 assert pos<data_offset
 code.extend(bytes(max(0,pos-len(code))));code[off:off+len(blob)]=blob
 for ins in md.disasm(blob,BASE+off):
  for op in ins.operands:
   if op.type==CS_OP_IMM and op.imm==data:fix.append((ins.address-BASE+ins.imm_offset,0,1))
   if op.type==CS_OP_MEM and data<=op.mem.disp<data+state_size:fix.append((ins.address-BASE+ins.disp_offset,0,1))
   if op.type==CS_OP_MEM and op.mem.disp==IMAGE+0x5ef72c:fix.append((ins.address-BASE+ins.disp_offset,1,0))
 return off
def guard(site,offset,name,end=None):
 if end is None:
  n=0
  for ins in md.disasm(raw[site:site+32],IMAGE+site):
   n+=ins.size
   if n>=5:break
 else:n=end-site
 b=raw[site:site+n];rel=[r-site for r in native_reloc if site<=r<site+n]
 hooks.append(dict(site=site,offset=offset,name=name,before=b.hex(),relocations=rel))
for site,name,n in [(0x21117,'lookup',1),(0x23f5c8,'clear',0),(0x23f619,'add',2),(0x23f6dc,'remove_object',1),(0x23f755,'restore',2),(0x23f7b2,'swap_objects',3),(0x23f7f4,'set_generation',1),(0x23f868,'next_object',1),(0x23f8bb,'save',1),(0x23f920,'load',1),(0x23f979,'insert',2)]:
 asm='push ebp;mov ebp,esp;'+''.join(f'push dword ptr [ebp+{8+4*i}];' for i in reversed(range(n)))
 asm+=f'push ecx;push {data};call {BASE+exports[name]};add esp,{8+4*n};leave;ret {4*n};'
 guard(site,put(asm),name)
# Generic thiscall adapter for native stream scope and virtual Read/Write.
call2=put('push ebp;mov ebp,esp;push dword ptr [ebp+20];push dword ptr [ebp+16];mov ecx,[ebp+12];call dword ptr [ebp+8];leave;ret;')
# Pointer codec, cdecl stream + pointer-to-pointer. Preserve old 16-bit save
# support by selecting width from the registry's format read before objects.
for site in (0x160452,0x1604fc):
 asm=f'push ebp;mov ebp,esp;mov eax,[ebp+12];push dword ptr [eax];push dword ptr [ebp+8];push dword ptr [{IMAGE+0x5ef72c}];push {data};call {BASE+exports["write_reference"]};add esp,16;leave;ret;'
 guard(site,put(asm),'write_reference')
for site,actor in ((0x160471,False),(0x16051b,True)):
 asm=f'push ebp;mov ebp,esp;push esi;push dword ptr [ebp+8];push dword ptr [{IMAGE+0x5ef72c}];push {data};call {BASE+exports["read_reference"]};add esp,12;'
 if actor:asm+='mov esi,eax;test esi,esi;jz done;mov edx,[esi];mov ecx,esi;call dword ptr [edx+0xb4];test al,al;jz no;mov eax,esi;jmp done;no:xor eax,eax;done:;'
 asm+='mov edx,[ebp+12];mov [edx],eax;pop esi;leave;ret;'
 guard(site,put(asm),'read_actor_reference' if actor else 'read_reference')
# Native inline readers intentionally omit generation checks. Keep that
# contract while decoding bank bits. Preserve every GPR and EFLAGS except
# the original result register; editor/world/actor pointers stay unchanged.
inlines=[(0x85725,0x85737,'mov eax,[esi];mov eax,[eax]','eax'),(0x858cd,0x858dd,'mov eax,[esi]','ecx'),(0x85929,0x85939,'mov eax,[esi]','ecx'),(0xc2aa5,0xc2ab7,'mov eax,[ebx+0xc]','esi'),(0x2344d4,0x2344e4,'mov eax,[ecx+0xc]','eax'),(0x234506,0x234516,'mov eax,[ecx+0xc]','eax'),(0x2345c9,0x2345d9,'mov eax,[ecx+0xc]','edx')]
saved={'eax':28,'ecx':24,'edx':20,'esi':4}
for site,end,get_id,result in inlines:
 off=pos
 asm=f'pushfd;pushad;{get_id};push eax;push dword ptr [{IMAGE+0x5ef72c}];push {data};call {BASE+exports["unchecked_lookup"]};add esp,12;mov [esp+{saved[result]}],eax;popad;popfd;'
 # A jump to the original image has both image and inverse cave relocation.
 blob=bytes(ks.asm(asm,BASE+off)[0]);jump_at=off+len(blob)
 off=put(asm+f'jmp {IMAGE+end};');fix.append((jump_at+1,1,-1))
 guard(site,off,'inline_'+result,end)
# The world loader compares the saved generation before it creates the object.
# Its inline 16-bit decoder otherwise rejects every banked object as stale.
off=pos
asm=f'pushad;push dword ptr [ebp+0x1c];push dword ptr [{IMAGE+0x5ef72c}];push {data};call {BASE+exports["generation_matches"]};add esp,12;cmp eax,1;popad;'
jump_at=off+len(bytes(ks.asm(asm,BASE+off)[0]))
put(asm+f'jmp {IMAGE+0x246cf0};');fix.append((jump_at+1,1,-1))
guard(0x246cdc,off,'world_load_generation',0x246cf0)
# Native world-save offset arrays are another independent 65,536-entry
# allocation. Both save/load and the exception cleanup must use the new size.
for site in (0x248040,0x2481ef,0x24821c,0x248315,0x2491d1):
 assert raw[site:site+5]==bytes.fromhex('6804000400')
 off=pos
 put(f'push 0x100004;jmp {IMAGE+site+5};')
 fix.append((off+6,1,-1))
 guard(site,off,'world_save_offset_capacity',site+5)
# Terrain variant lookup hashes the linear tile index with 4723*x+1600.
# Signed 32-bit overflow makes its remainder negative on large maps, reading
# before the variant pointer array (log-440: EDX=-1, invalid ECX=0x00650074).
# Keep the mathematical hash and all small-map results; widen only its product.
site=0x1a7860;end=0x1a7873
assert raw[site:end]==bytes.fromhex('69c173120000b9551b0000054006000099f7f9')
off=pos
asm='mov eax,ecx;mov ecx,0x1273;imul ecx;add eax,0x640;adc edx,0;mov ecx,0x1b55;idiv ecx;'
jump_at=off+len(bytes(ks.asm(asm,BASE+off)[0]))
put(asm+f'jmp {IMAGE+end};');fix.append((jump_at+1,1,-1))
guard(site,off,'terrain_variant_hash_wide',end)
# Reuse the native staging Continue/Cancel dialog and StagingReallyLaunch
# callback. Only substitute its body for the large-map performance warning;
# the existing player-count warning, cancellation and EH cleanup stay native.
warning_offset=32+196608*6
def warning_adapter(asm):
 off=put(asm)
 for ins in md.disasm(bytes(code[off:pos]),BASE+off):
  for op in ins.operands:
   if op.type==CS_OP_IMM and IMAGE<=op.imm<IMAGE+len(raw):
    relative=ins.mnemonic=='call' or ins.mnemonic.startswith('j')
    assert ins.imm_size==4
    fix.append((ins.address-BASE+ins.imm_offset,1,-1 if relative else 0))
 return off
guard(0x104e89,warning_adapter(
 f'pushfd;pushad;push {data};call {BASE+exports["large_map_warning"]};add esp,4;test eax,eax;jz normal;'
 f'popad;popfd;jmp {IMAGE+0x104f8d};normal:popad;popfd;cmp dword ptr [ecx+0x6c],1;'
 f'jne {IMAGE+0x10503b};jmp {IMAGE+0x104e93};'),'large_map_warning_gate',0x104e93)
guard(0x104fbf,warning_adapter(
 f'push eax;mov eax,dword ptr [{data+warning_offset}];test eax,eax;jnz selected;'
 f'mov eax,{IMAGE+0x4a0258};selected:xchg eax,[esp];jmp {IMAGE+0x104fc4};'),
 'large_map_warning_body',0x104fc4)
guard(0x104fff,warning_adapter(
 f'cmp dword ptr [{data+warning_offset}],0;je native;mov eax,[esp];add esp,8;'
 f'jmp {IMAGE+0x105004};native:call {IMAGE+0x71800};jmp {IMAGE+0x105004};'),
 'large_map_warning_localization',0x105004)
# Textured minimap markers: EAX is texture width, [ESP+90] its height.
# Preserve all live XMM values and GPRs; the native loop uses the returned
# width and the resampled local source/stride, retaining native outlines.
guard(0x2d660f,warning_adapter(
 f'pushfd;pushad;lea edx,[esp+36];sub esp,128;'
 +''.join(f'movups [esp+{16*n}],xmm{n};' for n in range(8))+
 f'push eax;push edx;push {data};call {BASE+exports["minimap_icon"]};add esp,12;'
 'mov [esp+156],eax;'+''.join(f'movups xmm{n},[esp+{16*n}];' for n in range(8))+
 f'add esp,128;popad;popfd;movss xmm0,[esp+0x84];jmp {IMAGE+0x2d6618};'),
 'minimap_marker_scale',0x2d6618)
meta=dict(image=IMAGE,cave=BASE,dataOffset=data_offset,allocation=data_offset+state_size,stateSize=state_size,call2=call2,exports=exports,fixups=fix,hooks=hooks)
(a.out/'registry.bin').write_bytes(code);(a.out/'registry.json').write_text(json.dumps(meta,indent=2))
src='internal static class ExtendedRegistryPayload {\n'
src+=f'internal const int DataOffset={data_offset}, Allocation={meta["allocation"]}; internal const uint Call2={call2};\n'
src+='internal static readonly uint[] Sites={'+','.join(str(h['site']) for h in hooks)+'};\n'
src+='internal static readonly uint[] Offsets={'+','.join(str(h['offset']) for h in hooks)+'};\n'
src+='internal static byte[][] Originals(uint image){byte[][] b=new byte[][]{'+','.join('TerrainPatch.Hex("'+h['before']+'")' for h in hooks)+'};\n'
for n,h in enumerate(hooks):
 for r in h['relocations']:src+=f'Add(b[{n}],{r},unchecked(image-0x460000));\n'
src+='return b;}\ninternal static byte[] Build(uint image,uint cave){byte[] b=TerrainPatch.Hex("'+code.hex()+'");\n'
for off,im,cv in fix:
 expr=[]
 if im:expr.append('(image-0x460000)')
 if cv==1:expr.append('(cave-0x10000000)')
 if cv==-1:expr.append('(0x10000000-cave)')
 src+=f'Add(b,{off},unchecked({"+".join(expr)}));\n'
src+='return b;} static void Add(byte[]b,int o,uint d){System.Buffer.BlockCopy(System.BitConverter.GetBytes(unchecked(System.BitConverter.ToUInt32(b,o)+d)),0,b,o,4);}}'
(a.out/'ExtendedRegistryPayload.cs').write_text(src)
print('EXTENDED_REGISTRY_BUILD',len(hooks),'guarded hooks;',len(code),'code bytes;',state_size,'state bytes')
