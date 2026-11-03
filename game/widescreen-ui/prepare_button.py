"""Replace only the existing Paw button; preserve the remaining installed layout."""
import argparse, importlib.util, hashlib, json
from pathlib import Path

def transform(raw):
 path=Path(__file__).parents[1]/'gold-sound-button/prepare.py'
 spec=importlib.util.spec_from_file_location('paw_button_prepare',path)
 module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
 result=module.add_button(raw,(Path(__file__).parent/'button.tgi').read_text())
 encoding='utf-16' if result[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig'
 text=result.decode(encoding)
 assert text.count('[PawGoldSoundButton Template=PushButtonWidget]')==1
 assert text.count('PawUniformButtonRevision2')==1
 return result

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
 if a.output.exists():raise FileExistsError(a.output)
 raw=a.source.read_bytes();result=transform(raw)
 a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_bytes(result)
 print(json.dumps(dict(sourceSha256=hashlib.sha256(raw).hexdigest(),outputSha256=hashlib.sha256(result).hexdigest())))
