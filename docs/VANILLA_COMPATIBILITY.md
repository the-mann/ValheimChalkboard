# Vanilla-client compatibility

## Supported policy

Chalkboards are custom networked prefabs. Every client and server in a shared
session must have this mod, BepInEx and Jotunn installed, with the **same mod
patch version**. Vanilla/unmodded clients cannot view or use these pieces.
This is enforced through Jotunn's NetworkCompatibility attribute:
EveryoneMustHaveMod + VersionStrictness.Patch.

The protection is an early compatibility rejection, not vanilla support.
It avoids allowing an unmodded client into a world where it cannot resolve the
custom chalkboard prefab. It also prevents a modded client from placing custom
pieces on an unmodded server. Existing vanilla signs are cloned as the source
of new prefabs; their identifier is never registered as a replacement.

## Automated checks

`python scripts/test_compatibility.py` checks the production declaration,
required Jotunn dependency, stable plugin/prefab identities, separate clone
registration, package version agreement, dependency strings and icon dimensions.
GitHub Actions runs these on every push and pull request. The multiplayer
handler tests additionally cover unauthorized and stale network requests.

These are static policy guards and simulated handler tests. They do not run
Jotunn's actual handshake, a vanilla game executable, Unity rendering or world
loading. They cannot prove that a vanilla client is safely rejected in-game.

## Live verification matrix (NOT RUN)

Use separate disposable profiles and a disposable world. Fully restart between
rows. Record logs from both endpoints and the Jotunn compatibility dialog.

| Server/host | Joining client | Expected |
| --- | --- | --- |
| Chalkboard 0.4.1 | Chalkboard 0.4.1 | Join; all three sizes and drawing sync work |
| Chalkboard 0.4.1 | Jotunn installed, Chalkboard absent | Reject before world entry; missing-mod explanation |
| Chalkboard 0.4.1 | Fully vanilla, no BepInEx/Jotunn | Reject before world entry; no crash or prefab-load spam |
| Chalkboard 0.4.1 | Chalkboard 0.4.0 | Reject with version mismatch |
| Fully vanilla | Chalkboard 0.4.1 | Reject modded client before custom-piece placement |
| Fully vanilla | Fully vanilla | Normal joining unaffected |

Repeat the modded-server rows on both a player-hosted game and a dedicated
server. For every rejection, confirm the vanilla client can dismiss the error,
return to its menus, and join an unrelated vanilla world. Verify the server
stays healthy and the world file is unchanged by the rejected connection.
Do not claim live vanilla safety until these rows have actually passed.

Removing the mod from a world with chalkboards is not a migration to vanilla.
Back up the world and remove custom pieces while the mod is still installed.
