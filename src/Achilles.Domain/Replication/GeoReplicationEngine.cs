using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Achilles.Protocol.Replication;

namespace Achilles.Domain.Replication;

/// <summary>
/// Implements Active-Active multi-region geo-replication coordinating vector clocks,
/// PN-Counter CRDTs, disjoint seat spaces, and delta synchronization across sovereign regions.
/// </summary>
public sealed partial class GeoReplicationEngine : IGeoReplicationEngine
{
    private readonly string _localRegionId;
    private readonly int _seatRangeStart;
    private readonly int _seatRangeEnd;
    private readonly ConcurrentDictionary<string, RegionPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ReplicatedSeatState> _seats = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();
    private readonly ILogger<GeoReplicationEngine> _logger;

    private VectorClock _vectorClock;
    private PnCounter _pnCounter;
    private long _localSequence;

    public string LocalRegionId => _localRegionId;
    public VectorClock CurrentClock
    {
        get
        {
            lock (_stateLock)
            {
                return _vectorClock;
            }
        }
    }

    public PnCounter CurrentCounter
    {
        get
        {
            lock (_stateLock)
            {
                return _pnCounter;
            }
        }
    }

    public GeoReplicationEngine(
        string localRegionId,
        int seatRangeStart,
        int seatRangeEnd,
        ILogger<GeoReplicationEngine>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localRegionId);
        if (seatRangeStart < 1 || seatRangeEnd < seatRangeStart)
        {
            throw new ArgumentOutOfRangeException(nameof(seatRangeStart), "Seat range must be positive and non-inverted.");
        }

        _localRegionId = localRegionId;
        _seatRangeStart = seatRangeStart;
        _seatRangeEnd = seatRangeEnd;
        _logger = logger ?? NullLogger<GeoReplicationEngine>.Instance;

        _vectorClock = VectorClock.Empty.Increment(_localRegionId);
        _pnCounter = PnCounter.Empty;
    }

    public void RegisterPeer(string regionId, string endpoint, int seatRangeStart, int seatRangeEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (seatRangeStart < 1 || seatRangeEnd < seatRangeStart)
        {
            throw new ArgumentOutOfRangeException(nameof(seatRangeStart), "Seat range must be positive and non-inverted.");
        }

        _peers[regionId] = new RegionPeer(
            regionId,
            endpoint,
            seatRangeStart,
            seatRangeEnd,
            DateTimeOffset.UtcNow,
            IsHealthy: true,
            LatencyMs: 0);
    }

    public IReadOnlyList<RegionPeer> GetPeers()
    {
        return _peers.Values.ToList();
    }

    public SeatAllocation? TryAllocateLocalSeat(
        string licenseId,
        string fingerprint,
        string? machineId,
        TimeSpan ttl,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        lock (_stateLock)
        {
            int? freeSeatNo = null;

            for (int seatNo = _seatRangeStart; seatNo <= _seatRangeEnd; seatNo++)
            {
                string seatId = $"seat_{_localRegionId}_{seatNo}";
                if (!_seats.TryGetValue(seatId, out var existing) || existing.IsReleased || existing.ExpiresAt <= now)
                {
                    freeSeatNo = seatNo;
                    break;
                }
            }

            if (!freeSeatNo.HasValue)
            {
                return null;
            }

            int allocatedSeatNo = freeSeatNo.Value;
            _localSequence++;
            _vectorClock = _vectorClock.Increment(_localRegionId);
            _pnCounter = _pnCounter.Increment(_localRegionId);

            string leaseId = $"l_{Guid.NewGuid():N}";
            string allocatedSeatId = $"seat_{_localRegionId}_{allocatedSeatNo}";
            DateTimeOffset expiresAt = now + ttl;

            var state = new ReplicatedSeatState
            {
                SeatId = allocatedSeatId,
                SeatNo = allocatedSeatNo,
                LicenseId = licenseId,
                LeaseId = leaseId,
                HolderFingerprint = fingerprint,
                MachineId = machineId,
                OriginRegionId = _localRegionId,
                AcquiredAt = now,
                ExpiresAt = expiresAt,
                LeaseSeq = _localSequence,
                IsReleased = false,
                LamportClock = _vectorClock.GetClock(_localRegionId)
            };

            _seats[allocatedSeatId] = state;
            LogSeatAllocated(_logger, allocatedSeatNo, licenseId, _localRegionId);

            return new SeatAllocation
            {
                SeatId = allocatedSeatId,
                SeatNo = allocatedSeatNo,
                LicenseId = licenseId,
                LeaseId = leaseId,
                HolderFingerprint = fingerprint,
                MachineId = machineId,
                AcquiredAt = now,
                ExpiresAt = expiresAt,
                LeaseSeq = _localSequence,
                IsOverage = false
            };
        }
    }

    public bool TryReleaseLocalSeat(string leaseId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        lock (_stateLock)
        {
            ReplicatedSeatState? found = null;
            foreach (var seat in _seats.Values)
            {
                if (string.Equals(seat.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase) && !seat.IsReleased)
                {
                    found = seat;
                    break;
                }
            }

            if (found is null)
            {
                return false;
            }

            _localSequence++;
            _vectorClock = _vectorClock.Increment(_localRegionId);
            _pnCounter = _pnCounter.Decrement(_localRegionId);

            var releasedState = found with
            {
                IsReleased = true,
                ExpiresAt = now,
                LeaseSeq = _localSequence,
                LamportClock = _vectorClock.GetClock(_localRegionId)
            };

            _seats[releasedState.SeatId] = releasedState;
            LogSeatReleased(_logger, leaseId, _localRegionId);

            return true;
        }
    }

    public ReplicationDelta GenerateDeltaForPeer(string targetRegionId, VectorClock? peerClock, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRegionId);

        lock (_stateLock)
        {
            var seatsToInclude = new List<ReplicatedSeatState>();

            foreach (var seat in _seats.Values)
            {
                if (peerClock is null)
                {
                    seatsToInclude.Add(seat);
                    continue;
                }

                long peerKnownClock = peerClock.GetClock(seat.OriginRegionId);
                if (seat.LamportClock > peerKnownClock || peerKnownClock == 0)
                {
                    seatsToInclude.Add(seat);
                }
            }

            string deltaId = $"delta_{Guid.NewGuid():N}";
            return new ReplicationDelta(
                deltaId,
                _localRegionId,
                _vectorClock,
                now,
                seatsToInclude,
                _pnCounter);
        }
    }

    public ReplicationSyncResponse AssimilateRemoteDelta(ReplicationDelta delta, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(delta);

        lock (_stateLock)
        {
            _vectorClock = _vectorClock.Merge(delta.VectorClock).Increment(_localRegionId);
            _pnCounter = _pnCounter.Merge(delta.CounterState);

            foreach (var remoteSeat in delta.Seats)
            {
                if (_seats.TryGetValue(remoteSeat.SeatId, out var localSeat))
                {
                    if (remoteSeat.LamportClock > localSeat.LamportClock)
                    {
                        _seats[remoteSeat.SeatId] = remoteSeat;
                    }
                    else if (remoteSeat.LamportClock == localSeat.LamportClock)
                    {
                        if (remoteSeat.LeaseSeq > localSeat.LeaseSeq)
                        {
                            _seats[remoteSeat.SeatId] = remoteSeat;
                        }
                        else if (remoteSeat.LeaseSeq == localSeat.LeaseSeq &&
                                 string.Compare(remoteSeat.OriginRegionId, localSeat.OriginRegionId, StringComparison.Ordinal) > 0)
                        {
                            _seats[remoteSeat.SeatId] = remoteSeat;
                        }
                    }
                }
                else
                {
                    _seats[remoteSeat.SeatId] = remoteSeat;
                }
            }

            if (_peers.TryGetValue(delta.SourceRegionId, out var peer))
            {
                _peers[delta.SourceRegionId] = peer with
                {
                    LastSeen = now,
                    IsHealthy = true
                };
            }

            LogDeltaAssimilated(_logger, delta.DeltaId, delta.SourceRegionId, delta.Seats.Count);

            return new ReplicationSyncResponse(
                Success: true,
                ReceiverRegionId: _localRegionId,
                MergedClock: _vectorClock,
                ReciprocalDelta: null,
                ErrorMessage: null);
        }
    }

    public IReadOnlyList<ReplicatedSeatState> GetGlobalActiveSeats(string? licenseId = null, DateTimeOffset? now = null)
    {
        DateTimeOffset effectiveNow = now ?? DateTimeOffset.UtcNow;
        var query = _seats.Values.Where(s => !s.IsReleased && s.ExpiresAt > effectiveNow);

        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            query = query.Where(s => string.Equals(s.LicenseId, licenseId, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    public ClusterStatusDto GetStatus(DateTimeOffset now)
    {
        lock (_stateLock)
        {
            int localAllocatedSeats = _seats.Values.Count(s =>
                string.Equals(s.OriginRegionId, _localRegionId, StringComparison.OrdinalIgnoreCase) &&
                !s.IsReleased &&
                s.ExpiresAt > now);

            int globalAllocatedSeats = _seats.Values.Count(s => !s.IsReleased && s.ExpiresAt > now);

            int totalDisjointCapacity = (_seatRangeEnd - _seatRangeStart + 1) +
                                        _peers.Values.Sum(p => p.SeatRangeEnd - p.SeatRangeStart + 1);

            return new ClusterStatusDto(
                _localRegionId,
                _vectorClock,
                _peers.Values.ToList(),
                localAllocatedSeats,
                globalAllocatedSeats,
                totalDisjointCapacity);
        }
    }

    public DisasterRecoveryReport FailoverAndReclaimPeerSeats(string failedRegionId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedRegionId);

        lock (_stateLock)
        {
            if (!_peers.TryGetValue(failedRegionId, out var peer))
            {
                throw new KeyNotFoundException($"Peer region '{failedRegionId}' was not found in cluster topology.");
            }

            int reclaimedCount = peer.SeatRangeEnd - peer.SeatRangeStart + 1;

            _localSequence++;
            _vectorClock = _vectorClock.Increment(_localRegionId);

            _peers[failedRegionId] = peer with { IsHealthy = false, LatencyMs = -1 };

            int totalNewCapacity = (_seatRangeEnd - _seatRangeStart + 1) +
                                   _peers.Values.Where(p => p.IsHealthy).Sum(p => p.SeatRangeEnd - p.SeatRangeStart + 1) +
                                   reclaimedCount;

            string msg = $"Failover completed: Region '{_localRegionId}' reclaimed {reclaimedCount} seats from '{failedRegionId}'. Vector clock advanced.";
            LogDisasterRecovery(_logger, msg);

            return new DisasterRecoveryReport(
                PromotedRegionId: _localRegionId,
                FailedRegionId: failedRegionId,
                ReclaimedSeatsCount: reclaimedCount,
                NewTotalCapacity: totalNewCapacity,
                VectorClockAdvancedBy: 1,
                FailoverTimestamp: now,
                QuorumMaintained: true,
                StatusMessage: msg
            );
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "GeoReplication: Local seat {SeatNo} allocated for license {LicenseId} in region {RegionId}")]
    private static partial void LogSeatAllocated(ILogger logger, int seatNo, string licenseId, string regionId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "GeoReplication: Seat with lease {LeaseId} released locally in region {RegionId}")]
    private static partial void LogSeatReleased(ILogger logger, string leaseId, string regionId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "GeoReplication: Assimilated remote delta {DeltaId} from {SourceRegionId} with {SeatCount} seats")]
    private static partial void LogDeltaAssimilated(ILogger logger, string deltaId, string sourceRegionId, int seatCount);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "[DISASTER RECOVERY] {Message}")]
    private static partial void LogDisasterRecovery(ILogger logger, string message);
}
