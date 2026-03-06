using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.Models
{
    public class DeviceProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 161;
        public SnmpVersionOption Version { get; set; } = SnmpVersionOption.V2c;
        public string Community { get; set; } = "public";
        public SnmpV3Credentials V3Credentials { get; set; }
        public List<string>  MibFilePaths { get; set; } = new List<string>();
        public DeviceStatus Status { get; set; } = DeviceStatus.Idle;

        /// <summary>
        /// Optional path to a pre-built MibPanelSchema JSON file.
        /// When set, RequestSchema loads this file instead of parsing MIB files.
        /// Exported from MIB Browser (SNMP) or IddPanelBuilderService (IDD).
        /// </summary>
        public string SchemaPath { get; set; }

        /// <summary>
        /// Optional IDD (non-SNMP) field definitions. When populated, the device
        /// is treated as an IDD device and uses IddPanelBuilderService for schema.
        /// </summary>
        public List<IddFieldDef> IddFields { get; set; }

        /// <summary>True if this device uses IDD fields instead of SNMP MIBs.</summary>
        [JsonIgnore]
        public bool IsIddDevice => IddFields != null && IddFields.Count > 0;
    }

    public class SnmpV3Credentials
    {
        public string Username { get; set; } = string.Empty;
        public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.MD5;
        public string AuthPassword { get; set; } = string.Empty;
        public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.DES;
        public string PrivPassword { get; set; } = string.Empty;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SnmpVersionOption { V2c, V3 }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthProtocol { MD5, SHA }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PrivProtocol { DES, AES }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeviceStatus { Idle, Recording, Simulating }
}
