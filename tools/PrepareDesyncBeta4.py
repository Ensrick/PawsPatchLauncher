"""Stage the beta.4 runtime-only update; verify public bytes before promotion.

Uses the signed beta.3 package set as its exact data baseline. Does not install
anything in the user's game or change launcher/stable/other-mod catalogs.
"""
import argparse,copy,hashlib,json,shutil,zipfile
from datetime import datetime,timezone
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from PrepareRelease081 import FEEDS,RAW,API,key,normalize,fetch,sign
from PrepareRelease070 import read,write,encode,sha,verify,validate_package
from PrepareSeptember20Release import contents,feature

REPO=Path(__file__).resolve().parents[1]
WORK=REPO.parents[1]
VERSION='0.4.0-beta.4'
BASELINE_VERSION='0.4.0-beta.3'
AI_POLICY_REVISION=36
TAG='patch-'+VERSION
URL='https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/'+TAG+'/'
VARIANTS=read(REPO/'game/beta7/variants.json')
TRACKED=[*FEEDS.values(),'feed/changelog.history.json','feed/patch-guide-beta.json']
RELEASE_DATE='2026-09-25'
EXTRA_CACHE_ROOTS=()

def extra_payload_transform(package,payload):
    """A later release may opt into explicitly scoped presentation changes."""
    return set()

def extra_payload_paths(package):
    """Later releases must explicitly allow any new file paths per package."""
    return set()

def extra_changelog_transform(entries):
    """Allow explicitly requested editorial corrections to recent notes."""
    return entries

def extra_feed_transform(feed):
    """Apply explicitly scoped guide/metadata additions for a later beta."""
    return feed

def prepare(a):
    stage=a.stage.resolve();out=stage/'publication';helpers=stage/'beta-helpers'
    assert not (out/'preparation.json').exists(),'Use a fresh publication folder'
    blob=(stage/'build.log').read_bytes()
    log=blob.decode('utf-16' if blob[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
    assert 'Built eight quiet helpers. No game launched.' in log
    assert read(helpers/'ai-native/peer-parity.json')['passed']
    head=json.loads(fetch(API+'/git/ref/heads/main'))['object']['sha']
    for name in TRACKED:assert normalize(fetch(RAW.replace('/main/','/'+head+'/')+name))==normalize((REPO/name).read_bytes()),'Baseline moved: '+name
    feeds={c:verify(read(REPO/p),key()) for c,p in FEEDS.items()};beta=feeds['beta']
    assert beta['patchGuide']['version']==BASELINE_VERSION
    private=serialization.load_pem_private_key((a.signing_dir/'pawpatch-signing-private.pem').read_bytes(),None)
    assert private.public_key().public_numbers()==key().public_numbers()
    features={name:feature(helpers/name) for name in VARIANTS}
    native=(helpers/'ai-native/ai-policy.bin').read_bytes()
    embedded=native.hex().encode('utf-16le')
    for name,row in features.items():
        assert row['patchVersion']==VERSION and row['aiPolicyRevision']==AI_POLICY_REVISION,(name,row)
        assert row['aiImprovementsSelectable'] and row['nightmareDifficultyRevision']==3
        assert embedded in (helpers/name).read_bytes(),'Helper does not embed tested native policy: '+name
    for c,p in FEEDS.items():
        dest=out/'previous'/(c+'.json');dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(REPO/p,dest)
    cached={}
    for root in (*EXTRA_CACHE_ROOTS,WORK/'outputs/release-20260925-beta6',WORK/'outputs/release-20260925-beta5',WORK/'outputs/release-20260925-beta4',WORK/'outputs/release-20260924',WORK/'outputs/release-20260920',REPO/'packages'):
        for p in root.rglob('*.zip'):cached.setdefault(p.stat().st_size,[]).append(p)
    resolved={};assets=[];replacements={};scope={};seen=set()
    for p in {p['sha256']:p for f in (feeds['stable'],beta) for p in f['packages']}.values():
        source=next((q for q in cached.get(p['size'],[]) if sha(q)==p['sha256']),None)
        if source is None:
            source=stage/'downloads'/(p['sha256']+'.zip');source.parent.mkdir(exist_ok=True)
            if not source.exists():source.write_bytes(fetch(p['urls'][0]))
        validate_package(source,p);resolved[p['sha256']]=str(source.resolve())
    for p in beta['packages']:
        if p.get('mods')!=['arcane-wars']:continue
        original=contents(Path(resolved[p['sha256']]),p);payload=copy.deepcopy(original)
        for n in payload:
            if n in VARIANTS or n=='paws_patch_versions.ini':
                payload[n]=(helpers/n).read_bytes()
                if n in VARIANTS:seen.add(n)
        extra=extra_payload_transform(p,payload)
        assert set(payload)==set(original)|extra_payload_paths(p),'Unexpected added or removed payload files'
        if payload==original:continue
        changed=[n for n in payload if payload[n]!=original.get(n)]
        assert all(n in VARIANTS or n=='paws_patch_versions.ini' or n in extra for n in changed)
        package=copy.deepcopy(p);package['version']=VERSION
        manifest=dict(id=p['id'],version=VERSION,files=[dict(path=n,size=len(b),sha256=hashlib.sha256(b).hexdigest().upper()) for n,b in sorted(payload.items())],remove=[])
        path=out/'assets'/TAG/(p['id']+'-'+VERSION+'.zip');path.parent.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
            for n,b in [('module.json',encode(manifest))]+[('payload/'+n,b) for n,b in sorted(payload.items())]:
                info=zipfile.ZipInfo(n,tuple(map(int,RELEASE_DATE.split('-')))+(0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16;z.writestr(info,b)
        package.update(size=path.stat().st_size,sha256=sha(path),urls=[URL+path.name]);validate_package(path,package)
        replacements[p['id']]=package;scope[p['id']]=changed
        assets.append(dict(path=str(path),id=p['id'],size=package['size'],sha256=package['sha256'],url=package['urls'][0]))
    assert seen==set(VARIANTS)
    final=copy.deepcopy(beta);final['packages']=[replacements.get(p['id'],p) for p in beta['packages']]
    assert final['launcher']==beta['launcher']
    assert [p for p in final['packages'] if p.get('mods')!=['arcane-wars']]==[p for p in beta['packages'] if p.get('mods')!=['arcane-wars']]
    bodies=read(REPO/('docs/release-patch-'+VERSION+'.json'))
    note=dict(category='patch',version=VERSION,publishedAt=RELEASE_DATE,mods=['arcane-wars'],channel='beta',title={c:'Paw’s Patch '+VERSION for c in bodies},body=bodies)
    final['changelog']=extra_changelog_transform([note]+final['changelog']);final['publishedAt']=datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ');final['patchGuide']['version']=VERSION
    final=extra_feed_transform(final)
    write(out/'final/beta.json',sign(final,private))
    # The standalone history has independently curated older entries. Do not
    # replace those with a catalog's historical copy during a runtime release.
    history=read(REPO/'feed/changelog.history.json');history['beta']=extra_changelog_transform([note]+history['beta'])
    write(out/'final/changelog.history.json',history);write(out/'final/patch-guide-beta.json',final['patchGuide'])
    for c,f in [('stable',feeds['stable']),('beta',final)]:
        test=copy.deepcopy(f)
        for p in test['packages']:
            own=next((x for x in assets if x['sha256']==p['sha256']),None)
            p['urls']=[own['path'] if own else resolved[p['sha256']]]
        write(out/'test-feeds'/(c+'.json'),sign(test,private))
    write(out/'scope.json',scope);write(out/'features.json',features)
    write(out/'native-payload-verification.json',dict(passed=True,helpers=len(features),sha256=hashlib.sha256(native).hexdigest().upper()))
    write(out/'preparation.json',dict(version=VERSION,baselineCommit=head,assets=assets,before={p:sha(REPO/p) for p in TRACKED}))
    print('STAGED',len(assets),'beta packages; unrelated payload bytes unchanged')

def public(a):
    out=a.stage/'publication';prep=read(out/'preparation.json')
    release=json.loads(fetch(API+'/releases/tags/'+TAG))
    assert release['prerelease'] and not release['draft']
    for row in prep['assets']:
        asset=next(x for x in release['assets'] if x['name']==Path(row['path']).name)
        raw=fetch(row['url']);digest=hashlib.sha256(raw).hexdigest().upper()
        assert len(raw)==row['size']==asset['size'] and digest==row['sha256']
        assert asset['digest'].upper()=='SHA256:'+digest
    write(out/'public-verification.json',dict(passed=True,assets=len(prep['assets']),url=release['html_url']))
    print('PUBLIC PASS',len(prep['assets']),'anonymous downloads, lengths and SHA-256')

def promote(a):
    out=a.stage/'publication';prep=read(out/'preparation.json')
    assert read(a.stage/'installation-verification.json')['passed']
    assert read(a.stage/'beta-helpers/ai-native/peer-parity.json')['passed']
    assert read(out/'public-verification.json')['passed']
    for p,h in prep['before'].items():assert sha(REPO/p)==h,'Catalog changed: '+p
    verify(read(out/'final/beta.json'),key())
    for src,dst in [('beta.json','feed/v2/beta.json'),('changelog.history.json','feed/changelog.history.json'),('patch-guide-beta.json','feed/patch-guide-beta.json')]:shutil.copyfile(out/'final'/src,REPO/dst)
    print('PROMOTED beta',VERSION)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--stage',type=Path,required=True);p.add_argument('--signing-dir',type=Path)
    p.add_argument('--action',choices=['prepare','public','promote'],default='prepare');a=p.parse_args()
    {'prepare':prepare,'public':public,'promote':promote}[a.action](a)
