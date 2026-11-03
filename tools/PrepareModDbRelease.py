"""Create v3 signed feeds from reviewed split-source artifacts. No upload or push.

Legacy/v2 catalogs are kept intact so old launchers can update before switching
to recipes. Their launcher metadata is promoted separately after CI publication.
"""
import argparse, copy, json, zipfile
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from PrepareRelease081 import key, sign
from PrepareRelease070 import read, write, sha, verify, validate_package

ROOT = Path(__file__).resolve().parents[1]
BASE = 'C0E279D88B10FFBE4EBDDBC361E694C89413D1109147DF8FDAB911F330C0DF07'
OLD = 'D187E0DE5920F591CF8668616CC036DCE6D261ECC436C68EBC657C565E2A049C'
NEW = 'E562BE5AF62752D61DB27306B4C7623CF30C37F916EB199DF9C5E4CFA0F9FE19'

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--prepared', type=Path, required=True)
    p.add_argument('--reference', type=Path, required=True)
    p.add_argument('--private-key', type=Path, required=True)
    a = p.parse_args()
    recipe = read(a.prepared/'package-live.json')
    assert recipe['id'] == 'arcane-wars' and recipe['version'] == '0.82.1.8-clean.1'
    assert recipe['sha256'] == NEW and sha(a.reference) == OLD
    assert recipe['sourceRecipe']['base']['sha256'] == BASE
    bundle = a.prepared/'darquan-0.82.1.8-fix-v2-source.zip'
    assert sha(bundle) == recipe['sourceRecipe']['patch']['sha256']
    assert bundle.stat().st_size == recipe['sourceRecipe']['patch']['size']
    assembled = a.prepared/'arcane-wars-source-assembled.zip'
    assert validate_package(assembled, recipe) == 6599
    with zipfile.ZipFile(a.reference) as before, zipfile.ZipFile(assembled) as after:
        assert set(before.namelist()) == set(after.namelist())
        for name in before.namelist():
            assert before.read(name) == after.read(name), name
    recipe['sourceRecipe']['patch']['url'] = 'https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/v1.0.0/' + bundle.name
    private = serialization.load_pem_private_key(a.private_key.read_bytes(), None)
    assert private.public_key().public_numbers() == key().public_numbers()
    for channel in ('stable','beta'):
        previous = verify(read(ROOT/f'feed/v2/{channel}.json'), key())
        old = next(x for x in previous['packages'] if x['id'] == 'arcane-wars')
        assert old['sha256'] == OLD
        updated = copy.deepcopy(previous)
        updated['packages'] = [copy.deepcopy(recipe) if x['id'] == 'arcane-wars' else x for x in updated['packages']]
        assert len(updated['packages']) == len(previous['packages'])
        for before, after in zip(previous['packages'], updated['packages']):
            if before['id'] != 'arcane-wars': assert before == after
            else:
                allowed = {'size','sha256','urls','sourceRecipe'}
                assert {k:v for k,v in before.items() if k not in allowed} == {k:v for k,v in after.items() if k not in allowed}
        write(ROOT/f'feed/v3/{channel}.json', sign(updated,private))
    print('V3 STAGED: two valid signatures, 6599 identical files; legacy/v2 untouched')

if __name__ == '__main__': main()
