# Local validation evidence

Candidate: unreleased 0.5.0 vanilla-sign rewrite.
Environment: Windows, Valheim dedicated server 1.0.17 (network 40, world format 41), BepInExPack 5.4.2351, Jotunn 2.30.2, YamlDotNet 16.3.1.

- Actual dedicated-server reference build: passed, no warnings or errors.
- Managed production-code integration suite: 41 passed (.NET 6 local override; CI uses .NET 8).
- Static compatibility/package guards: passed.
- Actual headless engine: 55 checks passed; report in `headless-local.txt`. Includes actual Jotunn optional-client/version enforcement and a successful production socket save using an actual Unity Player on a stock-owned sign.
- Actual world cycle: legacy fixtures seeded and saved, first restart migrated and committed them, second restart loaded converted signs with all tested fields intact.
- Removal test: 8 checks passed on a copy of the migrated world using stock game logic plus a separate read-only observer; Chalkboard and Jotunn were absent. All three pieces resolved as vanilla signs with drawing metadata intact. See `stock-world-local.txt`.
- Pinned CI dependency URLs: all three downloaded and extracted successfully.
- Initial published candidate `f60551e1340e68356ad9d991db5f834ea6d04bce`: Windows/Linux managed and static jobs passed; [headless CI](https://github.com/the-mann/ValheimChalkboard/actions/runs/38671755777) passed the seed cycle and two 46-check verification boots. Later checks added the Jotunn and successful-save cases above; their CI outcome is tracked on the PR.

The server ran in the shell sandbox's low-integrity mode. Its logs also contain graphics-disabled shader notices and denied PlayerPrefs writes; those are not treated as proof of clean visual/input behavior. No normal player worlds or installed profile were modified.

Not yet established: a fully unmodified Steam client joining, a simultaneous real modded writer plus vanilla owner, rendered UI at different game scales, and actual player interaction/refund observations. The real TCP stock endpoint is an engine protocol fixture, not a Steam login or untouched vanilla executable. CI results are recorded separately by GitHub Actions.
