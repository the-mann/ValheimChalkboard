# Valheim Chalkboard

Timber-framed drawing boards in three sizes, with chalk colors, undo, wiping and automatic saving. The editor fills most of the screen and uses Valheim's UI scale setting.

## 0.5.0 rewrite candidate

The host/server runs Chalkboard. Modded clients see chalkboards and can draw; vanilla clients are intended to see ordinary wooden signs. The public Thunderstore release is still 0.4.1 and requires the mod on every peer. This source candidate must finish mixed-client acceptance testing before publication.

Every new board uses the vanilla `sign` network prefab from creation. Size and drawings live in separate ZDO fields. The mod decorates marked signs locally; normal signs remain unchanged. Vanilla text edits retain drawing data, and dismantling removes the shared sign and its drawing.

Find Small, Medium and Large under **Hammer > Furniture**. Writing areas are 2 x 1.15 m, 3 x 1.725 m and 4 x 2.3 m. All sizes use the stock sign's recipe and physical collider so vanilla placement/refunds remain consistent. Larger artwork extends beyond that collider; interact near the center. All use a 128 x 72, eight-color canvas.

## Install and migrate

Requires BepInExPack_Valheim and Jotunn. Install on the host/server and on clients that want to draw. Other installed mods may independently require modded clients. Modded peers must use compatible 0.5.x versions; old 0.4.x clients are incompatible.

Back up worlds before upgrading. The host scans loaded and unloaded records for the three old chalkboard prefab identifiers, writes a durable record backup, converts them to marked vanilla signs and commits the world before admitting peers. A failed backup/save keeps entry closed. Drawings, size, placement, health and creator survive conversion. Old boards now use vanilla sign refunds. Conversion is not a supported downgrade to 0.4.x; restore the pre-upgrade world backup to downgrade.

Fully restart Valheim after updating. Remove duplicate manually installed copies. Plugin GUID and assembly name stay `marcus.valheim.whiteboard` and `ValheimWhiteboard.dll`.

## Build

Use your own installed game/BepInEx/Jotunn assemblies:

```powershell
dotnet build src -c Release -p:ValheimDir="PATH_TO_VALHEIM" -p:BepInExDir="PROFILE/BepInEx" -p:JotunnPath="PROFILE/BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll"
```

For dedicated-server references, set `-p:GameManaged="SERVER/valheim_server_Data/Managed"`. The repository does not redistribute game assemblies.

## Validation

```powershell
dotnet run --project tests/MultiplayerTests -c Release
python scripts/test_compatibility.py
```

Managed tests use .NET 8 and compile production schema, migration, authority, RPC, access and drawing code with game doubles. They cover stale/concurrent requests, rebasing, unauthorized/spoofed senders, ranges/wards, vanilla ownership, late owner packets, migration durability and session resets. They do not prove Unity rendering or a Steam connection.

A separate Windows Actions job downloads the real dedicated server anonymously, builds against it and runs Unity checks without a desktop. It verifies Harmony binding, prefab identities, actual serialization and socket routing, plus saving legacy fixtures and restarting twice to test migration and persistence. See [headless setup](docs/HEADLESS_TESTING.md).

See [compatibility evidence](docs/VANILLA_COMPATIBILITY.md) and [live acceptance cases](docs/MULTIPLAYER_TESTING.md). A Steam-authenticated completely unmodified client join and visual/input acceptance remain separate gates. Passing CI must not be described as proof those gates passed.

## Drawing behavior

Saves occur after strokes, rather than streaming every point. Revision checks reject stale writes. Retries rebase changed draft pixels over the newest drawing; the retrying draft wins same-pixel conflicts. Done/Escape waits for acknowledgement. Failed saves keep the draft and show Retry. Death, losing access, destruction or exiting can interrupt an unacknowledged stroke.

The host owns accepted drawing state even when a vanilla player owns the sign. It restores drawing fields from its canonical copy if an old owner packet arrives, preserving vanilla text/health changes. Custom replies go only to negotiated modded peers. Ward checks fail closed if the required game APIs are unavailable.

Mouse only. MIT licensed; see [LICENSE](LICENSE).
