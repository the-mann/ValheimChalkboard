# Local validation evidence

Candidate: unreleased 0.5.0 vanilla-sign rewrite.
Environment: Windows, Valheim dedicated server 1.0.17 (network 40, world format 41), BepInExPack 5.4.2351, Jotunn 2.30.2, YamlDotNet 16.3.1.

- Actual dedicated-server reference build: passed, no warnings or errors.
- Managed production-code integration suite: 40 passed (.NET 6 local override; CI uses .NET 8).
- Static compatibility/package guards: passed.
- Actual headless engine: 46 checks passed; report in `headless-local.txt`.
- Actual world cycle: legacy fixtures seeded and saved, first restart migrated and committed them, second restart loaded converted signs with all tested fields intact.
- Pinned CI dependency URLs: all three downloaded and extracted successfully.

The server ran in the shell sandbox's low-integrity mode. Its logs also contain graphics-disabled shader notices and denied PlayerPrefs writes; those are not treated as proof of clean visual/input behavior. No normal player worlds or installed profile were modified.

Not yet established: a fully unmodified Steam client joining, a simultaneous real modded writer plus vanilla owner, rendered UI at different game scales, and actual player interaction/refund observations. The real TCP stock endpoint is an engine protocol fixture, not a Steam login or untouched vanilla executable. CI results are recorded separately by GitHub Actions.
