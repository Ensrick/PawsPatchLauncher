"""Check resolved nested icon rectangles from both generated UI families.

Uses layout sizes, including parent-relative insets, not replacement strings.
Live native widget measurements and clicking remain a separate test.
"""
import argparse
import json
import math
import re
from pathlib import Path
from build_icon_assets import decode

p=argparse.ArgumentParser();p.add_argument('--assets',type=Path,required=True)
p.add_argument('--report',type=Path,required=True);a=p.parse_args()
checks=0;cases=[]
def check(ok, why):
    global checks
    assert ok,why
    checks+=1
def clean(text):
    text=re.sub(r'/\*.*?\*/','',text,flags=re.S)
    return re.sub(r';[^\n]*','',text)
def block(text,name):
    m=re.search(r'\[(?:Template )?'+re.escape(name)+r'(?:\s[^\]\n]*)?\]?\s*\{',text)
    assert m,name
    depth=1
    for i in range(m.end(),len(text)):
        depth+=(text[i]=='{')-(text[i]=='}')
        if not depth:return text[m.end():i]
    raise AssertionError(name)
def anchor(text,key):
    m=re.search(r'^\s*'+key+r'\s*=\s*(\S+)',text,re.M)
    return m[1] if m else '0'
def coord(value,parent,start):
    if value.endswith('%'):return parent*float(value[:-1])/100
    if value.endswith(('r','b')):return parent-float(value[:-1])
    if value.startswith('+'):return start+float(value)
    return float(value)
def box(text,parent=(0,0)):
    left=coord(anchor(text,'L'),parent[0],0);top=coord(anchor(text,'T'),parent[1],0)
    return (left,top,coord(anchor(text,'R'),parent[0],left),coord(anchor(text,'B'),parent[1],top))
def view(text):
    m=re.search(r'\[View[^\]]*\]',text);assert m
    tail=text[m.end():]
    return block(text,'View') if tail.lstrip().startswith('{') else re.split(r'[\[}]',tail)[0]
def square(family,label,b):
    w,h=b[2]-b[0],b[3]-b[1]
    check(w>0 and math.isclose(w,h,abs_tol=1e-6),f'{family}/{label}: {w} x {h}')
    for pw,ph in ((1024,768),(1280,720),(1920,1200),(1920,1080),(2560,1440),(3440,1440),(3840,2160)):
        logical=max(1024,pw/ph*768)
        check(math.isclose(w*pw/logical,h*ph/768,abs_tol=.001),label+' physical aspect')
    cases.append(dict(family=family,icon=label,logicalSize=[w,h]))

for family in ('pure','arcane'):
    root=a.assets/family/'payload/data/ui/game'
    read=lambda name:clean(decode((root/(name+'.tgi')).read_bytes()))
    f=read('floaters');t=read('template_game');r=read('organization_editor');s=read('favorites_display')
    hero=block(block(f,'RecruitItemFloater'),'Item')
    hb=box(view(block(hero,'UnitButton')))
    square(family,'hero portrait and click view',hb)
    health=box(view(block(hero,'Health')))
    frame=box(view(hero))
    check(hb[3]<health[1] and health[3]<=frame[3],'hero health below portrait and inside frame')
    ability=block(block(f,'FactionAbilitiesFloater'),'Ability')
    ability_icon=block(ability,'AbilityButtonDecal') if family=='arcane' else block(ability,'AbilityButton')
    square(family,'faction ability icon',box(view(ability_icon)))
    recharge=box(view(block(ability,'Recharge')))
    check(recharge[3]<=box(view(ability))[3],'ability recharge inside cell')
    formation=block(t,'FormationButtonsTemplate')
    fb=box(view(block(formation,'FormationButton')))
    square(family,'formation button',fb)
    check(3*(fb[3]-fb[1])<=box(view(formation))[3]-box(view(formation))[1],'three formation buttons fit')
    square(family,'unit icon template',box(view(block(t,'GameUnitButtonWidget'))))
    unit=block(t,'UnitDisplayStatic')
    square(family,'static unit portrait',box(view(block(unit,'UnitButton'))))
    portrait=block(block(t,'UnitDisplay'),'PortraitButton')
    pb=box(view(portrait));ps=(pb[2]-pb[0],pb[3]-pb[1])
    for name in ('Portrait','ModelPortrait'):
        square(family,'large '+name,box(block(portrait,name),ps))
    mini=block(block(block(t,'MiniDisplay'),'PortraitButton'),'Icon')
    square(family,'mini unit icon',box(re.split(r'\[Decal\s[^\]]*\]',mini)[1]))
    square(family,'unit statistics icon',box(block(block(block(t,'DisplayValuesBasic'),'Icon'),'Decal')))
    structure=block(block(t,'SettlementStructures'),'SettlementStructure')
    sb=box(view(block(structure,'StructureButton')))
    square(family,'building upgrade icon',box(view(block(structure,'StructureButtonDecal')),(sb[2]-sb[0],sb[3]-sb[1])))
    favorite=block(s,'FavoriteButton');decal=block(favorite,'DecalPanel')
    db=box(view(decal));square(family,'saved recruitment frame',db)
    square(family,'saved recruitment icon',box(view(block(decal,'FavoriteDecal')),(db[2],db[3])))
    square(family,'saved recruitment role',box(view(block(s,'RoleDecal'))))
    square(family,'saved recruitment pressed client',box('\n'.join(
        key+' = '+anchor(favorite,'pressed_client_'+key) for key in 'LTRB'),(37,72)))
    slot=block(r,'SlotDefinitionPanel')
    for name in ('CandidateButton','ButtonDecal'):
        candidate=block(slot,name);square(family,'recruitment '+name,box(view(candidate)))
        square(family,'recruitment '+name+' inner',box(view(block(candidate,'Decal'))))
        square(family,'recruitment '+name+' pressed',box('\n'.join(
            key+' = '+anchor(candidate,'pressed_client_'+key) for key in 'LTRB')))
    # These are intentionally high banners with morale/efficiency and troop bars.
    banner=box(view(block(block(t,'CompanyMiniDisplay'),'PortraitButton')))
    check(banner[3]-banner[1]>banner[2]-banner[0],'company banner remains vertical')
    # Existing square action buttons, minimap background and input band stay intact.
    ab=box(view(block(read('action_buttons'),'ActionButtonsButton')))
    square(family,'action button (unchanged)',ab)
a.report.parent.mkdir(parents=True,exist_ok=True)
a.report.write_text(json.dumps(dict(checks=checks,icons=cases,resolutions=7),indent=2)+'\n')
print('ICON_ASPECT_PASS',checks,'checks;',len(cases),'icon cases; 7 resolutions')
