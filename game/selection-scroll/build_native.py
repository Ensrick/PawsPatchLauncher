import argparse,subprocess,sys,struct,json,hashlib
from pathlib import Path
sys.dont_write_bytecode=True
HOOKS=[(0x493404,0xdacca,'build'),(0x493408,0xdae69,'reset'),(0x2f5109,0x2f5043,'wheel'),(0x498c9c,0xed498,'batch')]
def main():
 p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('--compiler',required=True);a=p.parse_args()
 sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
 import pefile
 from keystone import Ks,KS_ARCH_X86,KS_MODE_32
 from capstone import Cs,CS_ARCH_X86,CS_MODE_32,CS_OP_IMM
 a.out.mkdir(parents=True,exist_ok=True);dll=a.out/'selection.dll'
 subprocess.run([a.compiler,'-shared','-nostdlib','-Wall','-Werror','-Wl,--image-base=0x10000000',str(Path(__file__).with_name('selection.c')),'-o',str(dll)],check=True)
 pe=pefile.PE(str(dll));assert not hasattr(pe,'DIRECTORY_ENTRY_IMPORT');base=pe.OPTIONAL_HEADER.ImageBase;assert base==0x10000000
 code=bytearray(pe.get_memory_mapped_image());size=(len(code)+4095)&~4095;code.extend(bytes(size-len(code)))
 exports={s.name.decode():s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols}
 reloc=[(e.rva,0,1) for block in getattr(pe,'DIRECTORY_ENTRY_BASERELOC',[]) for e in block.entries if e.type==3]
 ks=Ks(KS_ARCH_X86,KS_MODE_32);md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
 image=0x460000;data=base+size+4096;hooks=[];raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
 assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
 assert raw[0xed127:0xed12c]==bytes.fromhex('83f8067d3b')
 for index,(site,target,name) in enumerate(HOOKS):
  assert struct.unpack_from('<I',raw,site)[0]==image+target
  off=size+index*512;addr=base+off
  asm='pushfd;pushad;mov ebp,esp;sub esp,528;lea eax,[esp+15];and eax,0xfffffff0;fxsave [eax];push eax;'
  if name=='build':asm+='push dword ptr [ebp+40];push ecx;'
  elif name=='reset':asm+='push ecx;'
  else:asm+='push edi;push ebx;push dword ptr [ebp+56];'
  if name!='batch':asm+=f'push {data};push {image};call {base+exports[name]};add esp,{16 if name=="build" else 12 if name=="reset" else 20};'
  if name=='wheel':asm+='mov [ebp+28],eax;'
  asm+='pop eax;fxrstor [eax];add esp,528;popad;popfd;'
  if name=='build':asm+='ret 4;'
  elif name=='reset':asm+=f'jmp {image+target};'
  else:asm+=f'test eax,eax;jnz consumed;jmp {image+target};consumed:jmp {image+0x2f4c40};'
  if name=='batch':asm=f'jmp {image+0x2d9ceb};'
  blob=bytes(ks.asm(asm,addr)[0]);assert len(blob)<512
  code.extend(bytes(max(0,off+len(blob)-len(code))));code[off:off+len(blob)]=blob
  for ins in md.disasm(blob,addr):
   for op in ins.operands:
    if op.type==CS_OP_IMM:
     if op.imm in (image,image+target,image+0x2f4c40,image+0x2d9ceb):reloc.append((ins.address-base+ins.imm_offset,1,-1 if ins.mnemonic=='jmp' else 0))
     elif op.imm==data:reloc.append((ins.address-base+ins.imm_offset,0,1))
  hooks.append(dict(site=site,target=target,name=name,offset=off))
 assert len(code)<size+4096
 meta=dict(image=image,cave=base,dataOffset=size+4096,allocation=size+8192,fixups=reloc,hooks=hooks,exports=exports)
 (a.out/'selection.bin').write_bytes(code);(a.out/'selection.json').write_text(json.dumps(meta,indent=2))
 src='using System;\ninternal static class SelectionScrollPayload {\n'
 src+=f'internal const int DataOffset={meta["dataOffset"]}, Allocation={meta["allocation"]};\n'
 for key in ('Sites','Targets','Offsets'):
  field={'Sites':'site','Targets':'target','Offsets':'offset'}[key];src+='internal static readonly uint[] '+key+'={'+','.join(str(x[field]) for x in hooks)+'};\n'
 src+='internal static byte[] Build(uint image,uint cave){byte[] b=TerrainPatch.Hex("'+code.hex()+'");\n'
 for off,im,cv in reloc:
  expr=[]
  if im:expr.append('(image-0x460000)')
  if cv==1:expr.append('(cave-0x10000000)')
  if cv==-1:expr.append('(0x10000000-cave)')
  src+=f'Add(b,{off},unchecked({"+".join(expr)}));\n'
 src+='return b;}static void Add(byte[]b,int o,uint d){Buffer.BlockCopy(BitConverter.GetBytes(unchecked(BitConverter.ToUInt32(b,o)+d)),0,b,o,4);}}'
 (a.out/'SelectionScrollPayload.cs').write_text(src)
 print('SELECTION_SCROLL_BUILD_PASS',len(code),'bytes',len(reloc),'relocations')
if __name__=='__main__':main()
