"""Square reviewed icon canvases without changing gameplay, text or panel anchors.

Input layouts are pinned separately for retail and Paw's Arcane Wars UI. This
adds four layouts to the existing r3 HUD asset bundle; never edits a game install.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

NAMES = ('floaters', 'template_game', 'favorites_display', 'organization_editor')


def section(text, name, edit):
    """Limit edits to one top-level TGI template; do not parse commented children."""
    matches = list(re.finditer(r'^\[(?:Template )?' + re.escape(name) + r'\b[^\n]*', text, re.M))
    if len(matches) != 1:
        raise ValueError('Ambiguous template: ' + name)
    start = matches[0].start()
    end = re.search(r'^\[', text[matches[0].end():], re.M)
    stop = matches[0].end() + end.start() if end else len(text)
    return text[:start] + edit(text[start:stop]) + text[stop:]


def replace(text, pattern, replacement, count=1):
    result, found = re.subn(pattern, replacement, text, flags=re.M)
    if found != count:
        raise ValueError(f'Layout changed: {pattern!r}; expected {count}, got {found}')
    return result


def pair(text, width, height, new_height, count=1):
    # Match two adjacent active properties, retaining comments/spacing elsewhere.
    return replace(text, r'(\bR\s*=\s*' + re.escape(width) + r'\s*\n\s*B\s*=\s*)'
                   + re.escape(height) + r'(?=\s*(?:\n|;))',
                   lambda m: m[1] + new_height, count)


def transform(name, text, family):
    if name == 'floaters':
        def heroes(t):
            if family == 'arcane':
                t = pair(t, '39', '51', '46')
                t = pair(t, '+33', '+38', '+33')
                return replace(t, r'(\bT = )43(?=\s*\n)', r'\g<1>38')
            t = pair(t, '48', '58', '55')
            t = pair(t, '+40', '+43', '+40')
            return replace(t, r'(\bT = )49(?=\s*\n)', r'\g<1>46')
        text = section(text, 'RecruitItemFloater', heroes)
        # The ability cell includes a recharge bar below the square image.
        def abilities(t):
            if family == 'arcane':
                t = pair(t, '+29', '+42', '+36', 2)
                t = pair(t, '+24', '+30', '+24')
                return replace(t, r'(\bT = )35\.5(?=\s*\n)', r'\g<1>29.5')
            t = pair(t, '44', '52', '50')
            t = pair(t, '41', '43', '41')
            return t  # Recharge placement remains in the available bottom band.
        text = section(text, 'FactionAbilitiesFloater', abilities)
    elif name == 'template_game':
        text = section(text, 'GameUnitButtonWidget', lambda t: pair(t, '42', '43', '42'))
        text = section(text, 'DisplayValuesBasic', lambda t:
                       replace(t, r'(\bB = )\+27(?= ;B = 100%)', r'\g<1>+24'))
        # Three 36px buttons fit the existing 114px-high formation panel.
        # Enlarging their height to 42 instead would overlap the panel bottom.
        text = section(text, 'FormationButtonsTemplate', lambda t:
                       replace(t, r'(\bR = )\+42(?=\s*\n\s*B = \+36)', r'\g<1>+36'))
        def unit(t):
            t = pair(t, '39', '46', '44')
            return replace(t, r'(\bB = )38(?= ; B = 43)', r'\g<1>36')
        text = section(text, 'UnitDisplayStatic', unit)
        text = section(text, 'SettlementStructures', lambda t: pair(t, '+47', '+53', '+52', 2))
        # Portrait frame includes a health strip; only the inner model is square.
        text = section(text, 'UnitDisplay', lambda t:
                       replace(t, r'(\bB = )10b(?=\s*\n)', r'\g<1>8b', 2))
        text = section(text, 'MiniDisplay', lambda t: pair(t, '+40', '+41', '+40'))
    elif name == 'favorites_display':
        text = pair(text, '+37', '+38', '+37')
        # Exclude the historical commented RoleDecal_1..3 definitions.
        start = text.index('[RoleDecal Template')
        stop = text.index('/* cwp042704', start)
        part = text[start:stop]
        part = replace(part, r'(\bR = )\+15(?=\s*\n\s*B = \+13)', r'\g<1>+13')
        text = text[:start] + part + text[stop:]
        # Keep the pressed client square and within the 37-unit icon frame.
        text = replace(text, r'(pressed_client_B = )\+36', r'\g<1>+35')
    elif name == 'organization_editor':
        def slots(t):
            if family == 'arcane':
                t = pair(t, '+43', '+46', '+43', 2)
                t = pair(t, '+41', '+44', '+41', 2)
                return replace(t, r'(pressed_client_B = )\+43', r'\g<1>+40', 2)
            t = pair(t, '+44', '+47', '+44', 2)
            t = pair(t, '+42', '+45', '+42', 2)
            return replace(t, r'(pressed_client_B = )\+43', r'\g<1>+40', 2)
        text = section(text, 'SlotDefinitionPanel', slots)
    else:
        raise ValueError(name)
    return text


def decode(raw):
    return raw.decode('utf-16') if raw[:2] in (b'\xff\xfe', b'\xfe\xff') else raw.decode('utf-8-sig')


def build(stock, arcane, base, output, pins):
    if output.exists():
        raise ValueError('Output directory must be new')
    bundles = {}
    audit = {}
    for family in ('pure', 'arcane'):
        manifest = json.loads((base/family/'assets.json').read_text())
        if len(manifest) != 67:
            raise ValueError('Expected complete revision 3 HUD assets')
        files = {n: (base/family/'payload'/n).read_bytes() for n in manifest}
        assert all(hashlib.sha256(b).hexdigest().upper() == manifest[n] for n,b in files.items())
        for name in NAMES:
            # Favorites have no Arcane override. All other Arcane templates must
            # come from that mod, retaining powers, compact spacing and labels.
            source = (arcane if family == 'arcane' and name != 'favorites_display' else stock)/(name+'.tgi')
            raw = source.read_bytes()
            sha = hashlib.sha256(raw).hexdigest().upper()
            assert sha == pins[family+'/'+name], 'Unreviewed UI source: ' + str(source)
            original = decode(raw).replace('\r\n', '\n')
            changed = transform(name, original, family)
            # Existing non-layout values (including localized text) are untouched.
            strip = lambda s: re.sub(r'(?m)^\s*(?:[LTRB]|pressed_client_[LTRB])\s*=.*$', '', s)
            assert strip(original) == strip(changed), name
            relative = 'data/ui/game/'+name+'.tgi'
            files[relative] = b'\xff\xfe' + changed.encode('utf-16le')
            audit[family+'/'+name] = dict(sourceSha256=sha, source=str(source),
                                         changedLines=sum(a!=b for a,b in zip(original.splitlines(),changed.splitlines(),strict=True)))
        assert len(files) == 71
        bundles[family] = files
    # All inputs validated before creating any output.
    for family, files in bundles.items():
        for name, raw in files.items():
            target = output/family/'payload'/name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(raw)
        (output/family/'assets.json').write_text(json.dumps({n:hashlib.sha256(b).hexdigest().upper()
                                                          for n,b in sorted(files.items())}, indent=2)+'\n')
    (output/'icon-audit.json').write_text(json.dumps(audit, indent=2)+'\n')
    print('ICON_ASSETS_BUILT: 71 assets per family; 4 scoped layout overrides; no executable changes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ('stock', 'arcane', 'base-assets', 'out'):
        parser.add_argument('--'+option, type=Path, required=True)
    args = parser.parse_args()
    build(args.stock, args.arcane, args.base_assets, args.out,
          json.loads(Path(__file__).with_name('icon-sources.json').read_text()))
