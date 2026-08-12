using System.Text.RegularExpressions;

namespace BPGit.Server.Services;

/// <summary>
/// Centralized XML sanitization helpers for BP-DB read/write paths.
///
/// Leading XML-Comments brechen BP's /import-Parser (per Martin #6277).
/// Konsolidiert die Regex-Logik aus WorktreeSyncService + BpSyncService
/// (zuvor dupliziert in beiden Service-Klassen, Refactoring Martin #6401).
/// </summary>
public static class XmlSanitizer
{
    // BP's /import-Parser ist strikt: Leading XML-Comments brechen den Parser
    // ("Failed to create... already exists"), obwohl /overwrite gesetzt ist.
    private static readonly Regex LeadingCommentsRegex =
        new(@"^\s*(?:<!--[\s\S]*?-->\s*)+", RegexOptions.Compiled);

    /// <summary>
    /// Strip leading XML comments that would otherwise break BP's /import-Parser
    /// (per #6277). Returns the input unchanged if null or empty.
    /// </summary>
    public static string StripLeadingXmlComments(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return xml;
        return LeadingCommentsRegex.Replace(xml, string.Empty);
    }
}
