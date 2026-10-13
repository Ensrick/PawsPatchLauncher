"""Promote accepted Pure beta.2 payloads to 0.4.0 with core lair recovery.

Immutable assets, server verification and catalog promotion are separate stages.
The shared 0.3.0 publication verifier also checks launcher bytes, ZIP contents,
source tags, production signing key and commit-pinned public readback.
"""
import argparse, copy, hashlib, io, json, shutil, sys, zipfile
from pathlib import Path
from cryptography.hazmat.primitives import serialization
import PreparePure030 as publication
from PrepareRelease081 import FEEDS, RAW, key, sign, fetch, normalize
from PrepareRelease070 import read, write, sha, verify, validate_package

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'game/pure-fixes'))
import build

VERSION, PATCH, TAG = '0.9.2', '0.4.0', 'patch-pure-0.4.0'
RUNTIME = '1.3.72-pure.14'
DOWNLOAD = 'https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/'

def scoped(before, after):
    for field in before:
        if field not in {'packages','modGuides','pureRuntimeOptions','pureLairRecoveryBuiltIn','launcher','publishedAt','changelog','newsTitle','newsBody'}:
            assert after[field] == before[field], field
    assert [p for p in before['packages'] if not p['id'].startswith('pure-')] == [p for p in after['packages'] if not p['id'].startswith('pure-')]
    assert [g for g in before.get('modGuides',[]) if g['id'] not in ('vanilla','immortals')] == [g for g in after.get('modGuides',[]) if g['id'] not in ('vanilla','immortals')]
    if any(p['id']=='pure-beta-common' for p in after['packages']):
        assert after['pureLairRecoveryBuiltIn'] and after['pureRuntimeOptions']
        assert not any(p['id']=='pure-lair-recovery' for p in after['packages'])
        for p in after['packages']:
            if p['id'].startswith('pure-'):
                assert set(p['mods']) <= {'vanilla','immortals'} and not p['experimental']

def prepare(a):
    out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
    pkey=publication.private(a)
    before={n:verify(read(ROOT/p),key()) for n,p in FEEDS.items()}
    baseline={}
    for n,p in FEEDS.items():
        assert before[n]['launcher']['version']=='0.9.1'
        assert normalize(fetch(RAW+p+'?pure040=baseline'))==normalize((ROOT/p).read_bytes()),p
        write(out/'previous'/(n+'.json'),read(ROOT/p));baseline[p]=sha(ROOT/p)
    assert json.loads(fetch(RAW+'feed/changelog.history.json?pure040=baseline'))==read(ROOT/'feed/changelog.history.json')
    assets=[];pure=[];parity={}
    for old in before['beta']['packages']:
        if not old['id'].startswith('pure-') or old['id']=='pure-lair-recovery':continue
        package=copy.deepcopy(old)
        if old['id']=='pure-player-colors':
            pure.append(package);continue
        raw=fetch(old['urls'][0]);prev=out/'previous'/(old['id']+'.zip');prev.write_bytes(raw)
        validate_package(prev,old)
        with zipfile.ZipFile(io.BytesIO(raw)) as z:
            manifest=json.loads(z.read('module.json'));assert not manifest.get('remove'),old['id']
            files={n[8:]:z.read(n) for n in z.namelist() if n.startswith('payload/')}
        if old['id']=='pure-fixes-runtime':
            files={p.name:p.read_bytes() for p in a.helpers.glob('k2_paws_*.exe')};assert len(files)==4
            features=read(a.helpers/'features.json')
            for name,data in files.items():
                assert hashlib.sha256(data).hexdigest().upper()==features[name]['sha256']
                f=features[name]['features']
                assert f['aiPolicyProfile']=='pure' and f['aiPolicyCompatibilityRevision']==1
                assert f['patchVersion']==PATCH and f['channel']=='stable' and f['version']==RUNTIME
                assert f['woundedLairDefendersBuiltIn'] and not f['woundedLairDefendersSelectable']
                assert all(not f[k] for k in ('automaticMilitia','additionalBuildingSlots','foundationDistribution','fractionalKingdomPoints','cityAssistant'))
            version=RUNTIME
        else:version=PATCH
        fresh=build.package(out,old['id'],version,files,old['executableIndependent'],old['priority'],old['name'],old['description'])
        path=Path(fresh['urls'][0]);validate_package(path,fresh)
        if old['id']!='pure-fixes-runtime':
            with zipfile.ZipFile(path) as z,zipfile.ZipFile(prev) as y:
                payload=lambda a:{n:a.read(n) for n in a.namelist() if n.startswith('payload/')}
                assert payload(z)==payload(y),'Static payload changed: '+old['id']
            parity[old['id']]=len(files)
        for field in ('version','size','sha256'):package[field]=fresh[field]
        package.update(urls=[DOWNLOAD+TAG+'/'+path.name],experimental=False)
        if package['id']=='pure-beta-common':
            package['name']={'ru':"Paw’s Patch: движок и интерфейс",'en':"Paw’s Patch: engine and interface"}
            package['description']=copy.deepcopy(package['name'])
        assets.append(dict(id=old['id'],path=str(path),url=package['urls'][0],size=package['size'],sha256=package['sha256']))
        pure.append(package)
    assert len(pure)>20 and len(assets)==len(pure)-1
    guides={g['id']:copy.deepcopy(g) for g in before['beta']['modGuides'] if g['id'] in ('vanilla','immortals')}
    for guide in guides.values():
        guide['patchGuide']['version']=PATCH
        for entry in guide['patchGuide']['entries']:
            if entry['category']=='beta' or entry['id']=='pure-lair-recovery':entry['category']='always'
    patch=read(ROOT/'docs/release-pure-0.4.0.json');launch=read(ROOT/'docs/release-0.9.2.json')
    history=read(ROOT/'feed/changelog.history.json');baseline['feed/changelog.history.json']=sha(ROOT/'feed/changelog.history.json')
    for name,b in before.items():
        f=copy.deepcopy(b)
        if name in ('stable','beta'):
            f['packages']=[p for p in f['packages'] if not p['id'].startswith('pure-')]+copy.deepcopy(pure)
            f['modGuides']=[copy.deepcopy(guides.get(g['id'],g)) for g in f['modGuides']]
            f.update(pureRuntimeOptions=True,pureLairRecoveryBuiltIn=True)
            f['changelog']=[launch,patch]+f['changelog'];history[name]=[launch,patch]+history[name]
        else:
            f['changelog']=[launch]+f['changelog'];assert f['packages']==b['packages']
        f.update(newsTitle=launch['title'],newsBody=launch['body'])
        scoped(b,f);write(out/'payloads'/(name+'.json'),f)
        if name in ('stable','beta'):
            local=copy.deepcopy(f)
            for p in local['packages']:
                match=next((x for x in assets if x['id']==p['id']),None)
                if match:p['urls']=[match['path']]
            write(out/'test-feeds'/(name+'.json'),sign(local,pkey))
    write(out/'changelog.history.json',history)
    write(out/'payload-parity.json',parity)
    write(out/'preparation.json',dict(baseline=baseline,assets=assets,notes={p:sha(ROOT/p) for p in ('docs/release-pure-0.4.0.json','docs/release-0.9.2.json')}))
    (out/'public.pem').write_bytes(key().public_bytes(serialization.Encoding.PEM,serialization.PublicFormat.SubjectPublicKeyInfo))
    config=read(ROOT/'src/PawsPatchLauncher/launcher.config.json')
    config.update(feedUrls=[str(out/'test-feeds/stable.json')],betaFeedUrls=[str(out/'test-feeds/beta.json')],cacheRoot=str(out/'ui-cache'))
    write(out/'ui.config.json',config)
    print('PURE_040_PREPARED',len(assets),'assets;',len(parity),'accepted static payloads preserved',flush=True)

def configure_verifier():
    publication.VERSION=VERSION;publication.PATCH=PATCH;publication.TAG=TAG
    publication.scoped=scoped

def promote(a):
    stage=a.out.parent
    assert read(stage/'matrix/summary.json')['complete']
    launches=[json.loads(line) for line in (stage/'matrix/launches.jsonl').read_text().splitlines() if line.strip()]
    expected={mod+'-pure-stable-'+language for mod in ('vanilla','immortals') for language in ('en','ru','de','fr','cs','uk')}
    current={r['id'] for r in launches if 'pure-fixes-runtime@'+RUNTIME in r['modules'] and r['phase']=='menu'}
    assert expected <= current
    for result in launches:
        if result['id'] not in current:continue
        assert not any(m.startswith('pure-lair-recovery@') for m in result['modules'])
        log=(stage/'matrix'/result['helperLog']).read_text(encoding='utf-8-sig')
        assert 'LAIR_WOUNDED r3;' in log and 'FAST_TRANSFER_R2_READY' in log
    assert read(stage/'profile-guard.json')['passed']
    publication.promote(a)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('action',choices=['prepare','finalize','promote','readback'])
    p.add_argument('--out',type=Path,required=True);p.add_argument('--helpers',type=Path)
    p.add_argument('--private-key',type=Path);p.add_argument('--commit');p.add_argument('--allow-unsigned-launcher',action='store_true')
    a=p.parse_args();configure_verifier()
    if a.action=='prepare':prepare(a)
    elif a.action=='promote':promote(a)
    else:getattr(publication,a.action)(a)
