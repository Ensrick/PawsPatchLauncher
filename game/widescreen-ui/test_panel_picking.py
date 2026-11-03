"""Execute native GroupVE/VE picking against panel geometry from built assets.

The full live-widget snapshot test is recorded in the verification report.
This portable regression uses minimal synthetic widget trees and original x86
picking (including native parent exclusion), with no hit-test function stubs.
"""
import argparse, hashlib, json, re, struct, sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--legacy',type=Path,required=True);p.add_argument('--assets',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
sys.path.insert(0,str(a.legacy/'lobby_colors_1372/deps_r15'))
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32
from unicorn.x86_const import UC_X86_REG_ECX,UC_X86_REG_ESP,UC_X86_REG_EAX,UC_X86_REG_EIP
native=(a.legacy/'k2_runtime_1372_20260904.bin').read_bytes()
assert hashlib.sha256(native).hexdigest().upper()=='B865D8206990C4F055C51DE857F0B09B88AB5EE3B6AE74EE5232134001AA591C'
checks=0
sizes=((1024,768),(1280,1024),(1280,720),(1600,900),(1920,1080),(1920,1200),(2560,1440),(2560,1080),(3440,1440),(3840,2160))
def check(ok,label):
 global checks
 assert ok,label
 checks+=1
for family in ('pure','arcane'):
 text=(a.assets/family/'payload/data/ui/game/control_panel.tgi').read_text('utf-16')
 decals=re.findall(r'\[Child Template=DecalVE\]\s*\{([^}]*PawUniform(?:Left|Center|Right)\.tga\s*)\}',text)
 check(len(decals)==3,'three decorative panel regions')
 client_bottoms=[float(re.search(r'client_T\s*=\s*([0-9.]+)b',d)[1]) for d in decals]
 check(client_bottoms==[183]*3,'all decorative regions use the stock client band')
 for image in (0x460000,0x80000,0x6a0000):
  for width,height in sizes:
   uc=Uc(UC_ARCH_X86,UC_MODE_32);uc.mem_map(image,0x700000);uc.mem_write(image,native)
   heap=0x30000000;uc.mem_map(heap,0x20000);cursor=heap+0x1000
   def alloc():
    global cursor
    v=cursor;cursor+=0x100;return v
   def wi(at,v):uc.mem_write(at,struct.pack('<I',v))
   def rect(at,v):uc.mem_write(at,struct.pack('<4f',*v))
   vt_group,vt_decal,vt_widget=[heap+i for i in (0,0x100,0x200)]
   wi(vt_group+0x38,image+0x2ad0e0);wi(vt_decal+0x38,image+0x2ac629);wi(vt_widget+0x7c,image+0x2b6bc0)
   logical=max(1024,width/height*768);sx=width/logical;sy=height/768
   def widget():
    v=alloc();wi(v,vt_widget);return v
   world,button,panel,minimap,pause,root_owner=[widget() for _ in range(6)]
   def view(owner,box,group=False):
    v=alloc();wi(v,vt_group if group else vt_decal);wi(v+0xc,owner);wi(v+0x88,4)
    scaled=(box[0]*sx,box[1]*sy,box[2]*sx,box[3]*sy)
    rect(v+0x20,scaled);rect(v+0x54,scaled)
    if group:uc.mem_write(v+0xd0,b'\1')
    return v
   def children(parent,items):
    previous=0
    for v in items:
     node=alloc();wi(node,v);wi(node+4,previous);previous=node
    wi(parent+0xcc,previous)
   root=view(root_owner,(0,0,logical,768),True)
   world_v=view(world,(0,0,logical,768))
   button_v=view(button,(logical-60,514,logical-4,570))
   panel_v=view(panel,(0,512,logical,768),True)
   backgrounds=[view(panel,box) for box in ((0,512,512,768),(512,512,logical-512,768),(logical-512,512,logical,768))]
   minimap_v=view(minimap,(15,524,255,764));pause_v=view(pause,(47,731,76,760))
   children(panel_v,[*backgrounds,minimap_v,pause_v]);children(root,[world_v,button_v,panel_v])
   end=heap+0x1f000;sp=heap+0x1e000
   def pick(x,y):
    uc.mem_write(sp,struct.pack('<IffI',end,x*sx,y*sy,1));uc.reg_write(UC_X86_REG_ESP,sp);uc.reg_write(UC_X86_REG_ECX,root)
    uc.emu_start(image+0x2ad0e0,end,count=10000)
    check(uc.reg_read(UC_X86_REG_EIP)==end,'native pick returned')
    check(uc.reg_read(UC_X86_REG_ESP)==sp+16,'native pick stack preserved')
    return uc.reg_read(UC_X86_REG_EAX)
   samples=[(logical/2,530,world),(logical/2,565,world),(logical-32,540,button),(logical-8,566,button)]
   for x,y,owner in samples:check(pick(x,y)==panel,'old full-height decals reproduce obstruction')
   for v,bottom in zip(backgrounds,client_bottoms):uc.mem_write(v+0x58,struct.pack('<f',(768-bottom)*sy))
   for x,y,owner in samples:check(pick(x,y)==owner,'field/button receive input after client-bound fix')
   for x,y,owner in [(100,600,minimap),(60,740,pause),(logical/2,700,panel),(logical-100,700,panel),(logical/2,450,world)]:
    check(pick(x,y)==owner,'native minimap/buttons/opaque panel/upper field routing preserved')
a.out.parent.mkdir(parents=True,exist_ok=True);a.out.write_text(json.dumps(dict(passed=True,checks=checks,families=2,resolutions=10,relocationBases=3,nativeFunctions=True,syntheticWidgets=True,gameLaunched=False),indent=2))
print('NATIVE_PANEL_PICKING_PASS',checks)
