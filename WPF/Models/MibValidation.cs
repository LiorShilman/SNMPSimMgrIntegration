using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SNMPSimMgr.Models
{
    /// <summary>Result of validating multiple MIB files together.</summary>
    public class MibValidationResult
    {
        public string DeviceName { get; set; } = string.Empty;
        public List<MibFileValidation>  Files { get; set; } = new List<MibFileValidation>();
        public List<MibFileDependencies>  Dependencies { get; set; } = new List<MibFileDependencies>();
    }

    /// <summary>Validation result for a single MIB file.</summary>
    public class MibFileValidation
    {
        public string FileName { get; set; } = string.Empty;
        public int DefinitionCount { get; set; }
        public int IssueCount { get; set; }
        public List<MibValidationIssue>  Issues { get; set; } = new List<MibValidationIssue>();
    }

    /// <summary>A single validation issue found in a MIB file.</summary>
    public class MibValidationIssue
    {
        public string Severity { get; set; } = "info";
        public string Message { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
    }

    /// <summary>Dependencies extracted from a MIB file's IMPORTS section.</summary>
    public class MibFileDependencies
    {
        public string FileName { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public List<MibDependency>  Imports { get; set; } = new List<MibDependency>();
    }

    /// <summary>A single MIB module dependency.</summary>
    public class MibDependency
    {
        public string ModuleName { get; set; } = string.Empty;
        public string Status { get; set; } = "missing"; // "loaded", "standard", "missing"
        public string ProvidedBy { get; set; }
    }
}
