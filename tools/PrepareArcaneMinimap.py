"""Add the visible Arcane marker to common-ui; no publication or installed writes."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ICON = 'data/Minimap/Icons/AWTechMiniMapIcon.png'
ICON_SHA = '98C205EC66B3C2A1E102BF3E845368732EAE7E32410276E61150B0016013DCED'

def digest(blob):
    return hashlib.sha256(blob).hexdigest().upper()

def overlay(files):
    """Called only for the Arcane common-ui payload, never pure-beta-common."""
    blob = (ROOT/'game/arcane-minimap'/ICON).read_bytes()
    if digest(blob) != ICON_SHA:
        raise ValueError('Unexpected Arcane minimap resource')
    names = [n for n in files if n.lower() == ICON.lower()]
    if len(names) > 1:
        raise ValueError('Duplicate minimap resource paths')
    target = names[0] if names else ICON
    files[target] = blob
    return target

def prepare(source, destination, version):
    if destination.exists():
        raise FileExistsError(destination)
    with zipfile.ZipFile(source) as z:
        names = z.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate ZIP entries')
        manifest = json.loads(z.read('module.json'))
        if manifest['id'] != 'common-ui' or manifest.get('remove'):
            raise ValueError('Expected additive Arcane common-ui package')
        files = {n[8:]: z.read(n) for n in names if n.startswith('payload/') and not n.endswith('/')}
    expected = {f['path']:f for f in manifest['files']}
    if set(expected) != set(files):
        raise ValueError('Payload differs from manifest')
    for name, blob in files.items():
        if len(blob) != expected[name]['size'] or digest(blob) != expected[name]['sha256'].upper():
            raise ValueError('Invalid source payload: '+name)
    original = files.copy()
    target = overlay(files)
    assert all(files[n] == b for n,b in original.items() if n != target)
    manifest['version'] = version
    manifest['files'] = [dict(path=n,size=len(b),sha256=digest(b)) for n,b in sorted(files.items())]
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination,'x',zipfile.ZIP_DEFLATED) as z:
        z.writestr('module.json',json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
        for n,b in sorted(files.items()):
            z.writestr('payload/'+n,b)
    return dict(sourceSha256=digest(source.read_bytes()),sha256=digest(destination.read_bytes()),
                changed=[target],unchangedFiles=len(original)-(target in original),version=version)

if __name__ == '__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path,required=True)
    p.add_argument('--out',type=Path,required=True)
    p.add_argument('--version',required=True)
    a=p.parse_args()
    print(json.dumps(prepare(a.source,a.out,a.version),indent=2))
