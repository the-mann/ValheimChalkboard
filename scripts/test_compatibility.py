"""Static release/compatibility guards. Does not simulate a vanilla-client handshake."""
import json
import re
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[1]
plugin = (root / 'src/WhiteboardPlugin.cs').read_text(encoding='utf-8-sig')
manifest = json.loads((root / 'thunderstore/manifest.json').read_text(encoding='utf-8-sig'))

def check(name, condition):
    if not condition:
        raise SystemExit('FAIL: ' + name)
    print('PASS: ' + name)

check('server mod is required; vanilla clients are optional', bool(re.search(
    r'\[NetworkCompatibility\(\s*CompatibilityLevel\.ServerMustHaveMod\s*,\s*VersionStrictness\.Minor\s*\)\]', plugin)))
check('Jotunn compatibility layer is a required dependency', '[BepInDependency(Jotunn.Main.ModGuid)]' in plugin)
check('plugin identity remains stable for existing worlds', '"marcus.valheim.whiteboard"' in plugin)
schema = (root / 'src/BoardSchema.cs').read_text(encoding='utf-8-sig')
patches = (root / 'src/BoardPatches.cs').read_text(encoding='utf-8-sig')
network = (root / 'src/BoardNetwork.cs').read_text(encoding='utf-8-sig')
check('local templates retain legacy identifiers and use sign backing',
      'new CustomPiece(BoardSchema.LegacyNames[kind - 1], "sign"' in plugin and
      all(f'"{name}"' in schema for name in
          ['piece_whiteboard', 'piece_whiteboard_medium', 'piece_whiteboard_large']) and
      '__result = "sign"' in patches)
check('drawing protocol never broadcasts or targets an arbitrary object owner',
      'Everybody' not in network and 'GetOwner()' not in network and
      'InvokeRoutedRPC(Host, Save' in network)
check('save reconciliation runs before the main-thread snapshot',
      '[HarmonyPatch(typeof(ZDOMan), "PrepareSave"), HarmonyPrefix]' in patches and
      '[HarmonyPatch(typeof(ZDO), "Save")' not in patches)
version = re.search(r'\[BepInPlugin\("marcus.valheim.whiteboard",\s*"[^"]+",\s*"([^"]+)"\)\]', plugin)
check('plugin and package versions match', version and version.group(1) == manifest['version_number'])
check('package identity and dependency declarations are valid',
      manifest['name'] == 'ValheimChalkboard' and
      len(manifest['description']) <= 250 and
      'ValheimModding-Jotunn-2.30.2' in manifest['dependencies'] and
      'denikson-BepInExPack_Valheim-5.4.2351' in manifest['dependencies'])
icon = (root / 'thunderstore/icon.png').read_bytes()
check('Thunderstore icon is a 256 x 256 PNG',
      icon[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', icon[16:24]) == (256, 256))
print('Compatibility/release guards passed. Static checks do not prove vanilla joins.')

# Invalid source encodings can compile with replacement glyphs in visible UI.
for folder, pattern in [('src', '*.cs'), ('thunderstore', '*.md')]:
    for path in (root / folder).glob(pattern):
        value = path.read_text(encoding='utf-8')
        check(f'{path.relative_to(root)} is valid UTF-8 without replacement glyphs', '\ufffd' not in value)
for name in ['README.md', 'CHANGELOG.md']:
    check(f'{name} is valid UTF-8 without replacement glyphs', '\ufffd' not in (root / name).read_text(encoding='utf-8'))
