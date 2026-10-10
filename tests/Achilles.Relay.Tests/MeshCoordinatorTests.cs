using FluentAssertions;
using Achilles.Relay.Mesh;
using Xunit;

namespace Achilles.Relay.Tests;

public sealed class MeshCoordinatorTests
{
    [Fact]
    public async Task MeshCoordinator_Transfers_Seats_And_Maintains_Lamport_Monotonicity()
    {
        // Relay 1 has 10 initial seats (seat numbers 1 to 10)
        var relay1 = new RelayMeshCoordinator("node-relay-1", Enumerable.Range(1, 10));

        // Relay 2 is registered as peer
        relay1.RegisterPeer("node-relay-2", "https://relay-2.internal:8443", initialSeats: 0, freeSeats: 0);

        var peers = relay1.GetPeers();
        peers.Should().ContainSingle(p => p.NodeId == "node-relay-2");

        // Relay 2 sends transfer proposal requesting 4 seats
        var proposal = new MeshTransferProposal(
            TransactionId: "tx-mesh-001",
            SourceNodeId: "node-relay-2",
            TargetNodeId: "node-relay-1",
            RequestedQuantity: 4,
            LamportClock: 10,
            Timestamp: DateTimeOffset.UtcNow);

        var result = await relay1.ReceiveTransferRequestAsync(proposal);

        result.Success.Should().BeTrue();
        result.TransferredSeatNumbers.Should().HaveCount(4);
        result.LamportClock.Should().BeGreaterThan(10); // L' = max(L, 10) + 1

        // Disjointness check: transferred seats must be unique numbers from 1 to 10
        result.TransferredSeatNumbers.Distinct().Should().HaveCount(4);
        result.TransferredSeatNumbers.All(s => s >= 1 && s <= 10).Should().BeTrue();

        // Second duplicate proposal with same TransactionId returns idempotent cached result
        var dupResult = await relay1.ReceiveTransferRequestAsync(proposal);
        dupResult.Success.Should().BeTrue();
        dupResult.TransferredSeatNumbers.Should().BeEquivalentTo(result.TransferredSeatNumbers);
    }

    [Fact]
    public async Task MeshCoordinator_Rejects_Transfer_When_Capacity_Exceeded()
    {
        // Relay only has 2 seats
        var relay = new RelayMeshCoordinator("node-relay-edge", [1, 2]);

        var proposal = new MeshTransferProposal(
            TransactionId: "tx-excess",
            SourceNodeId: "node-relay-client",
            TargetNodeId: "node-relay-edge",
            RequestedQuantity: 5,
            LamportClock: 1,
            Timestamp: DateTimeOffset.UtcNow);

        var result = await relay.ReceiveTransferRequestAsync(proposal);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Insufficient local capacity");
        result.TransferredSeatNumbers.Should().BeEmpty();
    }
}
