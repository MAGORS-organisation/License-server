using System.IO.Pipelines;
using Achilles.Protocol.Streaming;
using Xunit;

namespace Achilles.Protocol.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task Encode_And_Decode_Frame_Matches_RoundTrip()
    {
        var ping = LeaseStreamFrame.CreatePing("lic_123", 42, "mch_test", "fp_hash_1", new StreamHeartbeatPayload(15, 1024 * 1024));
        byte[] encoded = LeaseStreamFraming.EncodeFrame(ping);

        Assert.NotEmpty(encoded);
        Assert.True(encoded.Length > 9);

        using var ms = new MemoryStream(encoded);
        var decoded = await LeaseStreamFraming.ReadFrameAsync(ms);

        Assert.NotNull(decoded);
        Assert.Equal(StreamFrameType.HeartbeatPing, decoded.Type);
        Assert.Equal("lic_123", decoded.LeaseId);
        Assert.Equal(42, decoded.SequenceNumber);
        Assert.Equal("mch_test", decoded.MachineId);
    }

    [Fact]
    public async Task LeaseStreamClient_Sends_Heartbeat_And_Updates_ValidUntil()
    {
        // Set up in-memory duplex pipe to simulate client-server network stream
        var clientToServer = new MemoryStream();
        var serverToClient = new MemoryStream();

        // Combined bidirectional stream mock
        var clientStream = new DuplexPipeStream(serverToClient, clientToServer);
        var serverStream = new DuplexPipeStream(clientToServer, serverToClient);

        var initialValidUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        var client = new LeaseStreamClient("lease_abc", "mch_1", "fp_1", initialValidUntil);
        await client.ConnectAsync(clientStream);

        // Background server simulation
        var serverTask = Task.Run(async () =>
        {
            var reqFrame = await LeaseStreamFraming.ReadFrameAsync(serverStream);
            Assert.NotNull(reqFrame);
            Assert.Equal(StreamFrameType.HeartbeatPing, reqFrame.Type);
            Assert.Equal(1, reqFrame.SequenceNumber);

            var newValidUntil = DateTimeOffset.UtcNow.AddMinutes(15);
            var ack = LeaseStreamFrame.CreateAck(reqFrame.LeaseId, reqFrame.SequenceNumber, newValidUntil);
            byte[] encodedAck = LeaseStreamFraming.EncodeFrame(ack);
            await serverStream.WriteAsync(encodedAck);
            await serverStream.FlushAsync();
        });

        // Client sends heartbeat
        var response = await client.SendHeartbeatAsync(cpuPercent: 20, memoryBytes: 50_000_000);
        await serverTask;

        Assert.NotNull(response);
        Assert.Equal(StreamFrameType.HeartbeatAck, response.Type);
        Assert.True(client.ValidUntil > initialValidUntil);

        await client.DisposeAsync();
    }

    private sealed class DuplexPipeStream : Stream
    {
        private readonly MemoryStream _readStream;
        private readonly MemoryStream _writeStream;

        public DuplexPipeStream(MemoryStream readStream, MemoryStream writeStream)
        {
            _readStream = readStream;
            _writeStream = writeStream;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Spin-wait until bytes become available in read stream
            while (_readStream.Position >= _readStream.Length)
            {
                Thread.Sleep(5);
            }
            return _readStream.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_readStream.Position >= _readStream.Length)
            {
                await Task.Delay(5, cancellationToken);
            }
            return await _readStream.ReadAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            long oldPos = _writeStream.Position;
            _writeStream.Seek(0, SeekOrigin.End);
            _writeStream.Write(buffer, offset, count);
            _writeStream.Seek(oldPos, SeekOrigin.Begin);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            long oldPos = _writeStream.Position;
            _writeStream.Seek(0, SeekOrigin.End);
            await _writeStream.WriteAsync(buffer, cancellationToken);
            _writeStream.Seek(oldPos, SeekOrigin.Begin);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
