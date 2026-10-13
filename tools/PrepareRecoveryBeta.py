"""Stage the Pure recovery beta; promotion requires verified installed-build tests.

Only v2 Pure beta runtime/data and its guide/history change. Launcher publication
uses PrepareLauncherRelease.py separately. No new server configuration grammar.
"""
import argparse, copy, hashlib, io, json, shutil, subprocess, sys, zipfile
from datetime import datetime, timezone
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from PrepareRelease081 import FEEDS, RAW, API, key, sign, fetch, normalize
from PrepareRelease070 import read, write, sha, verify, validate_package

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'game/pure-fixes'))
import build
VERSION='0.4.1-beta.1'; RUNTIME='1.3.72-pure.15-beta.1'; TAG='patch-pure-'+VERSION
IDS={'pure-fixes-runtime','pure-fixes-data'}
DOWNLOAD='https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/'+TAG+'/'
NOTE='docs/release-pure-'+VERSION+'.json'
def committed_raw():
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
 assert json.loads(fetch(API+'/git/ref/heads/main'))['object']['sha']==head,'Push source/catalog commit first'
 return RAW.replace('/main/','/'+head+'/')
GUIDE=dict(id='pure-match-recovery',category='beta',titleRu='Восстановление матча',titleEn='Match recovery',
 bodyRu='После отключения игрока хост сохраняет матч и открывает лобби с выбранным сейвом. Вернитесь в лобби, получите сохранение, займите свой слот и нажмите «Готов». Хост запускает сохранённый матч. При потере первоначального хоста новый хост сохраняет матч и оставляет паузу; сейв нужно загрузить через лобби вручную. При зависшем соединении используйте штатное подтверждение отключения игрока. Обычный выход игрока тоже может запустить восстановление. Вход прямо в работающую симуляцию пока недоступен.',
 bodyEn='After a client disconnects, the host saves the match and opens the lobby with that save selected. Rejoin the lobby, download the save, reclaim your slot and become ready. The host launches the saved match. If the original host disconnects, the new host saves and pauses; load the save through a lobby manually. For a stalled connection use the native disconnect vote. An ordinary player exit may also trigger recovery. Direct entry into the running simulation is not available yet.')

def private(a):
 p=serialization.load_pem_private_key(a.private_key.read_bytes(),None)
 assert p.public_key().public_numbers()==key().public_numbers()
 return p

def scoped(before,after):
 assert before['launcher']==after['launcher']
 assert set(before)==set(after)
 for name in before:
  if name not in {'packages','modGuides','changelog','publishedAt'}:assert before[name]==after[name],name
 assert [p for p in before['packages'] if p['id'] not in IDS]==[p for p in after['packages'] if p['id'] not in IDS]
 assert {p['id'] for p in before['packages']}=={p['id'] for p in after['packages']}
 for old,new in zip(before['modGuides'],after['modGuides']):
  if old['id'] not in ('vanilla','immortals'):assert old==new;continue
  normalized=copy.deepcopy(new);assert normalized['patchGuide']['entries'][-1]==GUIDE
  normalized['patchGuide']['entries'].pop();normalized['patchGuide']['version']=old['patchGuide']['version']
  assert normalized==old and new['patchGuide']['version']==VERSION

def prepare(a):
 out=a.out.resolve();out.mkdir(parents=True,exist_ok=False);pkey=private(a);baseline={};raw=committed_raw()
 for name,path in FEEDS.items():
  assert normalize(fetch(raw+path))==normalize((ROOT/path).read_bytes()),path
  baseline[path]=sha(ROOT/path);shutil.copyfile(ROOT/path,out/(name+'-before.json'))
 baseline['feed/changelog.history.json']=sha(ROOT/'feed/changelog.history.json')
 assert read(ROOT/'feed/changelog.history.json')==json.loads(fetch(raw+'feed/changelog.history.json'))
 before=verify(read(ROOT/FEEDS['beta']),key());after=copy.deepcopy(before);assets=[]
 for package in after['packages']:
  if package['id'] not in IDS:continue
  if package['id']=='pure-fixes-runtime':
   files={p.name:p.read_bytes() for p in a.helpers.glob('k2_paws_*.exe')};assert len(files)==4
   features=read(a.helpers/'features.json')
   for name,data in files.items():
    f=features[name]['features'];assert hashlib.sha256(data).hexdigest().upper()==features[name]['sha256']
    assert f['patchVersion']==VERSION and f['version']==RUNTIME and f['channel']=='beta'
    assert f['matchRecoveryRevision']==2 and f['matchRecoveryMode']=='save-lobby' and not f['automaticDropVoting']
    assert f['nativeTransferRevision']=='R2' and f['aiPolicyProfile']=='pure'
    assert all(not f[k] for k in ('cityAssistant','automaticMilitia','additionalBuildingSlots','foundationDistribution','fractionalKingdomPoints'))
   version=RUNTIME
   files['PawsPatch-recovery-MIT.txt']=(ROOT/'game/reconnect-test/LICENSE.Ensrick').read_bytes()
  else:
   raw=fetch(package['urls'][0]);previous=out/'previous-data.zip';previous.write_bytes(raw);validate_package(previous,package)
   with zipfile.ZipFile(io.BytesIO(raw)) as z:files={n[8:]:z.read(n) for n in z.namelist() if n.startswith('payload/')}
   version=VERSION
  fresh=build.package(out,package['id'],version,files,package['executableIndependent'],package['priority'],package['name'],package['description'])
  path=Path(fresh['urls'][0]);validate_package(path,fresh)
  for field in ('version','size','sha256'):package[field]=fresh[field]
  package.update(urls=[DOWNLOAD+path.name],experimental=True)
  assets.append(dict(id=package['id'],path=str(path),url=package['urls'][0],size=package['size'],sha256=package['sha256']))
 assert len(assets)==2
 for guide in after['modGuides']:
  if guide['id'] in ('vanilla','immortals'):
   guide['patchGuide']['version']=VERSION;guide['patchGuide']['entries'].append(copy.deepcopy(GUIDE))
 notes=read(ROOT/NOTE);after['changelog']=[notes]+after['changelog'];after['publishedAt']=datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
 scoped(before,after);write(out/'payload.json',after)
 history=read(ROOT/'feed/changelog.history.json');history['beta']=[notes]+history['beta'];write(out/'history.json',history)
 local=copy.deepcopy(after)
 for p in local['packages']:
  if p['id'] in IDS:p['urls']=[next(x['path'] for x in assets if x['id']==p['id'])]
 write(out/'test-feeds/beta.json',sign(local,pkey));shutil.copyfile(ROOT/FEEDS['stable'],out/'test-feeds/stable.json')
 (out/'public.pem').write_bytes(key().public_bytes(serialization.Encoding.PEM,serialization.PublicFormat.SubjectPublicKeyInfo))
 write(out/'preparation.json',dict(baseline=baseline,assets=assets,notesSha=sha(ROOT/NOTE)))
 print('RECOVERY_BETA_PREPARED',out,flush=True)

def promote(a):
 out=a.out;prep=read(out/'preparation.json');proof=read(out.parent/'release-validation.json')
 assert proof['passed'] and proof['integratedMultiplayer'] and proof['installationRollback'] and proof['nativeChecks']
 assert set(proof['startupLanguages'])=={'en','ru','de','fr','cs','uk'}
 assert proof['runtimeHashes']=={p.name:sha(p) for p in a.helpers.glob('k2_paws_*.exe')}
 release=json.loads(fetch(API+'/releases/tags/'+TAG));assert not release['draft'] and release['prerelease']
 ref=json.loads(fetch(API+'/git/ref/tags/'+TAG))['object']
 while ref['type']=='tag':ref=json.loads(fetch(API+'/git/tags/'+ref['sha']))['object']
 assert ref['type']=='commit' and ref['sha']==a.commit
 for asset in prep['assets']:
  data=fetch(asset['url']);assert len(data)==asset['size'] and hashlib.sha256(data).hexdigest().upper()==asset['sha256']
 raw=committed_raw()
 for path,digest in prep['baseline'].items():
  assert sha(ROOT/path)==digest,path
  assert normalize(fetch(raw+path))==normalize((ROOT/path).read_bytes()),path
 assert sha(ROOT/NOTE)==prep['notesSha']
 after=read(out/'payload.json');scoped(verify(read(out/'beta-before.json'),key()),after)
 write(ROOT/FEEDS['beta'],sign(after,private(a)));write(ROOT/'feed/changelog.history.json',read(out/'history.json'))
 write(out/'public-verification.json',dict(passed=True,sourceCommit=a.commit,tag=TAG))
 print('PROMOTED LOCALLY: Pure beta only; push/readback still required',flush=True)

def readback(a):
 out=a.out;prep=read(out/'preparation.json');raw=committed_raw()
 head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
 for name,path in FEEDS.items():
  public=json.loads(fetch(raw+path));assert public==read(ROOT/path),path
  payload=verify(public,key());before=verify(read(out/(name+'-before.json')),key())
  if name=='beta':
   scoped(before,payload);assert payload==read(out/'payload.json')
   for asset in prep['assets']:
    package=next(p for p in payload['packages'] if p['id']==asset['id'])
    assert package['urls']==[asset['url']] and package['sha256']==asset['sha256']
    data=fetch(asset['url'])
    assert len(data)==asset['size'] and hashlib.sha256(data).hexdigest().upper()==asset['sha256']
  else:assert public==read(out/(name+'-before.json')),name
 assert json.loads(fetch(raw+'feed/changelog.history.json'))==read(out/'history.json')==read(ROOT/'feed/changelog.history.json')
 write(out/'readback.json',dict(passed=True,tag=TAG,catalogCommit=head,launcher=payload['launcher']['version'],unchangedOtherCatalogs=True))
 print('PUBLICATION PASS: '+TAG+'; four signatures, assets, scoped beta/history; '+head,flush=True)

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['prepare','promote','readback']);p.add_argument('--out',type=Path,required=True)
 p.add_argument('--helpers',type=Path,required=True);p.add_argument('--private-key',type=Path);p.add_argument('--commit')
 a=p.parse_args();globals()[a.action](a)
