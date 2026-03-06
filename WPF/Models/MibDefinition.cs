using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SNMPSimMgr.Models
{
    public class MibDefinition
    {
        // Identity
        public string Name { get; set; } = string.Empty;
        public string Oid { get; set; } = string.Empty;
        public string ParentName { get; set; }
        public int Index { get; set; }
        public string ModuleName { get; set; }

        // MIB metadata
        public string Description { get; set; }
        public string Syntax { get; set; }
        public string Access { get; set; }
        public string Status { get; set; }
        public string Units { get; set; }
        public string DefVal { get; set; }
        public string DisplayHint { get; set; }
        public string IndexParts { get; set; }

        // Parsed syntax breakdown
        public string BaseType { get; set; }
        public long? RangeMin { get; set; }
        public long? RangeMax { get; set; }
        public long? SizeMin { get; set; }
        public long? SizeMax { get; set; }
        public Dictionary<string, int> EnumValues { get; set; }
    }

    public class MibFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public int DefinitionCount { get; set; }
        public List<MibDefinition>  Definitions { get; set; } = new List<MibDefinition>();
    }
}
