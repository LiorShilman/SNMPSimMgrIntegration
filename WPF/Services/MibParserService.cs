using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SNMPSimMgr.Hubs;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public static class MibParserService
{
    // Well-known root OIDs that serve as the resolution base
    private static readonly Dictionary<string, string> RootOids = new()
    {
        ["iso"] = "1",
        ["org"] = "1.3",
        ["dod"] = "1.3.6",
        ["internet"] = "1.3.6.1",
        ["directory"] = "1.3.6.1.1",
        ["mgmt"] = "1.3.6.1.2",
        ["mib-2"] = "1.3.6.1.2.1",
        ["system"] = "1.3.6.1.2.1.1",
        ["interfaces"] = "1.3.6.1.2.1.2",
        ["at"] = "1.3.6.1.2.1.3",
        ["ip"] = "1.3.6.1.2.1.4",
        ["icmp"] = "1.3.6.1.2.1.5",
        ["tcp"] = "1.3.6.1.2.1.6",
        ["udp"] = "1.3.6.1.2.1.7",
        ["transmission"] = "1.3.6.1.2.1.10",
        ["snmp"] = "1.3.6.1.2.1.11",
        ["experimental"] = "1.3.6.1.3",
        ["private"] = "1.3.6.1.4",
        ["enterprises"] = "1.3.6.1.4.1",
        ["snmpV2"] = "1.3.6.1.6",
        ["snmpDomains"] = "1.3.6.1.6.1",
        ["snmpProxys"] = "1.3.6.1.6.2",
        ["snmpModules"] = "1.3.6.1.6.3",
        // SNMP-FRAMEWORK-MIB (RFC 3411)
        ["snmpFrameworkMIB"] = "1.3.6.1.6.3.10",
        ["snmpFrameworkAdmin"] = "1.3.6.1.6.3.10.1",
        ["snmpFrameworkMIBObjects"] = "1.3.6.1.6.3.10.2",
        ["snmpFrameworkMIBConformance"] = "1.3.6.1.6.3.10.3",
        ["snmpAuthProtocols"] = "1.3.6.1.6.3.10.1.1",
        ["snmpPrivProtocols"] = "1.3.6.1.6.3.10.1.2",
        // IEEE 802 standards (used by LLDP, 802.1Q, etc.)
        // OID path: iso(1).org(3).ieee(111).standards-association-numbers-series-standards(2).lan-man-stds(802).ieee802dot1(1).1
        ["ieee802dot1mibs"] = "1.3.111.2.802.1.1",
        // Special: zeroDotZero ::= { 0 0 }
        ["0"] = "0",
    };

    // MIB keywords that should never be treated as definition names
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IMPORTS", "EXPORTS", "BEGIN", "END", "DEFINITIONS", "MACRO",
        "MODULE-IDENTITY", "OBJECT-TYPE", "OBJECT-IDENTITY", "NOTIFICATION-TYPE",
        "MODULE-COMPLIANCE", "OBJECT-GROUP", "NOTIFICATION-GROUP",
        "AGENT-CAPABILITIES", "TEXTUAL-CONVENTION",
        "SEQUENCE", "CHOICE", "INTEGER", "OCTET", "OBJECT",
        "TYPE", "VALUE", "NOTATION", "STATUS", "SYNTAX",
    };

    // Regex: captures name and ::= { parent index } assignment
    // Allow leading whitespace (tabs/spaces) before the name — many vendor MIBs indent definitions
    private static readonly Regex AssignmentRegex = new(
        @"^\s*(\w[\w-]*)\s+(?:OBJECT-TYPE|OBJECT\s+IDENTIFIER|OBJECT-IDENTITY|MODULE-IDENTITY|NOTIFICATION-TYPE|MODULE-COMPLIANCE|OBJECT-GROUP|NOTIFICATION-GROUP|AGENT-CAPABILITIES|TEXTUAL-CONVENTION)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // OID assignment: captures everything inside ::= { ... } for programmatic parsing
    // Handles both standard "{ parent 1 }" and IEEE multi-component "{ org ieee(111) stds(2) ... 1 }"
    private static readonly Regex OidAssignRegex = new(
        @"::=\s*\{([^}]+)\}",
        RegexOptions.Compiled);

    // Simpler form: name OBJECT IDENTIFIER ::= { ... }  (single or multi-line)
    private static readonly Regex SimpleOidRegex = new(
        @"^\s*(\w[\w-]*)\s+OBJECT\s+IDENTIFIER\s*::=\s*\{([^}]+)\}",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // DESCRIPTION extraction
    private static readonly Regex DescriptionRegex = new(
        @"DESCRIPTION\s*""([^""]*?)""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // SYNTAX extraction — supports multi-line enum blocks like INTEGER { up(1), down(2) }
    private static readonly Regex SyntaxRegex = new(
        @"SYNTAX\s+([\s\S]+?)(?:\r?\n\s*(?:MAX-ACCESS|ACCESS|STATUS|DESCRIPTION|INDEX|DEFVAL|::=))",
        RegexOptions.Compiled);

    // Rich metadata extraction
    private static readonly Regex AccessRegex = new(
        @"(?:MAX-ACCESS|ACCESS)\s+([\w-]+)",
        RegexOptions.Compiled);

    private static readonly Regex StatusRegex = new(
        @"STATUS\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex UnitsRegex = new(
        @"UNITS\s+""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex DefValRegex = new(
        @"DEFVAL\s*\{\s*([^}]*)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex DisplayHintRegex = new(
        @"DISPLAY-HINT\s+""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex IndexPartsRegex = new(
        @"INDEX\s*\{\s*([^}]+)\s*\}",
        RegexOptions.Compiled);

    // Boundary: detects the start of the NEXT definition (to limit defRegion scope)
    private static readonly Regex NextDefinitionRegex = new(
        @"\n[ \t]*\w[\w-]*\s+(?:OBJECT-TYPE|OBJECT IDENTIFIER|MODULE-IDENTITY|OBJECT-IDENTITY|NOTIFICATION-TYPE|MODULE-COMPLIANCE|OBJECT-GROUP|NOTIFICATION-GROUP)\b",
        RegexOptions.Compiled);

    // Syntax sub-patterns
    private static readonly Regex EnumValuesRegex = new(
        @"\{\s*((?:\w[\w-]*\s*\(\s*-?\d+\s*\)\s*,?\s*)+)\}",
        RegexOptions.Compiled);

    private static readonly Regex SingleEnumRegex = new(
        @"(\w[\w-]*)\s*\(\s*(-?\d+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex IntRangeRegex = new(
        @"\(\s*(-?\d+)\s*\.\.\s*(-?\d+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex SizeConstraintRegex = new(
        @"SIZE\s*\(\s*(\d+)\s*\.\.\s*(\d+)\s*\)",
        RegexOptions.Compiled);

    // IMPORTS block: everything from IMPORTS keyword to the terminating semicolon
    private static readonly Regex ImportsBlockRegex = new(
        @"\bIMPORTS\s+([\s\S]*?);",
        RegexOptions.Compiled);

    // FROM clause within IMPORTS block
    private static readonly Regex FromClauseRegex = new(
        @"FROM\s+([\w][\w-]*)",
        RegexOptions.Compiled);

    // MODULE-IDENTITY or DEFINITIONS ::= BEGIN (allow leading whitespace — many vendor MIBs indent)
    private static readonly Regex ModuleNameRegex = new(
        @"^\s*(\w[\w-]*)\s+(?:DEFINITIONS\s*::=\s*BEGIN|MODULE-IDENTITY)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static MibFileInfo ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return Parse(content, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Parse multiple MIB files together, resolving cross-file dependencies.
    /// Names defined in one file can be used as parents in another.
    /// </summary>
    public static List<MibFileInfo> ParseMultiple(List<string> filePaths)
    {
        if (filePaths.Count == 0) return new();
        if (filePaths.Count == 1) return new() { ParseFile(filePaths[0]) };

        // Phase 1: Collect raw assignments from all files
        var perFile = new List<(string fileName, string moduleName, string content,
            Dictionary<string, (string parent, int index)> rawAssignments)>();

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            var fileName = Path.GetFileName(path);

            var moduleMatch = ModuleNameRegex.Match(content);
            var moduleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : fileName;

            var stripped = StripComments(content);
            var rawAssignments = CollectRawAssignments(stripped);

            perFile.Add((fileName, moduleName, stripped, rawAssignments));
        }

        // Phase 2: Merge all raw assignments and resolve together
        var allAssignments = new Dictionary<string, (string parent, int index)>();
        foreach (var (_, _, _, rawAssignments) in perFile)
        {
            foreach (var kvp in rawAssignments)
            {
                if (!allAssignments.ContainsKey(kvp.Key))
                    allAssignments[kvp.Key] = kvp.Value;
            }
        }

        // Resolve all names using the merged map
        var resolved = new Dictionary<string, string>(RootOids);
        int lastCount;
        do
        {
            lastCount = resolved.Count;
            foreach (var kvp in allAssignments)
            {
                var name = kvp.Key;
                var parent = kvp.Value.parent;
                var index = kvp.Value.index;
                if (resolved.ContainsKey(name)) continue;
                if (resolved.TryGetValue(parent, out var parentOid))
                    resolved[name] = $"{parentOid}.{index}";
            }
        } while (resolved.Count > lastCount);

        // Phase 3: Build MibFileInfo per file using the globally resolved names
        var results = new List<MibFileInfo>();

        foreach (var (fileName, moduleName, content, rawAssignments) in perFile)
        {
            var info = new MibFileInfo { FileName = fileName, ModuleName = moduleName };
            var definitions = new Dictionary<string, MibDefinition>();

            // Strip IMPORTS/MACRO blocks for metadata extraction — prevents MACRO grammar
            // from leaking into definition regions (e.g. snmpModules getting "Text RevisionPart").
            var cleanContent = StripImportsAndMacros(content);

            foreach (var kvp in rawAssignments)
            {
                var name = kvp.Key;
                if (name.StartsWith("_synth_")) continue; // Skip synthetic intermediate nodes
                var parent = kvp.Value.parent;
                var index = kvp.Value.index;
                if (!resolved.TryGetValue(name, out var oid)) continue;

                var def = new MibDefinition
                {
                    Name = name,
                    Oid = oid,
                    ParentName = parent,
                    Index = index
                };

                // Extract all metadata from MACRO-stripped content
                var defRegion = ExtractDefinitionRegion(cleanContent, name);
                if (defRegion != null)
                    ExtractMetadata(def, defRegion, moduleName);

                definitions[oid] = def;
            }

            info.Definitions = definitions.Values.ToList();
            info.DefinitionCount = info.Definitions.Count;
            results.Add(info);
        }

        return results;
    }

    public static MibFileInfo Parse(string content, string fileName)
    {
        var result = new MibFileInfo { FileName = fileName };

        // Detect module name
        var moduleMatch = ModuleNameRegex.Match(content);
        result.ModuleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : fileName;

        // Strip single-line comments
        content = StripComments(content);

        // Phase 1: Collect raw assignments (internally also strips IMPORTS/MACROs)
        var rawAssignments = CollectRawAssignments(content);

        // Strip IMPORTS/MACRO blocks for metadata extraction — prevents MACRO grammar
        // (e.g. "DESCRIPTION" Text, RevisionPart) from leaking into definition regions.
        var cleanContent = StripImportsAndMacros(content);

        // Phase 2: Resolve all names to full numeric OIDs
        var resolved = new Dictionary<string, string>(RootOids);
        var definitions = new Dictionary<string, MibDefinition>();

        int lastCount;
        do
        {
            lastCount = resolved.Count;
            foreach (var kvp in rawAssignments)
            {
                var name = kvp.Key;
                var parent = kvp.Value.parent;
                var index = kvp.Value.index;
                if (resolved.ContainsKey(name)) continue;
                if (resolved.TryGetValue(parent, out var parentOid))
                    resolved[name] = $"{parentOid}.{index}";
            }
        } while (resolved.Count > lastCount);

        // Phase 3: Build MibDefinition objects
        foreach (var kvp2 in rawAssignments)
        {
            var name = kvp2.Key;
            if (name.StartsWith("_synth_")) continue; // Skip synthetic intermediate nodes
            var parent = kvp2.Value.parent;
            var index = kvp2.Value.index;
            if (!resolved.TryGetValue(name, out var oid)) continue;

            var def = new MibDefinition
            {
                Name = name,
                Oid = oid,
                ParentName = parent,
                Index = index
            };

            var defRegion = ExtractDefinitionRegion(cleanContent, name);
            if (defRegion != null)
                ExtractMetadata(def, defRegion, result.ModuleName);

            definitions[oid] = def;
        }

        result.Definitions = definitions.Values.ToList();
        result.DefinitionCount = result.Definitions.Count;
        return result;
    }

    /// <summary>
    /// Parse a parent token that may include an inline numeric label, e.g. "enterprises" or "mminc(48246)".
    /// Returns the clean name (without the label).
    /// </summary>
    private static string CleanParentName(string raw)
    {
        var paren = raw.IndexOf('(');
        return paren >= 0 ? raw.Substring(0, paren) : raw;
    }

    /// <summary>
    /// Parse the full content inside an OID assignment's braces.
    /// Handles standard format: "parent 1" or "parent 48246 1"
    /// and IEEE multi-component format: "org ieee(111) stds(2) lan(802) ieee802dot1(1) 1"
    /// Returns (parentName, numericIndices) or null if the content can't be parsed as an OID.
    /// </summary>
    private static (string parent, int[] indices)? TryParseOidBraceContent(string braceContent)
    {
        var tokens = braceContent.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return null;

        var parent = CleanParentName(tokens[0]);
        var indices = new List<int>();

        for (int i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var parenIdx = token.IndexOf('(');
            if (parenIdx >= 0)
            {
                // name(num) format — extract the number
                var numStr = token.Substring(parenIdx + 1).TrimEnd(')');
                if (int.TryParse(numStr, out var num))
                    indices.Add(num);
                else
                    return null;
            }
            else if (int.TryParse(token, out var num))
            {
                indices.Add(num);
            }
            else
            {
                // Plain name without number — can't resolve numerically
                return null;
            }
        }

        return indices.Count > 0 ? (parent, indices.ToArray()) : null;
    }

    /// <summary>
    /// Strip IMPORTS sections and MACRO definition blocks from MIB content.
    /// These contain keywords (MODULE-IDENTITY, OBJECT-TYPE, etc.) that confuse
    /// the definition regex when they appear as imported names or macro grammar.
    /// </summary>
    private static string StripImportsAndMacros(string content)
    {
        // Strip IMPORTS ... ; sections
        content = Regex.Replace(content, @"\bIMPORTS\b.*?;", "", RegexOptions.Singleline);

        // Strip MACRO ::= BEGIN ... END blocks
        content = Regex.Replace(content, @"\bMACRO\s*::=\s*\n?\s*BEGIN\b.*?\bEND\b", "", RegexOptions.Singleline);

        return content;
    }

    private static Dictionary<string, (string parent, int index)> CollectRawAssignments(string content)
    {
        // Strip IMPORTS and MACRO blocks to avoid false matches
        content = StripImportsAndMacros(content);

        // Strip quoted strings to avoid matching patterns inside DESCRIPTION blocks
        // (e.g. IF-MIB has "noTest OBJECT IDENTIFIER ::= { 0 0 }" inside a DESCRIPTION)
        content = Regex.Replace(content, @"""[^""]*""", "\"\"", RegexOptions.Singleline);

        var rawAssignments = new Dictionary<string, (string parent, int index)>();
        // Track multi-part OID chains: name → (parent, full sub-ID list)
        var multiPartChains = new Dictionary<string, (string parent, int[] indices)>();

        // Simple OBJECT IDENTIFIER assignments (single or multi-line)
        foreach (Match m in SimpleOidRegex.Matches(content))
        {
            var name = m.Groups[1].Value;
            if (ReservedKeywords.Contains(name)) continue;

            var parsed = TryParseOidBraceContent(m.Groups[2].Value);
            if (parsed == null) continue;

            var (parent, indices) = parsed.Value;
            if (indices.Length == 1)
            {
                rawAssignments[name] = (parent, indices[0]);
            }
            else
            {
                multiPartChains[name] = (parent, indices);
                rawAssignments[name] = (parent, indices[0]);
            }
        }

        // Complex multi-line definitions (OBJECT-TYPE, MODULE-IDENTITY, etc.)
        var typeMatches = AssignmentRegex.Matches(content);
        var matchList = typeMatches.Cast<Match>().ToList();
        for (int mi = 0; mi < matchList.Count; mi++)
        {
            var typeMatch = matchList[mi];
            var name = typeMatch.Groups[1].Value;
            if (ReservedKeywords.Contains(name)) continue;
            if (rawAssignments.ContainsKey(name)) continue;

            // Limit search to the region between this definition and the next one.
            // Prevents crossing definition boundaries (e.g. objectID-value finding
            // zeroDotZero's ::= { 0 0 } instead of its own).
            // No hard cap — vendor MIBs can have 40KB+ MODULE-IDENTITY REVISION blocks.
            var searchStart = typeMatch.Index + typeMatch.Length;
            var nextDefStart = (mi + 1 < matchList.Count)
                ? matchList[mi + 1].Index
                : content.Length;
            var regionLength = nextDefStart - searchStart;
            if (regionLength <= 0) continue;

            var searchRegion = content.Substring(searchStart, regionLength);
            var oidMatch = OidAssignRegex.Match(searchRegion);
            if (oidMatch.Success)
            {
                var parsed = TryParseOidBraceContent(oidMatch.Groups[1].Value);
                if (parsed != null)
                {
                    var (parent, indices) = parsed.Value;
                    if (indices.Length == 1)
                    {
                        rawAssignments[name] = (parent, indices[0]);
                    }
                    else
                    {
                        multiPartChains[name] = (parent, indices);
                        rawAssignments[name] = (parent, indices[0]);
                    }
                }
            }
        }

        // Expand multi-part chains: { enterprises 48246 1 } →
        //   _synth_enterprises_48246 = (enterprises, 48246)
        //   mmiODU = (_synth_enterprises_48246, 1)
        foreach (var kvp in multiPartChains)
        {
            var name = kvp.Key;
            var parent = kvp.Value.parent;
            var indices = kvp.Value.indices;

            if (indices.Length < 2) continue;

            // Build chain of synthetic intermediate parents
            var currentParent = parent;
            for (int i = 0; i < indices.Length - 1; i++)
            {
                var synthName = $"_synth_{currentParent}_{indices[i]}";
                if (!rawAssignments.ContainsKey(synthName))
                    rawAssignments[synthName] = (currentParent, indices[i]);
                currentParent = synthName;
            }
            // Final assignment uses the last index with the last synthetic parent
            rawAssignments[name] = (currentParent, indices[indices.Length - 1]);
        }

        return rawAssignments;
    }

    /// <summary>
    /// Extract all rich metadata from a definition region in the MIB source.
    /// </summary>
    private static void ExtractMetadata(MibDefinition def, string defRegion, string moduleName)
    {
        def.ModuleName = moduleName;

        // Description
        var descMatch = DescriptionRegex.Match(defRegion);
        if (descMatch.Success)
            def.Description = descMatch.Groups[1].Value.Trim();

        // Syntax (raw)
        var syntaxMatch = SyntaxRegex.Match(defRegion);
        if (syntaxMatch.Success)
            def.Syntax = syntaxMatch.Groups[1].Value.Trim();

        // Access
        var accessMatch = AccessRegex.Match(defRegion);
        if (accessMatch.Success)
            def.Access = accessMatch.Groups[1].Value;

        // Status
        var statusMatch = StatusRegex.Match(defRegion);
        if (statusMatch.Success)
            def.Status = statusMatch.Groups[1].Value;

        // Units
        var unitsMatch = UnitsRegex.Match(defRegion);
        if (unitsMatch.Success)
            def.Units = unitsMatch.Groups[1].Value;

        // Default value
        var defValMatch = DefValRegex.Match(defRegion);
        if (defValMatch.Success)
            def.DefVal = defValMatch.Groups[1].Value.Trim();

        // Display hint
        var hintMatch = DisplayHintRegex.Match(defRegion);
        if (hintMatch.Success)
            def.DisplayHint = hintMatch.Groups[1].Value;

        // Index parts (for table entries)
        var indexMatch = IndexPartsRegex.Match(defRegion);
        if (indexMatch.Success)
            def.IndexParts = indexMatch.Groups[1].Value.Trim();

        // Parse syntax breakdown
        ParseSyntaxDetails(def);
    }

    private static void ParseSyntaxDetails(MibDefinition def)
    {
        var syntax = def.Syntax;
        if (string.IsNullOrEmpty(syntax)) return;

        // Known direct types
        var knownTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Counter32"] = "Counter32",
            ["Counter64"] = "Counter64",
            ["Gauge32"] = "Gauge32",
            ["TimeTicks"] = "TimeTicks",
            ["IpAddress"] = "IpAddress",
            ["Opaque"] = "Opaque",
            ["Unsigned32"] = "Unsigned32",
            ["Integer32"] = "Integer32",
        };

        foreach (var kvp in knownTypes)
        {
            if (syntax.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                def.BaseType = kvp.Value;
                // Check for range on these types too
                var rangeMatch = IntRangeRegex.Match(syntax);
                if (rangeMatch.Success)
                {
                    def.RangeMin = long.Parse(rangeMatch.Groups[1].Value);
                    def.RangeMax = long.Parse(rangeMatch.Groups[2].Value);
                }
                return;
            }
        }

        // INTEGER with enum values: INTEGER { up(1), down(2) }
        if (syntax.StartsWith("INTEGER", StringComparison.OrdinalIgnoreCase))
        {
            def.BaseType = "INTEGER";

            var enumMatch = EnumValuesRegex.Match(syntax);
            if (enumMatch.Success)
            {
                def.EnumValues = new Dictionary<string, int>();
                foreach (Match m in SingleEnumRegex.Matches(enumMatch.Groups[1].Value))
                    def.EnumValues[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
                return;
            }

            var rangeMatch = IntRangeRegex.Match(syntax);
            if (rangeMatch.Success)
            {
                def.RangeMin = long.Parse(rangeMatch.Groups[1].Value);
                def.RangeMax = long.Parse(rangeMatch.Groups[2].Value);
            }
            return;
        }

        // OCTET STRING with SIZE
        if (syntax.IndexOf("OCTET STRING", StringComparison.OrdinalIgnoreCase) >= 0
            || syntax.IndexOf("DisplayString", StringComparison.OrdinalIgnoreCase) >= 0
            || syntax.IndexOf("SnmpAdminString", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            def.BaseType = "OCTET STRING";
            var sizeMatch = SizeConstraintRegex.Match(syntax);
            if (sizeMatch.Success)
            {
                def.SizeMin = long.Parse(sizeMatch.Groups[1].Value);
                def.SizeMax = long.Parse(sizeMatch.Groups[2].Value);
            }
            return;
        }

        if (syntax.IndexOf("OBJECT IDENTIFIER", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            def.BaseType = "OBJECT IDENTIFIER";
            return;
        }

        if (syntax.StartsWith("BITS", StringComparison.OrdinalIgnoreCase))
        {
            def.BaseType = "BITS";
            var enumMatch = EnumValuesRegex.Match(syntax);
            if (enumMatch.Success)
            {
                def.EnumValues = new Dictionary<string, int>();
                foreach (Match m in SingleEnumRegex.Matches(enumMatch.Groups[1].Value))
                    def.EnumValues[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
            }
            return;
        }

        // Textual conventions and other types — try to detect enum or range
        def.BaseType = syntax.Split('(', '{', ' ')[0].Trim();
        var enumFallback = EnumValuesRegex.Match(syntax);
        if (enumFallback.Success)
        {
            def.EnumValues = new Dictionary<string, int>();
            foreach (Match m in SingleEnumRegex.Matches(enumFallback.Groups[1].Value))
                def.EnumValues[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }
        else
        {
            var rangeFallback = IntRangeRegex.Match(syntax);
            if (rangeFallback.Success)
            {
                def.RangeMin = long.Parse(rangeFallback.Groups[1].Value);
                def.RangeMax = long.Parse(rangeFallback.Groups[2].Value);
            }
            var sizeFallback = SizeConstraintRegex.Match(syntax);
            if (sizeFallback.Success)
            {
                def.SizeMin = long.Parse(sizeFallback.Groups[1].Value);
                def.SizeMax = long.Parse(sizeFallback.Groups[2].Value);
            }
        }
    }

    /// <summary>
    /// Find the definition start for a name — skips occurrences inside SEQUENCE blocks.
    /// Looks for "name OBJECT-TYPE", "name OBJECT IDENTIFIER", "name MODULE-IDENTITY", etc.
    /// </summary>
    /// <summary>
    /// Extract the definition region for a name, bounded by the next definition start.
    /// This prevents metadata leaking from one definition to another.
    /// </summary>
    private static string? ExtractDefinitionRegion(string content, string name)
    {
        var defStart = FindDefinitionStart(content, name);
        if (defStart < 0) return null;

        // Find the end: next definition boundary or max 3000 chars
        var maxLen = Math.Min(3000, content.Length - defStart);
        var region = content.Substring(defStart, maxLen);

        // Look for the next definition keyword (skip the first line which IS this definition)
        var nextDef = NextDefinitionRegex.Match(region, name.Length + 1);
        if (nextDef.Success)
            region = region.Substring(0, nextDef.Index);

        return region;
    }

    private static int FindDefinitionStart(string content, string name)
    {
        var searchFor = $"{name} ";
        int pos = 0;
        while (pos < content.Length)
        {
            var idx = content.IndexOf(searchFor, pos, StringComparison.Ordinal);
            if (idx < 0) return -1;

            // Check: is this inside a SEQUENCE { ... } block?
            // Look backwards for the nearest '{' or '}' to determine context
            bool insideSequence = false;
            var before = content.Substring(Math.Max(0, idx - 500), Math.Min(500, idx));
            var lastSeqStart = before.LastIndexOf("SEQUENCE", StringComparison.Ordinal);
            if (lastSeqStart >= 0)
            {
                var region = before.Substring(lastSeqStart);
                // If there's a '{' after SEQUENCE but no matching '}' before our position, we're inside
                int braceOpen = region.IndexOf('{');
                int braceClose = region.LastIndexOf('}');
                if (braceOpen >= 0 && (braceClose < 0 || braceClose < braceOpen))
                    insideSequence = true;
            }

            if (!insideSequence)
                return idx;

            pos = idx + searchFor.Length;
        }
        return -1;
    }

    // ── MIB Validation ────────────────────────────────────────────

    public static MibValidationResult ValidateMultiple(List<string> filePaths, string deviceName)
    {
        var result = new MibValidationResult { DeviceName = deviceName };

        // Phase 1: Collect raw assignments from ALL files (same as ParseMultiple)
        var perFile = new List<(string path, string fileName, string content,
            Dictionary<string, (string parent, int index)> rawAssignments, bool moduleFound)>();

        foreach (var path in filePaths)
        {
            var fileName = Path.GetFileName(path);

            if (!File.Exists(path))
            {
                var missing = new MibFileValidation { FileName = fileName };
                missing.Issues.Add(new MibValidationIssue
                {
                    Severity = "error",
                    Message = "File not found",
                    Context = path
                });
                missing.IssueCount = 1;
                result.Files.Add(missing);
                continue;
            }

            try
            {
                var content = File.ReadAllText(path);
                var stripped = StripComments(content);
                var moduleMatch = ModuleNameRegex.Match(content);
                var rawAssignments = CollectRawAssignments(stripped);
                perFile.Add((path, fileName, stripped, rawAssignments, moduleMatch.Success));
            }
            catch (Exception ex)
            {
                var errFile = new MibFileValidation { FileName = fileName };
                errFile.Issues.Add(new MibValidationIssue
                {
                    Severity = "error",
                    Message = $"Parse error: {ex.Message}",
                    Context = ex.GetType().Name
                });
                errFile.IssueCount = 1;
                result.Files.Add(errFile);
            }
        }

        // Phase 2: Merge all raw assignments and resolve together (cross-file)
        var allAssignments = new Dictionary<string, (string parent, int index)>();
        foreach (var (_, _, _, rawAssignments, _) in perFile)
        {
            foreach (var kvp in rawAssignments)
            {
                if (!allAssignments.ContainsKey(kvp.Key))
                    allAssignments[kvp.Key] = kvp.Value;
            }
        }

        var globalResolved = new Dictionary<string, string>(RootOids);
        int lastCount;
        do
        {
            lastCount = globalResolved.Count;
            foreach (var kvp in allAssignments)
            {
                if (globalResolved.ContainsKey(kvp.Key)) continue;
                if (globalResolved.TryGetValue(kvp.Value.parent, out var parentOid))
                    globalResolved[kvp.Key] = $"{parentOid}.{kvp.Value.index}";
            }
        } while (globalResolved.Count > lastCount);

        // Phase 3: Validate each file using the globally-resolved dictionary
        foreach (var (path, fileName, stripped, rawAssignments, moduleFound) in perFile)
        {
            var validation = new MibFileValidation { FileName = fileName };

            if (!moduleFound)
            {
                validation.Issues.Add(new MibValidationIssue
                {
                    Severity = "warning",
                    Message = "No MODULE-IDENTITY or DEFINITIONS found",
                    Context = "Could not detect module name"
                });
            }

            // Check for unresolved names (only truly unresolved across ALL files)
            foreach (var kvp in rawAssignments)
            {
                if (kvp.Key.StartsWith("_synth_")) continue; // Skip synthetic intermediate nodes
                if (!globalResolved.ContainsKey(kvp.Key))
                {
                    validation.Issues.Add(new MibValidationIssue
                    {
                        Severity = "warning",
                        Message = $"Unresolved parent '{kvp.Value.parent}' for '{kvp.Key}'",
                        Context = $"::= {{ {kvp.Value.parent} {kvp.Value.index} }}"
                    });
                }
            }

            // Check definitions for missing metadata
            var resolvedCount = 0;
            var duplicateOids = new Dictionary<string, string>();

            foreach (var kvp in rawAssignments)
            {
                if (kvp.Key.StartsWith("_synth_")) continue; // Skip synthetic intermediate nodes
                if (!globalResolved.TryGetValue(kvp.Key, out var oid)) continue;
                resolvedCount++;

                // Check for duplicate OIDs
                if (duplicateOids.TryGetValue(oid, out var existingName))
                {
                    validation.Issues.Add(new MibValidationIssue
                    {
                        Severity = "warning",
                        Message = $"Duplicate OID: '{kvp.Key}' and '{existingName}' both map to {oid}",
                        Context = oid
                    });
                }
                else
                {
                    duplicateOids[oid] = kvp.Key;
                }

                // Check for missing SYNTAX/ACCESS on OBJECT-TYPE definitions
                var defRegion = ExtractDefinitionRegion(stripped, kvp.Key);
                if (defRegion != null && defRegion.Contains("OBJECT-TYPE"))
                {
                    if (!SyntaxRegex.IsMatch(defRegion) && !defRegion.Contains("SYNTAX"))
                    {
                        validation.Issues.Add(new MibValidationIssue
                        {
                            Severity = "warning",
                            Message = $"Missing SYNTAX clause for '{kvp.Key}'",
                            Context = oid
                        });
                    }

                    if (!AccessRegex.IsMatch(defRegion))
                    {
                        validation.Issues.Add(new MibValidationIssue
                        {
                            Severity = "warning",
                            Message = $"Missing ACCESS clause for '{kvp.Key}'",
                            Context = oid
                        });
                    }
                }
            }

            validation.DefinitionCount = resolvedCount;
            validation.IssueCount = validation.Issues.Count;
            result.Files.Add(validation);
        }

        // Phase 4: Extract IMPORTS dependencies
        result.Dependencies = ExtractDependencies(filePaths);

        return result;
    }

    /// <summary>
    /// Extract IMPORTS dependencies from MIB files and classify each as loaded/standard/missing.
    /// </summary>
    public static List<MibFileDependencies> ExtractDependencies(List<string> filePaths)
    {
        var standardModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SNMPv2-SMI", "SNMPv2-TC", "SNMPv2-CONF", "SNMPv2-MIB",
            "RFC1155-SMI", "RFC-1212", "RFC1213-MIB", "SNMPv2-TM"
        };

        // Phase 1: Read each file, detect module name, parse IMPORTS
        var fileModules = new List<(string fileName, string moduleName, List<string> importedModules)>();

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            var content = File.ReadAllText(path);
            var fileName = Path.GetFileName(path);
            var stripped = StripComments(content);

            var moduleMatch = ModuleNameRegex.Match(content);
            var moduleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : fileName;

            var importedModules = new List<string>();
            var importsMatch = ImportsBlockRegex.Match(stripped);
            if (importsMatch.Success)
            {
                var block = importsMatch.Groups[1].Value;
                foreach (Match fromMatch in FromClauseRegex.Matches(block))
                {
                    var depModule = fromMatch.Groups[1].Value;
                    if (!importedModules.Contains(depModule))
                        importedModules.Add(depModule);
                }
            }

            fileModules.Add((fileName, moduleName, importedModules));
        }

        // Phase 2: Build module → providing file lookup
        var moduleProviders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, moduleName, _) in fileModules)
        {
            if (!moduleProviders.ContainsKey(moduleName))
                moduleProviders[moduleName] = fileName;
        }

        // Phase 3: Classify each import
        var results = new List<MibFileDependencies>();

        foreach (var (fileName, moduleName, importedModules) in fileModules)
        {
            var deps = new MibFileDependencies
            {
                FileName = fileName,
                ModuleName = moduleName
            };

            foreach (var imp in importedModules)
            {
                var dep = new MibDependency { ModuleName = imp };

                if (standardModules.Contains(imp))
                {
                    dep.Status = "standard";
                }
                else if (moduleProviders.TryGetValue(imp, out var provider))
                {
                    dep.Status = "loaded";
                    dep.ProvidedBy = provider;
                }
                else
                {
                    dep.Status = "missing";
                }

                deps.Imports.Add(dep);
            }

            results.Add(deps);
        }

        return results;
    }

    /// <summary>Validate a single MIB file in isolation (no cross-file resolution).</summary>
    public static MibFileValidation ValidateFile(string filePath)
    {
        // Delegate to ValidateMultiple with a single file
        var result = ValidateMultiple(new List<string> { filePath }, Path.GetFileName(filePath));
        return result.Files.Count > 0 ? result.Files[0] : new MibFileValidation { FileName = Path.GetFileName(filePath) };
    }

    private static string StripComments(string content)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool inString = false;
            for (int j = 0; j < line.Length - 1; j++)
            {
                if (line[j] == '"') inString = !inString;
                if (!inString && line[j] == '-' && line[j + 1] == '-')
                {
                    lines[i] = line.Substring(0, j);
                    break;
                }
            }
        }
        return string.Join("\n", lines);
    }
}
