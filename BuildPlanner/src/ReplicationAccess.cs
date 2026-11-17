using System;
using System.Collections.Generic;
using System.Reflection;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Multiplayer.SecureIds;

namespace BuildPlanner;

internal static class ReplicationAccess
{
    // Verified in VRage.Multiplayer.dll: NotifyReplicationStarted adds localInstances[i]
    // -> remoteInstances[i] using ReferenceEqualityComparer. No name or spatial fallback.
    private static readonly Type? TransportType = typeof(EntityId).Assembly.GetType(
        "Keen.VRage.Multiplayer.Transport.InProcess.InProcessClientPeerTransportSessionComponent");
    private static readonly FieldInfo? Pairings = TransportType?
        .GetField("_clientToServerMapping", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static object? Resolve(Session? client, object? local)
    {
        var transport = client == null || TransportType == null ? null : GameEntityExtensions.TryGet(client, TransportType);
        var pairs = transport == null ? null : Pairings?.GetValue(transport) as Dictionary<object, object>;
        return FindExactPair(pairs, local);
    }

    /// <summary>
    /// Resolve an authoritative server object back to its exact replicated client instance.
    /// The transport only stores client-to-server pairs, so this is intentionally a reference
    /// scan rather than an ID/name guess.
    /// </summary>
    internal static object? ResolveLocal(Session? client, object? remote)
    {
        var transport = client == null || TransportType == null ? null : GameEntityExtensions.TryGet(client, TransportType);
        var pairs = transport == null ? null : Pairings?.GetValue(transport) as Dictionary<object, object>;
        return FindExactLocal(pairs, remote);
    }

    internal static object? FindExactLocal(IReadOnlyDictionary<object, object>? pairs, object? remote)
    {
        if (pairs == null || remote == null) return null;
        foreach (var pair in pairs)
            if (ReferenceEquals(pair.Value, remote)) return pair.Key;
        return null;
    }

    internal static object? FindExactPair(IReadOnlyDictionary<object, object>? pairs, object? local)
    {
        if (pairs == null || local == null) return null;
        if (pairs is Dictionary<object, object> dictionary &&
            ReferenceEquals(dictionary.Comparer, ReferenceEqualityComparer.Instance))
            return dictionary.TryGetValue(local, out var remote) ? remote : null;
        // Require reference semantics even if a caller supplies a dictionary with value equality.
        foreach (var pair in pairs)
            if (ReferenceEquals(pair.Key, local)) return pair.Value;
        return null;
    }

    internal static Entity? ResolveEntity(Session? client, Session? server, Entity? local, string role)
    {
        try
        {
            var remote = Resolve(client, local) as Entity;
            if (remote == null || server == null || ReferenceEquals(remote, local) ||
                !ReferenceEquals(remote.Scene?.UserObject, server))
            {
                Log.Write($"  {role}: exact replication pairing unavailable or wrong session; local='{local}'; refusing operation");
                return null;
            }
            Log.Write($"  {role}: exact replication pair client='{local}' server='{remote}'");
            return remote;
        }
        catch (Exception ex)
        {
            Log.Error($"{role}: replication pairing failed; refusing operation", ex);
            return null;
        }
    }
}
