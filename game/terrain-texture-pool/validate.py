"""Check the checked-in overlay without the game: it is the retail UVars_k2.tgi plus exactly prepare.LINE, and
data/manifest.json matches both hashes. Prints TERRAIN_POOL_VALIDATE_PASS <checks>."""
import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('terrain_pool_prepare', HERE / 'prepare.py')
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)

checks = 0


def check(ok, why):
    global checks
    if not ok:
        raise SystemExit('TERRAIN_POOL_VALIDATE_FAIL ' + why)
    checks += 1


after = (HERE / 'data' / prepare.TARGET).read_bytes()
manifest = json.loads((HERE / 'data' / 'manifest.json').read_text(encoding='utf-8'))
check(len(manifest) == 1 and manifest[0]['path'] == prepare.TARGET, 'manifest names only the overlay')
check(manifest[0]['before'] == prepare.STOCK_SHA256, 'manifest before is the retail file')
check(hashlib.sha256(after).hexdigest() == manifest[0]['after'], 'manifest after matches the overlay')
check(after.count(prepare.LINE) == 1, 'the variable line appears once')
before = after.replace(prepare.LINE, b'')
check(hashlib.sha256(before).hexdigest() == prepare.STOCK_SHA256, 'removing the line restores the retail bytes')
check(prepare.overlay(before) == after, 'prepare.py reproduces the overlay')
check(after.endswith(b'}\r\n') and b'\n' not in after.replace(b'\r\n', b''), 'CRLF line endings as retail')
check(after.count(b'TerrainTexturePoolDesiredNumEntries') == 1 and prepare.ENTRIES >= 120, 'one larger pool size')
try:
    prepare.overlay(after)
    check(False, 'an already prepared file is rejected')
except ValueError:
    check(True, '')
try:
    prepare.overlay(before + b' ')
    check(False, 'other bytes are rejected')
except ValueError:
    check(True, '')
print('TERRAIN_POOL_VALIDATE_PASS %d' % checks)
