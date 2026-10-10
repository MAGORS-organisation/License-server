using System.Globalization;
using Microsoft.Data.Sqlite;
using Achilles.Domain;
using Achilles.Domain.Grants;
using Achilles.Format;

namespace Achilles.Relay;

/// <summary>
/// SQLite implementation of ISeatStore for Achilles.Relay using BEGIN IMMEDIATE transactions.
/// </summary>
internal sealed class SqliteSeatStore : ISeatStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteSeatStore(string connectionString = "Data Source=symbolon-relay.db")
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS seats (
                id TEXT PRIMARY KEY,
                seat_no INTEGER NOT NULL,
                license_id TEXT NOT NULL,
                lease_id TEXT,
                holder_fp TEXT,
                machine_id TEXT,
                acquired_at TEXT,
                expires_at TEXT,
                lease_seq INTEGER NOT NULL DEFAULT 0,
                is_overage INTEGER NOT NULL DEFAULT 0,
                reserved_for TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_seats_lookup 
                ON seats (license_id, lease_id, expires_at, is_overage);

            CREATE TABLE IF NOT EXISTS idempotency_records (
                license_id TEXT NOT NULL,
                idempotency_key TEXT NOT NULL,
                lease_id TEXT NOT NULL,
                seat_no INTEGER NOT NULL,
                expires_at TEXT NOT NULL,
                PRIMARY KEY (license_id, idempotency_key)
            );

            CREATE TABLE IF NOT EXISTS seat_grants (
                id TEXT PRIMARY KEY,
                license_id TEXT NOT NULL,
                relay_id TEXT NOT NULL,
                seats INTEGER NOT NULL,
                seat_from INTEGER NOT NULL,
                seat_to INTEGER NOT NULL,
                seq INTEGER NOT NULL,
                supersedes INTEGER,
                not_before TEXT NOT NULL,
                not_after TEXT NOT NULL,
                revoked_at TEXT,
                raw_document TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS relay_sequences (
                license_id TEXT PRIMARY KEY,
                last_seq INTEGER NOT NULL
            );
        """;
        cmd.ExecuteNonQuery();

        try
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE seats ADD COLUMN reserved_for TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists
        }

        try
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE seats ADD COLUMN borrowed_until TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists
        }

        try
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE seats ADD COLUMN possession_key TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists
        }
    }

    /// <summary>
    /// Seeds material seat rows for a license into the SQLite store.
    /// </summary>
    public async Task SeedSeatsAsync(string licenseId, int seatCount, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                for (int i = 0; i < seatCount; i++)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = (SqliteTransaction)tx;
                    cmd.CommandText = """
                        INSERT OR IGNORE INTO seats (id, seat_no, license_id, expires_at, lease_seq)
                        VALUES (@id, @seatNo, @licenseId, @expiresAt, 0);
                    """;
                    cmd.Parameters.AddWithValue("@id", $"{licenseId}_seat_{i}");
                    cmd.Parameters.AddWithValue("@seatNo", i);
                    cmd.Parameters.AddWithValue("@licenseId", licenseId);
                    cmd.Parameters.AddWithValue("@expiresAt", DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture));
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SeatAllocation?> TryAcquireOneAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        DateTimeOffset now,
        TimeSpan ttl,
        string? reservationTarget = null,
        CancellationToken ct = default)
    {
        var many = await TryAcquireManyAsync(licenseId, fingerprint, machineId, 1, now, ttl, reservationTarget, ct).ConfigureAwait(false);
        return many is { Length: > 0 } ? many[0] : null;
    }

    public async Task<SeatAllocation[]?> TryAcquireManyAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        int quantity,
        DateTimeOffset now,
        TimeSpan ttl,
        string? reservationTarget = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                string nowIso = now.ToString("O", CultureInfo.InvariantCulture);

                // Select N free or expired seats, respecting reservationTarget
                var availableSeatIds = new List<(string Id, int SeatNo)>();
                using (var selectCmd = _connection.CreateCommand())
                {
                    selectCmd.Transaction = (SqliteTransaction)tx;
                    if (!string.IsNullOrWhiteSpace(reservationTarget))
                    {
                        selectCmd.CommandText = """
                            SELECT id, seat_no FROM seats
                            WHERE license_id = @licenseId
                              AND (lease_id IS NULL OR expires_at < @now)
                              AND is_overage = 0
                              AND (reserved_for = @resTarget OR reserved_for IS NULL)
                              AND (
                                  NOT EXISTS (SELECT 1 FROM seat_grants g0 WHERE g0.license_id = seats.license_id)
                                  OR EXISTS (
                                      SELECT 1 FROM seat_grants g
                                      WHERE g.license_id = seats.license_id
                                        AND seats.seat_no >= g.seat_from
                                        AND seats.seat_no <= g.seat_to
                                        AND g.revoked_at IS NULL
                                        AND g.not_before <= @now
                                        AND g.not_after >= @now
                                  )
                              )
                            ORDER BY (CASE WHEN reserved_for = @resTarget THEN 0 ELSE 1 END), seat_no
                            LIMIT @qty;
                        """;
                        selectCmd.Parameters.AddWithValue("@resTarget", reservationTarget);
                    }
                    else
                    {
                        selectCmd.CommandText = """
                            SELECT id, seat_no FROM seats
                            WHERE license_id = @licenseId
                              AND (lease_id IS NULL OR expires_at < @now)
                              AND is_overage = 0
                              AND reserved_for IS NULL
                              AND (
                                  NOT EXISTS (SELECT 1 FROM seat_grants g0 WHERE g0.license_id = seats.license_id)
                                  OR EXISTS (
                                      SELECT 1 FROM seat_grants g
                                      WHERE g.license_id = seats.license_id
                                        AND seats.seat_no >= g.seat_from
                                        AND seats.seat_no <= g.seat_to
                                        AND g.revoked_at IS NULL
                                        AND g.not_before <= @now
                                        AND g.not_after >= @now
                                  )
                              )
                            ORDER BY seat_no
                            LIMIT @qty;
                        """;
                    }
                    selectCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    selectCmd.Parameters.AddWithValue("@now", nowIso);
                    selectCmd.Parameters.AddWithValue("@qty", quantity);

                    using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        availableSeatIds.Add((reader.GetString(0), reader.GetInt32(1)));
                    }
                }

                if (availableSeatIds.Count < quantity)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    return null;
                }

                string leaseId = $"lse_{Guid.NewGuid():N}";
                var expiresAt = now + ttl;
                string expiresAtIso = expiresAt.ToString("O", CultureInfo.InvariantCulture);

                var allocations = new List<SeatAllocation>(quantity);
                foreach (var (seatId, seatNo) in availableSeatIds)
                {
                    using var updateCmd = _connection.CreateCommand();
                    updateCmd.Transaction = (SqliteTransaction)tx;
                    updateCmd.CommandText = """
                        UPDATE seats
                        SET lease_id = @leaseId,
                            holder_fp = @fingerprint,
                            machine_id = @machineId,
                            acquired_at = @now,
                            expires_at = @expiresAt,
                            lease_seq = 0
                        WHERE id = @seatId;
                    """;
                    updateCmd.Parameters.AddWithValue("@leaseId", leaseId);
                    updateCmd.Parameters.AddWithValue("@fingerprint", fingerprint);
                    updateCmd.Parameters.AddWithValue("@machineId", (object?)machineId ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@now", nowIso);
                    updateCmd.Parameters.AddWithValue("@expiresAt", expiresAtIso);
                    updateCmd.Parameters.AddWithValue("@seatId", seatId);

                    await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                    allocations.Add(new SeatAllocation
                    {
                        SeatId = seatId,
                        SeatNo = seatNo,
                        LicenseId = licenseId,
                        LeaseId = leaseId,
                        HolderFingerprint = fingerprint,
                        MachineId = machineId,
                        AcquiredAt = now,
                        ExpiresAt = expiresAt,
                        LeaseSeq = 0,
                        IsOverage = false
                    });
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return allocations.ToArray();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RenewOutcome> TryRenewAsync(
        string leaseId,
        string fingerprint,
        long clientSeq,
        DateTimeOffset now,
        TimeSpan ttl,
        TimeSpan resurrectionWindow,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                using var selectCmd = _connection.CreateCommand();
                selectCmd.Transaction = (SqliteTransaction)tx;
                selectCmd.CommandText = """
                    SELECT id, seat_no, license_id, holder_fp, machine_id, acquired_at, expires_at, lease_seq, is_overage
                    FROM seats
                    WHERE lease_id = @leaseId;
                """;
                selectCmd.Parameters.AddWithValue("@leaseId", leaseId);

                string seatId;
                int seatNo;
                string licenseId;
                string currentHolder;
                string? machineId;
                DateTimeOffset acquiredAt;
                DateTimeOffset currentExpiresAt;
                long currentSeq;
                bool isOverage;

                using (var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return RenewOutcome.Unknown;
                    }

                    seatId = reader.GetString(0);
                    seatNo = reader.GetInt32(1);
                    licenseId = reader.GetString(2);
                    currentHolder = reader.GetString(3);
                    bool isDbNull = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false);
                    machineId = isDbNull ? null : reader.GetString(4);
                    acquiredAt = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
                    currentExpiresAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture);
                    currentSeq = reader.GetInt64(7);
                    isOverage = reader.GetInt32(8) != 0;
                }

                // Verify holder fingerprint
                if (!string.Equals(currentHolder, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    return RenewOutcome.Conflict;
                }

                // Verify sequence monotonicity (replay defense)
                if (clientSeq != currentSeq)
                {
                    return RenewOutcome.SeqReplay;
                }

                // Verify expiration within resurrection window
                if (currentExpiresAt < now - resurrectionWindow)
                {
                    return RenewOutcome.Taken;
                }

                bool isResurrected = currentExpiresAt < now;
                var newExpiresAt = now + ttl;
                long newSeq = currentSeq + 1;

                using var updateCmd = _connection.CreateCommand();
                updateCmd.Transaction = (SqliteTransaction)tx;
                updateCmd.CommandText = """
                    UPDATE seats
                    SET expires_at = @newExpiresAt,
                        lease_seq = @newSeq
                    WHERE id = @seatId;
                """;
                updateCmd.Parameters.AddWithValue("@newExpiresAt", newExpiresAt.ToString("O", CultureInfo.InvariantCulture));
                updateCmd.Parameters.AddWithValue("@newSeq", newSeq);
                updateCmd.Parameters.AddWithValue("@seatId", seatId);

                await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);

                var allocation = new SeatAllocation
                {
                    SeatId = seatId,
                    SeatNo = seatNo,
                    LicenseId = licenseId,
                    LeaseId = leaseId,
                    HolderFingerprint = currentHolder,
                    MachineId = machineId,
                    AcquiredAt = acquiredAt,
                    ExpiresAt = newExpiresAt,
                    LeaseSeq = newSeq,
                    IsOverage = isOverage
                };

                return isResurrected ? RenewOutcome.Resurrected(allocation) : RenewOutcome.Renewed(allocation);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> TryReleaseAsync(string leaseId, DateTimeOffset now, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE seats
                SET lease_id = NULL,
                    holder_fp = NULL,
                    machine_id = NULL,
                    expires_at = @now
                WHERE lease_id = @leaseId
                  AND (borrowed_until IS NULL OR borrowed_until < @now);
            """;
            cmd.Parameters.AddWithValue("@now", now.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@leaseId", leaseId);

            int rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> TryBorrowSeatAsync(
        string leaseId,
        DateTimeOffset borrowedUntil,
        string possessionKeyJwk,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE seats
                SET borrowed_until = @borrowedUntil,
                    expires_at = @borrowedUntil,
                    possession_key = @possessionKey
                WHERE lease_id = @leaseId;
            """;
            cmd.Parameters.AddWithValue("@borrowedUntil", borrowedUntil.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@possessionKey", possessionKeyJwk);
            cmd.Parameters.AddWithValue("@leaseId", leaseId);

            int rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> TryReturnBorrowedSeatAsync(
        string leaseId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE seats
                SET lease_id = NULL,
                    holder_fp = NULL,
                    machine_id = NULL,
                    expires_at = @now,
                    borrowed_until = NULL,
                    possession_key = NULL
                WHERE lease_id = @leaseId;
            """;
            cmd.Parameters.AddWithValue("@now", now.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@leaseId", leaseId);

            int rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetActiveBorrowedCountAsync(
        string licenseId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM seats
                WHERE license_id = @licenseId
                  AND borrowed_until IS NOT NULL
                  AND borrowed_until > @now;
            """;
            cmd.Parameters.AddWithValue("@licenseId", licenseId);
            cmd.Parameters.AddWithValue("@now", now.ToString("O", CultureInfo.InvariantCulture));

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetAllocatedSeatCountAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM seats WHERE lease_id IS NOT NULL AND expires_at > @now;";
            cmd.Parameters.AddWithValue("@now", now.ToString("O", CultureInfo.InvariantCulture));
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetTotalSeatCountAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM seats;";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SeatAllocation[]?> TryGetIdempotentAsync(
        string licenseId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT lease_id, expires_at
                FROM idempotency_records
                WHERE license_id = @licenseId
                  AND idempotency_key = @key;
            """;
            cmd.Parameters.AddWithValue("@licenseId", licenseId);
            cmd.Parameters.AddWithValue("@key", idempotencyKey);

            string? leaseId = null;
            DateTimeOffset expiresAt = default;

            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    leaseId = reader.GetString(0);
                    expiresAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
                }
            }

            if (leaseId is null || expiresAt < now)
            {
                return null;
            }

            // Retrieve the active allocations for this lease_id
            using var seatsCmd = _connection.CreateCommand();
            seatsCmd.CommandText = """
                SELECT id, seat_no, license_id, lease_id, holder_fp, machine_id, acquired_at, expires_at, lease_seq, is_overage
                FROM seats
                WHERE lease_id = @leaseId;
            """;
            seatsCmd.Parameters.AddWithValue("@leaseId", leaseId);

            var list = new List<SeatAllocation>();
            using (var reader = await seatsCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    bool isDbNull = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false);
                    list.Add(new SeatAllocation
                    {
                        SeatId = reader.GetString(0),
                        SeatNo = reader.GetInt32(1),
                        LicenseId = reader.GetString(2),
                        LeaseId = reader.GetString(3),
                        HolderFingerprint = reader.GetString(4),
                        MachineId = isDbNull ? null : reader.GetString(5),
                        AcquiredAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                        ExpiresAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                        LeaseSeq = reader.GetInt64(8),
                        IsOverage = reader.GetInt32(9) != 0
                    });
                }
            }

            return list.Count > 0 ? list.ToArray() : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveIdempotentAsync(
        string licenseId,
        string idempotencyKey,
        SeatAllocation[] allocations,
        DateTimeOffset now,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        if (allocations.Length == 0) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO idempotency_records (license_id, idempotency_key, lease_id, seat_no, expires_at)
                VALUES (@licenseId, @key, @leaseId, @seatNo, @expiresAt);
            """;
            cmd.Parameters.AddWithValue("@licenseId", licenseId);
            cmd.Parameters.AddWithValue("@key", idempotencyKey);
            cmd.Parameters.AddWithValue("@leaseId", allocations[0].LeaseId ?? string.Empty);
            cmd.Parameters.AddWithValue("@seatNo", allocations[0].SeatNo);
            cmd.Parameters.AddWithValue("@expiresAt", (now + ttl).ToString("O", CultureInfo.InvariantCulture));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TimeSpan?> EstimateWaitAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT MIN(expires_at)
                FROM seats
                WHERE license_id = @licenseId
                  AND lease_id IS NOT NULL;
            """;
            cmd.Parameters.AddWithValue("@licenseId", licenseId);

            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (scalar is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, out var nextExpiry))
            {
                var diff = nextExpiry - now;
                return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SyncSeatReservationsAsync(
        string licenseId,
        IReadOnlyList<(string Target, int Count)> reservations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(licenseId);
        ArgumentNullException.ThrowIfNull(reservations);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                string nowIso = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                // Clear reserved_for on free seats (FLT-25)
                using (var clearCmd = _connection.CreateCommand())
                {
                    clearCmd.Transaction = (SqliteTransaction)tx;
                    clearCmd.CommandText = """
                        UPDATE seats
                        SET reserved_for = NULL
                        WHERE license_id = @licenseId
                          AND (lease_id IS NULL OR expires_at < @now)
                          AND reserved_for IS NOT NULL;
                    """;
                    clearCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    clearCmd.Parameters.AddWithValue("@now", nowIso);
                    await clearCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Apply reservations to free seats (FLT-23)
                foreach (var (target, count) in reservations)
                {
                    using var applyCmd = _connection.CreateCommand();
                    applyCmd.Transaction = (SqliteTransaction)tx;
                    applyCmd.CommandText = """
                        UPDATE seats
                        SET reserved_for = @target
                        WHERE id IN (
                            SELECT id FROM seats
                            WHERE license_id = @licenseId
                              AND (lease_id IS NULL OR expires_at < @now)
                              AND (reserved_for IS NULL)
                            ORDER BY seat_no
                            LIMIT @count
                        );
                    """;
                    applyCmd.Parameters.AddWithValue("@target", target);
                    applyCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    applyCmd.Parameters.AddWithValue("@now", nowIso);
                    applyCmd.Parameters.AddWithValue("@count", count);
                    await applyCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<long> GetLastSeqAsync(string licenseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT last_seq FROM relay_sequences WHERE license_id = @licenseId;";
            cmd.Parameters.AddWithValue("@licenseId", licenseId);
            var val = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return val is long seq ? seq : (val is int sInt ? sInt : 0L);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ImportSeatGrantAsync(
        SeatGrantDocumentClaims grant,
        string rawDocument,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawDocument);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string licenseId = grant.Sub;
            long seq = grant.Symgrant.Seq;

            // GNT-7: Relay MUST persist last_seq and reject grants with seq <= last_seq
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT last_seq FROM relay_sequences WHERE license_id = @licenseId;";
                checkCmd.Parameters.AddWithValue("@licenseId", licenseId);
                var val = await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                long lastSeq = val is long l ? l : (val is int i ? i : 0L);

                if (seq <= lastSeq)
                {
                    throw new InvalidOperationException(
                        $"sequence-rollback-detected: seq {seq} is not strictly greater than lastSeq {lastSeq} (GNT-7)");
                }
            }

            var tx = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                // GNT-9: If grant contains supersedes, relay MUST immediately stop using grant with specified seq
                if (grant.Symgrant.Supersedes.HasValue)
                {
                    using var revokeCmd = _connection.CreateCommand();
                    revokeCmd.Transaction = (SqliteTransaction)tx;
                    revokeCmd.CommandText = """
                        UPDATE seat_grants
                        SET revoked_at = @now
                        WHERE license_id = @licenseId
                          AND seq <= @supersedes
                          AND revoked_at IS NULL;
                    """;
                    revokeCmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    revokeCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    revokeCmd.Parameters.AddWithValue("@supersedes", grant.Symgrant.Supersedes.Value);
                    await revokeCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Insert into seat_grants
                using (var grantCmd = _connection.CreateCommand())
                {
                    grantCmd.Transaction = (SqliteTransaction)tx;
                    grantCmd.CommandText = """
                        INSERT OR REPLACE INTO seat_grants (
                            id, license_id, relay_id, seats, seat_from, seat_to, seq, supersedes,
                            not_before, not_after, revoked_at, raw_document
                        ) VALUES (
                            @id, @licenseId, @relayId, @seats, @seatFrom, @seatTo, @seq, @supersedes,
                            @notBefore, @notAfter, NULL, @rawDocument
                        );
                    """;
                    grantCmd.Parameters.AddWithValue("@id", grant.Jti);
                    grantCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    grantCmd.Parameters.AddWithValue("@relayId", grant.Aud);
                    grantCmd.Parameters.AddWithValue("@seats", grant.Symgrant.Seats);
                    grantCmd.Parameters.AddWithValue("@seatFrom", grant.Symgrant.SeatRange[0]);
                    grantCmd.Parameters.AddWithValue("@seatTo", grant.Symgrant.SeatRange[1]);
                    grantCmd.Parameters.AddWithValue("@seq", seq);
                    grantCmd.Parameters.AddWithValue("@supersedes", grant.Symgrant.Supersedes.HasValue ? grant.Symgrant.Supersedes.Value : DBNull.Value);
                    grantCmd.Parameters.AddWithValue("@notBefore", DateTimeOffset.FromUnixTimeSeconds(grant.Nbf).ToString("O", CultureInfo.InvariantCulture));
                    grantCmd.Parameters.AddWithValue("@notAfter", DateTimeOffset.FromUnixTimeSeconds(grant.Exp).ToString("O", CultureInfo.InvariantCulture));
                    grantCmd.Parameters.AddWithValue("@rawDocument", rawDocument);
                    await grantCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Update relay_sequences (GNT-7, GNT-8)
                using (var seqCmd = _connection.CreateCommand())
                {
                    seqCmd.Transaction = (SqliteTransaction)tx;
                    seqCmd.CommandText = """
                        INSERT OR REPLACE INTO relay_sequences (license_id, last_seq)
                        VALUES (@licenseId, @seq);
                    """;
                    seqCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    seqCmd.Parameters.AddWithValue("@seq", seq);
                    await seqCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Materialize seats strictly within [seatFrom, seatTo] (GNT-10)
                int seatFrom = grant.Symgrant.SeatRange[0];
                int seatTo = grant.Symgrant.SeatRange[1];

                for (int s = seatFrom; s <= seatTo; s++)
                {
                    using var seatCmd = _connection.CreateCommand();
                    seatCmd.Transaction = (SqliteTransaction)tx;
                    seatCmd.CommandText = """
                        INSERT OR IGNORE INTO seats (id, seat_no, license_id, expires_at, lease_seq)
                        VALUES (@id, @seatNo, @licenseId, @expiresAt, 0);
                    """;
                    seatCmd.Parameters.AddWithValue("@id", $"{licenseId}_seat_{s}");
                    seatCmd.Parameters.AddWithValue("@seatNo", s);
                    seatCmd.Parameters.AddWithValue("@licenseId", licenseId);
                    seatCmd.Parameters.AddWithValue("@expiresAt", DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture));
                    await seatCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SeatGrantRecord>> GetActiveGrantsAsync(
        string licenseId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<SeatGrantRecord>();
            string nowIso = now.ToString("O", CultureInfo.InvariantCulture);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, license_id, relay_id, seats, seat_from, seat_to, seq, supersedes,
                       not_before, not_after, revoked_at, raw_document
                FROM seat_grants
                WHERE license_id = @licenseId
                  AND revoked_at IS NULL
                  AND not_before <= @now
                  AND not_after >= @now
                ORDER BY seat_from;
            """;
            cmd.Parameters.AddWithValue("@licenseId", licenseId);
            cmd.Parameters.AddWithValue("@now", nowIso);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new SeatGrantRecord(
                    Id: reader.GetString(0),
                    LicenseId: reader.GetString(1),
                    RelayId: reader.GetString(2),
                    Seats: reader.GetInt32(3),
                    SeatFrom: reader.GetInt32(4),
                    SeatTo: reader.GetInt32(5),
                    Seq: reader.GetInt64(6),
                    Supersedes: await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? null : reader.GetInt64(7),
                    NotBefore: DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                    NotAfter: DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                    RevokedAt: await reader.IsDBNullAsync(10, ct).ConfigureAwait(false) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                    Document: reader.GetString(11)
                ));
            }

            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Retrieves all active, non-revoked grants across all licenses for health checks (FLT-32, §11.1).
    /// </summary>
    public async Task<IReadOnlyList<SeatGrantRecord>> GetAllActiveGrantsAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<SeatGrantRecord>();
            string nowIso = now.ToString("O", CultureInfo.InvariantCulture);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, license_id, relay_id, seats, seat_from, seat_to, seq, supersedes,
                       not_before, not_after, revoked_at, raw_document
                FROM seat_grants
                WHERE revoked_at IS NULL
                  AND not_before <= @now
                  AND not_after >= @now
                ORDER BY license_id, seat_from;
            """;
            cmd.Parameters.AddWithValue("@now", nowIso);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new SeatGrantRecord(
                    Id: reader.GetString(0),
                    LicenseId: reader.GetString(1),
                    RelayId: reader.GetString(2),
                    Seats: reader.GetInt32(3),
                    SeatFrom: reader.GetInt32(4),
                    SeatTo: reader.GetInt32(5),
                    Seq: reader.GetInt64(6),
                    Supersedes: await reader.IsDBNullAsync(7, ct).ConfigureAwait(false) ? null : reader.GetInt64(7),
                    NotBefore: DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                    NotAfter: DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                    RevokedAt: await reader.IsDBNullAsync(10, ct).ConfigureAwait(false) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                    Document: reader.GetString(11)
                ));
            }

            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        _lock.Dispose();
    }
}
