using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace SNMPSimMgr.Models
{
    /// <summary>
    /// Container for recorded SNMP data of a single device.
    /// </summary>
    public class DeviceData
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
        public List<SnmpRecord>  WalkData { get; set; } = new List<SnmpRecord>();
    }
}
