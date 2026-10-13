"""Verify a tagged CI launcher and stage its four signed catalogs, without gameplay changes.

Publishing the Git tag, promoting catalogs and pushing them remain separate steps.
Release notes come from docs/release-VERSION.json. No credentials are printed.
"""
import argparse, copy, hashlib, io, json, shutil, subprocess, zipfile
from datetime import datetime, timezone
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from PrepareRelease081 import FEEDS, RAW, API, key, sign, fetch, normalize
from PrepareRelease070 import read, write, sha, verify

ROOT = Path(__file__).resolve().parents[1]
DOWNLOAD = 'https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/'
ALLOWED = {'launcher', 'publishedAt', 'changelog', 'newsTitle', 'newsBody'}


def public_root():
    # Raw main URLs can briefly serve the previous catalog after a successful
    # push. Compare against the current remote commit without weakening the
    # unchanged-baseline check or relying on cache-busting query parameters.
    ref = subprocess.check_output(['git', 'ls-remote', 'origin', 'refs/heads/main'], cwd=ROOT, text=True).split()
    assert len(ref) == 2 and ref[1] == 'refs/heads/main'
    return RAW.replace('/main/', '/' + ref[0] + '/')


def scoped(before, after):
    assert {k: v for k, v in before.items() if k not in ALLOWED} == {
        k: v for k, v in after.items() if k not in ALLOWED}, 'Game catalog changed'


def check_bytes(data, record):
    assert len(data) == record['size']
    assert hashlib.sha256(data).hexdigest().upper() == record['sha256'].upper()


def stage(a):
    out = a.out
    assert not out.exists(), 'Use a fresh staging directory'
    remote = public_root()
    tag = 'v' + a.version
    release = json.loads(fetch(API + '/releases/tags/' + tag))
    assert not release['draft'] and not release['prerelease']
    ref = json.loads(fetch(API + '/git/ref/tags/' + tag))['object']
    while ref['type'] == 'tag':
        ref = json.loads(fetch(API + '/git/tags/' + ref['sha']))['object']
    assert ref['type'] == 'commit' and ref['sha'] == a.commit
    artifact = json.loads(fetch(DOWNLOAD + tag + '/launcher-artifact.json'))
    assert artifact['version'] == a.version and artifact['sourceCommit'] == a.commit
    if artifact['authenticodeRequired']:
        assert artifact['authenticodeStatus'] == 'Valid' and artifact['timestamped']
    else:
        assert a.allow_unsigned_launcher and artifact['authenticodeStatus'] == 'NotSigned'
    records = {f['name']: f for f in artifact['files']}
    exe = fetch(DOWNLOAD + tag + '/PawsPatchLauncher.exe')
    check_bytes(exe, records['PawsPatchLauncher.exe'])
    zip_name = 'PawsPatchLauncher-' + tag + '-win-x64.zip'
    archive = fetch(DOWNLOAD + tag + '/' + zip_name)
    check_bytes(archive, records[zip_name])
    with zipfile.ZipFile(io.BytesIO(archive)) as z:
        assert set(z.namelist()) == set(records) - {zip_name}
        for name in z.namelist():
            check_bytes(z.read(name), records[name])
    notes_path = ROOT / ('docs/release-' + a.version + '.json')
    notes = read(notes_path)
    assert notes['category'] == 'launcher' and notes['version'] == a.version
    assert set(notes['body']) == {'ru', 'en', 'de', 'fr', 'cs', 'uk'}
    private = serialization.load_pem_private_key(a.private_key.read_bytes(), None)
    assert private.public_key().public_numbers() == key().public_numbers()
    launcher = dict(version=a.version, size=len(exe), sha256=hashlib.sha256(exe).hexdigest().upper(),
                    urls=[DOWNLOAD + tag + '/PawsPatchLauncher.exe'])
    history = read(ROOT / 'feed/changelog.history.json')
    before_hashes = {}
    for name, path in FEEDS.items():
        raw = (ROOT / path).read_bytes()
        assert normalize(fetch(remote + path)) == normalize(raw), 'Public catalog changed: ' + name
        before = verify(json.loads(raw), key())
        assert before['launcher']['version'] == a.previous
        assert not any(n['category'] == 'launcher' and n['version'] == a.version for n in before['changelog'])
        updated = copy.deepcopy(before)
        updated.update(launcher=launcher, publishedAt=datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
                       changelog=[notes] + before['changelog'], newsTitle=notes['title'], newsBody=notes['body'])
        scoped(before, updated)
        before_hashes[name] = sha(ROOT / path)
        write(out / 'previous' / (name + '.json'), json.loads(raw))
        write(out / 'signed' / (name + '.json'), sign(updated, private))
        if name in ('stable', 'beta'):
            # The history includes older entries from several mod branches; a
            # scoped signed feed can carry different archived notes/metadata.
            # Prepend this release independently, preserving both histories.
            assert not any(n['category'] == 'launcher' and n['version'] == a.version for n in history[name])
            history[name] = [notes] + history[name]
    write(out / 'changelog.history.json', history)
    write(out / 'launcher-artifact.json', artifact)
    (out / 'PawsPatchLauncher.exe').write_bytes(exe)
    (out / zip_name).write_bytes(archive)
    write(out / 'preparation.json', dict(version=a.version, commit=a.commit, before=before_hashes,
        notesSha256=sha(notes_path), historyBefore=sha(ROOT / 'feed/changelog.history.json'), launcher=launcher))
    print('STAGED: public EXE/ZIP verified against tagged CI; four signatures; gameplay unchanged', flush=True)


def promote(a):
    proof = read(a.out / 'preparation.json')
    remote = public_root()
    assert proof['version'] == a.version
    assert proof['notesSha256'] == sha(ROOT / ('docs/release-' + a.version + '.json'))
    assert proof['historyBefore'] == sha(ROOT / 'feed/changelog.history.json')
    for name, path in FEEDS.items():
        assert proof['before'][name] == sha(ROOT / path), 'Catalog changed: ' + name
        assert normalize(fetch(remote + path)) == normalize((ROOT / path).read_bytes())
        after = verify(read(a.out / 'signed' / (name + '.json')), key())
        scoped(verify(read(ROOT / path), key()), after)
        assert after['launcher'] == proof['launcher']
    for name, path in FEEDS.items():
        shutil.copyfile(a.out / 'signed' / (name + '.json'), ROOT / path)
    shutil.copyfile(a.out / 'changelog.history.json', ROOT / 'feed/changelog.history.json')
    print('PROMOTED LOCALLY: push and public readback still required')


def readback(a):
    proof = read(a.out / 'preparation.json')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    assert json.loads(fetch(API + '/git/ref/heads/main'))['object']['sha'] == head
    base = RAW.replace('/main/', '/' + head + '/')
    for name, path in FEEDS.items():
        public = verify(json.loads(fetch(base + path)), key())
        assert public == verify(read(ROOT / path), key())
        assert public['launcher'] == proof['launcher']
        scoped(verify(read(a.out / 'previous' / (name + '.json')), key()), public)
    assert json.loads(fetch(base + 'feed/changelog.history.json')) == read(ROOT / 'feed/changelog.history.json')
    write(a.out / 'readback.json', dict(passed=True, launcher=proof['version'], catalogCommit=head))
    print('PUBLICATION PASS: launcher ' + proof['version'] + ', four signed catalogs, unchanged game packages; ' + head)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=['stage', 'promote', 'readback'])
    p.add_argument('--out', type=Path, required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--previous')
    p.add_argument('--commit')
    p.add_argument('--private-key', type=Path)
    p.add_argument('--allow-unsigned-launcher', action='store_true')
    a = p.parse_args()
    globals()[a.action](a)
