"""Build the independent Pure beta composition. Never launches or installs a game."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
NATIVE = {
    'lair-recovery': ('lair-native', 'LairRecoveryPatch', 'LairRecoveryPayload'),
    'camera-zoom': ('camera-native', 'CameraZoomPatch', 'CameraZoomPayload'),
    'company-position': ('company-native', 'CompanyPositionPatch', 'CompanyPositionPayload'),
    'exhaustion-recovery': ('exhaustion-native', 'ExhaustionRecoveryPatch', 'ExhaustionRecoveryPayload'),
    'ally-economy': ('ally-native', 'AllyEconomyPatch', 'AllyEconomyPayload'),
    'engine-crash-fixes': ('crash-native', 'EngineCrashFixesPatch', 'EngineCrashPayload'),
    'bot-lobby': ('bot-lobby-native', 'BotLobbyPatch', 'BotLobbyPayload'),
    'ai-policy': ('ai-native', 'AiPolicyRuntime', 'AiPolicyPayload'),
}

def run(args):
    result = subprocess.run([str(x) for x in args], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(' '.join(map(str,args))+'\n'+result.stdout+'\n'+result.stderr)
    return result.stdout

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('out','native','ai-native','compiler'): parser.add_argument('--'+name,type=Path,required=True)
    parser.add_argument('--dotnet',type=Path)
    parser.add_argument('--mapped-image',type=Path)
    parser.add_argument('--recovery-native',type=Path,help='Verified integrated recovery/lobby DLL for Pure beta only')
    parser.add_argument('--saves-native',type=Path,help='Verified multiplayer saves DLL, paired with the recovery beta')
    args=parser.parse_args(); out=args.out.resolve(); out.mkdir(parents=True,exist_ok=False)
    assert bool(args.recovery_native)==bool(args.saves_native), 'Recovery beta requires both coordinated native modules'
    assert json.loads((args.ai_native/'ai-policy.json').read_text())['profile']=='pure', 'Pure requires its own native AI profile'
    assert 'Profile="pure"' in (args.ai_native/'AiPolicyPayload.cs').read_text(), 'Wrong AI payload'
    source=ROOT/'game/pure-fixes'; shared=ROOT/'game/beta7'
    sources=[source/(name+'.cs') for name in ('Program','NativeMemory','PurePatch','PureChannel','PureBetaRuntime','PureFamilyRelations')]
    sources += [shared/(name+'.cs') for name in ('TerrainRuntime','ReleaseStartup','RandomMapPatch','RandomMapBundle','GamePresentation1372','GameText','BuildFeatures','LobbyColorsNative','SyncDiagnostics1372','k2_paws_family_herd_relations_1372')]
    sources += [ROOT/'game/fast-transfer/FastTransfer.cs', ROOT/'game/lobby-compatibility/PawLobbyCompatibility.cs',
                ROOT/'game/graphics-diagnostics/GraphicsDiagnostics.cs', ROOT/'game/ai-policy/PawAiOptions.cs',
                ROOT/'game/ai-policy/AiDiagnosticsSnapshot.cs', args.native/'nightmare-data/NightmareDifficultyData.cs']
    for folder,(generated,patch,payload) in NATIVE.items():
        sources += [ROOT/'game'/folder/(patch+'.cs'), (args.ai_native if folder=='ai-policy' else args.native/generated)/(payload+'.cs')]
    resources={name:shared/(name+'.bin') for name in ('PawCommonUiPayload','PawCommonUiFixups','RandomMapPayload','RandomMapFixups','PawLobbyColorsPayload','PawLobbyColorsFixups')}
    resources.update(FastTransferGuards=args.native/'FastTransferGuards.bin',
                     PawLobbyCompatibilityNative=args.recovery_native or args.native/'paws_lobby_compatibility.dll',
                     PawsGraphicsRecorder=args.native/'PawsGraphicsRecorder.exe')
    if args.saves_native:
        sources += [ROOT/'game/multiplayer-saves/MultiplayerSaves.cs']
        resources['PawMultiplayerSavesNative']=args.saves_native
    # A compile-time allowlist is deliberate: excluded gameplay payloads cannot
    # be accidentally enabled by a launcher setting or stale installed file.
    assert not any(any(part in str(p) for part in ('city-assistant','foundation-native','slots-native','fractional-native')) for p in sources)
    report={}; compiler=[args.compiler]; deterministic=[]
    if args.dotnet:
        sdk=run([args.dotnet,'--list-sdks']).strip().splitlines()[-1].split(' [',1)
        compiler=[args.dotnet,Path(sdk[1].rstrip(']'))/sdk[0]/'Roslyn/bincore/csc.dll']
        deterministic=['/nostdlib+','/langversion:7.3','/deterministic+','/pathmap:'+str(ROOT)+'=/src']
        deterministic+=['/r:'+str(args.compiler.parent/(n+'.dll')) for n in ('mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Web.Extensions')]
    for profile in ('pure','arcane'):
        test=out/('AiOptions-'+profile+'.exe')
        options=['/nologo','/platform:x86','/target:exe','/main:OptionsTests','/out:'+str(test)]
        options+=deterministic or ['/r:System.Core.dll','/r:System.Web.Extensions.dll']
        if profile=='pure':options+=['/define:PAW_PURE_EXTENDED']
        run(compiler+options+[ROOT/'game/ai-policy/PawAiOptions.cs',ROOT/'game/ai-policy/OptionsTests.cs'])
        check=run([test]);(out/('ai-options-'+profile+'.txt')).write_text(check);print(profile,check,flush=True)
    for colors in (False,True):
        for sync in (False,True):
            name='k2_paws_pure'+('_colors' if colors else '')+('_sync' if sync else '')+('_fixes' if not(colors or sync) else '')+'_1372.exe'
            flags=['PAW_PURE_EXTENDED','PAW_PURE_CHANNEL','PAW_PURE_FAST_TRANSFER','PAW_MENU_PRESENTATION','HERD_RELATIONS_ONLY','PROVOKED_NEUTRAL_ATTACK','RACE_RELATIONS','PRESERVE_NONINDEPENDENT_OWNER']
            if args.recovery_native: flags.append('PAW_PURE_RECOVERY')
            if args.saves_native: flags.append('PAW_PURE_MULTIPLAYER_SAVES')
            src=list(sources); res=dict(resources)
            if colors:
                flags.append('PAW_PURE_COLORS')
                res.update({n:shared/(n+'.bin') for n in ('PawLobbyColorsPayload','PawLobbyColorsFixups')})
            if sync:
                flags.append('PAW_PURE_SYNC'); src += [source/'PureSync.cs']
            exe=out/name
            options=['/nologo','/platform:x86','/optimize+','/define:'+';'.join(flags)]
            options+=deterministic or ['/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll']
            options += ['/resource:'+str(path)+','+key for key,path in res.items()]
            run(compiler+options+['/target:winexe','/main:PawPureFixes.Program','/out:'+str(exe)]+src)
            if args.dotnet:
                repro=out/'repro';repro.mkdir(exist_ok=True)
                run(compiler+options+['/target:winexe','/main:PawPureFixes.Program','/out:'+str(repro/name)]+src)
                assert exe.read_bytes()==(repro/name).read_bytes(),'Non-reproducible helper'
            if not colors and not sync and args.mapped_image:
                test=out/'PureBetaNativeTests.exe'
                run(compiler+options+['/target:exe','/main:PawPureFixes.PureBetaNativeTests','/out:'+str(test)]+src+[source/'PureBetaNativeTests.cs'])
                check=run([test,args.mapped_image]);(out/'composition-tests.txt').write_text(check);print(check,flush=True)
            features=json.loads(run([exe,'--features']))
            for key in ('cityAssistant','automaticMilitia','additionalBuildingSlots','foundationDistribution','fractionalKingdomPoints'):
                assert features[key] is False, key
            report[name]={'sha256':hashlib.sha256(exe.read_bytes()).hexdigest().upper(),'features':features}
            print('PURE_BETA_BUILD_PASS',name,flush=True)
    (out/'features.json').write_text(json.dumps(report,indent=2))
    (out/'inputs.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in set(sources)|set(resources.values())},indent=2))

if __name__=='__main__':main()
