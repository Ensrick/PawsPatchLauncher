"""Build deterministic UI-only layout hooks; no live process access."""
import argparse, struct, json, base64
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True)
p.add_argument('--update-source', type=Path, help='Replace the marked generated C# block')
a=p.parse_args()
import sys
sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
from keystone import Ks,KS_ARCH_X86,KS_MODE_32
from capstone import Cs,CS_ARCH_X86,CS_MODE_32,CS_OP_IMM
image=0x460000;cave=0x10000000
def match(name,label,index):
 b=(name+'\0').encode('utf-16le');s=''
 for off in range(0,len(b),4):
  part=b[off:off+4];kind='dword' if len(part)==4 else 'word'
  s+=f'cmp {kind} ptr [edx+{off}], {int.from_bytes(part,"little")}\njne next{index}\n'
 return s+f'jmp {label}\nnext{index}:\n'
layout=f'''
 pushfd
 pushad
 sub esp,48
 movdqu [esp],xmm0
 movdqu [esp+16],xmm1
 mov eax,[esi+0xc]
 test eax,eax
 jz done
 cmp [eax+0x10],esi
 jne done
 mov edx,[eax+4]
 test edx,edx
 jz done
 '''
for i,(n,label) in enumerate([('FavoritesDisplay','favorites'),('ConnectionStatusDialog','dialog'),('EconomicImpact','economy'),('OrganizationEditor','editor'),('GameOrganizationEditor','editor')]):layout+=match(n,label,i)
layout+=f'''jmp done
editor:
 xor ebx,ebx
 jmp center
favorites:
 mov ebx,0x41080000
 jmp center
dialog:
 mov ebx,0x3fc00000
center:
 call {cave+0xd00}
 test al,al
 jz done
 cvtsi2ss xmm0,dword ptr [{image+0x5f9228}]
 subss xmm0,[esi+0x28]
 addss xmm0,[esi+0x20]
 push 0x3f000000
 mulss xmm0,[esp]
 add esp,4
 subss xmm0,[esi+0x20]
 movd xmm1,ebx
 mov eax,[{image+0x5f94d0}]
 mov eax,[eax+0x88]
 divss xmm1,[eax]
 movdqu [esp+32],xmm7
 cvtsi2ss xmm7,dword ptr [{image+0x5f9228}]
 mulss xmm1,xmm7
 movdqu xmm7,[esp+32]
 addss xmm0,xmm1
 jmp shift
economy:
 call {cave+0xd00}
 test al,al
 jz done
 cvtsi2ss xmm0,dword ptr [{image+0x5f9228}]
 subss xmm0,[esi+0x28]
shift:
 movss xmm1,[esi+0x20]
 addss xmm1,xmm0
 movss [esi+0x20],xmm1
 movss xmm1,[esi+0x28]
 addss xmm1,xmm0
 movss [esi+0x28],xmm1
done:
 movdqu xmm0,[esp]
 movdqu xmm1,[esp+16]
 add esp,48
 popad
 popfd
 mov eax,[esi+0x88]
 ret
'''
tooltip=f'''
 push ebp
 mov ebp,esp
 push esi
 mov esi,ecx
 push dword ptr [esi+0x94]
 cmp dword ptr [esi+0x94],0x44800000
 jne native
 push eax
 call {cave+0xd00}
 test al,al
 jz skip
 mov eax,[{image+0x5f94d0}]
 mov eax,[eax+0x88]
 mov eax,[eax]
 mov [esi+0x94],eax
skip:
 pop eax
native:
 push dword ptr [ebp+16]
 push dword ptr [ebp+12]
 push dword ptr [ebp+8]
 call {image+0x2b8185}
 pop dword ptr [esi+0x94]
 pop esi
 leave
 ret 12
'''
group_spacing='''
 subss xmm0,[esp+16]
 push 0x43ce0000
 minss xmm0,[esp]
 lea esp,[esp+4]
 ret
'''
md=Cs(CS_ARCH_X86,CS_MODE_32);md.detail=True;fixes=[];buf=bytearray(0x800)
for offset,source in [(0x1000,layout),(0x1500,tooltip),(0x1600,group_spacing)]:
 code=bytes(Ks(KS_ARCH_X86,KS_MODE_32).asm(source,cave+offset)[0]);assert len(code)<0x500
 buf[offset-0x1000:offset-0x1000+len(code)]=code
 for ins in md.disasm(code,cave+offset):
  at=ins.address-cave-0x1000
  if ins.disp_size==4 and image<=ins.disp<image+0x700000:fixes.append((1,at+ins.disp_offset,ins.disp-image))
  for op in ins.operands:
   if op.type==CS_OP_IMM and ins.mnemonic in ('call','jmp'):
    val=op.imm&0xffffffff
    if image<=val<image+0x700000:fixes.append((3,at+ins.imm_offset,val-image))
    # Internal relative calls remain unchanged when the complete cave moves.
a.out.mkdir(parents=True,exist_ok=True)
(a.out/'hud.bin').write_bytes(buf);(a.out/'hud-fixups.json').write_text(json.dumps(fixes));(a.out/'layout.asm').write_text(layout);(a.out/'tooltip.asm').write_text(tooltip)
(a.out/'group-spacing.asm').write_text(group_spacing)
lines=['    // BEGIN GENERATED HUD LAYOUT (build_hud_native.py)','    internal static byte[] HudLayoutCode(uint image,uint cave)','    {','        byte[] b=Convert.FromBase64String("'+base64.b64encode(buf).decode()+'");']
for kind,off,rva in fixes:
 expr=f'image+0x{rva:x}' if kind==1 else f'image+0x{rva:x}-(cave+0x{0x1000+off+4:x})'
 lines.append(f'        Buffer.BlockCopy(BitConverter.GetBytes(unchecked({expr})),0,b,0x{off:x},4);')
lines+=['        return b;','    }','    // END GENERATED HUD LAYOUT']
(a.out/'HudLayout.generated.txt').write_text('\n'.join(lines)+'\n')
if a.update_source:
 text=a.update_source.read_text(encoding='utf-8-sig')
 begin=text.index(lines[0]);end=text.index(lines[-1],begin)+len(lines[-1])
 assert text.count(lines[0])==text.count(lines[-1])==1
 a.update_source.write_text(text[:begin]+'\n'.join(lines)+text[end:],encoding='utf-8')
print('HUD_CODE_BUILT',len(buf),'bytes',len(fixes),'relocations')
