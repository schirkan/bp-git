using System.IO;
using System.Text;
using System.Threading.Tasks;
using BPGit.Server.GitHttp;
using Xunit;

namespace BPGit.Server.Tests;

public class PktInternalTests
{
    [Fact]
    public async Task WriteDataAsync_PayloadWithoutLf_EmitsHeaderPayloadLf()
    {
        using var ms = new MemoryStream();
        await Pkt.WriteDataAsync(ms, "hello");

        var bytes = ms.ToArray();
        // Per gitprotocol-pack.txt: len-prefix = 4 + payload-bytes (LF terminator
        // is NOT counted in the length). Total bytes on the wire = 4 (header) +
        // payload-bytes + 1 (LF).
        Assert.Equal(10, bytes.Length);
        Assert.Equal(0x00, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
        Assert.Equal(0x00, bytes[2]);
        Assert.Equal(0x09, bytes[3]); // len = 4 + 5 = 9
        Assert.Equal("hello", Encoding.UTF8.GetString(bytes, 4, 5));
        Assert.Equal((byte)'\n', bytes[9]);
    }

    [Fact]
    public async Task WriteFlushAsync_EmitsAscii0000()
    {
        using var ms = new MemoryStream();
        await Pkt.WriteFlushAsync(ms);

        Assert.Equal(new byte[] { 0x30, 0x30, 0x30, 0x30 }, ms.ToArray());
    }

    [Fact]
    public async Task WriteDelimAsync_EmitsAscii0001()
    {
        using var ms = new MemoryStream();
        await Pkt.WriteDelimAsync(ms);

        Assert.Equal(new byte[] { 0x30, 0x30, 0x30, 0x31 }, ms.ToArray());
    }

    [Fact]
    public async Task WriteDataAsync_EmptyString_EmitsHeader0004LfOnly()
    {
        using var ms = new MemoryStream();
        await Pkt.WriteDataAsync(ms, "");

        var bytes = ms.ToArray();
        Assert.Equal(5, bytes.Length);
        Assert.Equal(0x04, bytes[3]); // len = 4 + 0 = 4
        Assert.Equal((byte)'\n', bytes[4]);
    }

    [Fact]
    public async Task WriteDataAsync_Utf8Payload_LengthReflectsByteCount()
    {
        using var ms = new MemoryStream();
        await Pkt.WriteDataAsync(ms, "ae");

        var bytes = ms.ToArray();
        Assert.Equal(4 + 2 + 1, bytes.Length);
        Assert.Equal(0x06, bytes[3]); // len = 4 + 2 = 6
    }

}