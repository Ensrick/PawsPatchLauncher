"""Build a reversible LOCAL ONLY map-size experiment from the installed template."""
import argparse
import hashlib
import json
from pathlib import Path

SIZES = tuple(range(1280, 2049, 128))
RELATIVE = 'data/Templates/template_rmc_k2.tgi'
BASE_HASH = 'DFD1B5E15507D9FE70DA7CAEDE5874DD0FC2E420B467BD662C5DDDE8DF6655E8'

def expand(source):
    if hashlib.sha256(source).hexdigest().upper() != BASE_HASH:
        raise ValueError('Unsupported map template; preserve it and inspect before rebuilding.')
    anchor = b'\t[MapSize]\r\n\twidth = 1152\r\n\theight = 1152\r\n\trecommended_kingdoms_min = 2\r\n\trecommended_kingdoms_max = 16\r\n'
    if source.count(anchor) != 1:
        raise ValueError('Expected one 1152 map-size block')
    extra = b''.join(b'\r\n' + anchor.replace(b'1152', str(size).encode()) for size in SIZES)
    return source.replace(anchor, anchor + extra)

def main():
    p=argparse.ArgumentParser()
    p.add_argument('--game', type=Path, required=True)
    p.add_argument('--out', type=Path, required=True)
    a=p.parse_args()
    source=(a.game/RELATIVE).read_bytes()
    candidate=expand(source)
    a.out.mkdir(parents=True, exist_ok=False)
    (a.out/'template.original.tgi').write_bytes(source)
    (a.out/'template.large.tgi').write_bytes(candidate)
    digest=hashlib.sha256(candidate).hexdigest().upper()
    version=json.loads((a.game/'.pawpatch/state.json').read_text(encoding='utf-8-sig'))['modules']['pawpatch-core']['version']
    import re
    if not re.fullmatch(r'0\.\d+\.\d+(?:-beta\.\d+)?',version): raise ValueError('Invalid installed patch version')
    (a.out/'LargeMapsLocal.cs').write_text('internal static class LargeMapsLocal { internal const string TemplateHash = "'+digest+'"; internal const string PatchVersion = "'+version+'"; }\n' + Path(__file__).with_name('LargeMapsPatch.cs').read_text())
    (a.out/'manifest.json').write_text(json.dumps(dict(localOnly=True,game=str(a.game),relative=RELATIVE,before=BASE_HASH,after=digest,sizes=SIZES),indent=2))
    print('LOCAL_MAP_PRESETS_READY',list(SIZES),digest)

if __name__=='__main__': main()
