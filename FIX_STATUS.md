# Withdrawal candidate: exact-pair-streamed-v1

Built against the installed SE2 Game2 assemblies (4.0.0.3165). Installed with user approval after confirming SE2 was closed. NOT verified in game.

## Evidence and cause

Installed DLL timestamp: 2026-11-01 15:39:56 local. Latest log timestamp: 15:54:49.
The 15:40:36 initialization and subsequent mapping probes are newer than that DLL.
The aimed client cargo entity was `8828:1`; server containers with the same name included
`5911:1` and `7998:1`. The old resolver rejected two name matches before any transfer.
This was a mapping failure, not an aiming failure. Character name matching was unsafe too.

Actual assembly inspection (read-only Mono.Cecil tool in tools/ApiInspector):

- SimpleReplicationMappingSessionComponent maps composite definitions, not live entities.
- InProcessClientPeerTransportSessionComponent._clientToServerMapping is the engine's
  reference-equality object pairing dictionary. NotifyReplicationStarted inserts localInstances
  -> remoteInstances; destroyed pairings are removed by RemovePendingPairings.
- InventoryModelImpl.Component identifies the exact server inventory behind a streamed model.
- StreamedInventorySessionComponent.TransferAsync uses RequestItemTransfer -> MoveTo.Server ->
  TestCanTransferItemImpl -> TransferByIdx -> NotifyItemTransferred. The test checks functionality,
  item filters and per-item conveyor connectivity; TransferByIdx enforces quantity/capacity.
- ValidateInteractedEntities in this build is effectively a no-op. The candidate additionally
  checks InteractionBoundsEstimator with InteractionConfiguration.MaxInteractionDistance and
  PermissionsSessionComponent.CanUseBlockAction with PermissionsConfiguration.BasicUse on the
  aimed block and each source inventory owner. Missing permission information fails closed.

## Changes

- PlayerAccess / ReplicationAccess: exact transport pairing for both player and target; verify
  server session, reject missing pairings, no name or position fallback, remove dead NPC selector.
  Player destination must be the exact character's tagged Inventory slot, with no hierarchy fallback.
- StreamedWithdrawal / TransferAccess: obtain engine-created inventory models, match them to
  verified server inventories, and use terminal TransferAsync instead of direct withdrawal mutation.
  Candidates remain constrained to the existing conveyor traversal; the engine validates each item.
  Detach temporary models/lists in finally. Log source, destination, result and moved amount.
- BuildPlannerController: async withdrawal with repeated-input guard; retain a changed queue rather
  than clearing newly queued work; specific target-resolution failure notification.
- BuildPlannerPlugin: startup marker exact-pair-streamed-v1 for log/build correlation.
- Project files reference VRage.Multiplayer (not shipped alongside the plugin).
- ReplicationAccessTests: repeated names, reference vs value equality, absent/null pairings.
- Update-BuildPlanner.ps1: source/plugin backup, restore/build/test, optional confirmed install,
  game-running guard, hash verification, and explicit confirmed rollback.

## Planner queue discrepancy

BuildPlannerController withdrawal computes requirements from its own BuildPlannerQueue.
EngineQueueMirror is a UI projection of that queue, and TerminalPlannerPanel binds the server
BuildPlannerData returned by EngineQueueMirror.Resolve. The newer 15:35:58 log shows one plugin
queue entry and one server planner entry. A client mirror of zero therefore does not explain the
withdrawal branch above. No speculative replication change was made. Queue/terminal behavior
must be checked again after an in-game transfer, including partial withdrawal and edits while pending.

## Local validation

Release build: 0 warnings, 0 errors. Unit tests: 70 passed, 0 failed, 0 skipped.
These tests do not instantiate live game sessions or prove transfer behavior.
Installed candidate SHA256: 4634932D4980C4FBF3CE9C690D2F3814BC90B0F139CD74326793179365EEE9CA.
Previous DLL: patch-backups/install-20261101-160644/BuildPlanner.before-replacement.dll.
Installation manifest: patch-backups/install-20261101-160644/installation.json.
Original source snapshot is under patch-backups/withdrawal-fix-*.

## Update commands (from this directory)

Build, test and back up only:

    .\Update-BuildPlanner.ps1

After approval and closing SE2, build/test and prompt before installing:

    .\Update-BuildPlanner.ps1 -Install

Rollback (also prompts and requires SE2 closed):

    .\Update-BuildPlanner.ps1 -Rollback '.\patch-backups\<snapshot>\BuildPlanner.before-replacement.dll'

## Required in-game acceptance test

Use a disposable offline Classic Survival save and nonvaluable items. Do not infer success from
compilation. First confirm the startup marker, then:

1. Rebind Withdraw to middle mouse in Options > Controls > Building. Keep the existing action GUIDs.
2. Place two same-type cargo containers with distinguishable stock and an NPC nearby.
3. Queue one unfinished/projected block with a welder's right-click. Record requirements and counts
   in both cargo containers, player inventory, NPC inventory and terminal panel.
4. Aim at one container and withdraw. Verify exact-pair log identities; only that container (or its
   connected permitted network) loses the queued shortfall, the local player's main inventory gains
   the identical amount, and the NPC/unconnected container remain unchanged. Confirm no duplication.
5. Repeat with partial stock, nearly full player inventory, already-carried components, keep-queue,
   x10, rapid repeated input, and queue edits while a request is pending.
6. Test a connected inventory, disconnected inventory, port-only block, denied access and out-of-range
   block. Per-item filters/one-way conveyors must deny prohibited routes. Verify clear diagnostics.
7. Verify all nine configurable controls, right-click queueing and terminal add/remove/produce/clear.
8. Share/inspect the new BuildPlanner.log and before/after inventory counts. Until these pass,
   withdrawal is a candidate implementation, not a verified fix.

Known integration risks: internal engine pairing/model APIs may change; streamed model attachment,
custom task scheduling, port context selection and inventory replication need live verification.
No hardware changes, game process stops, or save edits were performed. The installed plugin was
replaced with explicit user approval and its SHA256 verified; the previous DLL was backed up.
