using System.Xml;
using System.Xml.Linq;

namespace BPGit.Format;

public class ProcessXmlSerializer
{
    public XDocument? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try { return XDocument.Parse(xml); }
        catch (XmlException) { return null; }
    }

    public bool IsValid(string? xml) => Parse(xml) != null;
}
