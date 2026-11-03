"""Cap native group-wait threshold by the company's own attainable speed."""
import argparse,sys,hashlib,json,struct
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from keystone import Ks,KS_ARCH_X86,KS_MODE_32
raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
sites=[(0x26f69e,'f30f10000f2f472c','edi',0x2c),(0x27dc2c,'f30f10000f2f4624','esi',0x24),(0x27f499,'f30f10000f2f4624','esi',0x24)]
codes=[]
for i,(site,expected,state,speed) in enumerate(sites):
 assert raw[site:site+8]==bytes.fromhex(expected)
 # All three sites already require a valid group. Read the organization from
 # the company, or from the unit's existing membership component. No lookups,
 # native calls, scheduling changes, order writes, floating point division.
 asm=f'''movss xmm0,[eax]; push eax; push edx;
 mov eax,[{state}+4]; test eax,eax; jz done;
 mov eax,[eax+4]; test eax,eax; jz done;
 mov edx,[eax+0x7c]; test edx,edx; jnz organization;
 mov eax,[eax+0x80]; test eax,eax; jz done;
 mov eax,[eax+0x10]; test eax,eax; jz done;
 mov edx,[eax+0x7c]; test edx,edx; jz done;
 organization: mov eax,[edx+0xd8]; test eax,eax; jle done;
 cmp eax,0x7f800000; jae done;
 minss xmm0,[edx+0xd8];
 done: pop edx; pop eax; comiss xmm0,[{state}+{speed}];'''
 # Last instruction is a rel32 jump patched by the C# installer.
 code=bytes(Ks(KS_ARCH_X86,KS_MODE_32).asm(asm)[0])+bytes.fromhex('e900000000')
 codes.append(code.hex())

# Match the original handle resolver's ABI and registry lookup. Membership in
# a live group alone is insufficient: modes 0 and 2 do not synchronize speed.
# Preserve the old result for unexpected actor types rather than reading their
# group-only fields. The native lookup also checks the full generation handle.
gate='''mov ecx,[ecx]; push ecx; mov eax,esp; mov [eax],ecx;
 mov ecx,[0xa4f72c]; call 0x481117;
 test eax,eax; jz inactive;
 cmp dword ptr [eax],0x947d60; jne valid;
 cmp dword ptr [eax+0x1c0],1; jne inactive;
 cmp dword ptr [eax+0x1ac],0; je inactive;
 valid: mov al,1; ret;
 inactive: xor al,al; ret;'''
gate=bytes(Ks(KS_ARCH_X86,KS_MODE_32).asm(gate,addr=0x60000000)[0])
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
call=next(i for i in Cs(CS_ARCH_X86,CS_MODE_32).disasm(gate,0x60000000) if i.mnemonic=='call')
relocs=[(gate.index(struct.pack('<I',0xa4f72c)),0x5ef72c),(gate.index(struct.pack('<I',0x947d60)),0x4e7d60)]
calloff=call.address-0x60000000+1
for site,expected in [(0x26f68a,'e8e102e2ff'),(0x27dc18,'e8531de1ff'),(0x27f485,'e8e604e1ff')]:
 assert raw[site:site+5]==bytes.fromhex(expected)
 sites.append((site,expected,'ecx',0));codes.append(gate.hex())
a.out.mkdir(parents=True,exist_ok=True)
(a.out/'speed.json').write_text(json.dumps({'sites':sites,'codes':codes},indent=2))
src='using System;\ninternal static class GroupMovementSpeedPayload {\ninternal static readonly string[] Codes = new string[] {\n'+',\n'.join('"'+x+'"' for x in codes)+'};\n'
src+='internal static byte[] Build(int index,uint image,uint cave) {byte[] b=TerrainPatch.Hex(Codes[index]);if(index<3){int o=b.Length-4;Array.Copy(BitConverter.GetBytes(unchecked(image+GroupMovementSpeedPatch.Sites[index]+8-(cave+(uint)b.Length))),0,b,o,4);}else{'
for off,rva in relocs:src+=f'Array.Copy(BitConverter.GetBytes(unchecked(image+0x{rva:x}u)),0,b,{off},4);'
src+=f'Array.Copy(BitConverter.GetBytes(unchecked(image+0x21117u-(cave+{calloff+4}u))),0,b,{calloff},4);'
src+='}return b;}\n}'
(a.out/'GroupMovementSpeedPayload.cs').write_text(src)
print('GROUP_MOVEMENT_SPEED_BUILD_PASS',list(map(lambda x:len(x)//2,codes)))
