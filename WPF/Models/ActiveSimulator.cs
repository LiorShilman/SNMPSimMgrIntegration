namespace SNMPSimMgr.Models;

/// <summary>
/// Represents a running SNMP simulator/agent instance for a device.
/// The Hub uses this to route SNMP GET/SET requests to the correct local port.
/// </summary>
public class ActiveSimulator
{
    /// <summary>Unique device identifier (matches DeviceProfile.Id).</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Human-readable device name.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Local UDP port the SNMP agent is listening on.</summary>
    public int Port { get; set; }
}
