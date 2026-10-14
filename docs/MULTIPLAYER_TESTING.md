# Live multiplayer acceptance test

Status: NOT RUN. Automated handler tests are a separate layer.

## Setup

Use a disposable world, two distinct player clients/accounts A and B, and the
same Valheim, BepInEx, Jotunn and Chalkboard versions. First use an A-hosted
session, then repeat on a dedicated server with the mod installed server-side.
Record versions, connection mode (Steam/crossplay), time, and all three
BepInEx/LogOutput.log files. Never use a production world for fault injection.

## Required cases (repeat for Small, Medium and Large)

1. Both players join without prefab/version errors. Build each board. Confirm
   all three registration log entries, correct size, interaction and refunds.
2. A draws red in the top left, ends the stroke, and waits for written status.
   B opens the board: the stroke must appear. Reverse roles using blue bottom right.
3. Both open at the same revision. A changes the upper half while B changes the
   lower half. Release near-simultaneously. After retries, reopen both editors:
   both independent changes must remain. Repeat with the same pixel: last
   accepted/rebased local edit wins, and both clients converge.
4. Test Undo, Wipe, Wipe all and Done/Escape. A finishes a long stroke then
   immediately presses Done: the editor must wait for acknowledgement, and B
   must see that final stroke. Repeat at UI scales 85%, 100%, 150%, 200%.
5. Under A's active ward, verify B cannot edit. Permit B and repeat successfully.
   Test creator access, disabled ward and overlapping wards on both server modes.
6. A opens the editor, then loses access or leaves range before the save.
   The owner must reject the request without overwriting the accepted drawing.
7. Save, disconnect both clients, gracefully stop/restart the server, and rejoin.
   Drawings and sizes must persist. Rejoin with B as the first nearby client.
8. Force ownership handoff by moving the original nearby owner out of the zone
   while B edits. A save either succeeds or reports failure/time-out without
   silently discarding the draft. Retry must converge without reverting A's marks.
9. Disconnect the writer after submitting, reconnect, and inspect saved state.
   A lost response may correspond to an accepted save; no duplicate revision
   effects, corruption, stuck cursor or permanently blocked controls are allowed.
10. Destroy an idle board and a board under edit. Verify controls/cursor release,
    resource refunds and absence of repeated exceptions on all clients/server.

## Evidence / result template

- Tester and date:
- Mod commit/version:
- Game/BepInEx/Jotunn versions:
- Host type and transport:
- Board size:
- Case number:
- Expected / actual:
- PASS / FAIL / BLOCKED:
- Screenshots and log excerpts:

All required cases must pass in both hosted and dedicated sessions before
claiming live multiplayer verification. Attach failures to a GitHub issue;
do not mark cases passed based only on simulated tests or compilation.
