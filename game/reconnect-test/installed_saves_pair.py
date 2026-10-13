"""Live QA: unchanged installed helper, ordinary script commands, private LAN peers.

Never points at the user's installed game. rig_input is a QA-only main-thread
command dispatcher; ReadProcessMemory observes timing and native session state.
"""
import argparse, ctypes as C, hashlib, json, os, shutil, struct, subprocess, time
from pathlib import Path

K=C.WinDLL('kernel32',use_last_error=True)
K.OpenProcess.restype=C.c_void_p
K.CloseHandle.argtypes=[C.c_void_p]
K.ReadProcessMemory.argtypes=[C.c_void_p,C.c_void_p,C.c_void_p,C.c_size_t,C.c_void_p]
K.QueryFullProcessImageNameW.argtypes=[C.c_void_p,C.c_uint,C.c_wchar_p,C.c_void_p]
K.GetProcessTimes.argtypes=[C.c_void_p,C.c_void_p,C.c_void_p,C.c_void_p,C.c_void_p]
K.TerminateProcess.argtypes=[C.c_void_p,C.c_uint]
K.K32EnumProcessModulesEx.argtypes=[C.c_void_p,C.c_void_p,C.c_uint,C.c_void_p,C.c_uint]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def text(p):
 try:return p.read_text(errors='replace')
 except OSError:return ''

def main():
 p=argparse.ArgumentParser()
 for key in ('game','artifacts','helpers','out'):p.add_argument('--'+key,type=Path,required=True)
 p.add_argument('--slots',type=int,choices=(0,1,2),default=2)
 a=p.parse_args();root=a.out.resolve();root.mkdir(exist_ok=False);owned=[];events=[];checks={}
 assert (a.game/'.paw-live-matrix.json').is_file()
 expected={f.name:sha(f) for f in a.helpers.glob('k2_paws_*.exe')};assert len(expected)==4
 assert all(sha(a.game/name)==digest for name,digest in expected.items())
 def report(message):
  events.append(dict(utc=time.time(),text=message));(root/'events.json').write_text(json.dumps(events,indent=2));print(message,flush=True)
 def checked(label,value):
  assert value,label
  checks[label]=True;report('PASS '+label)
 def check_alive():
  failed=[str(i['dir']) for i in owned if i['required'] and i['process'].poll() is not None]
  if failed:raise RuntimeError('Owned helper exited: '+str(failed))
 def wait(predicate,seconds,label):
  until=time.monotonic()+seconds
  while True:
   check_alive()
   if predicate():report(label);return
   if time.monotonic()>until:raise TimeoutError(label)
   time.sleep(.25)
 def stay(seconds):
  until=time.monotonic()+seconds
  while time.monotonic()<until:check_alive();time.sleep(.25)
 def prepare(role,save=None):
  d=root/role;shutil.copytree(a.game,d/'game',ignore=shutil.ignore_patterns('Logs','paws_ai_diagnostics','*.log','*status*.txt'))
  game=d/'game';mods=game/'Mods';mods.mkdir(exist_ok=True)
  shutil.copy2(a.artifacts/'rig_input.dll',mods/'rig_input.dll')
  (mods/'plugins.txt').write_text('rig_input.dll\n');(mods/'rig.txt').write_text('Echo isolated rig ready\n')
  (game/'reconnect-test.marker').write_text('Owned isolated live test\n')
  # A previously verified, unmodified MIT loader; production helpers do not ship it.
  loader=Path('D:/PawPub-Kit-immortals/game/avifil32.dll')
  assert sha(loader)=='449da31a592cc53d48845ef980258f226ff389091e40d9a27373ee79fd8ba7e0'
  shutil.copy2(loader,game/'avifil32.dll');shutil.copy2(a.artifacts/'native/shfolder.dll',game/'shfolder.dll')
  data=d/'docs/Kohan2/data';(data/'User').mkdir(parents=True);(data/'Save').mkdir()
  (data/'User/UVars.tgi').write_text('[Vars]\n{\n int ResolutionX = 800\n int ResolutionY = 600\n flag ResolutionWindowed = true\n flag ResolutionCoopFullscreenMode = false\n flag MouseClipCursor = false\n flag RunWhenInactive = true\n flag FirstRun = false\n float AudioMainVolume = 0\n float FrameRateLimit = 30\n}\n',encoding='utf-16')
  if save:shutil.copy2(save,data/'Save'/save.name)
  assert all(sha(game/name)==digest for name,digest in expected.items())
  return d
 def log(i):return text(i['dir']/'game/Logs/paws_recovery.log')
 def identity(handle,i):
  size=C.c_uint(32768);name=C.create_unicode_buffer(size.value)
  if not K.QueryFullProcessImageNameW(handle,0,name,C.byref(size)):raise C.WinError(C.get_last_error())
  assert Path(name.value).resolve()==(i['dir']/'game/k2.exe').resolve(),'Owned game identity changed'
  times=[C.c_ulonglong() for _ in range(4)]
  if not K.GetProcessTimes(handle,*[C.byref(t) for t in times]):raise C.WinError(C.get_last_error())
  if 'created' in i:assert times[0].value==i['created'],'PID was reused'
  else:i['created']=times[0].value
 def snapshot(i):
  h=K.OpenProcess(0x1410,False,i['pid'])
  if not h:return {}
  try:
   identity(h,i)
   if 'base' not in i:
    modules=(C.c_void_p*1024)();needed=C.c_uint()
    if not K.K32EnumProcessModulesEx(h,modules,C.sizeof(modules),C.byref(needed),3):raise C.WinError(C.get_last_error())
    i['base']=modules[0]
   def read(at,n):
    b=C.create_string_buffer(n);got=C.c_size_t()
    if not K.ReadProcessMemory(h,at,b,n,C.byref(got)) or got.value!=n:raise OSError('Unstable native snapshot')
    return b.raw
   u=lambda at:struct.unpack('<I',read(at,4))[0]
   b=i['base'];session=u(b+0x5f3fe4);world=u(b+0x5f3fb8)
   if not session:return {}
   admin=u(u(b+0x15000e));controller=u(b+0x5f3fe8)
   result=dict(state=u(session+0xf0),world=world,backup=read(admin+0x2f4,1)[0],host=u(admin+0x2e4)==admin)
   if world:result['seconds']=struct.unpack('<f',read(world+0xe8,4))[0]
   if controller:result['paused']=any(read(controller+0x28,2))
   return result
  except OSError:return {}
  finally:K.CloseHandle(h)
 def launch(d,client=False,load=False):
  mods=d/'game/Mods';(mods/'rig-command.txt').write_text('0')
  # Startup navigation uses the previously validated +Exec entry point, after
  # resource/bootstrap scripts finish. Dynamic commands are for the ready lobby
  # or match; state 0 by itself does not mean the menu has finished registering.
  opening='Wait 45\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\n'
  if client:opening+='LobbySetLANNickname PawSavesClient\nWait 2\nLobbyJoinDirect\nWait 2\nJoinDirectJoin\n'
  else:opening+='LobbySetLANNickname PawSavesHost\nWait 2\nInternetGameCreate PawSaves 2 2\nWait 3\nSelectLaunchType '+('LOAD_SAVED_GAME\nWait 3\nCommit\n' if load else 'RANDOM_MAP\n')
  (mods/'rig.txt').write_text(opening)
  old=d/'game/Logs/paws_recovery.log'
  serial=sum(1 for i in owned if i['dir']==d)
  if old.exists():shutil.move(old,d/('recovery-before-'+str(serial)+'.log'))
  env=os.environ.copy();env.update(SteamAppId='97130',SteamGameId='97130',PAWS_TEST_DOCUMENTS=str(d/'docs'))
  if client:env['PAWS_TEST_JOIN']='127.0.0.1:5860'
  else:env.pop('PAWS_TEST_JOIN',None)
  handle=open(d/('harness-'+str(serial)+'.log'),'w')
  proc=subprocess.Popen([str(a.artifacts/'InstalledHarness.exe'),str(d/'game')],cwd=d/'game',env=env,stdout=handle,stderr=subprocess.STDOUT)
  i=dict(process=proc,dir=d,required=True,output=Path(handle.name),handle=handle,seq=100*serial)
  owned.append(i)
  wait(lambda:'OWNED_GAME ' in text(i['output']),45,'Owned process '+d.name)
  i['pid']=int(text(i['output']).split('OWNED_GAME ')[1].split()[0]);snapshot(i)
  wait(lambda:'PACKAGED_HELPER_READY' in text(i['output']),50,'Packaged helper ready '+d.name)
  checked('both modules active '+d.name+' '+str(serial),'MULTIPLAYER_SAVES_READY revision=2 saveCopies=True autosave=True/5min' in text(d/'game/harness-status.log') and 'installed for k2.exe 1.3.72: save copies on, autosave on (5 min)' in log(i))
  wait(lambda:snapshot(i).get('state')==0,30,'Main menu '+d.name)
  # Match entry-point +Exec and startup bindings must finish before adding a
  # second script. Native state 0 already exists while those are still queued.
  stay(8)
  return i
 def command(i,body):
  i['seq']+=1;mods=i['dir']/'game/Mods';n=i['seq']
  (mods/('rig-request-'+str(n)+'.txt')).write_text(body+'\n')
  temp=mods/'rig-command.tmp';temp.write_text(str(n));os.replace(temp,mods/'rig-command.txt')
  report(i['dir'].name+' request '+str(n)+': '+body.replace('\n','; '))
 def host_lobby(i,load=False):
  wait(lambda:snapshot(i).get('state')==1,75,'Host lobby ready');stay(10)
 def join(i):
  wait(lambda:snapshot(i).get('state')==1,90,'Client joined lobby');stay(15)
 def begin(h,c):
  command(c,'StagingReady 0');stay(4);command(h,'StagingLaunch')
  wait(lambda:all(snapshot(i).get('state')==2 and snapshot(i).get('world') for i in (h,c)),90,'Both peers entered match')
  stay(5)
 def stop(i):
  i['required']=False
  if i['process'].poll() is not None:return
  h=K.OpenProcess(0x1001,False,i['pid'])
  if h:
   try:
    identity(h,i)
    if not K.TerminateProcess(h,0):raise C.WinError(C.get_last_error())
   finally:K.CloseHandle(h)
  i['process'].wait(timeout=15)
 def progress(h,c,label):
  # A recovered match is intentionally paused until the host unpauses it.
  command(h,'GamePause 0');stay(3);before=[snapshot(i) for i in (h,c)];stay(8);after=[snapshot(i) for i in (h,c)]
  checked(label,all(y.get('seconds',0)>x.get('seconds',0)+3 for x,y in zip(before,after)) and abs(after[0]['seconds']-after[1]['seconds'])<2)
  (root/(label+'.json')).write_text(json.dumps(dict(before=before,after=after),indent=2))
 def saves(i):return i['dir']/'docs/Kohan2/data/Save'
 def errors():
  result=[]
  for i in owned:
   for f in (i['dir']/'game/Logs').glob('*.log'):
    for line in text(f).splitlines():
     if any(s in line.lower() for s in ('out of sync','out-of-sync','assert:','unhandled exception','timeout waiting for mutex')):result.append(dict(file=str(f),line=line))
  return result
 try:
  hd=prepare('host');cd=prepare('client');h=launch(hd);host_lobby(h);c=launch(cd,True);join(c);begin(h,c)
  progress(h,c,'initial-simulation')
  command(h,'GameSave "Save/Rig Manual.RSG"')
  wait(lambda:'save copy "Save/Rig Manual (copy).RSG" written' in log(c),45,'Client wrote manual save copy')
  checked('manual-copy-files',all(f.stat().st_size>1000 for f in (saves(h)/'Rig Manual.RSG',saves(c)/'Rig Manual (copy).RSG')))
  for slot in 'AB'[:a.slots]:
   wait(lambda:('autosave "Save/MP Autosave '+slot+'.RSG":') in log(h) and ('save copy "Save/MP Autosave '+slot+' (copy).RSG" written') in log(c),340,'Autosave '+slot+' and client copy completed at shipping interval')
   checked('autosave-'+slot,all(f.stat().st_size>1000 for f in (saves(h)/('MP Autosave '+slot+'.RSG'),saves(c)/('MP Autosave '+slot+' (copy).RSG'))))
  checked('only-host-autosaves','autosave: saving' not in log(c));progress(h,c,'after-autosave-simulation')
  stop(c);report('Simulated client crash')
  # A closed socket can deliver the leave decree immediately, without the
  # stalled-connection vote. Never require backup mode after recovery finished.
  wait(lambda:'RESUME_READY:' in log(h) or snapshot(h).get('backup')==1,50,'Host detected client loss')
  if 'RESUME_READY:' not in log(h):command(h,'CSDKickLaggingPlayers 0')
  wait(lambda:'RESUME_READY:' in log(h),100,'Recovery selected completed save in lobby')
  c=launch(cd,True);join(c)
  saved=max(saves(h).glob('Paw Resume*.RSG'),key=lambda p:p.stat().st_mtime)
  wait(lambda:(saves(c)/saved.name).exists() and sha(saves(c)/saved.name)==sha(saved),75,'Recovery save transferred byte-identical')
  begin(h,c);progress(h,c,'resumed-simulation');checked('recovery-transfer',sha(saved)==sha(saves(c)/saved.name))
  stop(h);report('Simulated original host crash after received-save reload')
  wait(lambda:'not saving: save source is unavailable or its header is not loaded' in log(c) or snapshot(c).get('backup')==1,50,'Client detected host loss')
  if 'not saving: save source is unavailable or its header is not loaded' not in log(c):command(c,'CSDKickLaggingPlayers 0')
  wait(lambda:'not saving: save source is unavailable or its header is not loaded' in log(c),60,'Replacement host rejected unsafe save source')
  takeover=snapshot(c);stay(70);afterLoss=snapshot(c)
  checked('received-save-host-loss-survives',c['process'].poll() is None and afterLoss.get('state')==2 and afterLoss.get('host'))
  (root/'received-save-host-loss.json').write_text(json.dumps(dict(before=takeover,after=afterLoss),indent=2))
  stop(c)
  # Prove a locally created client copy is usable as the next host's save.
  lh=prepare('copy-host',saves(c)/'Rig Manual (copy).RSG');lc=prepare('copy-client')
  h=launch(lh,load=True);host_lobby(h,True);c=launch(lc,True);join(c)
  wait(lambda:(saves(c)/'Rig Manual (copy).RSG').exists() and sha(saves(c)/'Rig Manual (copy).RSG')==sha(saves(h)/'Rig Manual (copy).RSG'),90,'Client copy transferred as the hosted save')
  begin(h,c);progress(h,c,'client-copy-loaded-simulation')
  command(h,'GameSave "Save/Loaded Copy.RSG"')
  wait(lambda:'save copy of "Save/Loaded Copy.RSG" skipped:' in log(c),45,'Received-save client safely skipped a new copy')
  checked('client-copy-loadable',(saves(h)/'Loaded Copy.RSG').stat().st_size>1000);stay(12)
  checked('no-assert-desync',not errors())
  (root/'result.json').write_text(json.dumps(dict(passed=True,checks=checks,runtimeHashes=expected,environment='two isolated local LAN processes',slots=a.slots,autosaveMinutes=5),indent=2));report('PASS all live scenarios')
 except Exception as error:
  (root/'result.json').write_text(json.dumps(dict(passed=False,error=str(error),checks=checks),indent=2));raise
 finally:
  for i in owned:
   try:stop(i)
   finally:i['handle'].close()
  (root/'error-scan.json').write_text(json.dumps(errors(),indent=2))

if __name__=='__main__':main()
