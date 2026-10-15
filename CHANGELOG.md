## 0.6.1
- Correct text encoding in the editor and README; add a UTF-8 release guard.

## 0.6.0
- Stream individual pixel edits while drawing; different pixels combine and the last server arrival wins for each pixel.
- Keep drawing while edits await acknowledgement; retry retains queued pixels.
- Send compact pixel notifications only to negotiated modded peers; vanilla sign fallback remains supported.
- Rename the wipe brush to Eraser; retain Wipe board for clearing the board.
- Requires 0.6.x on the host and drawing clients because the wire protocol changed.

## 0.5.0
- Store all boards as vanilla signs; decorate marked signs on modded clients.
- Allow optional vanilla clients through the server-required Jotunn policy.
- Route drawing requests to the host with capability negotiation and revision acknowledgements.
- Preserve canonical drawing fields across late vanilla-owner updates.
- Migrate loaded/unloaded legacy records after backup; commit before admitting peers.
- Retain vanilla sign colliders, recipes and refunds for every visual size.
- Add actual headless Unity, socket, world-save and restart tests alongside managed CI.
- Real vanilla Steam join and visual/input acceptance remain release gates.
## 0.4.1
- Require identical mod versions on all peers through Jotunn network compatibility checks.
- Add Windows/Linux multiplayer CI and vanilla-client policy guards.
- Document the live vanilla-client acceptance matrix; not yet run in-game.

## 0.4.0
- Native Valheim wood-panel editor, Norse headings, serif labels and game buttons.
- Chalk toolbar and framed slate; editor renders above the game HUD.
- Native GuiScaler follows game UI scale.
- Small, Medium and Large boards; automatic save retained.
- 16 production RPC multiplayer simulation tests plus live testing checklist.
- Live two-client/dedicated-server acceptance testing remains pending.
