# Vanilla-client compatibility

## Candidate policy

0.5.0 replaces custom network prefabs with real vanilla signs. Jotunn declares `ServerMustHaveMod` with `VersionStrictness.Minor`: the server needs the mod, while clients may be vanilla. The public 0.4.1 release still uses the older required-client policy.

Modded clients decorate marked signs and get drawing tools. Vanilla clients see an ordinary sign and can change its label or dismantle it. Drawing data is stored separately from vanilla text. All sizes retain the vanilla collider and recipe/refunds. Other installed mods can still reject vanilla clients.

The host validates drawing requests and retains canonical revisions. Requests target the host, never the current object owner. Capability negotiation gates custom replies, so an unmodded owner does not need drawing RPC handlers. Old owner packets can update vanilla fields but cannot roll back accepted drawing data.

## Migration and recovery

The host scans all records after world loading, including unloaded chunks. Before changing legacy prefab hashes, it writes a `ChalkboardMigrationBackupV2` record journal under `BepInEx/config/ValheimChalkboard/migration-backups`. Each entry contains its original ID, owner, position and serialized record. A journal failure leaves records unchanged.

The converted world must complete a synchronous save before opening or accepting peers. A failed commit keeps the gate closed and can be retried. Migration is idempotent. Always keep a full pre-upgrade world backup as well: the journal is a diagnostic/recovery record snapshot, not a complete replacement world or an automatic rollback utility. Restore the full backup to downgrade.

## Evidence layers

- Managed production-code integration tests: simulated game/storage/transport; cover authority, migration and racing owner updates.
- Static policy/package guards: declarations, identifiers and metadata; do not establish runtime behavior.
- Headless actual engine: Harmony bindings, all template hashes, recipe/collider consistency, generic ZDO serialization, real local TCP/RPC framing and targeted acknowledgements.
- Headless actual world: seed unloaded legacy records, save, restart, verify conversion and retained fields, restart again to check persisted/idempotent migration.
- Real client acceptance: still required for Steam authentication, actual vanilla world entry, scene rendering, interaction and UI.

The stock socket endpoint in the engine suite intentionally registers no chalkboard handlers. It is a protocol test inside the engine, not an untouched vanilla executable joining Steam.

## Client acceptance matrix

| Host | Client | Expected |
| --- | --- | --- |
| Chalkboard 0.5.x | Chalkboard 0.5.x | Join, sizes and drawings sync |
| Chalkboard 0.5.x | Jotunn, no Chalkboard | Join, ordinary signs |
| Chalkboard 0.5.x | Fully vanilla | Join, ordinary signs, no unknown-prefab/RPC spam |
| Chalkboard 0.5.x | Chalkboard 0.4.x | Reject incompatible version |
| Fully vanilla | Chalkboard 0.5.x | Reject before chalkboard placement |
| Fully vanilla | Fully vanilla | Normal join |

Repeat supported rows on player-hosted and dedicated sessions. Confirm vanilla ownership, label edits, nearby movement, dismantling, refunds and restart. All real-client rows remain unverified until logs and observations are recorded; automated results do not fill them in.
