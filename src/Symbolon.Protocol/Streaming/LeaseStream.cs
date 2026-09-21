using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Symbolon.Protocol.Streaming;

public enum StreamFrameType
{
    None = 0,
    HeartbeatPing = 1,
    HeartbeatAck = 2,
    ReleaseNotice = 3,
    ReleaseAck = 4,
    LeaseRevoked = 5,
    LeaseExpired = 6,
    QuotaClaim = 7,
    QuotaAck = 8,
    Error = 255
}

public sealed record StreamHeartbeatPayload(
    int CpuPercent,
    long MemoryBytes);

public sealed record StreamReleasePayload(
    string Reason);

public sealed record StreamQuotaPayload(
    string Entitlement,
    long Units);

public sealed record LeaseStreamFrame(
    StreamFrameType Type,
    string LeaseId,
    long SequenceNumber,
    long TimestampUtc,
    string? MachineId = null,
    string? FingerprintHash = null,
    long? ValidUntilUtc = null,
    string? NewToken = null,
    string? ErrorMessage = null,
    string? PayloadJson = null)
{
    public static LeaseStreamFrame CreatePing(string leaseId, long sequence, string machineId, string fingerprint, StreamHeartbeatPayload payload)
    {
        return new LeaseStreamFrame(
            StreamFrameType.HeartbeatPing,
            leaseId,
            sequence,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            MachineId: machineId,
            FingerprintHash: fingerprint,
            PayloadJson: JsonSerializer.Serialize(payload));
    }

    public static LeaseStreamFrame CreateAck(string leaseId, long sequence, DateTimeOffset validUntil, string? newToken = null)
    {
        return new LeaseStreamFrame(
            StreamFrameType.HeartbeatAck,
            leaseId,
            sequence,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ValidUntilUtc: validUntil.ToUnixTimeSeconds(),
            NewToken: newToken);
    }

    public static LeaseStreamFrame CreateRevoked(string leaseId, string reason)
    {
        return new LeaseStreamFrame(
            StreamFrameType.LeaseRevoked,
            leaseId,
            0,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ErrorMessage: reason);
    }

    public static LeaseStreamFrame CreateRelease(string leaseId, long sequence, string reason)
    {
        return new LeaseStreamFrame(
            StreamFrameType.ReleaseNotice,
            leaseId,
            sequence,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PayloadJson: JsonSerializer.Serialize(new StreamReleasePayload(reason)));
    }
}

public static class LeaseStreamFraming
{
    private const uint FrameMagic = 0x53594D53; // 'SYMS' (Symbolon Stream)

    public static byte[] EncodeFrame(LeaseStreamFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        string json = JsonSerializer.Serialize(frame);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(json);

        // Header: 4 bytes Magic + 1 byte Type + 4 bytes Length
        byte[] buffer = new byte[9 + payloadBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0, 4), FrameMagic);
        buffer[4] = (byte)frame.Type;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(5, 4), payloadBytes.Length);

        payloadBytes.CopyTo(buffer.AsSpan(9));
        return buffer;
    }

    public static async Task<LeaseStreamFrame?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[9];
        int read = await stream.ReadAtLeastAsync(header, 9, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (read < 9)
        {
            return null;
        }

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (magic != FrameMagic)
        {
            throw new InvalidDataException($"Invalid frame magic 0x{magic:X8}, expected SYMS.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5, 4));
        if (payloadLength < 0 || payloadLength > 1024 * 1024)
        {
            throw new InvalidDataException($"Frame payload length {payloadLength} is out of allowable bounds.");
        }

        byte[] payload = new byte[payloadLength];
        int payloadRead = await stream.ReadAtLeastAsync(payload, payloadLength, throwOnEndOfStream: true, ct).ConfigureAwait(false);
        if (payloadRead < payloadLength)
        {
            throw new EndOfStreamException("Premature end of stream while reading frame payload.");
        }

        string json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<LeaseStreamFrame>(json);
    }
}

public interface ILeaseStreamClient : IAsyncDisposable
{
    bool IsConnected { get; }
    DateTimeOffset ValidUntil { get; }
    Task ConnectAsync(Stream transportStream, CancellationToken ct = default);
    Task<LeaseStreamFrame> SendHeartbeatAsync(int cpuPercent, long memoryBytes, CancellationToken ct = default);
    Task SendReleaseAsync(string reason, CancellationToken ct = default);
}

public sealed class LeaseStreamClient : ILeaseStreamClient
{
    private readonly string _leaseId;
    private readonly string _machineId;
    private readonly string _fingerprintHash;
    private Stream? _transport;
    private long _sequence;
    private DateTimeOffset _validUntil;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _transport is not null && _transport.CanRead && _transport.CanWrite;
    public DateTimeOffset ValidUntil => _validUntil;

    public LeaseStreamClient(string leaseId, string machineId, string fingerprintHash, DateTimeOffset initialValidUntil)
    {
        _leaseId = leaseId;
        _machineId = machineId;
        _fingerprintHash = fingerprintHash;
        _validUntil = initialValidUntil;
    }

    public Task ConnectAsync(Stream transportStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transportStream);
        ct.ThrowIfCancellationRequested();
        _transport = transportStream;
        return Task.CompletedTask;
    }

    public async Task<LeaseStreamFrame> SendHeartbeatAsync(int cpuPercent, long memoryBytes, CancellationToken ct = default)
    {
        if (_transport is null) throw new InvalidOperationException("Transport is not connected.");

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long seq = Interlocked.Increment(ref _sequence);
            var ping = LeaseStreamFrame.CreatePing(_leaseId, seq, _machineId, _fingerprintHash, new StreamHeartbeatPayload(cpuPercent, memoryBytes));
            byte[] encoded = LeaseStreamFraming.EncodeFrame(ping);

            await _transport.WriteAsync(encoded, ct).ConfigureAwait(false);
            await _transport.FlushAsync(ct).ConfigureAwait(false);

            var response = await LeaseStreamFraming.ReadFrameAsync(_transport, ct).ConfigureAwait(false)
                ?? throw new EndOfStreamException("Server closed streaming connection during heartbeat.");

            if (response.Type == StreamFrameType.HeartbeatAck && response.ValidUntilUtc.HasValue)
            {
                _validUntil = DateTimeOffset.FromUnixTimeSeconds(response.ValidUntilUtc.Value);
            }

            return response;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendReleaseAsync(string reason, CancellationToken ct = default)
    {
        if (_transport is null) return;

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long seq = Interlocked.Increment(ref _sequence);
            var rel = LeaseStreamFrame.CreateRelease(_leaseId, seq, reason);
            byte[] encoded = LeaseStreamFraming.EncodeFrame(rel);

            await _transport.WriteAsync(encoded, ct).ConfigureAwait(false);
            await _transport.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        _transport?.Dispose();
        return ValueTask.CompletedTask;
    }
}
