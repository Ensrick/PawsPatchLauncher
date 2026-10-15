"""Shared animation-target validation, missing network-client handling and the network enum-read assertion,
1.3.72.

The vectored probe handler catches only reads in its own validation block. It
never catches an exception from a game function or resumes a failed instruction.
"""
import argparse, hashlib, json, struct, sys
from pathlib import Path

IMAGE=0x460000
CAVE=0x10000000
SITES=[0x31EAA6,0x14C047,0xB764B,0xB766C,0xB7688,0xB77D2,0x6AD8C,0x6ADC5]
ORIGINALS=['8b4e34894df8','8b400c83782800','56ff7424088bf1',
           'c781dc01000002000000','535556578bf9','b874fd8700',
           # call AssertionHandler for Serialize.h `_data>=0` / `_data<_n_possibilities`
           'e8bd672900','e884672900']
OFFSETS=[0,0x100,0x500,0x600,0x700,0x800,0x900,0x980]
# TeamCommand starts with an absolute SEH descriptor address.
ORIGINAL_FIXUPS=[(5,1)]
PROBE=0x200
HANDLER=0x400
FAULT=0x380
DATA=0x1000
SIZE=0x2000

def build(ks,image,cave):
    data=cave+DATA
    parts={
        0:f'''
            mov ecx,[esi+0x34]; mov [ebp-8],ecx;
            pushfd; pushad;
            push ecx; call {cave+PROBE}; add esp,4;
            test eax,eax; jz valid;
            inc dword ptr [{data}]; inc dword ptr [{data}+eax*4];
            mov [{data+24}],esi; mov [{data+32}],eax;
            mov eax,[ebp-8]; mov [{data+28}],eax;
            popad; popfd; jmp {image+0x31EB1A};
        valid: popad; popfd; jmp {image+0x31EAAC};
        ''',
        0x100:f'''
            test eax,eax; jz missing;
            mov eax,[eax+0xc]; test eax,eax; jz missing;
            cmp dword ptr [eax+0x28],0; jmp {image+0x14C04E};
        missing:
            inc dword ptr [{data+20}]; xor eax,eax;
            jmp {image+0x14C04E};
        ''',
        PROBE:f'''
            push ebp; mov ebp,esp;
            cmp dword ptr [{data+40}],0; jne ready;
            cmp dword ptr [{data+48}],0; jne valid;
            pushad; push {cave+HANDLER}; push 1;
            call dword ptr [{data+44}]; mov [{data+40}],eax;
            test eax,eax; jnz registered;
            inc dword ptr [{data+48}];
        registered: popad;
            cmp dword ptr [{data+40}],0; je valid;
        ready:
            mov ecx,[ebp+8]; test ecx,ecx; jz valid;
            cmp dword ptr [ecx+4],0; jle dead;
            mov eax,[ecx];
            cmp eax,{image+0x44D000}; jb bad_table;
            cmp eax,{image+0x5CFFDC}; ja bad_table;
            mov eax,[eax+4];
            cmp eax,{image+0x1000}; jb bad_function;
            cmp eax,{image+0x44CBB6}; jae bad_function;
        valid: xor eax,eax; jmp done;
        dead: mov eax,1; jmp done;
        bad_table: mov eax,2; jmp done;
        bad_function: mov eax,3;
        done:
            pop ebp; ret;
        ''',
        FAULT:f'''
            mov eax,4; pop ebp; ret;
        ''',
        HANDLER:f'''
            mov edx,[esp+4]; mov eax,[edx];
            cmp dword ptr [eax],0xc0000005; jne search;
            cmp dword ptr [eax+4],0; jne search;
            cmp dword ptr [eax+0x10],2; jb search;
            cmp dword ptr [eax+0x14],0; jne search;
            mov edx,[edx+4]; mov ecx,[edx+0xb8];
            cmp ecx,{cave+PROBE}; jb search;
            cmp ecx,{cave+0x300}; jae search;
            mov dword ptr [edx+0xb8],{cave+FAULT};
            mov eax,-1; ret 4;
        search: xor eax,eax; ret 4;
        '''}
    # Guard every step of the F macro before changing menus, selecting allies
    # or entering the team-command handler. The native local-kingdom getter
    # returns null for observers (player mode 3), even while viewing a kingdom.
    # These are UI-only handlers; no simulation or network state is changed.
    for index,replay in [(2,'push esi; push dword ptr [esp+8]; mov esi,ecx;'),
                         (3,'mov dword ptr [ecx+0x1dc],2;'),
                         (4,'push ebx; push ebp; push esi; push edi; mov edi,ecx;'),
                         (5,f'mov eax,{image+0x41FD74};')]:
        parts[OFFSETS[index]]=f'''
            pushfd; pushad; call {image+0x2BE702};
            test eax,eax; jnz player;
            inc dword ptr [{data+52}]; popad; popfd; ret 4;
        player:
            popad; popfd; {replay}
            jmp {image+SITES[index]+len(bytes.fromhex(ORIGINALS[index]))};
        '''
    # The network enum read (Serialize.h) asserts on a value outside its range, on the simulation thread and while
    # it holds the session lock; the main thread then gives up waiting for that lock while the dialog waits. Both
    # call sites get Ignore, as if a player had clicked it (AL=1: the site stops checking for this run), and a count.
    # Every other assertion keeps its dialog.
    for offset,site in [(0x900,0x6AD8C),(0x980,0x6ADC5)]:
        parts[offset]=f'''
            inc dword ptr [{data+56}]; mov al,1;
            jmp {image+site+5};
        '''
    code=bytearray(DATA)
    lengths={}
    for offset,asm in parts.items():
        blob=bytes(ks.asm(asm,cave+offset)[0]); lengths[str(offset)]=len(blob)
        assert len(blob)<(0x180 if offset==PROBE else 0x100)
        code[offset:offset+len(blob)]=blob
    assert lengths[str(PROBE)]<0x100
    return bytes(code),lengths

def main():
    p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
    sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
    from keystone import Ks,KS_ARCH_X86,KS_MODE_32
    raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
    assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
    for site,s in zip(SITES,ORIGINALS):assert raw[site:site+len(bytes.fromhex(s))]==bytes.fromhex(s)
    ks=Ks(KS_ARCH_X86,KS_MODE_32);code,lengths=build(ks,IMAGE,CAVE)
    # Derive all absolute and relative fixups by independent rebuilds; validate
    # at widely separated ASLR bases. Instruction lengths must remain constant.
    fixes=[]
    for image,cave,kind in [(IMAGE+0x123000,CAVE,'image'),(IMAGE,CAVE+0x234000,'cave')]:
        altered,_=build(ks,image,cave);assert len(altered)==len(code)
        delta=(image-IMAGE)+(cave-CAVE);i=0
        while i<len(code):
            if code[i]==altered[i]:i+=1;continue
            # A relocation may start with unchanged low bytes (page alignment).
            found=False
            for start in range(max(0,i-3),i+1):
                old=struct.unpack_from('<I',code,start)[0];new=struct.unpack_from('<I',altered,start)[0]
                diff=(new-old)&0xffffffff
                if diff in (delta,(-delta)&0xffffffff):
                    fixes.append([start,kind,1 if diff==delta else -1]);i=start+4;found=True;break
            assert found,(i,kind)
    def relocate(ib,cb):
        b=bytearray(code)
        for off,kind,sign in fixes:
            d=(ib-IMAGE if kind=='image' else cb-CAVE)*sign
            struct.pack_into('<I',b,off,(struct.unpack_from('<I',b,off)[0]+d)&0xffffffff)
        return bytes(b)
    for ib,cb in [(0xe40000,0x21000000),(0x12000000,0x61000000),(0x730000,0x33000000)]:assert relocate(ib,cb)==build(ks,ib,cb)[0]
    a.out.mkdir(parents=True,exist_ok=True);(a.out/'payload.bin').write_bytes(code)
    meta=dict(sites=SITES,originals=ORIGINALS,originalFixups=ORIGINAL_FIXUPS,offsets=OFFSETS,probe=PROBE,handler=HANDLER,fault=FAULT,dataOffset=DATA,allocation=SIZE,lengths=lengths,fixups=fixes)
    (a.out/'payload.json').write_text(json.dumps(meta,indent=2))
    src='// Generated by engine-crash-fixes/build_native.py\nusing System;\ninternal static class EngineCrashPayload {\n'
    src+=f'internal const int Allocation={SIZE},DataOffset={DATA},ProbeOffset={PROBE};\n'
    src+='internal static readonly uint[] Sites={'+','.join(hex(s) for s in SITES)+'};\n'
    src+='internal static readonly uint[] Offsets={'+','.join(map(str,OFFSETS))+'};\n'
    src+='internal static readonly string[] Originals={'+','.join('"'+s+'"' for s in ORIGINALS)+'};\n'
    src+='internal static byte[] Original(int i,uint image){byte[] b=TerrainPatch.Hex(Originals[i]);\n'
    for index,offset in ORIGINAL_FIXUPS:
        src+=f'if(i=={index})Add(b,{offset},unchecked(image-0x460000));\n'
    src+='return b;}\n'
    src+='internal static byte[] Build(uint image,uint cave){byte[] b=TerrainPatch.Hex("'+code.hex()+'");\n'
    for off,kind,sign in fixes:
        expr='image-0x460000' if kind=='image' else 'cave-0x10000000'
        if sign<0:expr='0-('+expr+')'
        src+=f'Add(b,{off},unchecked({expr}));\n'
    src+='return b;}\nstatic void Add(byte[] b,int o,uint d){Buffer.BlockCopy(BitConverter.GetBytes(unchecked(BitConverter.ToUInt32(b,o)+d)),0,b,o,4);}\n}\n'
    (a.out/'EngineCrashPayload.cs').write_text(src)
    print('ENGINE_CRASH_BUILD_PASS',len(fixes),'fixups; guards; four ASLR layouts')
if __name__=='__main__':main()
