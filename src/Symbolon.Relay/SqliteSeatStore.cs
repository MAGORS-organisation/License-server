using System.Globalization;
using Microsoft.Data.Sqlite;
using Symbolon.Domain;

namespace Symbolon.Relay;

/// <summary>
/// SQLite implementation of ISeatStore for Symbolon.Relay using BEGIN IMMEDIATE transactions.
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

    public void Dispose()
    {
        _connection.Dispose();
        _lock.Dispose();
    }
}
