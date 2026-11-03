"""Compiled recording/playback routing parity; native services are stubbed.

The real replay creates human-type film slots for every participant. Keep that
behavior and recover only path-policy identity from serialized kingdom setup.
The fixture checks write bounds, ABI preservation and state transitions too.
"""
from pathlib import Path
import json

path=Path(__file__).with_name('test_routing.py')
r={'__file__':str(path)}
exec(compile(path.read_text().split('\nfor image,cave in ')[0],str(path),'exec'),r)
checks=0
def check(ok,why):
    global checks
    assert ok,why
    checks+=1

def string(f,p,s):
    encoded=s.encode('utf-16le')
    f.w(p-12,len(encoded)//2)
    f.u.mem_write(p,encoded+b'\0\0')

def film(f,profile):
    f.creator=0x51016000;f.rows=0x51017000
    f.key=0x51019000;f.ident=f.key+0x200;f.profile=f.key+0x400
    f.w(f.session+0x64,2);f.w(f.session+0xf0,4)
    f.w(f.slot+0xc,0x10000) # native bot=0, implicit=0, film=1
    f.w(f.session+0x7c,f.creator);f.w(f.creator,f.image+0x4fdfe4)
    f.w(f.creator+0x14,f.rows);f.w(f.creator+0x18,1);f.w(f.creator+0x1c,1)
    f.w(f.rows,f.key);f.w(f.rows+0x2c,f.profile);f.w(f.kingdom+0x18,f.ident)
    string(f,f.key,'kingdom03');string(f,f.ident,'kingdom03');string(f,f.profile,profile)
    f.w(f.image+0x5f3fc8,0) # replay has no strategic AI controllers

def route(f):
    threats=f.begin()
    los=f.call(f.hook(13),[r['bits'](v) for v in (8,64,120,64)],native=0xcafe01)
    node=f.actor+0x800;f.w(node,15);f.w(node+4,16)
    cost=f.call(f.hook(15),[node,16,16],fp=5);f.end()
    return threats,los,r['bits'](cost)

for image,cave in [(0x460000,0x10000000),(0xf20000,0x22000000),(0x18000000,0x38000000)]:
    for bot in (False,True):
        for hostile in (False,True):
            record=r['Fixture'](image,cave);record.w(record.slot+0xc,int(bot))
            playback=r['Fixture'](image,cave);film(playback,'custom_ai_profile' if bot else '')
            for f in (record,playback):
                if hostile:f.w(f.query+0x20,f.lair)
            check(route(record)==route(playback),('record/replay route parity',bot,hostile))
            check(playback.r(playback.slot+0xc)==0x10000,'native film player flags unchanged')
    for case in ('missing_creator','wrong_creator','missing_rows','empty_rows','too_many',
                 'capacity','missing_id','long_id','unterminated_id','mismatch','duplicate',
                 'missing_profile','long_profile','unterminated_profile','observer','no_film_flag'):
        f=r['Fixture'](image,cave);film(f,'haroun_hard_2')
        if case=='missing_creator':f.w(f.session+0x7c,0)
        if case=='wrong_creator':f.w(f.creator,image+0x4fdf48)
        if case=='missing_rows':f.w(f.creator+0x14,0)
        if case=='empty_rows':f.w(f.creator+0x18,0)
        if case=='too_many':f.w(f.creator+0x18,97);f.w(f.creator+0x1c,97)
        if case=='capacity':f.w(f.creator+0x1c,0)
        if case=='missing_id':f.w(f.kingdom+0x18,0)
        if case=='long_id':f.w(f.ident-12,129)
        if case=='unterminated_id':f.u.mem_write(f.ident+18,b'xx')
        if case=='mismatch':string(f,f.ident,'kingdom04')
        if case=='duplicate':
            f.w(f.rows+108,f.key);f.w(f.rows+108+0x2c,f.profile)
            f.w(f.creator+0x18,2);f.w(f.creator+0x1c,2)
        if case=='missing_profile':f.w(f.rows+0x2c,0)
        if case=='long_profile':f.w(f.profile-12,129)
        if case=='unterminated_profile':f.u.mem_write(f.profile+26,b'xx')
        if case=='observer':f.w(f.slot+0x28,0)
        if case=='no_film_flag':f.w(f.slot+0xc,0)
        check(route(f)[0]==0,('invalid film metadata preserves native path',case))
    # Both directions, same process/data block: no cached identity leaks into
    # a later replay, saved game, normal match or human-controlled kingdom.
    f=r['Fixture'](image,cave);film(f,'haroun_hard_2')
    for state,source,bot,want in [(4,2,False,1),(2,2,False,0),(4,2,False,1),
                                  (3,0,True,1),(3,0,False,0),(4,2,False,1)]:
        f.w(f.session+0xf0,state);f.w(f.session+0x64,source);f.w(f.slot+0xc,0x10000|int(bot))
        check(route(f)[0]==want,('session transition',state,source,bot))
    string(f,f.profile,'');check(route(f)[0]==0,'another film: human profile stays human')
    f.w(f.slot+0xc,0x10001);check(route(f)[0]==0,'film metadata overrides native type')
    string(f,f.profile,'custom_ai_profile');check(route(f)[0]==1,'profile names are not hardcoded')
    # Slot ordering and independent kingdoms need not match kingdom indices.
    f.u.mem_write(f.rows+108,bytes(f.u.mem_read(f.rows,108)))
    f.w(f.rows,0);f.w(f.creator+0x18,2);f.w(f.creator+0x1c,2);f.w(f.slot+0x20,77)
    check(route(f)[0]==1,'internal kingdom ID, not participant number or row position')

report=dict(passed=True,checks=checks,abiChecks=r['checks'],scope='compiled x86 routing; native services stubbed')
(r['a'].native/'film-routing.json').write_text(json.dumps(report,indent=2)+'\n')
print('AI_FILM_ROUTING_PASS',checks,'checks;',r['checks'],'ABI/write-bound checks')
