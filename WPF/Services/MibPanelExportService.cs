using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    public class MibPanelExportService
    {
        private readonly MibStore _mibStore;
        private readonly IDeviceStore _deviceStore;

        private static readonly JsonSerializerOptions  JsonOptions = new JsonSerializerOptions() {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Known "label" column OID suffixes — used to pick the best label for table rows
        private static readonly Dictionary<string, string[]>  LabelColumnPreference = new Dictionary<string, string[]>() {
            // ifTable entry columns: prefer ifDescr, then ifName
            ["1.3.6.1.2.1.2.2.1"] = new[] { "1.3.6.1.2.1.2.2.1.2" },
            // ifXTable entry columns: prefer ifName
            ["1.3.6.1.2.1.31.1.1.1"] = new[] { "1.3.6.1.2.1.31.1.1.1.1" },
            // entPhysicalTable
            ["1.3.6.1.2.1.47.1.1.1.1"] = new[] { "1.3.6.1.2.1.47.1.1.1.1.7", "1.3.6.1.2.1.47.1.1.1.1.2" },
        };

        public MibPanelExportService(MibStore mibStore, IDeviceStore deviceStore)
        {
            _mibStore = mibStore;
            _deviceStore = deviceStore;
        }

        public async Task<MibPanelSchema> BuildSchemaAsync(DeviceProfile device)
        {
            // Load walk data for current values
            var walkData = await _deviceStore.LoadWalkDataAsync(device);
            var walkLookup = new Dictionary<string, SnmpRecord>();
            foreach (var r in walkData)
            {
                if (!walkLookup.ContainsKey(r.Oid))
                    walkLookup[r.Oid] = r;
            }

            // Ensure MIBs are loaded
            if (_mibStore.LoadedOids.Count == 0)
                await _mibStore.LoadForDeviceAsync(device);

            // Classify MIB definitions as scalar vs table column
            // A table column has an IndexParts or its OID pattern matches walk data with instance suffixes
            var allDefs = _mibStore.LoadedOids.Values.ToList();
            var tableColumns = new Dictionary<string, List<MibDefinition>>(); // entryOid → columns
            var scalarDefs = new List<MibDefinition>();

            foreach (var def in allDefs)
            {
                // Find DIRECT instances in walk data: def.Oid + ".X" where X has no dots
                // This avoids matching deep descendants (e.g., table OID matching column instance data)
                var prefix = def.Oid + ".";
                var directInstances = walkData
                    .Where(r => r.Oid.StartsWith(prefix) && !r.Oid.Substring(prefix.Length).Contains('.'))
                    .ToList();

                if (directInstances.Count > 1 ||
                    (directInstances.Count == 1 && directInstances[0].Oid != def.Oid + ".0"))
                {
                    // Multiple direct instances, or single non-.0 instance → table column
                    var lastDot = def.Oid.LastIndexOf('.');
                    var entryOid = lastDot > 0 ? def.Oid.Substring(0, lastDot) : def.Oid;

                    if (!tableColumns.ContainsKey(entryOid))
                        tableColumns[entryOid] = new List<MibDefinition>();
                    tableColumns[entryOid].Add(def);
                }
                else
                {
                    // Scalar: has .0 instance in walk data, or is a readable OBJECT-TYPE without walk data
                    if (walkLookup.ContainsKey(def.Oid + ".0") || walkLookup.ContainsKey(def.Oid))
                    {
                        scalarDefs.Add(def);
                    }
                    else if (!string.IsNullOrEmpty(def.Access) &&
                             def.Access != "not-accessible" &&
                             !string.IsNullOrEmpty(def.Syntax))
                    {
                        // Include readable OBJECT-TYPEs even without walk data
                        scalarDefs.Add(def);
                    }
                    // Skip non-accessible items: tables, entries, notifications, groups, compliance
                }
            }

            // MIB-structural table detection: use INDEX clauses on entry definitions
            // to identify table columns even without Walk data.
            // Entry defs have IndexParts (e.g., "piranhaSettingNumber"), and their
            // children are columns that should be displayed as table fields.
            var entryDefs = allDefs
                .Where(d => !string.IsNullOrEmpty(d.IndexParts))
                .ToDictionary(d => d.Name, d => d.Oid);

            var reclassified = new List<MibDefinition>();
            foreach (var def in scalarDefs)
            {
                if (def.ParentName != null && entryDefs.TryGetValue(def.ParentName, out var entryOid))
                {
                    if (!tableColumns.ContainsKey(entryOid))
                        tableColumns[entryOid] = new List<MibDefinition>();
                    if (!tableColumns[entryOid].Any(c => c.Oid == def.Oid))
                    {
                        tableColumns[entryOid].Add(def);
                        reclassified.Add(def);
                    }
                }
            }
            foreach (var def in reclassified)
                scalarDefs.Remove(def);

            // Infrastructure MIBs that only define base types/structure — not useful in panel
            var excludedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SNMPv2-SMI", "SNMPv2-TC", "SNMPv2-CONF", "SNMPv2-MIB",
                "RFC1155-SMI", "RFC1213-MIB", "RFC-1212", "RFC-1215",
                "IANAifType-MIB", "IF-MIB", "INET-ADDRESS-MIB"
            };

            // Group by module
            var moduleGroups = new Dictionary<string, (List<MibDefinition> scalars, Dictionary<string, List<MibDefinition>> tables)>();

            foreach (var def in scalarDefs)
            {
                var mod = def.ModuleName ?? "Unknown";
                if (excludedModules.Contains(mod)) continue;
                if (!moduleGroups.ContainsKey(mod))
                    moduleGroups[mod] = (new List<MibDefinition>(), new Dictionary<string, List<MibDefinition>>());
                moduleGroups[mod].scalars.Add(def);
            }

            foreach (var kvp in tableColumns)
            {
                var firstDef = kvp.Value.First();
                var mod = firstDef.ModuleName ?? "Unknown";
                if (excludedModules.Contains(mod)) continue;
                if (!moduleGroups.ContainsKey(mod))
                    moduleGroups[mod] = (new List<MibDefinition>(), new Dictionary<string, List<MibDefinition>>());
                moduleGroups[mod].tables[kvp.Key] = kvp.Value;
            }

            // Build schema
            var schema = new MibPanelSchema() {
                DeviceName = device.Name,
                DeviceIp = device.IpAddress,
                DevicePort = device.Port,
                Community = device.Community,
                SnmpVersion = device.Version.ToString(),
                ExportedAt = DateTime.UtcNow,
                Modules = new List<MibModuleSchema>()
            };

            foreach (var kvp in moduleGroups.OrderBy(g => g.Key))
            {
                var module = new MibModuleSchema() {
                    ModuleName = kvp.Key,
                    Scalars = new List<MibFieldSchema>(),
                    Tables = new List<MibTableSchema>()
                };

                // Scalars
                foreach (var def in kvp.Value.scalars.OrderBy(d => d.Oid))
                {
                    var field = MapToField(def);
                    // Attach scalar value (.0 instance)
                    if (walkLookup.TryGetValue(def.Oid + ".0", out var rec))
                    {
                        field.CurrentValue = rec.Value;
                        field.CurrentValueType = rec.ValueType;
                    }
                    else if (walkLookup.TryGetValue(def.Oid, out rec))
                    {
                        field.CurrentValue = rec.Value;
                        field.CurrentValueType = rec.ValueType;
                    }
                    module.Scalars.Add(field);
                }

                // Tables
                foreach (var tableKvp in kvp.Value.tables.OrderBy(t => t.Key))
                {
                    var entryOid = tableKvp.Key;
                    var columns = tableKvp.Value.OrderBy(c => c.Oid).ToList();
                    var table = BuildTable(entryOid, columns, walkData);
                    module.Tables.Add(table);
                }

                module.ScalarCount = module.Scalars.Count;
                module.TableCount = module.Tables.Count;
                schema.Modules.Add(module);
            }

            schema.TotalFields = schema.Modules.Sum(m => m.ScalarCount)
                               + schema.Modules.Sum(m => m.Tables.Sum(t => t.ColumnCount));
            return schema;
        }

        public async Task ExportToFileAsync(DeviceProfile device, string filePath)
        {
            var schema = await BuildSchemaAsync(device);
            var json = JsonSerializer.Serialize(schema, JsonOptions);
            await Task.Run(() => File.WriteAllText(filePath, json));
        }

        private MibTableSchema BuildTable(string entryOid, List<MibDefinition> columns, List<SnmpRecord> walkData)
        {
            // Resolve entry/table name from MibStore or derive from first column
            var entryName = "unknown";
            var entryDescription = (string)null;
            if (_mibStore.LoadedOids.TryGetValue(entryOid, out var entryDef))
            {
                entryName = entryDef.Name;
                entryDescription = entryDef.Description;
            }
            else if (columns.Count > 0)
            {
                entryName = columns[0].ParentName ?? columns[0].Name + "Table";
            }

            var table = new MibTableSchema() {
                Name = entryName,
                Oid = entryOid,
                Description = entryDescription,
                Columns = columns.Select(c => MapToField(c)).ToList(),
                Rows = new List<MibTableRow>()
            };

            // Collect all instance indices from walk data for this table's columns
            var allIndices = new SortedSet<string>(StringComparer.Ordinal);
            var columnOids = columns.Select(c => c.Oid).ToList();

            foreach (var record in walkData)
            {
                foreach (var colOid in columnOids)
                {
                    var prefix = colOid + ".";
                    if (record.Oid.StartsWith(prefix))
                    {
                        var instanceIdx = record.Oid.Substring(prefix.Length);
                        allIndices.Add(instanceIdx);
                        break;
                    }
                }
            }

            // Find the label column for this table
            string labelColumnOid = FindLabelColumn(entryOid, columns, walkData);
            table.LabelColumn = labelColumnOid != null && _mibStore.LoadedOids.TryGetValue(labelColumnOid, out var labelDef)
                ? labelDef.Name : null;

            // Build label map from the label column
            var labelMap = new Dictionary<string, string>();
            if (labelColumnOid != null)
            {
                var labelPrefix = labelColumnOid + ".";
                foreach (var record in walkData)
                {
                    if (record.Oid.StartsWith(labelPrefix))
                    {
                        var idx = record.Oid.Substring(labelPrefix.Length);
                        labelMap[idx] = record.Value;
                    }
                }
            }

            // Build enum lookup for resolving enum labels in cell values
            var enumLookup = new Dictionary<string, Dictionary<string, int>>();
            foreach (var col in columns)
            {
                if (col.EnumValues != null && col.EnumValues.Count > 0)
                    enumLookup[col.Oid] = col.EnumValues;
            }

            // Build rows
            // Sort indices: numeric first, then lexicographic
            var sortedIndices = allIndices
                .OrderBy(idx => int.TryParse(idx, out var n) ? n : int.MaxValue)
                .ThenBy(idx => idx)
                .ToList();

            foreach (var idx in sortedIndices)
            {
                var row = new MibTableRow() {
                    Index = idx,
                    Label = labelMap.TryGetValue(idx, out var lbl) ? lbl : null,
                    Values = new Dictionary<string, MibCellValue>()
                };

                // Fill cell values for each column
                foreach (var col in columns)
                {
                    var cellOid = col.Oid + "." + idx;
                    var walkRecord = walkData.FirstOrDefault(r => r.Oid == cellOid);
                    if (walkRecord != null)
                    {
                        var cell = new MibCellValue() {
                            Value = walkRecord.Value,
                            Type = walkRecord.ValueType
                        };

                        // Resolve enum label
                        if (enumLookup.TryGetValue(col.Oid, out var enumVals) &&
                            int.TryParse(walkRecord.Value, out var intVal))
                        {
                            var match = enumVals.FirstOrDefault(e => e.Value == intVal);
                            if (match.Key != null)
                                cell.EnumLabel = match.Key;
                        }

                        row.Values[col.Oid] = cell;
                    }
                }

                table.Rows.Add(row);
            }

            table.RowCount = table.Rows.Count;
            table.ColumnCount = table.Columns.Count;
            return table;
        }

        private string FindLabelColumn(string entryOid, List<MibDefinition> columns, List<SnmpRecord> walkData)
        {
            // Priority 1: known label column preferences
            if (LabelColumnPreference.TryGetValue(entryOid, out var preferred))
            {
                foreach (var pref in preferred)
                {
                    if (columns.Any(c => c.Oid == pref))
                        return pref;
                }
            }

            // Priority 2: column named *Descr, *Name, *Alias
            foreach (var col in columns)
            {
                var name = col.Name.ToLowerInvariant();
                if (name.EndsWith("descr") || name.EndsWith("name") || name.EndsWith("alias"))
                    return col.Oid;
            }

            // Priority 3: first OCTET STRING column with non-empty values
            foreach (var col in columns)
            {
                var bt = (col.BaseType ?? col.Syntax ?? "").ToUpperInvariant();
                if (bt.Contains("STRING") || bt.Contains("DISPLAYSTRING"))
                {
                    var prefix = col.Oid + ".";
                    if (walkData.Any(r => r.Oid.StartsWith(prefix) &&
                        r.ValueType == "OctetString" &&
                        !string.IsNullOrEmpty(r.Value) &&
                        r.Value.Length > 2))
                    {
                        return col.Oid;
                    }
                }
            }

            return null;
        }

        private static MibFieldSchema MapToField(MibDefinition def)
        {
            var access = def.Access ?? "read-only";
            var isWritable = access.IndexOf("write", StringComparison.OrdinalIgnoreCase) >= 0
                          || access.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0;

            var field = new MibFieldSchema() {
                Oid = def.Oid,
                Name = def.Name,
                Description = def.Description,
                Access = access,
                IsWritable = isWritable,
                BaseType = def.BaseType ?? def.Syntax ?? "OCTET STRING",
                Units = def.Units,
                DisplayHint = def.DisplayHint,
                DefaultValue = def.DefVal,
                Status = def.Status,
                TableIndex = def.IndexParts,
                MinValue = def.RangeMin,
                MaxValue = def.RangeMax,
                MinLength = def.SizeMin,
                MaxLength = def.SizeMax,
            };

            // Map InputType based on SNMP type
            field.InputType = MapInputType(def, isWritable);

            // Enum options
            if (def.EnumValues != null && def.EnumValues.Count > 0)
            {
                field.Options = def.EnumValues
                    .OrderBy(e => e.Value)
                    .Select(e => new EnumOption { Label = e.Key, Value = e.Value })
                    .ToList();
            }

            return field;
        }

        private static string MapInputType(MibDefinition def, bool isWritable)
        {
            var baseType = (def.BaseType ?? def.Syntax ?? "").Trim();

            // Enum classification: toggle / status-led / dropdown
            if (def.EnumValues != null && def.EnumValues.Count > 0)
            {
                // Writable 2-value enum with values 0,1 → toggle switch
                if (isWritable && def.EnumValues.Count == 2)
                {
                    var vals = def.EnumValues.Values.OrderBy(v => v).ToList();
                    if (vals[0] == 0 && vals[1] == 1)
                        return "toggle";
                }

                // Read-only status enum → LED indicator
                if (!isWritable && IsStatusEnum(def.EnumValues))
                    return "status-led";

                return "enum";
            }

            switch (baseType.ToUpperInvariant())
            {
                case "COUNTER32":
                case "COUNTER64":
                    return "counter";

                case "GAUGE32":
                case "UNSIGNED32":
                    return "gauge";

                case "TIMETICKS":
                    return "timeticks";

                case "IPADDRESS":
                    return "ip";

                case "OBJECT IDENTIFIER":
                    return "oid";

                case "INTEGER":
                case "INTEGER32":
                    // If range is 0..1 or 1..2, treat as toggle
                    if (def.RangeMin == 0 && def.RangeMax == 1)
                        return "toggle";
                    return "number";

                case "BITS":
                    return "bits";

                case "OCTET STRING":
                    return "text";

                default:
                    // For textual conventions, try to infer
                    if (baseType.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "text";
                    if (def.RangeMin != null || def.RangeMax != null)
                        return "number";
                    if (def.SizeMin != null || def.SizeMax != null)
                        return "text";
                    return "text";
            }
        }

        // Known status vocabulary for LED indicator detection
        private static readonly HashSet<string> StatusKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ok", "fail", "fault", "alarm", "normal", "low", "high",
            "on", "off", "up", "down", "enabled", "disabled",
            "active", "inactive", "error", "warning", "critical"
        };

        private static bool IsStatusEnum(Dictionary<string, int> enumValues)
            => enumValues.Keys.All(label =>
                StatusKeywords.Contains(label) ||
                label.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
