using System;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Simulation.GameSystems.Interaction;
using Keen.Game2.Simulation.GameSystems.Permissions;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Definitions;

namespace BuildPlanner;

internal static class TransferAccess
{
    internal static bool CanUse(Session client, Session server, Entity character, Entity target)
    {
        var distance = DefinitionManager.Instance.GetConfiguration<InteractionConfiguration>().MaxInteractionDistance;
        if (InteractionBoundsEstimator.IsFartherThanInteractionDistance(character, target, distance))
        {
            Log.Write("  transfer: target outside engine interaction distance; refused");
            return false;
        }
        return CanAccessInventoryOwner(client, server, target);
    }

    internal static bool CanAccessInventoryOwner(Session client, Session server, Entity owner)
    {
        var block = owner.TryGet<CubeBlockComponent>();
        var permissions = server.SessionComponents?.TryGet<PermissionsSessionComponent>();
        var players = client.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
        var action = DefinitionManager.Instance.GetConfiguration<PermissionsConfiguration>().BasicUse;
        if (block?.Grid == null || permissions == null || players == null ||
            owner.TryGet<BlockPermissionsComponent>() == null ||
            !permissions.CanUseBlockAction(owner, block.Grid.Entity, players.LocalPlayerIdentity, action))
        {
            Log.Write($"  transfer: inventory access denied or unverifiable for '{owner}'");
            return false;
        }
        return true;
    }
}
