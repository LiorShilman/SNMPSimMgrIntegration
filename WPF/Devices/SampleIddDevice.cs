// ═══════════════════════════════════════════════════════════════════
// SampleIddDevice — Example of defining an IDD device in code
// ═══════════════════════════════════════════════════════════════════
//
// This class shows how to define a non-SNMP device using IDD fields.
// It's the equivalent of a MIB file — but defined in C# code instead.
//
// To create your own IDD device:
//   1. Copy this file and rename the class
//   2. Change DeviceId, DeviceName, DeviceIp
//   3. Define your fields in BuildFields()
//   4. Add your device in DeviceProfileStore.SeedDefaultDevices()
//   5. Add a case in SampleSimulatorService.RaiseIddSet()
//
// The Angular client will render it identically to an SNMP device.
//
// ═══════════════════════════════════════════════════════════════════
// IDD FIELD TYPE REFERENCE
// ═══════════════════════════════════════════════════════════════════
//
// ┌─────────────┬────────┬─────────────────────────────────────────┐
// │ Type        │ Icon   │ Description                             │
// ├─────────────┼────────┼─────────────────────────────────────────┤
// │ text        │ T      │ Free text input                         │
// │ number      │ #      │ Integer with optional Min/Max range     │
// │ enum        │ ☰      │ Dropdown — requires Options (2+ values) │
// │ toggle      │ ⊘      │ ON/OFF switch — Options must be 0 and 1 │
// │ status-led  │ ◉      │ Read-only LED indicator (always r/o)    │
// │ gauge       │ ◔      │ Numeric gauge with Units                │
// │ counter     │ ⟳      │ Read-only counter                       │
// │ timeticks   │ ⏱      │ Duration (displayed as Xd Yh Zm Ss)    │
// │ ip          │ ⌘      │ IP address (X.X.X.X format)            │
// │ oid         │ ⎆      │ OID path string                        │
// │ bits        │ ⊞      │ Bit flags (hex string)                 │
// └─────────────┴────────┴─────────────────────────────────────────┘
//
// IddFieldDef properties:
//   Id           (string)  — Unique field ID, e.g. "idd.group.name"
//   Name         (string)  — Display name in the panel
//   Description  (string?) — Tooltip text
//   Group        (string?) — Section/module name (fields grouped by this)
//   Type         (string?) — One of the 11 types above
//   IsWritable   (bool)    — true = editable, false = read-only
//   DefaultValue (string?) — Initial value (always string, e.g. "42")
//   Units        (string?) — Display suffix, e.g. "dBm", "°C", "%"
//   Min          (int?)    — Minimum value (number, gauge)
//   Max          (int?)    — Maximum value (number, gauge)
//   MaxLength    (int?)    — Max string length (text)
//   Options      (Dict?)   — Enum/toggle labels: { "Label" = value }
//
// Rules:
//   • toggle  → Options must have exactly 2 entries with values 0 and 1
//   • enum    → Options must have 2+ entries
//   • status-led → IsWritable must be false
//   • DefaultValue is always a string, even for numbers
//
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.Devices;

/// <summary>
/// Example IDD device definition — equivalent of a MIB file for non-SNMP devices.
/// Defines the device identity, fields, groups, types, and constraints.
/// </summary>
public static class SampleIddDevice
{
    // ── Device Identity ──────────────────────────────────────────────
    public const string DeviceId = "idd-sample-001";
    public const string DeviceName = "IDD-Sample-Device";
    public const string DeviceIp = "10.0.0.200";

    /// <summary>
    /// Build a DeviceProfile from this IDD definition.
    /// Register the result in your IDeviceStore so Angular can discover it.
    /// </summary>
    public static DeviceProfile CreateProfile()
    {
        return new DeviceProfile
        {
            Id = DeviceId,
            Name = DeviceName,
            IpAddress = DeviceIp,
            Port = 0,                  // No SNMP port — this is IDD
            IddFields = BuildFields()
        };
    }

    /// <summary>
    /// Define all IDD fields for this device.
    /// Each field becomes a row in the Angular panel, grouped by Group name.
    /// This example demonstrates all 11 field types.
    /// </summary>
    public static List<IddFieldDef> BuildFields() => new()
    {
        // ══════════════════════════════════════════════════════════════
        // Group: Device Info — read-only status fields
        // ══════════════════════════════════════════════════════════════

        // ── text: free text (read-only here) ──
        new()
        {
            Id          = "idd.info.hostname",
            Name        = "Hostname",
            Description = "Device hostname / system name",
            Group       = "Device Info",
            Type        = "text",
            IsWritable  = false,
            DefaultValue = "IDD-Device-Lab"
        },
        new()
        {
            Id          = "idd.info.firmware",
            Name        = "Firmware Version",
            Description = "Current firmware version",
            Group       = "Device Info",
            Type        = "text",
            IsWritable  = false,
            DefaultValue = "2.4.1"
        },

        // ── counter: incrementing counter (read-only) ──
        new()
        {
            Id          = "idd.info.uptime",
            Name        = "Uptime",
            Description = "Seconds since last reboot",
            Group       = "Device Info",
            Type        = "counter",
            IsWritable  = false,
            DefaultValue = "86400",
            Units       = "sec"
        },

        // ── gauge: numeric value with range + units ──
        new()
        {
            Id          = "idd.info.temperature",
            Name        = "Temperature",
            Description = "Internal device temperature",
            Group       = "Device Info",
            Type        = "gauge",
            IsWritable  = false,
            DefaultValue = "42",
            Units       = "°C",
            Min         = -40,
            Max         = 85
        },

        // ── status-led: colored LED indicator (always read-only) ──
        new()
        {
            Id          = "idd.info.hwStatus",
            Name        = "Hardware Status",
            Description = "Overall hardware health",
            Group       = "Device Info",
            Type        = "status-led",
            IsWritable  = false,
            DefaultValue = "1",
            Options     = new() { ["Fault"] = 0, ["OK"] = 1 }
        },

        // ── timeticks: duration (displayed as Xd Yh Zm Ss) ──
        new()
        {
            Id          = "idd.info.lastReboot",
            Name        = "Last Reboot",
            Description = "Time since last reboot in timeticks (100ths of second)",
            Group       = "Device Info",
            Type        = "timeticks",
            IsWritable  = false,
            DefaultValue = "8640000"    // = 1 day in timeticks
        },

        // ── oid: OID path string ──
        new()
        {
            Id          = "idd.info.sysObjectId",
            Name        = "System OID",
            Description = "Device object identifier",
            Group       = "Device Info",
            Type        = "oid",
            IsWritable  = false,
            DefaultValue = "1.3.6.1.4.1.99999.1.1"
        },

        // ══════════════════════════════════════════════════════════════
        // Group: Network Configuration — writable network settings
        // ══════════════════════════════════════════════════════════════

        // ── ip: IP address input (X.X.X.X) ──
        new()
        {
            Id          = "idd.net.ip",
            Name        = "IP Address",
            Description = "Device IP address",
            Group       = "Network Configuration",
            Type        = "ip",
            IsWritable  = true,
            DefaultValue = "192.168.1.100"
        },
        new()
        {
            Id          = "idd.net.mask",
            Name        = "Subnet Mask",
            Group       = "Network Configuration",
            Type        = "ip",
            IsWritable  = true,
            DefaultValue = "255.255.255.0"
        },
        new()
        {
            Id          = "idd.net.gateway",
            Name        = "Gateway",
            Group       = "Network Configuration",
            Type        = "ip",
            IsWritable  = true,
            DefaultValue = "192.168.1.1"
        },

        // ── toggle: ON/OFF switch (Options must be 0 and 1) ──
        new()
        {
            Id          = "idd.net.dhcp",
            Name        = "DHCP Enabled",
            Description = "Enable or disable DHCP",
            Group       = "Network Configuration",
            Type        = "toggle",
            IsWritable  = true,
            DefaultValue = "0",
            Options     = new() { ["Disabled"] = 0, ["Enabled"] = 1 }
        },

        // ══════════════════════════════════════════════════════════════
        // Group: Control — writable operational settings
        // ══════════════════════════════════════════════════════════════

        // ── enum: dropdown selection (2+ options) ──
        new()
        {
            Id          = "idd.ctrl.logLevel",
            Name        = "Log Level",
            Description = "Syslog severity level",
            Group       = "Control",
            Type        = "enum",
            IsWritable  = true,
            DefaultValue = "3",
            Options     = new()
            {
                ["Emergency"] = 0,
                ["Error"]     = 1,
                ["Warning"]   = 2,
                ["Info"]      = 3,
                ["Debug"]     = 4
            }
        },

        // ── number: integer with min/max range ──
        new()
        {
            Id          = "idd.ctrl.txPower",
            Name        = "TX Power",
            Description = "Transmit power in dBm",
            Group       = "Control",
            Type        = "number",
            IsWritable  = true,
            DefaultValue = "25",
            Units       = "dBm",
            Min         = 0,
            Max         = 50
        },

        // ── text: writable text with max length ──
        new()
        {
            Id          = "idd.ctrl.sysName",
            Name        = "System Name",
            Description = "Editable system name / label",
            Group       = "Control",
            Type        = "text",
            IsWritable  = true,
            DefaultValue = "Lab-Device-01",
            MaxLength   = 64
        },

        // ── bits: bit flags (hex string) ──
        new()
        {
            Id          = "idd.ctrl.features",
            Name        = "Feature Flags",
            Description = "Enabled features as hex bit mask",
            Group       = "Control",
            Type        = "bits",
            IsWritable  = true,
            DefaultValue = "0x0F"
        },
    };

    // ══════════════════════════════════════════════════════════════════
    // IDD SET handler — called when Angular changes a field value
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle an IDD SET for this specific device.
    /// This is where you send the command to your real hardware.
    /// Call IntegrationWiring.NotifyIddSet() after hardware confirms.
    /// </summary>
    public static void HandleSet(string fieldId, string value)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[{DeviceName}] SET: {fieldId} = {value}");

        // ┌──────────────────────────────────────────────────────────┐
        // │  YOUR HARDWARE CODE HERE                                 │
        // │                                                          │
        // │  Examples:                                               │
        // │    TcpClient.Send($"SET {fieldId}={value}\r\n");         │
        // │    SerialPort.Write($"CMD:{fieldId}={value}\r\n");       │
        // │    HttpClient.PostAsync(url, content);                   │
        // │    Modbus.WriteSingleRegister(address, value);           │
        // └──────────────────────────────────────────────────────────┘

        // After hardware confirms — broadcast the new value back to Angular
        Startup.IntegrationWiring.NotifyIddSet(DeviceId, fieldId, value);
    }
}
