# Mixed vanilla/modded sessions: vanilla-sign backing

Status: implemented in the unreleased 0.5.0 candidate. Managed and headless engine validation are in progress; real client acceptance remains a release gate.
Current 0.4.1 still requires the mod on every peer. Do not relax that flag alone.

## Intended result

The host/server runs the mod. Clients can be fully vanilla or modded.
Modded clients see the three chalkboard sizes and the drawing editor.
Vanilla clients see an ordinary wooden sign in the same position, with a short
fallback label. They do not receive a bitmap rendering or drawing controls.
Normal signs remain normal on all clients.

## Representation

Each board must be a real vanilla `sign` in its ZDO prefab field from the moment
it is created. Add separate `chalkboard_kind_v2` and schema/version fields to
identify decorated signs. Keep the existing pixel and revision keys so drawings
survive migration. Never put drawing data in vanilla `text`.

On modded clients only, watch instantiated signs for the marker and add the
chalkboard visuals/editor interaction. A marker can arrive after Sign.Awake,
so re-check on ZDO revision changes rather than relying on Awake ordering.
Keep vanilla Sign and WearNTear behavior present. Hook Sign's interaction and
hover methods only for marked instances; remove the local decoration when its
backing sign is destroyed. Do not modify the shared vanilla sign prefab.

The build-menu choices can remain local clones, but placement must instantiate
the actual sign and initialize marker/size before its state is sent. Merely
cloning `sign` under `piece_whiteboard` is insufficient: that still serializes
a custom prefab hash. The locally inspected ZNetView.Awake calls GetPrefabName,
hashes the result, and sets that hash on the new ZDO.

## Authority and protocol

Replace owner-directed WB_Save with a registered global host-directed RPC:
ChalkboardV2_Save(board ZDOID, request ID, expected revision, encoded pixels).
Resolve the ZDO on the host, verify it is a marked vanilla sign, and retain
sender identity, nearby/alive, ward, payload and revision checks. The host must
own the ZDO while committing and force replication of the updated record.
Do not rely on the current sign owner having the mod: vanilla players can
become owners while walking nearby or editing sign text.

Target acknowledgements only to the modded requesting client; check that the
sender is the host. Add a schema/capability handshake before enabling drawing.
Do not broadcast custom RPCs to unmodded peers. Vanilla peers only receive
ordinary sign/ZDO updates. Test ownership transfer carefully: SetOwner plus
ForceSendZDO compiling is not proof that a racing vanilla owner cannot revert
an update. If necessary, retain canonical drawing revisions server-side and
reconcile owner changes rather than trusting client ownership.

Jotunn policy after the representation and protocol are ready:

[NetworkCompatibility(CompatibilityLevel.ServerMustHaveMod, VersionStrictness.Minor)]

Jotunn documents ServerMustHaveMod as requiring the server mod when the client
has it; it does not require every joining client to install the mod.
Other installed mods can independently require modded clients.

## Vanilla gameplay consistency

Retain the normal sign's physical collider on every client. Larger chalkboard
frames are a modded-client visual overlay; do not create invisible solid walls
for vanilla clients. Modded interaction may use a non-solid targeting surface.
Keep the root transform's network scale vanilla; store visual size separately.

Use the vanilla sign's recipe for all three sizes. Otherwise a vanilla client
may dismantle the sign using vanilla refund amounts, disagreeing with the
custom recipe. If distinct recipes are essential, a separately validated
host-authoritative refund implementation is required before claiming parity.

Vanilla text editing may change the fallback label but must never remove the
marker, bitmap, schema or revision. Vanilla removal intentionally removes the
shared backing object and its drawing, just like dismantling any piece.

## Existing-world migration

Before opening the server to clients, scan ALL records for:
- piece_whiteboard -> sign + Small marker
- piece_whiteboard_medium -> sign + Medium marker
- piece_whiteboard_large -> sign + Large marker

Retain ZDOID, position/rotation, creator, health, pixel payload and revision.
Use a backed-up world and an idempotent migration marker. Scan unloaded sectors
as well as active GameObjects. Persist the completed migration before allowing
vanilla peers; otherwise travelling to an old board can expose a custom prefab.
Leave unrelated signs and unknown prefabs untouched. Migration must not be a
lazy client-side patch or a change that exists only in a loaded Unity instance.

## Installed API evidence

A compile-only probe against installed Valheim/Jotunn succeeded for:
- ZDO.SetPrefab and arbitrary string/int metadata fields;
- ZDOMan.GetAllZDOsWithPrefabIterative including unloaded-sector records;
- ZRoutedRpc.Register<ZDOID,int,string> and targeted InvokeRoutedRPC;
- ZNet.GetUID, GetServerPeer().m_uid, ZDOMan.GetZDO;
- ZDO.SetOwner and ZDOMan.ForceSendZDO;
- Jotunn ServerMustHaveMod / Minor declaration.

The installed ZDO.Serialize serializes generic extra-data maps, including
strings and ints; it does not restrict those maps to fields known by Sign.
This supports preserving mod metadata on vanilla signs, but mixed-client
ownership and disk-save behavior still require a live test.

## Tests required before releasing mixed-client support

Automated:
1. Newly placed and migrated boards advertise only the vanilla sign hash.
2. Migration preserves all drawing/identity data and is idempotent.
3. Ordinary signs and unrelated custom pieces are not converted/decorated.
4. Host processes writes when a vanilla peer is the backing sign owner.
5. Stale/replayed/spoofed requests still fail, and concurrent drawings merge.
6. Replies are sent only to capability-registered modded peers.
7. Vanilla text changes leave bitmap/marker/revision data intact.
8. All physical piece and refund behavior remains vanilla-compatible.

Live, using separate profiles/accounts and a disposable world:
- Host + modded writer + fully vanilla player join together, including a
  dedicated server run; verify no unknown-prefab or unhandled-RPC errors.
- Vanilla player enters first, becomes sign owner, edits the sign, walks away,
  and returns while modded players draw. Verify all drawings converge.
- Repeat after full server restart, zone unload/reload and ownership handoff.
- Test each size, permissions, death, dismantling, refunds and late joining.
- Test a migrated existing world with boards outside the initially loaded area.
- Remove the mod from a COPY of the migrated world: boards should be ordinary
  signs, with no missing prefab references or world-load errors.

## Primary reference

https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html
