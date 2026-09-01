using System.Text;
using BPGit.Server.GitHttp;

namespace BPGit.Server.Tests;

/// <summary>
/// Unit tests for the inverse-of-Pkt (write) helpers in <see cref="PktReader"/>.
/// Covers both <see cref="PktReader.ReadAsync"/> (forward-only stream) and
/// <see cref="PktReader.ReadFromBuffer"/> (in-memory byte[]) variants.
/// </summary>
public class PktReaderTests
{
    private static byte[] PktData(string payload)
    {
        // Wire-format: 4-byte big-endian hex length + payload (no trailing LF in length).
        var bytes = Encoding.UTF8.GetBytes(payload);
        var len = bytes.Length + 4;
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)len);
        var result = new byte[4 + bytes.Length];
        header.CopyTo(result, 0);
        bytes.CopyTo(result, 4);
        return result;
    }

    [Fact]
    public async Task ReadAsync_DataPacket_ReturnsData()
    {
        var bytes = PktData("hello");
        var ms = new MemoryStream(bytes);

        var pkt = await PktReader.ReadAsync(ms);

        Assert.NotNull(pkt);
        Assert.Equal(PktLineType.Data, pkt!.Type);
        Assert.Equal("hello", pkt.PayloadString);
    }

    [Fact]
    public async Task ReadAsync_FlushPacket_ReturnsFlush()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("0000"));

        var pkt = await PktReader.ReadAsync(ms);

        Assert.NotNull(pkt);
        Assert.Equal(PktLineType.Flush, pkt!.Type);
        Assert.Empty(pkt.Payload);
    }

    [Fact]
    public async Task ReadAsync_DelimPacket_ReturnsDelim()
    {
        var ms = new MemoryStream(Encoding.ASCII.GetBytes("0001"));

        var pkt = await PktReader.ReadAsync(ms);

        Assert.NotNull(pkt);
        Assert.Equal(PktLineType.Delim, pkt!.Type);
        Assert.Empty(pkt.Payload);
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsNull()
    {
        var ms = new MemoryStream();

        var pkt = await PktReader.ReadAsync(ms);

        Assert.Null(pkt);
    }

    [Fact]
    public async Task ReadAsync_TruncatedHeader_ThrowsEndOfStream()
    {
        var ms = new MemoryStream(new byte[] { 0x30, 0x30 }); // "00"

        await Assert.ThrowsAsync<EndOfStreamException>(() => PktReader.ReadAsync(ms));
    }

    [Fact]
    public async Task ReadAsync_TruncatedPayload_ThrowsEndOfStream()
    {
        // Length says 9 (= 4 length + 5 payload) but only 3 payload bytes provided.
        var header = Encoding.ASCII.GetBytes("0009");
        var payload = Encoding.UTF8.GetBytes("abc"); // 3 bytes
        var ms = new MemoryStream(header.Concat(payload).ToArray());

        await Assert.ThrowsAsync<EndOfStreamException>(() => PktReader.ReadAsync(ms));
    }

    [Fact]
    public async Task ReadAsync_InvalidLengthTwo_ThrowsInvalidData()
    {
        // Length < 4 is reserved for flush/delim; anything else is invalid.
        // Wire format: 4 bytes big-endian uint32. Value 2 is invalid.
        var ms = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x02 });

        await Assert.ThrowsAsync<InvalidDataException>(() => PktReader.ReadAsync(ms));
    }

    [Fact]
    public async Task ReadAsync_InvalidLengthThree_ThrowsInvalidData()
    {
        var ms = new MemoryStream(new byte[] { 0x00, 0x00, 0x00, 0x03 });

        await Assert.ThrowsAsync<InvalidDataException>(() => PktReader.ReadAsync(ms));
    }

    [Fact]
    public async Task ReadAsync_PayloadString_StripsTrailingLf()
    {
        // Some clients include the LF in the payload bytes despite spec saying otherwise.
        // We strip it for ergonomic string decoding.
        var bytes = PktData("hello\n");
        var ms = new MemoryStream(bytes);

        var pkt = await PktReader.ReadAsync(ms);

        Assert.NotNull(pkt);
        Assert.Equal("hello", pkt!.PayloadString);
    }

    [Fact]
    public void ReadFromBuffer_DataPacket_AdvancesOffset()
    {
        var bytes = PktData("hello")     // "0009hello" — 9 bytes total
                  .Concat(PktData("world")) // "0009world" — 9 bytes total
                  .ToArray();
        var offset = 0;

        var first = PktReader.ReadFromBuffer(bytes, ref offset);
        var second = PktReader.ReadFromBuffer(bytes, ref offset);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("hello", first!.PayloadString);
        Assert.Equal("world", second!.PayloadString);
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void ReadFromBuffer_FlushPacket_ReturnsFlush()
    {
        var bytes = Encoding.ASCII.GetBytes("0000");
        var offset = 0;

        var pkt = PktReader.ReadFromBuffer(bytes, ref offset);

        Assert.NotNull(pkt);
        Assert.Equal(PktLineType.Flush, pkt!.Type);
        Assert.Equal(4, offset);
    }

    [Fact]
    public void ReadFromBuffer_EmptyBuffer_ReturnsNull()
    {
        var bytes = Array.Empty<byte>();
        var offset = 0;

        var pkt = PktReader.ReadFromBuffer(bytes, ref offset);

        Assert.Null(pkt);
    }

    [Fact]
    public void ReadFromBuffer_TruncatedHeader_ThrowsEndOfStream()
    {
        var bytes = new byte[] { 0x30, 0x30 };
        var offset = 0;

        Assert.Throws<EndOfStreamException>(() => PktReader.ReadFromBuffer(bytes, ref offset));
    }

    [Fact]
    public void ReadFromBuffer_InvalidLengthTwo_ThrowsInvalidData()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x02 };
        var offset = 0;

        Assert.Throws<InvalidDataException>(() => PktReader.ReadFromBuffer(bytes, ref offset));
    }
}