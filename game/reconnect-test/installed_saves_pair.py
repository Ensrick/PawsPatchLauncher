"""Live QA: installed Pure helper or explicitly adapted Arcane QA helper.

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
class MemoryStatus(C.Structure):
 _fields_=[('length',C.c_ulong),('load',C.c_ulong)]+[(n,C.c_ulonglong) for n in ('totalPhys','availPhys','totalPage','availPage','totalVirtual','availVirtual','extended')]
K.GlobalMemoryStatusEx.argtypes=[C.POINTER(MemoryStatus)]
def resource_guard():
 status=MemoryStatus();status.length=C.sizeof(status)
 if not K.GlobalMemoryStatusEx(C.byref(status)):raise C.WinError(C.get_last_error())
 if status.availPhys<3*1024**3 or status.availPage<5*1024**3:
  raise RuntimeError('Low available memory: stopping only owned QA games; physicalMiB='+str(status.availPhys//1024**2)+'; commitMiB='+str(status.availPage//1024**2))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def text(p):
 try:return p.read_text(errors='replace')
 except OSError:return ''

def main():
 p=argparse.ArgumentParser()
 for key in ('game','artifacts','helpers','out'):p.add_argument('--'+key,type=Path,required=True)
 p.add_argument('--loader',type=Path,default=Path('D:/PawPub-Kit-immortals/game/avifil32.dll'))
 p.add_argument('--client-game',type=Path,help='Separately installed localization, with identical gameplay components')
 p.add_argument('--different-keys',action='store_true')
 p.add_argument('--recovery-cycles',type=int,choices=(1,2),default=1)
 p.add_argument('--arcane-qa',type=Path,help='Startup/private-profile QA helper; runs host takeover, not Pure recovery')
 p.add_argument('--slots',type=int,choices=(0,1,2),default=2)
 p.add_argument('--replay-only',action='store_true',help='Record and replay a fresh short match with the same installed runtime')
 p.add_argument('--film',type=Path,help='Existing film recorded with the same runtime; requires --replay-only')
 a=p.parse_args()
 if a.film and not a.replay_only:p.error('--film requires --replay-only')
 for key in ('game','artifacts','helpers','out','loader'):setattr(a,key,getattr(a,key).resolve())
 if a.client_game:a.client_game=a.client_game.resolve()
 if a.arcane_qa:a.arcane_qa=a.arcane_qa.resolve()
 root=a.out;root.mkdir(parents=True,exist_ok=False);owned=[];events=[];checks={}
 assert (a.game/'.paw-live-matrix.json').is_file()
 expected={f.name:sha(f) for f in a.helpers.glob('k2_paws_*.exe')};assert len(expected)==(8 if a.arcane_qa else 4)
 if a.arcane_qa:
  # Arcane distributes variants among optional modules; test the installed set.
  expected={name:digest for name,digest in expected.items() if (a.game/name).is_file()}
  assert a.arcane_qa.name in expected
 assert all(sha(a.game/name)==digest for name,digest in expected.items())
 if a.client_game:
  assert (a.client_game/'.paw-live-matrix.json').is_file()
  assert all(sha(a.client_game/name)==digest for name,digest in expected.items())
 def report(message):
  events.append(dict(utc=time.time(),text=message));(root/'events.json').write_text(json.dumps(events,indent=2));print(message,flush=True)
 def checked(label,value):
  assert value,label
  checks[label]=True;report('PASS '+label)
 def check_alive():
  resource_guard()
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
  def omit(folder,names):
   if Path(folder).name=='.pawpatch':return [n for n in names if n!='state.json']
   return shutil.ignore_patterns('Logs','paws_ai_diagnostics','*.log','*status*.txt')(folder,names)
  source=a.client_game if 'client' in role and a.client_game else a.game
  d=root/role;shutil.copytree(source,d/'game',ignore=omit)
  game=d/'game';mods=game/'Mods';mods.mkdir(exist_ok=True)
  shutil.copy2(a.artifacts/'rig_input.dll',mods/'rig_input.dll')
  (mods/'plugins.txt').write_text('rig_input.dll\n');(mods/'rig.txt').write_text('Echo isolated rig ready\n')
  (game/'reconnect-test.marker').write_text('Owned isolated live test\n')
  # A previously verified, unmodified MIT loader; production helpers do not ship it.
  loader=a.loader.resolve()
  assert sha(loader)=='449da31a592cc53d48845ef980258f226ff389091e40d9a27373ee79fd8ba7e0'
  shutil.copy2(loader,game/'avifil32.dll');shutil.copy2(a.artifacts/'native/shfolder.dll',game/'shfolder.dll')
  data=d/'docs/Kohan2/data';(data/'User').mkdir(parents=True);(data/'Save').mkdir()
  (data/'User/UVars.tgi').write_text('[Vars]\n{\n int ResolutionX = 800\n int ResolutionY = 600\n flag ResolutionWindowed = true\n flag ResolutionCoopFullscreenMode = false\n flag MouseClipCursor = false\n flag RunWhenInactive = true\n flag SinglePlayerAutoPause = false\n flag FirstRun = false\n float AudioMainVolume = 0\n float FrameRateLimit = 30\n}\n',encoding='utf-16')
  if save:shutil.copy2(save,data/'Save'/save.name)
  assert all(sha(game/name)==digest for name,digest in expected.items())
  if a.arcane_qa:
   manifest=json.loads((a.arcane_qa.parent/'qa-manifest.json').read_text())
   assert manifest['publicHelper']==expected[a.arcane_qa.name]
   assert manifest['testHelper']==sha(a.arcane_qa)
   shutil.copy2(a.arcane_qa,game/a.arcane_qa.name)
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
   result=dict(state=u(session+0xf0),world=world,backup=read(admin+0x2f4,1)[0],host=u(admin+0x2e4)==admin,taskStatus=u(admin+0xa0))
   if world:result['seconds']=struct.unpack('<f',read(world+0xe8,4))[0]
   if controller:result['paused']=any(read(controller+0x28,2))
   return result
  except OSError:return {}
  finally:K.CloseHandle(h)
 def launch(d,client=False,load=False,film=None):
  mods=d/'game/Mods';(mods/'rig-command.txt').write_text('0')
  # Startup navigation uses the previously validated +Exec entry point, after
  # resource/bootstrap scripts finish. Dynamic commands are for the ready lobby
  # or match; state 0 by itself does not mean the menu has finished registering.
  opening='Wait 45\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\n'
  if a.different_keys:
   opening='map '+('F11' if client else 'F12')+' game|GamePause\n'+opening
  if client:opening+='LobbySetLANNickname PawSavesClient\nWait 2\nLobbyJoinDirect\nWait 2\nJoinDirectJoin\n'
  else:opening+='LobbySetLANNickname PawSavesHost\nWait 2\nInternetGameCreate PawSaves 2 2\nWait 3\nSelectLaunchType '+('LOAD_SAVED_GAME\nWait 3\nCommit\n' if load else 'RANDOM_MAP\n')
  if film:opening='Wait 45\nRequestFilm\nWait 3\nSelect Films/'+film+'\nWait 3\nFilmOK Films/'+film+'\n'
  (mods/'rig.txt').write_text(opening)
  old=d/'game/Logs/paws_recovery.log'
  serial=sum(1 for i in owned if i['dir']==d)
  if old.exists():shutil.move(old,d/('recovery-before-'+str(serial)+'.log'))
  env=os.environ.copy();env.update(SteamAppId='97130',SteamGameId='97130',PAWS_TEST_DOCUMENTS=str(d/'docs'))
  env['PAWS_RIG_EXTERNAL_SCRIPT']='1'
  env['PAWS_TEST_APPDATA']=str(d/'appdata')
  if client:env['PAWS_TEST_JOIN']='127.0.0.1:5860'
  else:env.pop('PAWS_TEST_JOIN',None)
  handle=open(d/('harness-'+str(serial)+'.log'),'w')
  invocation=[str(d/'game'/a.arcane_qa.name),'--game-dir',str(d/'game')] if a.arcane_qa else [str(a.artifacts/'InstalledHarness.exe'),str(d/'game')]
  proc=subprocess.Popen(invocation,cwd=d/'game',env=env,stdout=handle,stderr=subprocess.STDOUT)
  i=dict(process=proc,dir=d,required=True,output=Path(handle.name),handle=handle,seq=100*serial)
  owned.append(i)
  wait(lambda:'OWNED_GAME ' in text(i['output']),45,'Owned process '+d.name)
  i['pid']=int(text(i['output']).split('OWNED_GAME ')[1].split()[0]);snapshot(i)
  wait(lambda:'PACKAGED_HELPER_READY' in text(i['output']),50,'Packaged helper ready '+d.name)
  if a.arcane_qa:
   status='\n'.join(text(f) for f in (d/'game').glob('*status.txt'))
   checked('Arcane core active '+d.name+' '+str(serial),all(s in status for s in ('TASK_STATUS_INIT r1','QUIET_READY','LOBBY_COMPATIBILITY_READY','FAST_TRANSFER_R2_READY')))
  else:
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
  resource_guard()
  if a.replay_only:
   d=prepare('replay');films=d/'docs/Kohan2/data/Films'
   if a.film:
    films.mkdir(exist_ok=True);source=a.film.resolve();assert source.is_file()
   else:
    i=launch(d);host_lobby(i)
    command(i,'StagingLaunch');wait(lambda:snapshot(i).get('state')==2 and snapshot(i).get('world'),90,'Recording match started')
    before=snapshot(i);stay(25);after=snapshot(i)
    checked('recorded-simulation',after.get('seconds',0)>before.get('seconds',0)+10)
    command(i,'ReallyQuit');i['required']=False;i['process'].wait(timeout=30)
    sources=list(films.glob('*.RFM'));assert sources,'No film recorded'
    source=max(sources,key=lambda p:p.stat().st_mtime)
   saved=films/'QA-current.RFM';shutil.copy2(source,saved)
   checked('film-recorded',saved.stat().st_size>1000)
   prior=sum(text(f).count('NetEvent Push: END_OF_FILM') for f in (d/'game/Logs').glob('log-*.log'))
   i=launch(d,film=saved.name)
   wait(lambda:snapshot(i).get('state')==4 and snapshot(i).get('world'),90,'Film entered playback')
   stay(10)
   command(i,'UserReady');stay(2)
   (root/'playback-start.json').write_text(json.dumps(snapshot(i),indent=2))
   wait(lambda:sum(text(f).count('NetEvent Push: END_OF_FILM') for f in (d/'game/Logs').glob('log-*.log'))>prior,180,'Fresh film completed')
   checked('film-no-desync',not errors() and not any('FILM OUT OF SYNC' in text(f) for f in (d/'game/Logs').glob('log-*.log')))
   command(i,'ReallyQuit');i['required']=False;i['process'].wait(timeout=30)
   (root/'result.json').write_text(json.dumps(dict(passed=True,checks=checks,filmSha256=sha(saved),publicRuntimeHashes=expected,scope='Fresh short match recording and complete replay with identical installed settings; no assertion of compatibility with older films'),indent=2))
   report('PASS fresh replay');return
  if a.arcane_qa:
   hd=prepare('host');cd=prepare('client');h=launch(hd);host_lobby(h);c=launch(cd,True);join(c)
   begin(h,c);progress(h,c,'arcane-two-peer-simulation')
   command(h,'GameSave "Save/Arcane two peers.RSG"')
   wait(lambda:(saves(h)/'Arcane two peers.RSG').exists() and (saves(h)/'Arcane two peers.RSG').stat().st_size>1000,45,'Arcane host saved two-player match')
   stop(c);report('Simulated Arcane client crash');stay(5)
   if snapshot(h).get('backup'):command(h,'CSDKickLaggingPlayers 0')
   command(h,'GamePause 0');stay(5);before=snapshot(h);stay(12);after=snapshot(h)
   checked('arcane-host-survives-client-crash',after.get('state')==2 and after.get('seconds',0)>before.get('seconds',0)+3)
   command(h,'ReallyQuit');h['required']=False;h['process'].wait(timeout=30)
   h=launch(hd);host_lobby(h);c=launch(cd,True);join(c)
   # Normal host departure in the lobby exercises the repaired TaskStatus path.
   command(h,'ReallyQuit');h['required']=False;h['process'].wait(timeout=30)
   wait(lambda:snapshot(c).get('host') and snapshot(c).get('state')==1,45,'Arcane client took over lobby')
   checked('arcane-host-takeover',True)
   checked('arcane-takeover-task-status',snapshot(c).get('taskStatus') in range(5))
   command(c,'StagingLaunch');wait(lambda:snapshot(c).get('state')==2 and snapshot(c).get('world'),90,'Arcane replacement host entered match')
   before=snapshot(c);stay(12);after=snapshot(c)
   checked('arcane-takeover-simulation',after.get('seconds',0)>before.get('seconds',0)+3)
   command(c,'GameSave "Save/Arcane takeover.RSG"')
   wait(lambda:(saves(c)/'Arcane takeover.RSG').exists() and (saves(c)/'Arcane takeover.RSG').stat().st_size>1000,45,'Arcane replacement host saved match')
   stay(5);command(c,'ReallyQuit');c['required']=False;c['process'].wait(timeout=30)
   checked('arcane-normal-exits',h['process'].returncode==0 and c['process'].returncode==0)
   checked('no-assert-desync',not errors())
   (root/'result.json').write_text(json.dumps(dict(passed=True,checks=checks,publicRuntimeHashes=expected,scope='Arcane full component composition with startup/private-profile QA adaptation; two-peer simulation, client loss, lobby takeover, launch and save; no automatic recovery module in public Arcane'),indent=2))
   report('PASS Arcane host takeover');return
  hd=prepare('host');cd=prepare('client');h=launch(hd);host_lobby(h);c=launch(cd,True);join(c);begin(h,c)
  if a.client_game:
   languages=[text(d/'game/paws_game_text.ini').splitlines()[0] for d in (hd,cd)]
   checked('different-localizations',len(set(languages))==2)
   tokens=[text(d/'game/harness-status.log').split('identity=')[1].splitlines()[0] for d in (hd,cd)]
   checked('same-network-identity-across-localizations',tokens[0]==tokens[1])
   (root/'language-proof.json').write_text(json.dumps(dict(languages=languages,identities=tokens),indent=2))
  if a.different_keys:
   for d,key in ((hd,'F12'),(cd,'F11')):
    checked('local-key-map-'+key,any(('executing "map '+key+' game|GamePause') in text(f) for f in (d/'game/Logs').glob('log-*.log')))
  progress(h,c,'initial-simulation')
  command(h,'GameSave "Save/Rig Manual.RSG"')
  wait(lambda:'save copy "Save/Rig Manual (copy).RSG" written' in log(c),45,'Client wrote manual save copy')
  checked('manual-copy-files',all(f.stat().st_size>1000 for f in (saves(h)/'Rig Manual.RSG',saves(c)/'Rig Manual (copy).RSG')))
  for slot in 'AB'[:a.slots]:
   wait(lambda:('autosave "Save/MP Autosave '+slot+'.RSG":') in log(h) and ('save copy "Save/MP Autosave '+slot+' (copy).RSG" written') in log(c),340,'Autosave '+slot+' and client copy completed at shipping interval')
   checked('autosave-'+slot,all(f.stat().st_size>1000 for f in (saves(h)/('MP Autosave '+slot+'.RSG'),saves(c)/('MP Autosave '+slot+' (copy).RSG'))))
  checked('only-host-autosaves','autosave: saving' not in log(c));progress(h,c,'after-autosave-simulation')
  for cycle in range(1,a.recovery_cycles+1):
   prior=log(h).count('RESUME_READY:')
   stop(c);report('Simulated client crash '+str(cycle))
   # A closed socket can deliver the leave decree immediately, without a vote.
   wait(lambda:log(h).count('RESUME_READY:')>prior or snapshot(h).get('backup')==1,50,'Host detected client loss')
   if log(h).count('RESUME_READY:')==prior:command(h,'CSDKickLaggingPlayers 0')
   wait(lambda:log(h).count('RESUME_READY:')>prior,100,'Recovery selected completed save in lobby')
   c=launch(cd,True);join(c)
   saved=max(saves(h).glob('Paw Resume*.RSG'),key=lambda p:p.stat().st_mtime)
   wait(lambda:(saves(c)/saved.name).exists() and sha(saves(c)/saved.name)==sha(saved),75,'Recovery save transferred byte-identical')
   begin(h,c);progress(h,c,'resumed-simulation-'+str(cycle));checked('recovery-transfer-'+str(cycle),sha(saved)==sha(saves(c)/saved.name))
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
  (root/'result.json').write_text(json.dumps(dict(passed=True,checks=checks,runtimeHashes=expected,environment='two isolated local LAN processes',slots=a.slots,autosaveMinutes=5,recoveryCycles=a.recovery_cycles,differentLanguages=bool(a.client_game),differentKeys=a.different_keys),indent=2));report('PASS all live scenarios')
 except Exception as error:
  (root/'result.json').write_text(json.dumps(dict(passed=False,error=str(error),checks=checks),indent=2));raise
 finally:
  for i in owned:
   try:stop(i)
   finally:i['handle'].close()
  (root/'error-scan.json').write_text(json.dumps(errors(),indent=2))

if __name__=='__main__':main()
