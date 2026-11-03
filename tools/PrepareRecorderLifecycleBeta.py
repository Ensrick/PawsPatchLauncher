"""Stage/publish the tested r3 recorder through both active beta catalogs."""
import argparse
from pathlib import Path
import PrepareTaskStatusBeta as release
from PrepareRelease070 import read, write, sha, verify
from PrepareRelease081 import key, sign, fetch, normalize

release.AW = '0.4.2-beta.6'
release.PURE = '0.4.1-beta.8'
release.RUNTIME = '1.3.72-pure.15-beta.8'

def prepare(a):
    import json, subprocess
    for folder, count in ((a.arcane_helpers, 8), (a.pure_helpers, 4)):
        helpers=read(folder/'features.json');assert len(helpers)==count
        for name, row in helpers.items():
            assert sha(folder/name)==row['sha256'].upper()
            actual=json.loads(subprocess.check_output([str(folder/name),'--features']))
            assert actual==row['features'] and actual['builtInGraphicsDiagnosticsRevision']==3
    release.prepare(a)

def promote(a):
    out=a.out;prep=read(out/'preparation.json');proof=read(a.validation)
    assert proof['passed'] and proof['installationCases']>=21 and proof['menuLaunches']>=3
    assert proof['nativeCases']==13 and proof['liveLifecycleCases']==6
    assert proof['recorderResourcesVerified']==12 and proof['recorderSha256']=='0ECA4EAB7BE4F2FAF1E7C9972CF3D80981560F5DC8F5B597C713B35BBEA9CA2E'
    assert proof['assetHashes']=={x['id']:x['sha256'] for x in prep['assets']}
    for file,keyname in [('payload.json','payloadSha'),('v2-payload.json','v2PayloadSha'),('history.json','historySha')]:assert sha(out/file)==prep[keyname]
    release.verify_assets(prep,a.commit);remote=release.public_root()
    for path,digest in prep['baseline'].items():
        assert sha(release.ROOT/path)==digest,path
        assert normalize(fetch(remote+path))==normalize((release.ROOT/path).read_bytes()),path
    for name,file in [('beta','payload.json'),('v2-beta','v2-payload.json')]:
        payload=read(out/file);release.scoped(verify(read(out/(name+'-before.json')),key()),payload)
        write(release.ROOT/release.FEEDS[name],sign(payload,release.private(a)))
    write(release.ROOT/'feed/changelog.history.json',read(out/'history.json'))
    write(release.ROOT/'feed/patch-guide-beta.json',read(out/'payload.json')['patchGuide'])
    print('RECORDER_BETAS_PROMOTED_LOCALLY; push and public readback remain')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('action',choices=['prepare','promote','readback']);p.add_argument('--out',type=Path,required=True)
    for name in ('private-key','arcane-helpers','pure-helpers','validation'):p.add_argument('--'+name,type=Path)
    p.add_argument('--cache',type=Path,action='append',default=[]);p.add_argument('--commit')
    a=p.parse_args()
    if a.action=='readback':release.readback(a)
    else:globals()[a.action](a)
