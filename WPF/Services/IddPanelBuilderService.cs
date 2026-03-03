using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

/// <summary>
/// Builds a MibPanelSchema from IDD (Interface Design Document) field definitions.
/// This allows custom non-SNMP devices to use the same Angular panel UI.
/// </summary>
public static class IddPanelBuilderService
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Export an IDD schema to a JSON file — same format as MIB Browser export.
    /// The file can then be used as SchemaPath in DeviceProfile.
    /// Synchronous — safe to call from constructors and startup code.
    /// </summary>
    public static void ExportSchemaToFile(string deviceName, string deviceIp, List<IddFieldDef> fields, string filePath)
    {
        var schema = BuildFromIdd(deviceName, deviceIp, fields);
        var json = JsonSerializer.Serialize(schema, ExportJsonOptions);
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Build a panel schema from IDD field definitions.
    /// The result uses the same MibPanelSchema format, so Angular renders it identically.
    /// </summary>
    public static MibPanelSchema BuildFromIdd(string deviceName, string deviceIp, List<IddFieldDef> fields)
    {
        var groups = fields.GroupBy(f => f.Group ?? "General").ToList();

        var modules = new List<MibModuleSchema>();
        foreach (var group in groups)
        {
            var scalars = group.Select(MapToField).ToList();
            modules.Add(new MibModuleSchema
            {
                ModuleName = group.Key,
                ScalarCount = scalars.Count,
                TableCount = 0,
                Scalars = scalars,
                Tables = new List<MibTableSchema>()
            });
        }

        return new MibPanelSchema
        {
            DeviceName = deviceName,
            DeviceIp = deviceIp,
            DevicePort = 0,
            Community = "",
            SnmpVersion = "",
            ExportedAt = DateTime.UtcNow,
            TotalFields = fields.Count,
            Modules = modules
        };
    }

    private static MibFieldSchema MapToField(IddFieldDef def)
    {
        var field = new MibFieldSchema
        {
            Oid = def.Id,
            Name = def.Name,
            Description = def.Description,
            InputType = def.Type ?? "text",
            BaseType = MapBaseType(def.Type),
            Access = def.IsWritable ? "read-write" : "read-only",
            IsWritable = def.IsWritable,
            CurrentValue = def.DefaultValue ?? "",
            Units = def.Units
        };

        if (def.Min.HasValue) field.MinValue = def.Min.Value;
        if (def.Max.HasValue) field.MaxValue = def.Max.Value;
        if (def.MaxLength.HasValue) field.MaxLength = def.MaxLength.Value;

        if (def.Options != null && def.Options.Count > 0)
        {
            field.Options = def.Options
                .Select(kvp => new EnumOption { Label = kvp.Key, Value = kvp.Value })
                .ToList();
        }

        return field;
    }

    private static string MapBaseType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "number" or "gauge" or "counter" => "INTEGER",
            "toggle" or "enum" => "INTEGER",
            "ip" => "IpAddress",
            _ => "text"
        };
    }
}

/// <summary>
/// Definition of a single IDD field for panel building.
/// </summary>
public class IddFieldDef
{
    /// <summary>Unique field identifier (used as OID in the panel).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name shown in the panel.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description/tooltip.</summary>
    public string? Description { get; set; }

    /// <summary>Section/group name for organizing fields.</summary>
    public string? Group { get; set; }

    /// <summary>Field type: text, number, enum, toggle, ip, gauge, counter.</summary>
    public string? Type { get; set; }

    /// <summary>Whether the field can be edited from the panel.</summary>
    public bool IsWritable { get; set; }

    /// <summary>Initial/default value.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Display units (e.g., "°C", "%", "MB").</summary>
    public string? Units { get; set; }

    /// <summary>Minimum numeric value.</summary>
    public int? Min { get; set; }

    /// <summary>Maximum numeric value.</summary>
    public int? Max { get; set; }

    /// <summary>Maximum string length.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Enum options: label → numeric value.</summary>
    public Dictionary<string, int>? Options { get; set; }
}
