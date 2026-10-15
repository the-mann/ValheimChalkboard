# Headless engine validation

The managed multiplayer suite exercises production handlers with game doubles. It does not prove Harmony patch binding, Unity lifecycle, native serialization or a vanilla Steam connection.

Use a separate disposable dedicated-server installation. Valve's SteamCMD can install app `896660` with anonymous login. Keep its files and saves outside the repository and outside normal player worlds. Install BepInEx, Jotunn, YamlDotNet and the candidate DLL in that server.

On Windows:

```powershell
./scripts/test_headless.ps1 -ServerDir C:/qa/server -SaveDir C:/qa/saves
```

For the full disposable-world migration cycle, use a fresh save directory:

```powershell
./scripts/test_headless.ps1 -ServerDir C:/qa/server -SaveDir C:/qa/migration-saves -Mode Seed
./scripts/test_headless.ps1 -ServerDir C:/qa/server -SaveDir C:/qa/migration-saves -Mode Verify
./scripts/test_headless.ps1 -ServerDir C:/qa/server -SaveDir C:/qa/migration-saves -Mode Verify
```

Then test removal on a copy of that migrated world:

```powershell
./scripts/test_stock_world.ps1 -ServerDir C:/qa/server -MigratedSaveDir C:/qa/migration-saves -OutputDir C:/qa/stock-copy
```

Use a fresh output directory. This builds a separate read-only test observer with no Chalkboard/Jotunn reference or Harmony patches, stages only that observer and BepInEx core, copies the migrated saves and launches the stock server through that isolated loader. It positively checks that only the observer is loaded, all three records resolve as vanilla signs, and opaque bitmap/size/revision fields survive stock disk loading. It does not test Steam client authentication.

The runner starts the actual server with `-batchmode -nographics -public 0` and the opt-in `-chalkboard-validate` flag. The plugin waits for a loaded host world, runs the engine suite, writes a fresh report, and exits with a failure code if any check fails. The runner rejects missing, stale or failed reports and kills its own server on timeout. No desktop interaction is needed.

The engine suite checks installed Harmony hooks, all three template network identities, vanilla Sign retention, recipe/collider consistency, legacy conversion, and actual `ZDO.Serialize`/`Deserialize` roundtrips including vanilla text edits. It also uses real localhost TCP sockets, `ZRpc` and `ZRoutedRpc` for the production capability handshake and typed rejection acknowledgement, with a stock endpoint that has no chalkboard handlers.

The opt-in headless flag additionally creates/destroys each actual piece and constructs its local decoration, checking the initial prefab/marker and final collider count. Seed mode writes three persistent legacy fixtures in unloaded areas using the real save pipeline. Verify mode checks their conversion and retained fields after restart; repeating Verify checks that converted signs were persisted and migration is idempotent. These modes mutate only the disposable test world. The in-game `chalkboard_validate` command without the headless flag uses unindexed scratch records and does not run the world/socket fixtures.

A passing engine report is not proof of an unmodified client joining over Steam. That requires a separate client connection test; document its outcome separately. Headless server success also cannot establish visual UI quality.

## Related implementations

- [ValheimTune CI](https://github.com/Akoozie/ValheimTune/blob/main/.github/workflows/port.yml) anonymously downloads real dedicated-server assemblies with SteamCMD before building/testing. This is evidence for assembly-backed CI, not a claim that its suite automates vanilla joins.
- [Pelican Valheim BepInEx server setup](https://github.com/pelican-eggs/games-steamcmd/blob/main/valheim/valheim_bepinex/egg-valheim-bep-i-nex.yaml) starts the real server with `-nographics -batchmode` and installs app 896660.
- [Jotunn headless guidance](https://valheim-modding.github.io/Jotunn/tutorials/gui.html#determine-a-headless-instance) documents headless detection and skipping GUI/input hooks.
- [Unity player command-line arguments](https://docs.unity3d.com/Manual/PlayerCommandLineArguments.html) documents batch mode, graphics disabling and log redirection.
- [ValheimTesting](https://github.com/tvongaza/ValheimTesting) provides a broader native session/CLI toolkit. Its [platform reference](https://github.com/tvongaza/ValheimTesting/blob/main/docs/reference/platforms.md) runs dedicated servers without a desktop, but its game-client automation requires a signed-in interactive desktop and Steam. Its [layer guidance](https://github.com/tvongaza/ValheimTesting/blob/main/docs/guides/choose-a-test-layer.md) keeps appearance acceptance separate. This candidate uses a self-contained in-engine runner rather than adding the toolkit's .NET 10/ValheimCLI dependencies to the player's mod.
