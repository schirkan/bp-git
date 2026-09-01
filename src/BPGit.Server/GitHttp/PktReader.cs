using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace BPGit.Server.GitHttp;

/// <summary>
/// Inverse helpers for the git pkt-line format (see gitprotocol-pack.txt).
/// Each packet is 4 bytes of big-endian binary length prefix followed by the
/// payload (LF terminator NOT counted in the length).
/// Flush packet: length = 0. Delim packet: length = 1.
///
/// IMPORTANT wire-format detail: the existing <see cref="Pkt.WriteDataAsync"/>
/// in this project writes the length prefix as a 4-byte big-endian uint32
/// (binary), NOT as 4 ASCII-hex digits. The byte-level tests in
/// <c>PktInternalTests</c> assert this binary form. This reader matches that
/// wire format. Tests + production rely on this alignment.
///
/// Used by PushOrchestrator to parse ref-update commands from the smart-HTTP
/// receive-pack request body (before forwarding the stream to native
/// git-receive-pack).
/// </summary>
internal static class PktReader
{
    /// <summary>
    /// Read next pkt-line from a forward-only stream. Returns null on clean EOF
    /// (stream at offset 0). Throws InvalidDataException on malformed length
    /// (values other than 0, 1, or >=4), EndOfStreamException on truncated payload.
    /// </summary>
    public static async Task<PktLine?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        var totalRead = 0;
        while (totalRead < 4)
        {
            var n = await stream.ReadAsync(header.AsMemory(totalRead, 4 - totalRead), ct);
            if (n == 0)
            {
                if (totalRead == 0) return null; // clean EOF
                throw new EndOfStreamException($"Truncated pkt-line header (got {totalRead}/4 bytes)");
            }
            totalRead += n;
        }

        var length = DecodeLength(header);

        if (length == 0)
            return new PktLine(PktLineType.Flush, Array.Empty<byte>());
        if (length == 1)
            return new PktLine(PktLineType.Delim, Array.Empty<byte>());
        if (length < 4)
            throw new InvalidDataException(
                $"Invalid pkt-line length: {length} (must be 0, 1, or >=4)");

        var payloadLen = length - 4;
        var payload = new byte[payloadLen];
        var read = 0;
        while (read < payloadLen)
        {
            var n = await stream.ReadAsync(payload.AsMemory(read, payloadLen - read), ct);
            if (n == 0) throw new EndOfStreamException($"Truncated pkt-line payload (got {read}/{payloadLen} bytes)");
            read += n;
        }

        return new PktLine(PktLineType.Data, payload);
    }

    /// <summary>
    /// Synchronous variant for in-memory byte arrays (used by PushOrchestrator
    /// to parse ref-updates from the buffered request body without going
    /// through async-over-sync conversion).
    /// </summary>
    public static PktLine? ReadFromBuffer(byte[] buffer, ref int offset)
    {
        if (offset >= buffer.Length) return null;

        if (offset + 4 > buffer.Length)
            throw new EndOfStreamException("Truncated pkt-line header");

        var length = DecodeLength(buffer.AsSpan(offset, 4));
        offset += 4;

        if (length == 0)
            return new PktLine(PktLineType.Flush, Array.Empty<byte>());
        if (length == 1)
            return new PktLine(PktLineType.Delim, Array.Empty<byte>());
        if (length < 4)
            throw new InvalidDataException(
                $"Invalid pkt-line length: {length} (must be 0, 1, or >=4)");

        var payloadLen = length - 4;
        if (payloadLen > buffer.Length - offset)
            throw new InvalidDataException(
                $"Pkt-line payload length {payloadLen} exceeds remaining buffer ({buffer.Length - offset} bytes)");

        var payload = new byte[payloadLen];
        Array.Copy(buffer, offset, payload, 0, payloadLen);
        offset += payloadLen;

        // Skip trailing LF terminator (git wire format: data packets end with \n).
        // We skip it here so the next ReadFromBuffer call advances to the next
        // packet's header rather than misreading the LF as header byte 0.
        // The LF is optional in our parser (lenient): absent is fine (some
        // test helpers don't emit it), present is consumed.
        if (offset < buffer.Length && buffer[offset] == (byte)'\n')
            offset++;

        return new PktLine(PktLineType.Data, payload);
    }

    /// <summary>
    /// Decode a pkt-line length prefix (4 bytes) supporting BOTH wire formats
    /// used in this project:
    ///
    ///  1. **Binary** (matches <see cref="Pkt.WriteDataAsync"/>):
    ///     4-byte big-endian uint32, e.g. <c>0x00 0x00 0x00 0x09</c> for length 9.
    ///
    ///  2. **ASCII hex** (matches <see cref="Pkt.WriteFlushAsync"/>, which writes
    ///     <c>"0000"</c> = 4 ASCII '0' chars; same for <see cref="Pkt.WriteDelimAsync"/>
    ///     with <c>"0001"</c>):
    ///     4 ASCII characters in <c>[0-9a-fA-F]</c>, e.g. <c>"0009"</c> for length 9.
    ///
    /// Auto-detect: if every byte is in the ASCII hex range, parse as ASCII hex
    /// (the git protocol spec format). Otherwise parse as binary uint32 (the
    /// project's internal data-packet format).
    ///
    /// This dual-format support is intentional — it keeps the existing
    /// <c>PktInternalTests</c> and <c>Pkt.Write*</c> behaviour unchanged while
    /// letting <c>PktReader</c> parse both real git wire output (ASCII hex) and
    /// our own <c>Pkt.WriteDataAsync</c> output (binary).
    /// </summary>
    private static int DecodeLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != 4)
            throw new InvalidDataException($"Pkt-line length header must be 4 bytes, got {header.Length}");

        // ASCII-hex path: every byte is a hex digit.
        if (IsAsciiHexDigit(header[0]) && IsAsciiHexDigit(header[1]) &&
            IsAsciiHexDigit(header[2]) && IsAsciiHexDigit(header[3]))
        {
            var hex = ((char)header[0]).ToString()
                    + ((char)header[1]).ToString()
                    + ((char)header[2]).ToString()
                    + ((char)header[3]).ToString();
            return int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
        }

        // Binary path: big-endian uint32.
        return (int)BinaryPrimitives.ReadUInt32BigEndian(header);
    }

    private static bool IsAsciiHexDigit(byte b) =>
        (b >= (byte)'0' && b <= (byte)'9') ||
        (b >= (byte)'a' && b <= (byte)'f') ||
        (b >= (byte)'A' && b <= (byte)'F');
}

/// <summary>Pkt-line type discriminator. See gitprotocol-pack.txt §2.</summary>
public enum PktLineType
{
    /// <summary>Data packet: length >= 4 followed by payload.</summary>
    Data,
    /// <summary>Flush packet: length = 0 — separates phases in the protocol.</summary>
    Flush,
    /// <summary>Delimiter packet: length = 1 — breaks sections without flushing.</summary>
    Delim,
}

/// <summary>One parsed pkt-line from the wire.</summary>
public sealed record PktLine(PktLineType Type, byte[] Payload)
{
    /// <summary>
    /// UTF-8 decode of the payload. Strips trailing LF if present (git clients
    /// may include the LF in the payload bytes despite the spec saying otherwise).
    /// </summary>
    public string PayloadString
    {
        get
        {
            var end = Payload.Length;
            if (end > 0 && Payload[end - 1] == (byte)'\n') end--;
            return Encoding.UTF8.GetString(Payload, 0, end);
        }
    }
}