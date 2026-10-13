"""Create a private Vanilla/Immortals test kit. Never changes the source game."""
import argparse,hashlib,json,shutil
from pathlib import Path
HERE=Path(__file__).resolve().parent
EXE_HASH='1eb79bbb678668be5a05f8c98103cd9988048490ce7c53c74c9a1d0813e9cd45'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser();p.add_argument('--game',type=Path,required=True);p.add_argument('--native',type=Path,required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
 out=a.out.resolve();game=a.game.resolve();assert out!=game and game not in out.parents
 assert sha(game/'k2.exe')==EXE_HASH
 out.mkdir(parents=True,exist_ok=True)
 if any(out.iterdir()) and not (out/'reconnect-test.marker').exists():raise ValueError('Output is not an existing recovery test directory')
 (out/'reconnect-test.marker').write_text('Paws reconnect local test r2\n')
 sandbox=out/'game';sandbox.mkdir(exist_ok=True);files={}
 def copy(src,dest):
  dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dest)
  files[str(dest.relative_to(out)).replace('\\','/')]=sha(dest)
 for name in ['k2.exe','Data.rwd','Music.rwd','BinkW32.dll','mss32.dll','steam_api.dll','DbgHelp.dll','GdiPlus.dll','UnicoWS.dll','avifil32.dll','D3DX9_43.dll','D3DCompiler_43.dll']:
  copy(game/name,sandbox/name)
 for src in (game/'mss').rglob('*'):
  if src.is_file():copy(src,sandbox/'mss'/src.relative_to(game/'mss'))
 for folder in ['startup','Mods','test-data/UI/Shared','mod-immortals']:(sandbox/folder).mkdir(parents=True,exist_ok=True)
 copy(game/'data/UI/Shared/options_dialog.tgi',sandbox/'test-data/UI/Shared/options_dialog.tgi')
 source=game/'.pawpatch/packages/immortals/2.1.0';manifest=json.loads((source/'module.json').read_text('utf-8-sig'))
 for entry in manifest['files']:
  relative=entry['path'].replace('\\','/');assert relative.lower().startswith('data/') and '..' not in relative.split('/')
  src=source/'payload'/relative;assert src.stat().st_size==entry['size'] and sha(src)==entry['sha256'].lower(),relative
  dest=sandbox/'mod-immortals'/relative[5:];copy(src,dest)
 for name,dest in [('paws_reconnect.dll',sandbox/'Mods/paws_reconnect.dll'),('shfolder.dll',sandbox/'shfolder.dll')]:copy(a.native/name,dest)
 (sandbox/'Mods/plugins.txt').write_text('paws_reconnect.dll\n')
 (sandbox/'steam_appid.txt').write_text('97130\n')
 shutil.copy2(HERE/'LICENSE.Ensrick',out/'LICENSE.Ensrick')
 shutil.copy2(HERE/'THIRD_PARTY.md',out/'THIRD_PARTY.md')
 shutil.copy2(HERE/'START-HERE.txt',out/'START-HERE.txt')
 shutil.copy2(HERE/'VALIDATION.txt',out/'VALIDATION.txt')
 for name in ['Mods/plugins.txt','steam_appid.txt']:files['game/'+name]=sha(sandbox/name)
 (out/'manifest.json').write_text(json.dumps(dict(build='recovery-r2-1372',files=files),indent=2))
 print('Prepared private test kit:',out,'files=',len(files))
if __name__=='__main__':main()
