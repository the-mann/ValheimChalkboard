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

check('all peers must install this mod, with matching patch version', bool(re.search(
    r'\[NetworkCompatibility\(\s*CompatibilityLevel\.EveryoneMustHaveMod\s*,\s*VersionStrictness\.Patch\s*\)\]', plugin)))
check('Jotunn compatibility layer is a required dependency', '[BepInDependency(Jotunn.Main.ModGuid)]' in plugin)
check('plugin identity remains stable for existing worlds', '"marcus.valheim.whiteboard"' in plugin)
check('vanilla sign is cloned under custom identifiers',
      'new CustomPiece(prefabName, "sign", config)' in plugin and
      all(f'RegisterBoard("{name}",' in plugin for name in
          ['piece_whiteboard', 'piece_whiteboard_medium', 'piece_whiteboard_large']) and
      'RegisterBoard("sign",' not in plugin)
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
print('7 compatibility/release guards passed. Actual vanilla rejection must be verified in-game.')
