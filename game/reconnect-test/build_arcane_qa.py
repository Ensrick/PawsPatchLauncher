"""Build a private two-process Arcane runner from hash-pinned release inputs.

Only process discovery/startup and the personal preferences path are adapted.
No gameplay/network patch sources or embedded payloads are edited. Never ship.
"""
import argparse, hashlib, json, subprocess
from pathlib import Path

def main():
 p=argparse.ArgumentParser();p.add_argument('--release',type=Path,required=True);p.add_argument('--out',type=Path,required=True)
 a=p.parse_args();release=a.release.resolve();out=a.out.resolve();out.mkdir(parents=True,exist_ok=False)
 for filename in ('paws_player_colors.ini','paws_patch_versions.ini'):
  (out/filename).write_bytes((release/filename).read_bytes())
 inputs=json.loads((release/'inputs.json').read_text());features=json.loads((release/'features.json').read_text())
 for name,digest in inputs.items():
  assert hashlib.sha256(Path(name).read_bytes()).hexdigest().upper()==digest.upper(),name
 name='k2_paws_ui_1372.exe'
 source=next(Path(n) for n in inputs if n.endswith(name+'.generated.cs')).read_text()
 startup=next(Path(n) for n in inputs if n.endswith('ReleaseStartup.cs')).read_text()
 def replace(text,old,new):
  assert text.count(old)==1,old
  return text.replace(old,new)
 source=replace(source,'Process[] running = Process.GetProcessesByName("k2");','Process[] running = Process.GetProcessesByName("k2").Where(p => String.Equals(p.MainModule.FileName, gamePath, StringComparison.OrdinalIgnoreCase)).ToArray();')
 source=replace(source,'ProcessStartInfo start = new ProcessStartInfo(gamePath)','if (!File.Exists(Path.Combine(gameDirectory, "reconnect-test.marker"))) throw new IOException("Private QA marker required");\n                ProcessStartInfo start = new ProcessStartInfo(gamePath, "+Exec Mods/rig.txt")')
 source=replace(source,'game = Process.Start(start);','game = Process.Start(start);\n                Console.WriteLine("OWNED_GAME " + game.Id); Console.Out.Flush();')
 source=replace(source,'MonitorRuntime(game, process, counters, syncSignal, logPath);','Console.WriteLine("PACKAGED_HELPER_READY"); Console.Out.Flush();\n            MonitorRuntime(game, process, counters, syncSignal, logPath);')
 # SYNC_ONLY excludes this migration, but isolate its path as an additional guard.
 source=replace(source,'Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),','Environment.GetEnvironmentVariable("PAWS_TEST_DOCUMENTS"),')
 startup=replace(startup,'Process[] games = Process.GetProcessesByName("k2");','Process[] games = Process.GetProcessesByName("k2").Where(p => String.Equals(p.MainModule.FileName, expectedPath, StringComparison.OrdinalIgnoreCase)).ToArray();')
 (out/'Main.cs').write_text(source);(out/'ReleaseStartup.cs').write_text(startup)
 sources=[Path(n) for n in inputs if n.endswith('.cs') and not n.endswith('.generated.cs') and not n.endswith('ReleaseStartup.cs')]
 adapted=['Main.cs','ReleaseStartup.cs']
 for filename,old,new in (
  ('PawAssistantParties.cs','Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)','Environment.GetEnvironmentVariable("PAWS_TEST_DOCUMENTS")'),
  ('PawAssistantRuntime.cs','Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)','Environment.GetEnvironmentVariable("PAWS_TEST_APPDATA")')):
  original=next(f for f in sources if f.name==filename)
  local=out/filename;local.write_text(replace(original.read_text(),old,new));sources[sources.index(original)]=local;adapted.append(filename)
 sources += [out/'Main.cs',out/'ReleaseStartup.cs']
 resources={}
 for n in inputs:
  file=Path(n)
  if file.suffix=='.bin':resources[file.stem]=file
  elif file.name=='paws_lobby_compatibility.dll':resources['PawLobbyCompatibilityNative']=file
  elif file.name=='PawsGraphicsRecorder.exe':resources['PawsGraphicsRecorder']=file
 flags='TERRAIN_TEXTURE_POOL;CITY_ASSISTANT;FAST_SAVE_TRANSFER;LOBBY_COMPATIBILITY;LAIR_RECOVERY_TEST;LAIR_RECOVERY;PAW_UNIFORM_UI;LARGE_MAPS;COMPANY_POSITION_RECOVERY;EXHAUSTION_RECOVERY;AI_POLICY;BOT_LOBBY;NIGHTMARE_DIFFICULTY;FRACTIONAL_KINGDOM_POINTS;GRAPHICS_DIAGNOSTICS;SETTLEMENT_SLOTS;ALLY_ECONOMY;FOUNDATION_COUNTS;ENGINE_CRASH_FIXES;CAMERA_ZOOM_2;SYNC_ONLY'
 exe=out/name
 command=[r'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe','/nologo','/target:exe','/platform:x86','/optimize+','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/define:'+flags,'/out:'+str(exe)]
 command += ['/resource:'+str(file)+','+key for key,file in resources.items()]+list(map(str,sources))
 subprocess.run(command,check=True)
 actual=json.loads(subprocess.check_output([str(exe),'--features']))
 assert actual==features[name]['features']
 for mode in ('--quiet-startup-self-test','--common-ui-self-test','--lobby-payload-test','--assistant-self-test','--fast-transfer-self-test'):
  subprocess.run([str(exe),mode],capture_output=True,check=True)
 (out/'qa-manifest.json').write_text(json.dumps(dict(publicHelper=features[name]['sha256'],testHelper=hashlib.sha256(exe.read_bytes()).hexdigest(),features=actual,scope='Startup and private-profile QA adaptation; not the byte-identical published helper',verifiedReleaseInputs=inputs,adaptedSources=adapted),indent=2))
 print('ARCANE_QA_BUILD_PASS',exe)

if __name__=='__main__':main()
