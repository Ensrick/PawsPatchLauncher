"""Shared animation-target validation and missing network-client handling, 1.3.72.

The vectored probe handler catches only reads in its own validation block. It
never catches an exception from a game function or resumes a failed instruction.
"""
import argparse, hashlib, json, struct, sys
from pathlib import Path

IMAGE=0x460000
CAVE=0x10000000
SITES=[0x31EAA6,0x14C047,0xB764B,0xB766C,0xB7688,0xB77D2,0x31CEAA]
ORIGINALS=['8b4e34894df8','8b400c83782800','56ff7424088bf1',
           'c781dc01000002000000','535556578bf9','b874fd8700','8b016a00ff5040']
OFFSETS=[0,0x100,0x500,0x600,0x700,0x800,0x900]
# TeamCommand starts with an absolute SEH descriptor address.
ORIGINAL_FIXUPS=[(5,1)]
PROBE=0x200
HANDLER=0x400
FAULT=0x380
DATA=0x1000
SIZE=0x2000
STOP_PROBE=0xA00
CONTROLLER_TABLE=0xC00

def controller_tables(raw):
    """Derive the native controller family from the pinned MSVC RTTI, offline.

    A readable recycled NiMaterialProperty also has a positive refcount, so
    executable-slot/range checks alone do not establish the object's type.
    Never invoke an untrusted object's virtual GetRTTI during this probe.
    """
    def u(o): return struct.unpack_from('<I',raw,o)[0]
    def name(td):
        o=td-IMAGE+8
        return raw[o:raw.index(b'\0',o)].decode('ascii')
    found=[]
    for table in range(0x44D004,0x5CFFDC,4):
        try:
            col=u(table-4)-IMAGE
            if not 0x44D000<=col<0x5CFFDC or u(col)!=0: continue
            hierarchy=u(col+16)-IMAGE
            if not 0x44D000<=hierarchy<0x5CFFDC: continue
            count=u(hierarchy+8); array=u(hierarchy+12)-IMAGE
            if not 0<count<100: continue
            bases=[name(u(u(array+i*4)-IMAGE)) for i in range(count)]
            if '.?AVNiTimeController@@' in bases:
                found.append(dict(rva=table,name=name(u(col+12)),stop=u(table+0x40)-IMAGE))
        except (ValueError,struct.error,UnicodeError,IndexError): pass
    assert len(found)==36
    assert all(0x1000<=t['stop']<0x44CBB6 for t in found)
    assert 0x53C3C0 not in [t['rva'] for t in found] # log-70 recycled material
    return found

def build(ks,image,cave,controllers):
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
            cmp ecx,{cave+0x300}; jb recover;
            cmp ecx,{cave+STOP_PROBE}; jb search;
            cmp ecx,{cave+STOP_PROBE+0x100}; jae search;
            mov dword ptr [{data+84}],0;
        recover:
            mov dword ptr [edx+0xb8],{cave+FAULT};
            mov eax,-1; ret 4;
        search: xor eax,eax; ret 4;
        ''',
        0x900:f'''
            pushfd; pushad; push ecx;
            call {cave+STOP_PROBE}; add esp,4;
            test eax,eax; jz stop_valid;
            inc dword ptr [{data+56}]; inc dword ptr [{data+56}+eax*4];
            mov [{data+88}],eax; mov [{data+80}],edi;
            mov eax,[esp+24]; mov [{data+76}],eax;
            popad; popfd; jmp {image+0x31CEB1};
        stop_valid:
            popad; popfd; mov eax,[ecx]; push 0;
            call dword ptr [eax+0x40]; jmp {image+0x31CEB1};
        ''',
        STOP_PROBE:f'''
            push ebp; mov ebp,esp;
            push 0; call {cave+PROBE}; add esp,4;
            cmp dword ptr [{data+40}],0; je stop_valid;
            mov ecx,[ebp+8]; test ecx,ecx; jz stop_valid;
            cmp dword ptr [ecx+4],0; jle stop_dead;
            mov eax,[ecx];
            mov edx,{len(controllers)};
        table_loop:
            dec edx; cmp eax,[{cave+CONTROLLER_TABLE}+edx*4]; je table_valid;
            test edx,edx; jnz table_loop;
            mov [{data+84}],eax; mov eax,2; jmp stop_done;
        table_valid:
            mov edx,eax; mov eax,[eax+0x40];
            cmp eax,{image+0x1000}; jb stop_function;
            cmp eax,{image+0x44CBB6}; jae stop_function;
        stop_valid: xor eax,eax; jmp stop_done;
        stop_dead: mov dword ptr [{data+84}],0; mov eax,1; jmp stop_done;
        stop_function: mov [{data+84}],edx; mov eax,3;
        stop_done: pop ebp; ret;
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
    code=bytearray(DATA)
    lengths={}
    for offset,asm in parts.items():
        blob=bytes(ks.asm(asm,cave+offset)[0]); lengths[str(offset)]=len(blob)
        assert len(blob)<(0x180 if offset==PROBE else 0x100)
        code[offset:offset+len(blob)]=blob
    assert lengths[str(PROBE)]<0x100
    for i,t in enumerate(controllers):struct.pack_into('<I',code,CONTROLLER_TABLE+4*i,image+t['rva'])
    return bytes(code),lengths

def main():
    p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
    sys.path[:0]=[str(a.legacy/'pydeps_readable'),str(a.legacy/'lobby_colors_1372/deps_r15')]
    from keystone import Ks,KS_ARCH_X86,KS_MODE_32
    raw=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
    assert hashlib.sha256(raw).hexdigest()=='b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c'
    for site,s in zip(SITES,ORIGINALS):assert raw[site:site+len(bytes.fromhex(s))]==bytes.fromhex(s)
    controllers=controller_tables(raw)
    ks=Ks(KS_ARCH_X86,KS_MODE_32);code,lengths=build(ks,IMAGE,CAVE,controllers)
    # Derive all absolute and relative fixups by independent rebuilds; validate
    # at widely separated ASLR bases. Instruction lengths must remain constant.
    fixes=[]
    for image,cave,kind in [(IMAGE+0x123000,CAVE,'image'),(IMAGE,CAVE+0x234000,'cave')]:
        altered,_=build(ks,image,cave,controllers);assert len(altered)==len(code)
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
    for ib,cb in [(0xe40000,0x21000000),(0x12000000,0x61000000),(0x730000,0x33000000)]:assert relocate(ib,cb)==build(ks,ib,cb,controllers)[0]
    a.out.mkdir(parents=True,exist_ok=True);(a.out/'payload.bin').write_bytes(code)
    meta=dict(sites=SITES,originals=ORIGINALS,originalFixups=ORIGINAL_FIXUPS,offsets=OFFSETS,probe=PROBE,stopProbe=STOP_PROBE,controllers=controllers,handler=HANDLER,fault=FAULT,dataOffset=DATA,allocation=SIZE,lengths=lengths,fixups=fixes)
    (a.out/'payload.json').write_text(json.dumps(meta,indent=2))
    src='// Generated by engine-crash-fixes/build_native.py\nusing System;\ninternal static class EngineCrashPayload {\n'
    src+=f'internal const int Allocation={SIZE},DataOffset={DATA},ProbeOffset={PROBE},StopProbeOffset={STOP_PROBE};\n'
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
