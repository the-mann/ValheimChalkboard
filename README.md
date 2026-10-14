# Valheim Chalkboard

Timber-framed chalkboards for Valheim: draw with the mouse, choose chalk colors,
undo strokes, and wipe the board. Changes save automatically after a stroke;
Done or Escape waits for the final save. The editor fills most of the screen
and follows Valheim's UI scale setting.

## Pieces

Find these under **Hammer > Furniture**:

| Piece | Writing area | Materials |
| --- | --- | --- |
| Small | 2 x 1.15 m | 4 Wood, 1 Coal |
| Medium | 3 x 1.725 m | 8 Wood, 2 Coal |
| Large | 4 x 2.3 m | 16 Wood, 4 Coal |

All sizes use a 128 x 72, eight-color canvas. Existing boards retain the
`piece_whiteboard` identifier and their drawings. The plugin GUID and assembly
name remain `marcus.valheim.whiteboard` and `ValheimWhiteboard.dll` for compatibility.

## Install

Requires BepInExPack_Valheim and Jotunn. Copy the built DLL to
`BepInEx/plugins/ValheimWhiteboard/`, or install the Thunderstore package through
r2modman. Install the same version on every player client and dedicated server.
Fully restart Valheim after updating. Remove an older manually installed copy
before using a mod-manager package to avoid loading duplicate DLLs.

## Build

Requires a .NET SDK and your own installed Valheim, BepInEx, and Jotunn DLLs:

```powershell
dotnet build src/ValheimWhiteboard.csproj -c Release -p:ValheimDir="PATH_TO_VALHEIM" -p:BepInExDir="PROFILE/BepInEx" -p:JotunnPath="PROFILE/BepInEx/plugins/ValheimModding-Jotunn/Jotunn.dll"
```

The repository does not redistribute proprietary game assemblies.

## Automated multiplayer regression tests

```powershell
dotnet run --project tests/MultiplayerTests -c Release
```

With .NET 8, add `-p:TargetFramework=net8.0`. GitHub Actions runs this on each
push and pull request. No game installation or test-framework package is needed.

The tests compile the **actual production DrawingBoard.cs** with minimal game
and networking doubles. Sixteen regression cases cover two-peer saves, conflicts,
merge/retry, spoofed identities and acknowledgements, range, death/disconnect,
ward permissions, ownership handoff, replay, corrupt payloads, world-data
readback, and the pixel codec. Exceptions fail the process and CI job.

These tests simulate Unity, ZDO storage, RPC delivery and wards. They do not
verify actual transport, GUI input, shaders, disk saves or a dedicated server.
The editor autosave state machine is not covered by these handler tests.
See [live multiplayer testing](docs/MULTIPLAYER_TESTING.md) for acceptance steps.

## Multiplayer behavior and limits

Saves replicate through world data after a stroke; strokes are not streamed.
Revision checks prevent stale saves from replacing a newer drawing. Retrying
merges changed draft pixels over the newest board; on same-pixel conflicts the
retrying local change wins. Save failures keep the editor draft with a Retry
button. Ownership changes or a lost response can time out even if a save landed.
Wait for the written status before quitting. Death, leaving range, destruction
or game exit can interrupt an unacknowledged stroke.

Ward checks use private game APIs and fail closed if unavailable. Mouse only.
Placement inherits vanilla sign snap points; larger board colliders follow
visual size. Back up worlds before removing mods with placed custom pieces.

## Validation status

0.4.0 compiles against the locally installed game and Jotunn 2.30.2.
All 16 simulated multiplayer regression cases pass locally. Live two-client
and dedicated-server testing has not yet been completed. Do not interpret
CI success as proof of in-game multiplayer compatibility.

MIT licensed. See [LICENSE](LICENSE).

0.4.0 replaces the generic IMGUI editor with native Jotunn wood panels, Valheim fonts and buttons, a framed slate and chalk tools. It uses the game's GuiScaler component and an overlay above the HUD. In-game visual verification of this update is pending.

