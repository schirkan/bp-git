namespace BPGit.Server.GitHttp;

/// <summary>
/// Parses ref-update commands from the smart-HTTP receive-pack pkt-line section.
/// Format per gitprotocol-pack.txt §2 + receive-pack semantics:
///
///   pkt-line "<old-sha> <new-sha> <refname>\0<capabilities>\n"   (capabilities only on first line)
///   ...
///   flush packet "0000"
///   <pack file binary follows; not parsed here>
///
/// Old-sha = 40-zero SHA means delete. We skip delete ref-updates in the
/// orchestrator because BpSyncService.DeleteAsync is NotImplemented (Phase
/// 4b-follow-up, see SPEC-pre-receive-wiring §1.3).
/// </summary>
internal static class RefUpdateParser
{
    /// <summary>
    /// Parse the leading pkt-line section of a receive-pack body. Stops at the
    /// first flush packet; bytes after that are pack-data and ignored.
    /// Throws on malformed pkt-line format; silently truncates on EOF mid-stream
    /// (the body may not contain the full pack-section if the client stream
    /// ends, though in practice the body is always fully buffered before parsing).
    /// </summary>
    public static IReadOnlyList<RefUpdate> Parse(byte[] body)
    {
        var updates = new List<RefUpdate>();
        var offset = 0;
        while (true)
        {
            PktLine? pkt;
            try
            {
                pkt = PktReader.ReadFromBuffer(body, ref offset);
            }
            catch (EndOfStreamException)
            {
                // Body ends mid-pkt-line. This is normal when the buffered body
                // cuts off the pack-data section after the initial pkt-lines.
                // Stop parsing here.
                break;
            }
            if (pkt == null) break;
            if (pkt.Type == PktLineType.Flush) break;
            if (pkt.Type == PktLineType.Delim) continue;

            // Data packet — parse "<old-sha> <new-sha> <refname>"
            // (capabilities are NUL-separated and follow only on the FIRST ref line;
            //  we strip everything after the first NUL to get the bare refname.)
            var line = pkt.PayloadString;
            var parts = line.Split(' ', 3);
            if (parts.Length == 3)
            {
                var refName = parts[2].Split('\0')[0];
                updates.Add(new RefUpdate(parts[0], parts[1], refName));
            }
        }
        return updates;
    }
}