"""Stage scoped crash-fix betas; preserve all unrelated payload bytes."""
import argparse, copy, hashlib, io, json, shutil, subprocess, zipfile
from datetime import datetime, timezone
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from PrepareRelease081 import FEEDS, RAW, API, key, sign, fetch, normalize
from PrepareRelease070 import read, write, sha, verify, validate_package, encode

ROOT=Path(__file__).resolve().parents[1]
AW='0.4.2-beta.2'; PURE='0.4.1-beta.4'; RUNTIME='1.3.72-pure.15-beta.4'
AW_IDS={'pawpatch-core','common-ui','player-colors','desync-continue'}
PURE_IDS={'pure-fixes-data','pure-fixes-runtime'}
VARIANTS=set(read(ROOT/'game/beta7/variants.json'))
def public_root():
 row=subprocess.check_output(['git','ls-remote','origin','refs/heads/main'],cwd=ROOT,text=True).split()
 assert len(row)==2 and row[1]=='refs/heads/main'
 return RAW.replace('/main/','/'+row[0]+'/')

def private(a):
 p=serialization.load_pem_private_key(a.private_key.read_bytes(),None)
 assert p.public_key().public_numbers()==key().public_numbers()
 return p

def scoped(before,after,scopes):
 allowed=({'arcane'}<=set(scopes) and AW_IDS or set()) | ({'pure'}<=set(scopes) and PURE_IDS or set())
 assert set(before)==set(after)
 for name in before:
  if name not in {'packages','patchGuide','modGuides','changelog','publishedAt'}:assert before[name]==after[name],name
 assert [p['id'] for p in before['packages']]==[p['id'] for p in after['packages']]
 for p,q in zip(before['packages'],after['packages'],strict=True):
  if p['id'] not in allowed:assert p==q,p['id']
  else:
   assert p['mods']==q['mods'] and p['priority']==q['priority'] and p['executableIndependent']==q['executableIndependent']
   assert q['experimental']
 assert before['launcher']==after['launcher']
 expected=copy.deepcopy(before['patchGuide'])
 if 'arcane' in scopes:expected['version']=AW
 assert expected==after['patchGuide']
 for old,new in zip(before['modGuides'],after['modGuides'],strict=True):
  expected=copy.deepcopy(old)
  if 'pure' in scopes and old['id'] in ('vanilla','immortals'):expected['patchGuide']['version']=PURE
  assert expected==new,old['id']

def prepare(a):
 out=a.out.resolve();pkey=private(a);out.mkdir(parents=True,exist_ok=False);remote=public_root()
 tracked=[*FEEDS.values(),'feed/changelog.history.json','feed/patch-guide-beta.json']
 baseline={}
 for path in tracked:
  assert normalize(fetch(remote+path))==normalize((ROOT/path).read_bytes()),'Public baseline changed: '+path
  baseline[path]=sha(ROOT/path)
 for name,path in FEEDS.items():shutil.copyfile(ROOT/path,out/(name+'-before.json'))
 before=verify(read(ROOT/FEEDS['beta']),key());after=copy.deepcopy(before)
 cache={}
 for root in a.cache:
  for path in root.rglob('*.zip'):cache.setdefault(path.stat().st_size,[]).append(path)
 assets=[];audit={};seen=set();notes=[]
 for scope in a.scope:
  version=AW if scope=='arcane' else PURE;tag=('patch-' if scope=='arcane' else 'patch-pure-')+version
  notes.append(read(ROOT/('docs/release-'+('patch-' if scope=='arcane' else 'pure-')+version+'.json')))
  selected=AW_IDS if scope=='arcane' else PURE_IDS
  for package in after['packages']:
   if package['id'] not in selected:continue
   source=next((p for p in cache.get(package['size'],[]) if sha(p)==package['sha256']),None)
   if source is None:
    source=out/'downloads'/(package['id']+'.zip');source.parent.mkdir(exist_ok=True);source.write_bytes(fetch(package['urls'][0]))
   validate_package(source,package)
   with zipfile.ZipFile(source) as z:
    manifest=json.loads(z.read('module.json'));assert not manifest.get('remove')
    original={n[8:]:z.read(n) for n in z.namelist() if n.startswith('payload/') and not n.endswith('/')}
   files=copy.deepcopy(original);changed=[];deleted=[];added=[]
   for name in list(files):
    if scope=='arcane' and (name in VARIANTS or name=='paws_patch_versions.ini'):
     files[name]=(a.arcane_helpers/name).read_bytes();changed.append(name)
     if name in VARIANTS:seen.add(name)
    if scope=='pure' and package['id']=='pure-fixes-runtime' and name.endswith('.exe'):
     assert name.startswith('k2_paws_pure')
     files[name]=(a.pure_helpers/name).read_bytes();changed.append(name)
   assert set(original)-set(files)==set(deleted)
   assert set(files)-set(original)==set(added)
   assert all(original[n]==b for n,b in files.items() if n not in changed and n not in added),'Unrelated payload edit'
   if scope=='arcane':
    for name in changed:
     if name in VARIANTS:
      f=json.loads(subprocess.check_output([str(a.arcane_helpers/name),'--features']))
      assert f['patchVersion']==AW and f['aiPolicyRevision']==39
      assert f['builtInGraphicsDiagnosticsRevision']==2
      assert f['animationStopControllerGuardRevision']==(1 if f['commonFixes'] else 0)
      assert f.get('widescreenUniformUiRevision',0)==(1 if f['commonFixes'] else 0)
   elif package['id']=='pure-fixes-runtime':
    assert len(changed)==4
    for name in changed:
     f=json.loads(subprocess.check_output([str(a.pure_helpers/name),'--features']))
     assert f['patchVersion']==PURE and f['version']==RUNTIME and f['widescreenUniformUiRevision']==1
     assert f['builtInGraphicsDiagnosticsRevision']==2 and f['animationStopControllerGuardRevision']==1
     assert f['aiPolicyProfile']=='pure' and f['multiplayerSavesRevision']==2 and f['matchRecoveryRevision']==3
     assert not any(f[k] for k in ('cityAssistant','automaticMilitia','additionalBuildingSlots','foundationDistribution','fractionalKingdomPoints'))
   package['version']=RUNTIME if package['id']=='pure-fixes-runtime' else version
   manifest.update(version=package['version'],files=[dict(path=n,size=len(b),sha256=hashlib.sha256(b).hexdigest().upper()) for n,b in sorted(files.items())])
   path=out/'assets'/tag/(package['id']+'-'+package['version']+'.zip');path.parent.mkdir(parents=True,exist_ok=True)
   with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for name,data in [('module.json',encode(manifest))]+[('payload/'+n,b) for n,b in sorted(files.items())]:
     entry=zipfile.ZipInfo(name,(2026,10,17,0,0,0));entry.compress_type=zipfile.ZIP_DEFLATED;entry.external_attr=0o100644<<16;z.writestr(entry,data)
   package.update(size=path.stat().st_size,sha256=sha(path),urls=['https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/'+tag+'/'+path.name],experimental=True)
   validate_package(path,package)
   assets.append(dict(id=package['id'],tag=tag,path=str(path),url=package['urls'][0],sha256=package['sha256'],size=package['size']))
   audit[package['id']]=dict(changed=changed,added=added,removed=deleted,unchangedPayloadFiles=len(files)-len(changed)-len(added))
 if 'arcane' in a.scope:
  assert seen==VARIANTS
  after['patchGuide']['version']=AW
 if 'pure' in a.scope:
  for guide in after['modGuides']:
   if guide['id'] in ('vanilla','immortals'):
    guide['patchGuide']['version']=PURE
 after['changelog']=notes+after['changelog'];after['publishedAt']=datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
 scoped(before,after,a.scope);write(out/'payload.json',after)
 history=read(ROOT/'feed/changelog.history.json');history['beta']=notes+history['beta'];write(out/'history.json',history)
 local=copy.deepcopy(after)
 for p in local['packages']:
  own=next((x for x in assets if x['id']==p['id']),None)
  if own:p['urls']=[own['path']]
 write(out/'test-feeds/beta.json',sign(local,pkey));shutil.copyfile(ROOT/FEEDS['stable'],out/'test-feeds/stable.json')
 (out/'public.pem').write_bytes(key().public_bytes(serialization.Encoding.PEM,serialization.PublicFormat.SubjectPublicKeyInfo))
 write(out/'preparation.json',dict(baseline=baseline,assets=assets,scopes=a.scope,payloadSha=sha(out/'payload.json'),historySha=sha(out/'history.json')))
 write(out/'payload-audit.json',audit);print('CRASH_FIX_BETAS_STAGED',len(assets),'packages; all unrelated payload bytes preserved',flush=True)

def promote(a):
 out=a.out;prep=read(out/'preparation.json');proof=read(a.validation)
 assert proof['passed'] and proof['nativeMenuLaunches'] >= 3 and proof['installationRollback'] and proof['nativeChecks']
 assert proof['assetHashes']=={x['id']:x['sha256'] for x in prep['assets']}
 assert sha(out/'payload.json')==prep['payloadSha'] and sha(out/'history.json')==prep['historySha']
 for tag in {x['tag'] for x in prep['assets']}:
  release=json.loads(fetch(API+'/releases/tags/'+tag));assert release['prerelease'] and not release['draft']
  ref=json.loads(fetch(API+'/git/ref/tags/'+tag))['object']
  while ref['type']=='tag':ref=json.loads(fetch(API+'/git/tags/'+ref['sha']))['object']
  expected_commit=(a.pure_commit or a.commit) if tag.startswith('patch-pure-') else a.commit
  assert ref['type']=='commit' and ref['sha']==expected_commit
 for item in prep['assets']:
  raw=fetch(item['url']);assert len(raw)==item['size'] and hashlib.sha256(raw).hexdigest().upper()==item['sha256']
 remote=public_root()
 for path,digest in prep['baseline'].items():
  assert sha(ROOT/path)==digest,path
  assert normalize(fetch(remote+path))==normalize((ROOT/path).read_bytes()),path
 payload=read(out/'payload.json');scoped(verify(read(out/'beta-before.json'),key()),payload,prep['scopes'])
 write(ROOT/FEEDS['beta'],sign(payload,private(a)));write(ROOT/'feed/changelog.history.json',read(out/'history.json'))
 if 'arcane' in prep['scopes']:write(ROOT/'feed/patch-guide-beta.json',payload['patchGuide'])
 print('CRASH_FIX_BETAS_PROMOTED_LOCALLY; push and readback remain',flush=True)

def readback(a):
 out=a.out;prep=read(out/'preparation.json');remote=public_root()
 for name,path in FEEDS.items():
  public=json.loads(fetch(remote+path));assert public==read(ROOT/path),path
  payload=verify(public,key())
  if name=='beta':assert payload==read(out/'payload.json')
  else:assert public==read(out/(name+'-before.json')),name
 assert json.loads(fetch(remote+'feed/changelog.history.json'))==read(out/'history.json')
 for item in prep['assets']:
  raw=fetch(item['url']);assert len(raw)==item['size'] and hashlib.sha256(raw).hexdigest().upper()==item['sha256']
 write(out/'readback.json',dict(passed=True,assets=len(prep['assets']),scopes=prep['scopes'],publicRoot=remote))
 print('CRASH_FIX_PUBLICATION_PASS: four signed catalogs, beta assets and history',flush=True)

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['prepare','promote','readback']);p.add_argument('--out',type=Path,required=True)
 p.add_argument('--private-key',type=Path);p.add_argument('--arcane-helpers',type=Path);p.add_argument('--pure-helpers',type=Path)
 p.add_argument('--cache',type=Path,action='append',default=[]);p.add_argument('--scope',choices=['arcane','pure'],action='append',default=[])
 p.add_argument('--validation',type=Path);p.add_argument('--commit');p.add_argument('--pure-commit');a=p.parse_args();globals()[a.action](a)
