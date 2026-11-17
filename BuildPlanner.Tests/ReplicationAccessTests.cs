using System.Collections.Generic;
using Xunit;

namespace BuildPlanner.Tests;

public class ReplicationAccessTests
{
    private sealed record NamedEntity(string Name);

    [Fact]
    public void IdenticalNamesResolveByReferenceToTheirOwnPairs()
    {
        var a = new NamedEntity("CargoContainer");
        var b = new NamedEntity("CargoContainer");
        var serverA = new object();
        var serverB = new object();
        var pairs = new Dictionary<object, object>(ReferenceEqualityComparer.Instance)
            { [a] = serverA, [b] = serverB };
        Assert.Same(serverA, ReplicationAccess.FindExactPair(pairs, a));
        Assert.Same(serverB, ReplicationAccess.FindExactPair(pairs, b));
        Assert.Null(ReplicationAccess.FindExactPair(pairs, new NamedEntity("CargoContainer")));
    }

    [Fact]
    public void ValueEqualityCannotSelectAnUnpairedObject()
    {
        var pairs = new Dictionary<object, object> { [new NamedEntity("Character")] = new object() };
        Assert.Null(ReplicationAccess.FindExactPair(pairs, new NamedEntity("Character")));
    }

    [Fact]
    public void MissingPairingsFailClosed()
    {
        Assert.Null(ReplicationAccess.FindExactPair(null, new object()));
        Assert.Null(ReplicationAccess.FindExactPair(new Dictionary<object, object>(), new object()));
        Assert.Null(ReplicationAccess.FindExactPair(new Dictionary<object, object>(), null));
    }

    [Fact]
    public void ReverseLookupUsesExactServerReference()
    {
        var local = new object();
        var server = new NamedEntity("CargoContainer");
        var pairs = new Dictionary<object, object> { [local] = server };

        Assert.Same(local, ReplicationAccess.FindExactLocal(pairs, server));
        Assert.Null(ReplicationAccess.FindExactLocal(pairs, new NamedEntity("CargoContainer")));
    }
}
