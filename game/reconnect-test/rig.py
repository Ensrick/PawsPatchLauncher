"""Private two-process LAN regression; never writes the installed game/profile.

Kill and silent-drop runs differ: the latter suspends only our client child,
so the host must resolve a timeout through the real native vote path.
"""
import argparse, ctypes, hashlib, json, os, shutil, subprocess, time
from pathlib import Path

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--kit',type=Path,required=True)
    p.add_argument('--out',type=Path,required=True)
    p.add_argument('--drop',choices=['kill','silent','host'],default='silent')
    p.add_argument('--mod',choices=['vanilla','immortals'],default='vanilla')
    a=p.parse_args(); root=a.out.resolve();kit=a.kit.resolve()
    if root.exists(): raise ValueError('Use a fresh private rig output directory')
    if not (kit/'reconnect-test.marker').is_file(): raise ValueError('Expected test kit')
    root.mkdir(parents=True); (root/'rig.marker').write_text('Paw recovery r2 isolated LAN test\n')
    manifests=json.loads((kit/'manifest.json').read_text())['files']
    for relative,expected in manifests.items():
        if hashlib.sha256((kit/relative).read_bytes()).hexdigest()!=expected: raise ValueError('Kit mismatch: '+relative)
    owned=[]; events=[]
    def report(message):
        events.append(dict(at=time.time(),message=message)); print(message,flush=True)
        (root/'events.json').write_text(json.dumps(events,indent=2))
    def prepare(role):
        d=root/role; shutil.copytree(kit/'game',d/'game')
        data=d/'docs/Kohan2/data';(data/'User').mkdir(parents=True);(data/'Save').mkdir()
        (data/'User/UVars.tgi').write_text('[Vars]\n{\n int ResolutionX = 800\n int ResolutionY = 600\n flag ResolutionWindowed = true\n flag ResolutionCoopFullscreenMode = false\n flag MouseClipCursor = false\n flag RunWhenInactive = true\n flag FirstRun = false\n float AudioMainVolume = 0\n float FrameRateLimit = 30\n}\n',encoding='utf-16')
        depots='adddepot data.rwd\nadddepot music.rwd\n'
        if a.mod=='immortals':depots+='adddepot mod-immortals/\n'
        (d/'game/startup/autoexec.txt').write_text(depots+'adddepot test-data/\nadddepot %USERDATA%/data/ 1\n')
        return d,data
    host,hostData=prepare('host');client,clientData=prepare('client')
    def text(d):
        try:return (d/'game/Mods/plugins.log').read_text(errors='replace')
        except OSError:return ''
    def wait_for(check,seconds,label):
        end=time.monotonic()+seconds
        while time.monotonic()<end:
            if check():report(label);return
            exited=[(proc.pid,proc.returncode) for proc in owned if getattr(proc,'required',False) and proc.poll() is not None]
            if exited:raise RuntimeError('A required game exited '+str(exited)+': '+label)
            time.sleep(.5)
        raise TimeoutError(label)
    def launch(d,data,script,join=False):
        path=d/'game/Mods/rig.txt';path.write_text(script,encoding='ascii')
        env=os.environ.copy();env.update(SteamAppId='97130',SteamGameId='97130',PAWS_RECOVERY_DATA=str(data),PAWS_TEST_DOCUMENTS=str(data.parents[1]),PAWS_RECOVERY_AUTO_VOTE='1')
        if join:env['PAWS_TEST_JOIN']='127.0.0.1:5860'
        else:env.pop('PAWS_TEST_JOIN',None)
        proc=subprocess.Popen([str(d/'game/k2.exe'),'+Exec Mods/rig.txt'],cwd=d/'game',env=env)
        proc.required=True;owned.append(proc);report(f'Started {d.name} PID {proc.pid}');return proc
    join='Wait 12\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\nLobbySetLANNickname PawR2Client\nWait 2\nLobbyJoinDirect\nWait 2\nJoinDirectJoin\nWait 15\nStagingReady 0\nWait 20\nStagingReady 0\nWait 400\nReallyQuit\n'
    # 1.3.72 uses the first numeric argument as lobby capacity (verified in UI).
    # The LAN listener itself selects the first free port from 5860.
    hostScript='Wait 12\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\nLobbySetLANNickname PawR2Host\nWait 2\nInternetGameCreate PawR2 2 2\nWait 3\nSelectLaunchType RANDOM_MAP\nWait 45\nStagingLaunch\nWait 200\nStagingLaunch\nWait 25\nStagingLaunch\nWait 200\nReallyQuit\n'
    def scan_errors():
        found=[]
        for d in [host,client]:
            errors=[]
            for log in (d/'game/Logs').glob('*.log'):
                errors.extend(line for line in log.read_text(errors='replace').splitlines() if any(s in line.lower() for s in ['out of sync','out-of-sync','assert:', 'unhandled exception']))
            (d/'error-scan.json').write_text(json.dumps(errors,indent=2))
            found.extend(errors)
        return found
    try:
        hp=launch(host,hostData,hostScript.replace('Wait 12\n','Wait 45\n',1));cp=launch(client,clientData,join.replace('Wait 12\n','Wait 50\n',1),True)
        wait_for(lambda:'MATCH_ROLE: original_host=1' in text(host) and 'MATCH_ROLE: original_host=0' in text(client),210,'Both peers entered match with correct original roles')
        time.sleep(8)
        if a.drop=='host':
            hp.required=False;hp.kill();hp.wait();report('Killed owned host process')
            wait_for(lambda:'HOST_LOST:' in text(client),100,'Replacement host saved and paused without assuming original-host role')
        else:
            if a.drop=='silent':
                kernel=ctypes.WinDLL('kernel32',use_last_error=True);nt=ctypes.WinDLL('ntdll');kernel.OpenProcess.restype=ctypes.c_void_p
                handle=kernel.OpenProcess(0x0800,False,cp.pid)
                if not handle:raise ctypes.WinError(ctypes.get_last_error())
                nt.NtSuspendProcess.argtypes=[ctypes.c_void_p];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
                result=nt.NtSuspendProcess(handle);kernel.CloseHandle(handle)
                if result:raise RuntimeError(f'NtSuspendProcess {result:x}')
                report('Suspended owned client; socket remains open')
                wait_for(lambda:'native drop vote 1' in text(host),80,'Native vote executed after silent timeout')
            else:
                cp.required=False;cp.kill();cp.wait();report('Killed owned client process')
            wait_for(lambda:'RESUME_READY:' in text(host),100,'Host save completed and selected in lobby')
            cp.required=False
            if cp.poll() is None:cp.kill();cp.wait()
            shutil.copy2(client/'game/Mods/plugins.log',client/'before-rejoin-plugins.log')
            time.sleep(5) # Allow Steam to release the terminated client instance.
            cp=launch(client,clientData,join.replace('Wait 12\n','Wait 45\n',1),True)
            wait_for(lambda:text(host).count('MATCH_ROLE: original_host=1')>=2 and 'MATCH_ROLE: original_host=0' in text(client),200,'Both peers re-entered saved match')
            saves=list((hostData/'Save').glob('Paw Resume*.RSG'));assert saves
            transferred=clientData/'Save'/saves[-1].name
            assert transferred.is_file() and hashlib.sha256(transferred.read_bytes()).digest()==hashlib.sha256(saves[-1].read_bytes()).digest()
            report('Transferred save byte-identical: '+hashlib.sha256(saves[-1].read_bytes()).hexdigest())
            time.sleep(15)
        if scan_errors():raise RuntimeError('Native game errors found; inspect error-scan.json')
        if any(proc.poll() is not None for proc in owned if getattr(proc,'required',False)):
            raise RuntimeError('A required game exited before final validation')
        (root/'result.json').write_text(json.dumps(dict(result='passed',scenario=a.drop,mod=a.mod,events=events),indent=2))
        report('PASS '+a.drop+' '+a.mod)
    except Exception as e:
        (root/'result.json').write_text(json.dumps(dict(result='failed',scenario=a.drop,mod=a.mod,error=str(e),events=events),indent=2))
        raise
    finally:
        for proc in owned:
            if proc.poll() is None:proc.kill();proc.wait(timeout=10)
        scan_errors()

if __name__=='__main__': main()
