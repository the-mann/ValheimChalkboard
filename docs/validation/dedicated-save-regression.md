# Dedicated-server autosave regression

A real modded client joined the disposable dedicated server but drawing reached three rejected saves and retained the unsynced draft. The permission check originally searched only instantiated Player components, which are not guaranteed on a dedicated server.

The regression creates a connected peer with a replicated Player character ZDO and deliberately leaves the instantiated player list empty. It exercises production BoardNetwork saves and the production AutosaveDraft retry state. The fix resolves the connected peer's character record and retains prefab, ownership, alive, range and ward validation.

Verified locally:
- Original access implementation: regression fails with `Dedicated writer rejected without Player GameObject`.
- Fixed implementation: 43 managed integration tests pass, including the autosave case completing after one accepted request.
- Actual dedicated-server reference build: passes without warnings or errors.
- Actual engine socket rerun: blocked by the process sandbox's socket access permissions; this is not recorded as a passing wire test.

RuntimeWireTests now uses a replicated character record without creating a Player GameObject for its real TCP successful-write fixture, so headless CI covers the missing-scene-object condition too. The user's live retry after installing the repair remains required before claiming the reported issue is resolved in-game.
