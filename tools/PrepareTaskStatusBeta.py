"""Publish a helper-only repair through both v2 and configured v3 beta catalogs."""
import argparse, copy, hashlib, json, shutil, subprocess, zipfile
from datetime import datetime, timezone
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from PrepareRelease081 import RAW, API, key, sign, fetch, normalize
from PrepareRelease070 import read, write, sha, verify, encode, validate_package

ROOT = Path(__file__).resolve().parents[1]
AW, PURE, RUNTIME = '0.4.2-beta.5', '0.4.1-beta.7', '1.3.72-pure.15-beta.7'
FEEDS = {name: 'feed/'+path for name,path in {
    'beta':'v3/beta.json', 'stable':'v3/stable.json', 'v2-beta':'v2/beta.json',
    'v2-stable':'v2/stable.json', 'legacy-beta':'beta.json', 'legacy-stable':'stable.json'}.items()}
AW_IDS = {'pawpatch-core','common-ui','player-colors','desync-continue'}
PURE_IDS = {'pure-fixes-data','pure-fixes-runtime','pure-beta-common'}
IDS = AW_IDS | PURE_IDS
VARIANTS = set(read(ROOT/'game/beta7/variants.json'))

def public_root():
    row = subprocess.check_output(['git','ls-remote','origin','refs/heads/main'],cwd=ROOT,text=True).split()
    assert len(row)==2 and row[1]=='refs/heads/main'
    return RAW.replace('/main/','/'+row[0]+'/')

def private(a):
    value=serialization.load_pem_private_key(a.private_key.read_bytes(),None)
    assert value.public_key().public_numbers()==key().public_numbers()
    return value

def scoped(before, after):
    assert set(before)==set(after)
    for name in before:
        if name not in {'packages','patchGuide','modGuides','changelog','publishedAt'}:
            assert before[name]==after[name], name
    assert [p['id'] for p in before['packages']]==[p['id'] for p in after['packages']]
    for p,q in zip(before['packages'],after['packages'],strict=True):
        expected=copy.deepcopy(p)
        if p['id'] in IDS:
            for field in ('version','size','sha256','urls'): expected[field]=q[field]
            assert q['experimental']
        assert expected==q,p['id']
    expected=copy.deepcopy(before['patchGuide']);expected['version']=AW
    assert expected==after['patchGuide']
    for p,q in zip(before['modGuides'],after['modGuides'],strict=True):
        expected=copy.deepcopy(p)
        if p['id'] in ('vanilla','immortals'): expected['patchGuide']['version']=PURE
        assert expected==q,p['id']

def prepare(a):
    out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
    pkey=private(a);remote=public_root()
    config=read(ROOT/'src/PawsPatchLauncher/launcher.config.json')
    assert config['betaFeedUrls']==[RAW+FEEDS['beta']], 'Configured active feed changed'
    tracked=[*FEEDS.values(),'feed/changelog.history.json','feed/patch-guide-beta.json']
    baseline={}
    for path in tracked:
        assert normalize(fetch(remote+path))==normalize((ROOT/path).read_bytes()),path
        baseline[path]=sha(ROOT/path)
    for name,path in FEEDS.items(): shutil.copyfile(ROOT/path,out/(name+'-before.json'))
    old={name:verify(read(ROOT/path),key()) for name,path in FEEDS.items()}
    before=old['beta'];after=copy.deepcopy(before)
    for id in IDS:
        left=next(p for p in before['packages'] if p['id']==id)
        right=next(p for p in old['v2-beta']['packages'] if p['id']==id)
        assert all(left[k]==right[k] for k in ('version','size','sha256','urls')),id
    cache={}
    for root in a.cache:
        for path in root.rglob('*.zip'): cache.setdefault(path.stat().st_size,[]).append(path)
    assets=[];audit={};seen=set()
    for package in after['packages']:
        id=package['id']
        if id not in IDS: continue
        source=next((p for p in cache.get(package['size'],[]) if sha(p)==package['sha256']),None)
        if source is None:
            source=out/'downloads'/(id+'.zip');source.parent.mkdir(exist_ok=True);source.write_bytes(fetch(package['urls'][0]))
        validate_package(source,package)
        with zipfile.ZipFile(source) as z:
            manifest=json.loads(z.read('module.json'));assert not manifest.get('remove')
            original={n[8:]:z.read(n) for n in z.namelist() if n.startswith('payload/') and not n.endswith('/')}
        files=copy.deepcopy(original);changed=[]
        for name in files:
            folder=None
            if id in AW_IDS and (name in VARIANTS or name=='paws_patch_versions.ini'): folder=a.arcane_helpers
            if id=='pure-fixes-runtime' and name.endswith('.exe'):
                assert name.startswith('k2_paws_pure');folder=a.pure_helpers
            if folder:
                files[name]=(folder/name).read_bytes();changed.append(name)
                if name in VARIANTS: seen.add(name)
                if name.endswith('.exe'):
                    f=json.loads(subprocess.check_output([str(folder/name),'--features']))
                    assert f['taskStatusInitializationRevision']==1
                    assert f['patchVersion']==(AW if id in AW_IDS else PURE)
                    assert f['terrainTexturePoolEntries']==1024
                    assert f['widescreenUniformUiRevision']==(2 if f.get('commonFixes',True) else 0)
                    if id=='pure-fixes-runtime':
                        assert f['version']==RUNTIME and f['aiPolicyProfile']=='pure'
                        assert f['multiplayerSavesRevision']==2 and f['matchRecoveryRevision']==3
                        assert not any(f[k] for k in ('cityAssistant','automaticMilitia','additionalBuildingSlots','foundationDistribution','fractionalKingdomPoints'))
        assert set(files)==set(original)
        assert all(files[n]==original[n] for n in files if n not in changed)
        if id=='pure-fixes-runtime': assert len(changed)==4
        version=AW if id in AW_IDS else RUNTIME if id=='pure-fixes-runtime' else PURE
        tag=('patch-' if id in AW_IDS else 'patch-pure-')+(AW if id in AW_IDS else PURE)
        manifest.update(version=version,files=[dict(path=n,size=len(b),sha256=hashlib.sha256(b).hexdigest().upper()) for n,b in sorted(files.items())])
        path=out/'assets'/tag/(id+'-'+version+'.zip');path.parent.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
            for name,data in [('module.json',encode(manifest))]+[('payload/'+n,b) for n,b in sorted(files.items())]:
                info=zipfile.ZipInfo(name,(2026,10,23,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16;z.writestr(info,data)
        package.update(version=version,size=path.stat().st_size,sha256=sha(path),urls=[API.replace('api.github.com/repos','github.com')+'/releases/download/'+tag+'/'+path.name])
        validate_package(path,package)
        assets.append(dict(id=id,tag=tag,path=str(path),url=package['urls'][0],sha256=package['sha256'],size=package['size']))
        audit[id]=dict(changed=changed,unchangedPayloadFiles=len(files)-len(changed),sourceSha256=sha(source))
    assert seen==VARIANTS
    notes=[read(ROOT/('docs/release-'+prefix+version+'.json')) for prefix,version in [('patch-',AW),('pure-',PURE)]]
    after['patchGuide']['version']=AW
    for guide in after['modGuides']:
        if guide['id'] in ('vanilla','immortals'): guide['patchGuide']['version']=PURE
    after['changelog']=notes+after['changelog'];after['publishedAt']=datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
    scoped(before,after);write(out/'payload.json',after)
    v2=copy.deepcopy(old['v2-beta'])
    for p in v2['packages']:
        if p['id'] in IDS:
            q=next(q for q in after['packages'] if q['id']==p['id'])
            for field in ('version','size','sha256','urls'): p[field]=copy.deepcopy(q[field])
    v2.update(patchGuide=copy.deepcopy(after['patchGuide']),modGuides=copy.deepcopy(after['modGuides']),changelog=notes+v2['changelog'],publishedAt=after['publishedAt'])
    scoped(old['v2-beta'],v2);write(out/'v2-payload.json',v2)
    history=read(ROOT/'feed/changelog.history.json');history['beta']=notes+history['beta'];write(out/'history.json',history)
    local=copy.deepcopy(after)
    for p in local['packages']:
        own=next((x for x in assets if x['id']==p['id']),None)
        if own: p['urls']=[own['path']]
    write(out/'test-feeds/beta.json',sign(local,pkey));shutil.copyfile(ROOT/FEEDS['stable'],out/'test-feeds/stable.json')
    (out/'public.pem').write_bytes(key().public_bytes(serialization.Encoding.PEM,serialization.PublicFormat.SubjectPublicKeyInfo))
    write(out/'preparation.json',dict(baseline=baseline,assets=assets,payloadSha=sha(out/'payload.json'),v2PayloadSha=sha(out/'v2-payload.json'),historySha=sha(out/'history.json')))
    write(out/'payload-audit.json',audit)
    print('TASK_STATUS_BETAS_STAGED',len(assets),'packages; helper-only payload changes')

def verify_assets(prep,commit):
    for tag in {x['tag'] for x in prep['assets']}:
        release=json.loads(fetch(API+'/releases/tags/'+tag));assert release['prerelease'] and not release['draft']
        ref=json.loads(fetch(API+'/git/ref/tags/'+tag))['object']
        while ref['type']=='tag': ref=json.loads(fetch(API+'/git/tags/'+ref['sha']))['object']
        assert ref['type']=='commit' and ref['sha']==commit
    for asset in prep['assets']:
        blob=fetch(asset['url']);assert len(blob)==asset['size'] and hashlib.sha256(blob).hexdigest().upper()==asset['sha256']

def promote(a):
    out=a.out;prep=read(out/'preparation.json');proof=read(a.validation)
    assert proof['passed'] and proof['installationCases']>=21 and proof['menuLaunches']>=3
    assert proof['nativeChecks'] and proof['networkChecks']==63
    assert proof['assetHashes']=={x['id']:x['sha256'] for x in prep['assets']}
    for file,keyname in [('payload.json','payloadSha'),('v2-payload.json','v2PayloadSha'),('history.json','historySha')]: assert sha(out/file)==prep[keyname]
    verify_assets(prep,a.commit);remote=public_root()
    for path,digest in prep['baseline'].items():
        assert sha(ROOT/path)==digest,path
        assert normalize(fetch(remote+path))==normalize((ROOT/path).read_bytes()),path
    for name,file in [('beta','payload.json'),('v2-beta','v2-payload.json')]:
        payload=read(out/file);scoped(verify(read(out/(name+'-before.json')),key()),payload)
        write(ROOT/FEEDS[name],sign(payload,private(a)))
    write(ROOT/'feed/changelog.history.json',read(out/'history.json'))
    write(ROOT/'feed/patch-guide-beta.json',read(out/'payload.json')['patchGuide'])
    print('TASK_STATUS_BETAS_PROMOTED_LOCALLY; push and public readback remain')

def readback(a):
    out=a.out;prep=read(out/'preparation.json');remote=public_root()
    for name,path in FEEDS.items():
        public=json.loads(fetch(remote+path));assert public==read(ROOT/path),path
        payload=verify(public,key())
        if name in ('beta','v2-beta'): assert payload==read(out/('payload.json' if name=='beta' else 'v2-payload.json'))
        else: assert public==read(out/(name+'-before.json')),name
    assert json.loads(fetch(remote+'feed/changelog.history.json'))==read(out/'history.json')
    verify_assets(prep,a.commit)
    write(out/'readback.json',dict(passed=True,assets=len(prep['assets']),catalogs=6,publicRoot=remote))
    print('TASK_STATUS_PUBLICATION_PASS: active v3 and v2 beta, unchanged stable/legacy catalogs, seven public assets')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['prepare','promote','readback']);p.add_argument('--out',type=Path,required=True)
    for name in ('private-key','arcane-helpers','pure-helpers','validation'): p.add_argument('--'+name,type=Path)
    p.add_argument('--cache',type=Path,action='append',default=[]);p.add_argument('--commit');a=p.parse_args();globals()[a.action](a)
