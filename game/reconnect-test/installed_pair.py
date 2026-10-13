"""Two owned LAN processes using unchanged public helper composition methods."""
import argparse,ctypes,hashlib,json,os,shutil,subprocess,time
from pathlib import Path

def main():
 p=argparse.ArgumentParser();p.add_argument('--kit',type=Path,required=True);p.add_argument('--harness',type=Path,required=True)
 p.add_argument('--input',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
 root=a.out.resolve();root.mkdir(exist_ok=False);owned=[];events=[]
 manifest=json.loads((a.kit/'manifest.json').read_text());assert manifest['build']=='pure-recovery-0.4.1-beta.1'
 for relative,digest in manifest['files'].items():assert hashlib.sha256((a.kit/relative).read_bytes()).hexdigest()==digest,relative
 def report(text):
  events.append(dict(time=time.time(),text=text));(root/'events.json').write_text(json.dumps(events,indent=2));print(text,flush=True)
 def prepare(role):
  d=root/role;shutil.copytree(a.kit/'game',d/'game');data=d/'docs/Kohan2/data'
  (data/'Save').mkdir(parents=True);(data/'User').mkdir();(d/'game/Logs').mkdir(exist_ok=True)
  (d/'game/reconnect-test.marker').write_text('isolated packaged-helper LAN test\n')
  shutil.copy2(a.input,d/'game/Mods/rig_input.dll')
  (data/'User/UVars.tgi').write_text('[Vars]\n{\n int ResolutionX = 800\n int ResolutionY = 600\n flag ResolutionWindowed = true\n flag ResolutionCoopFullscreenMode = false\n flag MouseClipCursor = false\n flag RunWhenInactive = true\n flag FirstRun = false\n float AudioMainVolume = 0\n float FrameRateLimit = 30\n}\n',encoding='utf-16')
  return d
 host=prepare('host');client=prepare('client')
 def text(d):
  try:return (d/'game/Logs/paws_recovery.log').read_text(errors='replace')
  except OSError:return ''
 def wait(check,seconds,label):
  end=time.monotonic()+seconds
  while not check():
   failed=[x for x in owned if x['required'] and x['p'].poll() is not None]
   if failed:raise RuntimeError('Owned helper stopped: '+str([x['d'] for x in failed]))
   if time.monotonic()>end:raise TimeoutError(label)
   time.sleep(.25)
  report(label)
 def launch(d,script,is_client=False):
  (d/'game/Mods/rig.txt').write_text(script)
  log=d/'game/Logs/paws_recovery.log'
  if log.exists():shutil.move(log,d/'before-rejoin.log')
  env=os.environ.copy();env.update(SteamAppId='97130',SteamGameId='97130',PAWS_TEST_DOCUMENTS=str(d/'docs'))
  if is_client:env['PAWS_TEST_JOIN']='127.0.0.1:5860'
  else:env.pop('PAWS_TEST_JOIN',None)
  handle=open(d/('harness-rejoin.log' if (d/'harness.log').exists() else 'harness.log'),'w')
  proc=subprocess.Popen([str(a.harness.resolve()),str(d/'game')],cwd=d/'game',env=env,stdout=handle,stderr=subprocess.STDOUT)
  item=dict(p=proc,d=d,required=True,log=Path(handle.name),handle=handle,pid=None);owned.append(item)
  wait(lambda:'OWNED_GAME ' in item['log'].read_text(errors='replace'),45,'Owned native process '+d.name)
  item['pid']=int(item['log'].read_text().split('OWNED_GAME ')[1].split()[0])
  wait(lambda:'PACKAGED_HELPER_READY' in item['log'].read_text(errors='replace'),45,'Complete packaged composition '+d.name)
  assert 'auto_vote=0' in text(d) and 'INTEGRATED:' in text(d)
  return item
 def stop(item):
  item['required']=False
  if item['p'].poll() is not None:return
  k=ctypes.WinDLL('kernel32',use_last_error=True);k.OpenProcess.restype=ctypes.c_void_p
  k.TerminateProcess.argtypes=[ctypes.c_void_p,ctypes.c_uint];k.CloseHandle.argtypes=[ctypes.c_void_p]
  h=k.OpenProcess(1,False,item['pid'])
  if h:
   try:
    if not k.TerminateProcess(h,0):raise ctypes.WinError(ctypes.get_last_error())
   finally:k.CloseHandle(h)
  item['p'].wait(timeout=15)
 hs='Wait 45\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\nLobbySetLANNickname PawPublicHost\nWait 2\nInternetGameCreate PawPublic 2 2\nWait 3\nSelectLaunchType RANDOM_MAP\nWait 90\nStagingLaunch\nWait 45\nCSDKickLaggingPlayers 0\nWait 140\nStagingLaunch\nWait 25\nStagingLaunch\nWait 200\nReallyQuit\n'
 cs='Wait 45\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\nLobbySetLANNickname PawPublicClient\nWait 2\nLobbyJoinDirect\nWait 2\nJoinDirectJoin\nWait 15\nStagingReady 0\nWait 20\nStagingReady 0\nWait 400\nReallyQuit\n'
 def errors():
  return [dict(file=str(f),line=line) for d in (host,client) for f in (d/'game/Logs').glob('*.log') for line in f.read_text(errors='replace').splitlines() if any(x in line.lower() for x in ['out of sync','out-of-sync','assert:','unhandled exception'])]
 try:
  hp=launch(host,hs);cp=launch(client,cs,True)
  wait(lambda:'MATCH_ROLE: original_host=1' in text(host) and 'MATCH_ROLE: original_host=0' in text(client),240,'Both peers entered the match')
  time.sleep(10);stop(cp);report('Client crash simulated; native disconnect confirmation is scripted once')
  wait(lambda:'RESUME_READY:' in text(host),100,'Host completed save and selected it in lobby')
  time.sleep(5);cp=launch(client,cs,True)
  wait(lambda:text(host).count('MATCH_ROLE: original_host=1')>=2 and 'MATCH_ROLE: original_host=0' in text(client),220,'Both peers resumed saved match')
  saves=list((host/'docs/Kohan2/data/Save').glob('Paw Resume*.RSG'));assert saves
  transferred=client/'docs/Kohan2/data/Save'/saves[-1].name
  digest=hashlib.sha256(saves[-1].read_bytes()).hexdigest();assert hashlib.sha256(transferred.read_bytes()).hexdigest()==digest
  report('Save received byte-identical: '+digest);time.sleep(20);found=errors();assert not found,found
  assert all(x['p'].poll() is None for x in owned if x['required'])
  (root/'result.json').write_text(json.dumps(dict(passed=True,saveSha256=digest,saveSize=saves[-1].stat().st_size,events=events),indent=2));report('PASS integrated LAN recovery')
 except Exception as e:
  (root/'result.json').write_text(json.dumps(dict(passed=False,error=str(e),events=events),indent=2));raise
 finally:
  for x in owned:
   try:stop(x)
   finally:x['handle'].close()
  (root/'error-scan.json').write_text(json.dumps(errors(),indent=2))
if __name__=='__main__':main()
