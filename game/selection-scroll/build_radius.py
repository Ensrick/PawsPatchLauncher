import argparse,subprocess,sys,struct,json,hashlib
from pathlib import Path
def main():
 p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('--compiler',required=True);a=p.parse_args()
 sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
 import pefile
 from keystone import Ks,KS_ARCH_X86,KS_MODE_32
 from capstone import Cs,CS_ARCH_X86,CS_MODE_32,CS_OP_IMM
 raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes();assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
 site=0x29941f;target=site+5;seh=0x442ce7
 assert raw[site:site+5]==bytes.fromhex('b8e72c8a00')
 a.out.mkdir(parents=True,exist_ok=True);dll=a.out/'radius.dll'
 subprocess.run([a.compiler,'-shared','-nostdlib','-Wall','-Werror','-Wl,--image-base=0x10000000',str(Path(__file__).with_name('radius.c')),'-o',str(dll)],check=True)
 pe=pefile.PE(str(dll));assert not hasattr(pe,'DIRECTORY_ENTRY_IMPORT');base=0x10000000;assert pe.OPTIONAL_HEADER.ImageBase==base
 code=bytearray(pe.get_memory_mapped_image());size=(len(code)+4095)&~4095;code.extend(bytes(size+4096-len(code)))
 exports={s.name.decode():s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols};fixups=[(e.rva,0,1) for b in getattr(pe,'DIRECTORY_ENTRY_BASERELOC',[]) for e in b.entries if e.type==3]
 image=0x460000;at=base+size
 # Preserve FP/SIMD state and registers. Replay the native SEH prologue after adjusting only the radius argument.
 asm=f"""pushfd;pushad;mov ebp,esp;sub esp,528;lea eax,[esp+15];and eax,0xfffffff0;fxsave [eax];push eax;
 cmp dword ptr [ebp+48],0;jle finish;push ecx;push {image};call {base+exports['radiusSquared']};add esp,8;
 test eax,eax;jz finish;movd xmm0,eax;sqrtss xmm0,xmm0;
 mov eax,0x40800000;movd xmm1,eax;addss xmm0,xmm1;
 maxss xmm0,[ebp+48];movss [ebp+48],xmm0;
 finish:pop eax;fxrstor [eax];add esp,528;popad;popfd;mov eax,{image+seh};jmp {image+target};"""
 blob=bytes(Ks(KS_ARCH_X86,KS_MODE_32).asm(asm,at)[0]);code[size:size+len(blob)]=blob
 md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
 for ins in md.disasm(blob,at):
  for op in ins.operands:
   if op.type==CS_OP_IMM:
    if ins.mnemonic=='push' and op.imm==image:fixups.append((ins.address-base+ins.imm_offset,1,0))
    if ins.mnemonic=='mov' and op.imm==image+seh:fixups.append((ins.address-base+ins.imm_offset,1,0))
    if ins.mnemonic=='jmp' and op.imm==image+target:fixups.append((ins.address-base+ins.imm_offset,1,-1))
 meta=dict(image=image,cave=base,offset=size,allocation=len(code),site=site,target=target,seh=seh,fixups=fixups,exports=exports)
 (a.out/'radius.bin').write_bytes(code);(a.out/'radius.json').write_text(json.dumps(meta,indent=2))
 src='using System;\ninternal static class GroupFormationRadiusPayload {\n'
 src+=f'internal const int Offset={size}, Allocation={len(code)};\ninternal static byte[] Build(uint image,uint cave){{byte[] b=TerrainPatch.Hex("'+code.hex()+'");\n'
 for off,im,cv in fixups:
  terms=[]
  if im:terms.append('(image-0x460000)')
  if cv==1:terms.append('(cave-0x10000000)')
  if cv==-1:terms.append('(0x10000000-cave)')
  src+=f'Add(b,{off},unchecked({"+".join(terms)}));\n'
 src+='return b;}static void Add(byte[] b,int o,uint d){Buffer.BlockCopy(BitConverter.GetBytes(unchecked(BitConverter.ToUInt32(b,o)+d)),0,b,o,4);}}'
 (a.out/'GroupFormationRadiusPayload.cs').write_text(src);print('GROUP_FORMATION_RADIUS_BUILD_PASS',len(code),len(fixups))
if __name__=='__main__':main()
