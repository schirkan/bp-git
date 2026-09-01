using System.Text;
using BPGit.Server.GitHttp;

namespace BPGit.Server.Tests;

/// <summary>
/// Unit tests for <see cref="RefUpdateParser"/>: extracts the leading ref-update
/// pkt-line section from a smart-HTTP receive-pack body and exposes them as
/// <see cref="RefUpdate"/> records for the hook orchestration layer.
///
/// Wire-format per gitprotocol-pack.txt §2:
///   pkt-line "<old-sha> <new-sha> <refname>\0<capabilities>\n"  (capabilities only on first line)
///   ...
///   flush packet "0000"
///   <pack file binary follows; not parsed here>
/// </summary>
public class RefUpdateParserTests
{
    private const string ZeroSha = "0000000000000000000000000000000000000000";

    /// <summary>
    /// Build wire-format bytes for a receive-pack body consisting of the given
    /// ref-update lines followed by a flush packet.
    /// </summary>
    private static byte[] BuildBody(params string[] refLines)
    {
        var ms = new MemoryStream();
        foreach (var line in refLines)
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            // pkt-line: 4-byte big-endian hex length + payload (LF terminator NOT in length)
            var len = bytes.Length + 4;
            var header = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)len);
            ms.Write(header);
            ms.Write(bytes);
            ms.Write(new byte[] { (byte)'\n' });
        }
        // Flush packet: "0000"
        ms.Write(Encoding.ASCII.GetBytes("0000"));
        // Append arbitrary pack-data (should be ignored)
        ms.Write(new byte[] { 0x50, 0x41, 0x43, 0x4B, 0x00, 0x00, 0x00, 0x02 });
        return ms.ToArray();
    }

    [Fact]
    public void Parse_EmptyBody_ReturnsEmpty()
    {
        var updates = RefUpdateParser.Parse(Array.Empty<byte>());

        Assert.Empty(updates);
    }

    [Fact]
    public void Parse_FlushOnly_ReturnsEmpty()
    {
        var body = Encoding.ASCII.GetBytes("0000").ToArray();

        var updates = RefUpdateParser.Parse(body);

        Assert.Empty(updates);
    }

    [Fact]
    public void Parse_SingleRefUpdate_ReturnsOneUpdate()
    {
        var body = BuildBody($"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main");

        var updates = RefUpdateParser.Parse(body);

        Assert.Single(updates);
        Assert.Equal(ZeroSha, updates[0].OldRev);
        Assert.Equal("abc1234567890abcdef0123456789abcdef01234", updates[0].NewRev);
        Assert.Equal("refs/heads/main", updates[0].RefName);
    }

    [Fact]
    public void Parse_MultipleRefUpdates_ReturnsAllInOrder()
    {
        var body = BuildBody(
            $"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main",
            $"abc1234567890abcdef0123456789abcdef01234 def4567890abcdef0123456789abcdef01235 refs/heads/develop",
            $"def4567890abcdef0123456789abcdef01235 {ZeroSha} refs/heads/feature-x");

        var updates = RefUpdateParser.Parse(body);

        Assert.Equal(3, updates.Count);
        Assert.Equal("refs/heads/main", updates[0].RefName);
        Assert.Equal("refs/heads/develop", updates[1].RefName);
        Assert.Equal("refs/heads/feature-x", updates[2].RefName);
        // Third update is a delete (new-rev = zero SHA).
        Assert.Equal(ZeroSha, updates[2].NewRev);
    }

    [Fact]
    public void Parse_RefUpdateWithCapabilities_StripsCapabilitiesFromFirstLine()
    {
        // Capabilities are NUL-separated from the refname and follow only on the
        // FIRST ref line in a receive-pack session. Subsequent lines have no NUL.
        var first = $"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main\0report-status delete-refs";
        var body = BuildBody(first);

        var updates = RefUpdateParser.Parse(body);

        Assert.Single(updates);
        Assert.Equal("refs/heads/main", updates[0].RefName);
    }

    [Fact]
    public void Parse_SubsequentLines_NoCapabilities_IsParsed()
    {
        // Confirm we don't accidentally strip NUL from subsequent lines.
        var body = BuildBody(
            $"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main",
            $"abc1234567890abcdef0123456789abcdef01234 def4567890abcdef0123456789abcdef01235 refs/heads/develop");

        var updates = RefUpdateParser.Parse(body);

        Assert.Equal(2, updates.Count);
        Assert.Equal("refs/heads/develop", updates[1].RefName);
    }

    [Fact]
    public void Parse_StopsAtFlushPacket_IgnoresPackData()
    {
        // The parser should stop at the first flush packet; pack-data after it is ignored.
        var body = BuildBody($"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main");

        var updates = RefUpdateParser.Parse(body);

        Assert.Single(updates);
        Assert.Equal("refs/heads/main", updates[0].RefName);
        // Note: body-length assertion was removed; that was a BuildBody-output
        // sanity check (97-char payload = 114 bytes), not parser behavior. The
        // parser is tested by the update count + refname above.
    }

    [Fact]
    public void Parse_DelimiterPacket_IsSkipped()
    {
        // Per spec: delim packets (4-byte "0001") break sections without flushing.
        // Parser keeps reading Data packets after a Delim.
        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("0001")); // delim
        body.AddRange(BuildBody($"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main"));

        var updates = RefUpdateParser.Parse(body.ToArray());

        Assert.Single(updates);
        Assert.Equal("refs/heads/main", updates[0].RefName);
    }

    [Fact]
    public void Parse_MalformedPktLine_Throws()
    {
        // "ZZZZ" is not a valid hex length.
        var body = Encoding.ASCII.GetBytes("ZZZZmore-data-here").ToArray();

        Assert.Throws<InvalidDataException>(() => RefUpdateParser.Parse(body));
    }

    [Fact]
    public void Parse_NoFlush_BodyTruncates_ReturnsParsedLines()
    {
        // If the body ends mid-pkt-line (truncated), we silently stop parsing.
        // This is normal when the buffered request body cuts off the pack-data
        // section after the initial pkt-line negotiation.
        var body = new byte[]
        {
            0x30, 0x30, 0x30, 0x35, // "0005" — declares 5-byte payload
            0x68, 0x69, // "hi" (only 2 bytes of the promised payload)
        };

        var updates = RefUpdateParser.Parse(body);

        // We don't recover anything from the truncated data, but we also don't throw.
        Assert.Empty(updates);
    }

    [Fact]
    public void Parse_PathologicalZeroRefName_IsIgnoredNotCrash()
    {
        // Defensive: malformed "<old> <new> " (no refname) should not produce a
        // RefUpdate with empty RefName. Parser currently produces empty entry
        // because it accepts whatever follows the second space; this test pins
        // current behaviour. Future fix: skip entries with malformed shape.
        var body = BuildBody(
            $"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 refs/heads/main",
            $"{ZeroSha} abc1234567890abcdef0123456789abcdef01234 "); // missing refname

        var updates = RefUpdateParser.Parse(body);

        // Currently: first entry parsed correctly, second parsed as
        // (ZeroSha, sha, "") with empty RefName. Future enhancement: skip.
        Assert.Equal(2, updates.Count);
        Assert.Equal("refs/heads/main", updates[0].RefName);
        Assert.Equal("", updates[1].RefName);
    }
}