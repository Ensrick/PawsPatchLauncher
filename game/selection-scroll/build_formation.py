"""Extend the stock six-slot formation rows without replacing native centering."""
import argparse,sys,struct,json,hashlib
from pathlib import Path
PATTERNS=[(3,[(0,4),(-10,4),(10,4),(0,-4),(-10,-4),(10,-4)]),
          (2,[(5.75,8),(-5.75,8),(5.75,0),(-5.75,0),(5.75,-8),(-5.75,-8)]),
          (3,[(0,8),(-4,8),(4,8),(0,-8),(-4,-8),(4,-8)])]
def main():
 p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
 sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
 from keystone import Ks,KS_ARCH_X86,KS_MODE_32
 from capstone import Cs,CS_ARCH_X86,CS_MODE_32,CS_OP_IMM
 image=0x460000;cave=0x60000000;site=0x2337ba;original=bytes.fromhex('3b5c24147d16')
 raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
 assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
 assert raw[site:site+6]==original
 asm=f'''cmp ebx,dword ptr [esp+20]; jl {image+0x2337c0};
 pushfd; pushad; sub esp,32; movups [esp],xmm0; movups [esp+16],xmm1;
 mov eax,[edi+440]; test eax,eax; jz fallback;
 cmp dword ptr [eax+28],6; jne fallback;
 mov edi,[eax+24]; test edi,edi; jz fallback;
 '''
 for i,(cols,points) in enumerate(PATTERNS):
  for j,word in enumerate(struct.unpack('<12I',b''.join(struct.pack('<ff',*v) for v in points))):asm+=f'cmp dword ptr [edi+{4*j}],{word}; jne pattern_{i+1};\n'
  asm+=f'mov ecx,{cols}; jmp calculate; pattern_{i+1}:\n'
 asm+=f'''jmp fallback;
 calculate: mov eax,ebx; xor edx,edx; div ecx;
 movss xmm4,[edi+edx*8]; movss xmm2,[edi+edx*8+4];
 movss xmm0,[edi+ecx*8+4]; subss xmm0,[edi+4];
 cvtsi2ss xmm1,eax; mulss xmm0,xmm1; addss xmm2,xmm0;
 movups xmm0,[esp]; movups xmm1,[esp+16]; add esp,32; popad; popfd;
 jmp {image+0x233828};
 fallback: movups xmm0,[esp]; movups xmm1,[esp+16]; add esp,32; popad; popfd;
 jmp {image+0x2337d6};'''
 blob=bytes(Ks(KS_ARCH_X86,KS_MODE_32).asm(asm,cave)[0]);fixups=[]
 md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True
 for ins in md.disasm(blob,cave):
  for op in ins.operands:
   if op.type==CS_OP_IMM and op.imm in (image+0x2337c0,image+0x233828,image+0x2337d6):fixups.append((ins.address-cave+ins.imm_offset,op.imm-image))
 assert len(fixups)==3 and len(blob)<4096
 a.out.mkdir(parents=True,exist_ok=True);(a.out/'formation.bin').write_bytes(blob);(a.out/'formation.asm').write_text(asm)
 meta=dict(image=image,cave=cave,site=site,fixups=fixups,patterns=PATTERNS);(a.out/'formation.json').write_text(json.dumps(meta,indent=2))
 src='using System;\ninternal static class GroupFormationPayload {\ninternal static byte[] Build(uint image,uint cave){byte[] b=TerrainPatch.Hex("'+blob.hex()+'");\n'
 for off,target in fixups:src+=f'Array.Copy(BitConverter.GetBytes(unchecked(image+{target}-(cave+{off}+4))),0,b,{off},4);\n'
 src+='return b;}}';(a.out/'GroupFormationPayload.cs').write_text(src)
 print('GROUP_FORMATION_BUILD_PASS',len(blob),'bytes')
if __name__=='__main__':main()
