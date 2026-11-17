using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Keen.Game2.Simulation.StreamedUI.Terminal;
using Keen.Game2.Simulation.StreamedUI.Terminal.Inventories;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks.ResourceDistribution.Conveyors;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.Game2.Simulation.WorldObjects.Tools;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.Library.Utils;

namespace BuildPlanner;

internal static class StreamedWithdrawal
{
    // Use the terminal's own request path: it moves execution to the server, checks
    // per-item conveyor connectivity and functionality, transfers and notifies replication.
    internal static async Task<WithdrawalResult> WithdrawAsync(Session client, Session server,
        Entity character, Entity target, InventoryComponent destination,
        IReadOnlyList<InventoryComponent> sources, IReadOnlyList<ItemAmount> required)
    {
        var api = client.SessionComponents?.TryGet<StreamedInventorySessionComponent>()
            ?? throw new InvalidOperationException("Client streamed inventory service unavailable");
        var localCharacter = PlayerAccess.GetClientCharacter(client)
            ?? throw new InvalidOperationException("Controlled character unavailable");
        var localTarget = localCharacter.FirstOrDefault<IInteractedEntityProvider>()?.InteractedEntity
            ?? throw new InvalidOperationException("Interaction target unavailable");
        if (!ReferenceEquals(ReplicationAccess.Resolve(client, localCharacter), character) ||
            !ReferenceEquals(ReplicationAccess.Resolve(client, localTarget), target))
            throw new InvalidOperationException("Interaction changed before withdrawal");

        var info = new InteractionInfo { InteractorEntity = localCharacter, InteractedEntity = localTarget };
        foreach (var tag in new[] { "Inventory", "InventoryIn", "InventoryOut" })
            if (target.TryGet<InventoryComponent>(StringId.Get(tag)) != null)
            { info.InventoryTag = StringId.Get(tag); break; }
        var conveyor = target.TryGet<ConveyorComponent>();
        if (info.InventoryTag == null && conveyor?.ConveyorSystem != null && conveyor.ConveyorSubgraph != null)
            for (var i = 0; i < conveyor.ConveyorSubgraph.Nodes.Count; i++)
                if (conveyor.ConveyorSystem.HasNodeWithSubgraphId(target.DEntity, i))
                { info.ConveyorSubgraphId = i; break; }

        InventoryListModelStreamed? playerList = null;
        var sourceLists = new List<InventoryListModelStreamed>();
        var attached = new List<(InventoryContainerModel Model, InventoryListModelStreamed List)>();
        try
        {
            playerList = await api.Attach(localCharacter, false, info);
            var destinationModels = await AttachAll(playerList);
            var sourceModels = new List<InventoryModel>();
            var attachedOwners = new HashSet<Entity>();

            // Attach every authoritative inventory owner discovered by the server conveyor walk.
            // Attaching only localTarget made the terminal stream expose the aimed container and
            // silently discarded the rest of the already-verified network.
            foreach (var source in sources)
            {
                var serverOwner = source?.Entity;
                if (serverOwner == null || !attachedOwners.Add(serverOwner)) continue;

                var localOwner = ReplicationAccess.ResolveLocal(client, serverOwner) as Entity;
                if (localOwner == null)
                {
                    Log.Write($"  withdrawal: no exact client replica for reachable source '{serverOwner}'; skipped");
                    continue;
                }

                var list = await api.Attach(localOwner, false, info);
                if (list == null) continue;
                sourceLists.Add(list);
                sourceModels.AddRange(await AttachAll(list));
            }

            Log.Write($"  withdrawal: attached {sourceModels.Count} streamed inventory model(s)"
                      + $" from {attachedOwners.Count} reachable owner(s)");
            InventoryModel? destinationModel = null;
            foreach (var model in destinationModels)
                if (ReferenceEquals(GetInventory(client, model), destination)) destinationModel = model;
            if (destinationModel == null) throw new InvalidOperationException("Verified player inventory model unavailable");

            var transferred = new List<ItemAmount>();
            var anyMoved = false;
            foreach (var want in required)
            {
                if (want.Item == null) continue;
                FixedPoint taken = 0;
                foreach (var sourceModel in sourceModels)
                {
                    var source = GetInventory(client, sourceModel);
                    if (source == null || !ContainsReference(sources, source) || ReferenceEquals(source, destination)) continue;
                    // Snapshot the model list: a successful request updates/removes its stack.
                    var stacks = new List<InventoryItemModel>(sourceModel.Items);
                    foreach (var stack in stacks)
                    {
                        // Re-measure after awaited requests and concurrent terminal inventory edits.
                        var outstanding = want.Amount - destination.CountItem(want.Item);
                        if (outstanding <= 0) break;
                        if (!Equals(stack.Definition, want.Item)) continue;
                        if (!ReferenceEquals(ReplicationAccess.Resolve(client, PlayerAccess.GetClientCharacter(client)), character) ||
                            !TransferAccess.CanUse(client, server, character, target))
                            throw new InvalidOperationException("Controlled character or interaction range changed");
                        if (!TransferAccess.CanAccessInventoryOwner(client, server, source.Entity)) break;
                        var (result, moved, _) = await api.TransferAsync(stack, destinationModel, info, outstanding, null);
                        Log.Write($"  withdrawal: request result={result} moved={moved} item='{want.Item.DisplayName}' source='{source.Entity}' destination='{character}'");
                        if (moved > 0) { taken += moved; anyMoved = true; }
                    }
                }
                if (taken > 0) transferred.Add(new ItemAmount(want.Item, taken));
            }
            var remaining = InventoryShortfall.Find(destination, required);
            return new WithdrawalResult(anyMoved ? ComponentWithdrawal.Classify(remaining.Count, true) :
                remaining.Count == 0 ? WithdrawalOutcome.AlreadySatisfied : WithdrawalOutcome.Nothing, transferred, remaining);
        }
        finally
        {
            foreach (var (model, list) in attached)
                try { api.Detach(model, list); } catch (Exception ex) { Log.Error("detaching inventory model failed", ex); }
            foreach (var list in sourceLists)
                try { api.Detach(list); }
                catch (Exception ex) { Log.Error("detaching source list failed", ex); }
            try { if (playerList != null) api.Detach(playerList); }
            catch (Exception ex) { Log.Error("detaching player list failed", ex); }
        }

        async Task<List<InventoryModel>> AttachAll(InventoryListModelStreamed list)
        {
            var models = new List<InventoryModel>();
            var ids = new List<Keen.VRage.Multiplayer.SecureIds.EntityId>(list.Entities);
            if (!ids.Contains(list.MainEntity)) ids.Insert(0, list.MainEntity);
            foreach (var id in ids)
            {
                var model = await api.Attach(id, info);
                if (model == null) continue;
                attached.Add((model, list));
                models.AddRange(model.Inventories);
            }
            return models;
        }
    }

    private static InventoryComponent? GetInventory(Session client, InventoryModel model)
    {
        var remote = ReplicationAccess.Resolve(client, model);
        // InventoryModelImpl.Component is a public readonly field on a private engine type.
        return remote?.GetType().GetField("Component", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(remote) as InventoryComponent;
    }

    private static bool ContainsReference(IReadOnlyList<InventoryComponent> sources, InventoryComponent inventory)
    {
        foreach (var source in sources) if (ReferenceEquals(source, inventory)) return true;
        return false;
    }
}
