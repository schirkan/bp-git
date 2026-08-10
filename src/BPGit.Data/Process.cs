using System;

namespace BPGit.Data
{
    public class Process
    {
        public Guid processid { get; set; }
        public string ProcessType { get; set; } = "";
        public string name { get; set; } = "";
        public string? description { get; set; }
        public string? version { get; set; }
        public DateTime createdate { get; set; }
        public Guid createdby { get; set; }
        public DateTime lastmodifieddate { get; set; }
        public Guid lastmodifiedby { get; set; }
        public int AttributeID { get; set; }
        public string? processxml { get; set; }
        public int runmode { get; set; }
        public bool sharedObject { get; set; }
        public bool forceLiteralForm { get; set; }
        public bool useLegacyNamespace { get; set; }
        public bool hasStartupParameters { get; set; }
        public string? wspublishname { get; set; }
    }
}
