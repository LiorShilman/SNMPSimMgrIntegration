using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SNMPSimMgr.Models
{
    public class MibPanelSchema
    {
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; } = string.Empty;

        [JsonProperty("deviceIp")]
        public string DeviceIp { get; set; } = string.Empty;

        [JsonProperty("devicePort")]
        public int DevicePort { get; set; }

        [JsonProperty("community")]
        public string Community { get; set; } = string.Empty;

        [JsonProperty("snmpVersion")]
        public string SnmpVersion { get; set; } = "V2c";

        [JsonProperty("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonProperty("totalFields")]
        public int TotalFields { get; set; }

        [JsonProperty("modules")]
        public List<MibModuleSchema>  Modules { get; set; } = new List<MibModuleSchema>();
    }

    public class MibModuleSchema
    {
        [JsonProperty("moduleName")]
        public string ModuleName { get; set; } = string.Empty;

        [JsonProperty("scalarCount")]
        public int ScalarCount { get; set; }

        [JsonProperty("tableCount")]
        public int TableCount { get; set; }

        /// <summary>Scalar OIDs — single-value fields (e.g., sysDescr.0, sysName.0)</summary>
        [JsonProperty("scalars")]
        public List<MibFieldSchema>  Scalars { get; set; } = new List<MibFieldSchema>();

        /// <summary>Table OIDs — multi-instance data with columns and rows</summary>
        [JsonProperty("tables")]
        public List<MibTableSchema>  Tables { get; set; } = new List<MibTableSchema>();
    }

    /// <summary>
    /// A single scalar field definition with its current value.
    /// </summary>
    public class MibFieldSchema
    {
        // Identity — 1:1 OID mapping
        [JsonProperty("oid")]
        public string Oid { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; }

        // Access control — determines if field is read-only or editable
        [JsonProperty("access")]
        public string Access { get; set; } = "read-only";

        [JsonProperty("isWritable")]
        public bool IsWritable { get; set; }

        // Type system — determines input control type
        [JsonProperty("inputType")]
        public string InputType { get; set; } = "text";

        [JsonProperty("baseType")]
        public string BaseType { get; set; } = "OCTET STRING";

        [JsonProperty("units")]
        public string Units { get; set; }

        [JsonProperty("displayHint")]
        public string DisplayHint { get; set; }

        // Constraints — for input validation
        [JsonProperty("minValue")]
        public long? MinValue { get; set; }

        [JsonProperty("maxValue")]
        public long? MaxValue { get; set; }

        [JsonProperty("minLength")]
        public long? MinLength { get; set; }

        [JsonProperty("maxLength")]
        public long? MaxLength { get; set; }

        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; }

        // Enum options — for dropdown/select
        [JsonProperty("options")]
        public List<EnumOption> Options { get; set; }

        // Current value (from walk data)
        [JsonProperty("currentValue")]
        public string CurrentValue { get; set; }

        [JsonProperty("currentValueType")]
        public string CurrentValueType { get; set; }

        // Metadata
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("tableIndex")]
        public string TableIndex { get; set; }
    }

    /// <summary>
    /// An SNMP table with column definitions and instance rows.
    /// Angular app renders this as a data grid or repeated card sections.
    /// </summary>
    public class MibTableSchema
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("oid")]
        public string Oid { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("labelColumn")]
        public string LabelColumn { get; set; }       // column name used for row labels (e.g., "ifDescr")

        [JsonProperty("rowCount")]
        public int RowCount { get; set; }

        [JsonProperty("columnCount")]
        public int ColumnCount { get; set; }

        [JsonProperty("columns")]
        public List<MibFieldSchema>  Columns { get; set; } = new List<MibFieldSchema>();

        [JsonProperty("rows")]
        public List<MibTableRow>  Rows { get; set; } = new List<MibTableRow>();
    }

    /// <summary>
    /// A single row (instance) in an SNMP table.
    /// </summary>
    public class MibTableRow
    {
        [JsonProperty("index")]
        public string Index { get; set; } = string.Empty;      // instance index (e.g., "1", "2")

        [JsonProperty("label")]
        public string Label { get; set; }                      // descriptive label (e.g., "FastEthernet0/1")

        [JsonProperty("values")]
        public Dictionary<string, MibCellValue>  Values { get; set; } = new Dictionary<string, MibCellValue>();  // columnOid → value
    }

    /// <summary>
    /// A single cell value in a table row.
    /// </summary>
    public class MibCellValue
    {
        [JsonProperty("value")]
        public string Value { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; }                       // SNMP value type

        [JsonProperty("enumLabel")]
        public string EnumLabel { get; set; }                  // resolved enum label (e.g., "up" for value 1)
    }

    public class EnumOption
    {
        [JsonProperty("label")]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("value")]
        public int Value { get; set; }
    }
}
