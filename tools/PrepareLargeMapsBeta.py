"""Publish Arcane-only 0.4.1 beta, retaining stable/Pure catalogs and launcher."""
import argparse, copy, hashlib, json, shutil, sys
from pathlib import Path
import PrepareDesyncBeta4 as release

release.VERSION='0.4.1'
release.BASELINE_VERSION='0.4.0'
release.AI_POLICY_REVISION=39
release.RELEASE_DATE='2026-10-11'
release.TAG='patch-'+release.VERSION
release.URL='https://github.com/VasiliyPaw/PawsPatchLauncher/releases/download/'+release.TAG+'/'
release.EXTRA_CACHE_ROOTS=(release.WORK/'outputs/arcane-stable-040-20261004',
                           release.WORK/'outputs/pure-beta-ai-scope-20261002')
sys.path.insert(0,str(release.REPO/'game/large-maps'))
from prepare import expand, BASE_HASH
TEMPLATE='data/templates/template_rmc_k2.tgi'
LARGE_HASH='339307F847E16C4197986D834526ED946D33062FF13F889EC19B88AF10480D9E'
FEATURES=dict(largeMapsRevision=1,maximumMapSide=2048,objectRegistryCapacity=262143,
              terrainHashRevision=1,largeMapWarningRevision=1,minimapMarkerScaleRevision=2)

def transform(package,payload):
    if package['id'] not in ('common-ui','pawpatch-core'):return set()
    paths={n for n in payload if n.lower()==TEMPLATE}
    for n in paths:
        payload[n]=expand(payload[n])
        assert hashlib.sha256(payload[n]).hexdigest().upper()==LARGE_HASH
    return paths

def guide(feed):
    for p in feed['packages']:
        if p.get('mods')==['arcane-wars'] and p['version']==release.VERSION:
            p['experimental']=True
    entry=next(e for e in feed['patchGuide']['entries'] if e['id']=='maps')
    entry.update(category='beta',
        bodyRu='Размеры карт до 2048×2048: добавлены 1280, 1408, 1536, 1664, 1792, 1920 и 2048. Увеличена вместимость объектов карты. При размере от 1536×1536 и частоте странствующих рот ×2/×4 перед запуском появляется предупреждение о возможных задержках. Значки миникарты уменьшаются на больших картах. Сохранения новых матчей этого выпуска требуют версии 0.4.1 или совместимой с ней; старые сохранения поддерживаются.',
        bodyEn='Map sizes up to 2048×2048: adds 1280, 1408, 1536, 1664, 1792, 1920 and 2048. Increases map object capacity. Maps of at least 1536×1536 with ×2/×4 roaming frequency show a performance warning before starting. Minimap markers scale down on large maps. Saves of new matches started with this release require version 0.4.1 or a compatible version; existing saves remain supported.')
    feed['newsTitle']=feed['changelog'][0]['title']
    feed['newsBody']=feed['changelog'][0]['body']
    return feed

def checks(stage):
    previous=release.read(release.WORK/'outputs/arcane-stable-040-20261004/publication/features.json')
    blob=(stage/'beta-helpers/registry-native/registry.bin').read_bytes()
    for name in release.VARIANTS:
        expected=copy.deepcopy(previous[name]);expected.update(FEATURES,patchVersion='0.4.1')
        assert release.feature(stage/'beta-helpers'/name)==expected,'Feature drift: '+name
        raw=(stage/'beta-helpers'/name).read_bytes()
        assert blob.hex().encode('utf-16le') in raw,'Missing tested registry: '+name
        assert '--local-data-check'.encode('utf-16le') not in raw

def verify_packages(stage):
    out=stage/'publication';checks(stage)
    feed=release.verify(release.read(out/'test-feeds/beta.json'),release.key())
    old=release.verify(release.read(out/'previous/beta.json'),release.key())
    assert feed['launcher']==old['launcher']
    helpers=set();templates=[];count=0
    for p,q in zip(feed['packages'],old['packages'],strict=True):
        assert p['id']==q['id']
        if p['sha256']==q['sha256']:continue
        assert p['mods']==['arcane-wars'] and p['version']=='0.4.1' and p['experimental']
        release.validate_package(Path(p['urls'][0]),p)
        for name,raw in release.contents(Path(p['urls'][0]),p).items():
            count+=1
            if name in release.VARIANTS:
                assert raw==(stage/'beta-helpers'/name).read_bytes();helpers.add(name)
            if name.lower()==TEMPLATE:
                assert hashlib.sha256(raw).hexdigest().upper()==LARGE_HASH;templates.append(p['id'])
    assert helpers==set(release.VARIANTS) and templates
    assert feed['modGuides']==old['modGuides']
    assert release.read(out/'final/changelog.history.json')['stable']==release.read(release.REPO/'feed/changelog.history.json')['stable']
    # Stage existing stable helpers and unchanged recruitment layers for the
    # real launcher installer matrix; no writes to the user's installation.
    stable=release.verify(release.read(out/'test-feeds/stable.json'),release.key())
    for p in stable['packages']:
        if p.get('mods')!=['arcane-wars']:continue
        if p['id'] not in ('pawpatch-core','common-ui','player-colors','desync-continue'):continue
        for name,raw in release.contents(Path(p['urls'][0]),p).items():
            dest=None
            if name in release.VARIANTS:dest=stage/'stable-helpers'/name
            if name.lower().endswith('/localization/hotkeys/hotkeys_favorites.txt'):dest=stage/'hotkeys'/name
            if dest:dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(raw)
    release.write(stage/'large-map-package-verification.json',dict(passed=True,payloadFiles=count,helpers=len(helpers),templates=templates))
    print('LARGE_MAP_PACKAGES_PASS',count,'files;',len(helpers),'helpers; template owners',templates)

def main():
    p=argparse.ArgumentParser();p.add_argument('--stage',type=Path,required=True)
    p.add_argument('--signing-dir',type=Path)
    p.add_argument('--action',choices=['prepare','verify','public','promote','readback'],default='prepare');a=p.parse_args()
    release.extra_payload_transform=transform
    release.extra_feed_transform=guide
    if a.action=='verify':verify_packages(a.stage);return
    if a.action=='readback':
        import subprocess
        head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=release.REPO,text=True).strip()
        assert json.loads(release.fetch(release.API+'/git/ref/heads/main'))['object']['sha']==head
        proof=release.read(a.stage/'publication/preparation.json')
        for path in release.TRACKED:
            raw=release.fetch(release.RAW.replace('/main/','/'+head+'/')+path)
            assert release.normalize(raw)==release.normalize((release.REPO/path).read_bytes()),path
            if path not in ('feed/v2/beta.json','feed/changelog.history.json','feed/patch-guide-beta.json'):
                assert release.sha(release.REPO/path)==proof['before'][path]
        release.write(a.stage/'publication/catalog-readback.json',dict(passed=True,commit=head,version='0.4.1'))
        print('LARGE_MAP_PUBLIC_CATALOGS_PASS',head);return
    checks(a.stage)
    if a.action=='promote':assert release.read(a.stage/'large-map-package-verification.json')['passed']
    getattr(release,a.action)(a)

if __name__=='__main__':main()
