"""Prepare a separate manual-test copy; never starts a process or edits the installed game."""
import argparse, hashlib, importlib.util, json, shutil
from pathlib import Path
p=argparse.ArgumentParser()
for key in ('game','helpers','out','documents','folder-shim'):p.add_argument('--'+key,type=Path,required=True)
a=p.parse_args()
source=a.game.resolve();target=a.out.resolve()
if target.exists() or target==source or source in target.parents:raise ValueError('Use a new separate fixture folder')
state=json.loads((source/'.pawpatch/state.json').read_text(encoding='utf-8-sig'))
assert state['appliedSettings']['mod']=='arcane-wars'
assert state['appliedSettings']['channel']=='beta'
target.mkdir(parents=True)
game=target/'game';game.mkdir()
for path in source.iterdir():
 if path.is_file() and (path.suffix.lower() in ('.rwd','.dll','.ini') or path.name.lower()=='k2.exe'):
  shutil.copy2(path,game/path.name)
 elif path.is_dir() and (path.name.lower() in ('data','skins','mss','startup','warchest') or path.name.startswith('Local_')):
  shutil.copytree(path,game/path.name)
(game/'.pawpatch').mkdir();shutil.copy2(source/'.pawpatch/state.json',game/'.pawpatch/state.json')
(game/'Mods').mkdir()
for name in ('k2mp.dll','plugins.txt'):
 if (source/'Mods'/name).exists():shutil.copy2(source/'Mods'/name,game/'Mods'/name)
for exe in a.helpers.glob('k2_paws*.exe'):shutil.copy2(exe,game/exe.name)
spec=importlib.util.spec_from_file_location('button',Path(__file__).with_name('prepare_button.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
layout=game/'data/UI/Game/game_interface.tgi'
layout.write_bytes(module.transform(layout.read_bytes()))
shutil.copy2(a.folder_shim,game/'shfolder.dll')
(game/'steam_appid.txt').write_text('97130\n')
(game/'reconnect-test.marker').write_text('Manual widescreen UI test only\n')
user=target/'Documents/Kohan2/data/User';user.mkdir(parents=True)
for name in ('Preferences.rup','UVars.tgi'):
 path=a.documents/'Kohan2/data/User'/name
 if path.exists():shutil.copy2(path,user/name)
settings=state['appliedSettings']
colors=settings.get('customPlayerColors',False);sync=settings.get('desyncMode')=='continue';hostility=settings.get('independentHostility',True)
# Keep the selection explicit: these are the shipped eight executable identities.
if colors:
 helper=('k2_paws_lobby_colors_mp_sync_1372.exe' if sync else 'k2_paws_lobby_colors_mp_1372_experimental.exe') if hostility else ('k2_paws_lobby_colors_mp_nohostility_sync_1372.exe' if sync else 'k2_paws_lobby_colors_mp_nohostility_1372.exe')
else:helper='k2_paws_'+('sync_family_herd_relations' if sync and hostility else 'family_herd_relations' if hostility else 'sync_continue' if sync else 'ui')+'_1372.exe'
assert (game/helper).exists(),helper
(target/'helper.txt').write_text(helper,encoding='ascii')
(target/'README.txt').write_text(
 'Локальная проверка меню, роликов и кнопки Paw’s Patch.\n'
 'Запусти «Проверка интерфейса.exe» при запущенном Steam. Не запускай обычную игру одновременно.\n'
 'Проверь стартовое видео, главное меню, лобби и меню параметров; затем в матче кнопку Paw’s Patch справа над нижней панелью.\n'
 'У кнопки должны быть квадратная картинка, подсказка и звук по нажатию.\n'
 'Исходная игра и её настройки не изменены. Эта копия использует отдельные настройки и сохранения.\n'
 'Сетевой матч с обычными сборками для этой проверки не нужен.\n',encoding='utf-8-sig')
files={str(f.relative_to(game)):hashlib.sha256(f.read_bytes()).hexdigest() for f in list(game.glob('k2_paws*.exe'))+[layout,game/'k2.exe',game/'.pawpatch/state.json']}
(target/'test-build.json').write_text(json.dumps(dict(source=str(source),helper=helper,files=files,launched=False),indent=2))
print('MANUAL_TEST_PREPARED',target,'helper='+helper,flush=True)
