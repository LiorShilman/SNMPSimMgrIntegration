using System.Collections.Generic;
using System.Threading.Tasks;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Interfaces;

/// <summary>
/// Provides access to device profiles and their recorded SNMP walk data.
/// Implement this interface in your real system to connect the MIB Panel
/// to your device management layer.
/// </summary>
public interface IDeviceStore
{
    /// <summary>Load all configured device profiles.</summary>
    Task<List<DeviceProfile>> LoadProfilesAsync();

    /// <summary>
    /// Load previously recorded SNMP walk data for a device.
    /// Used to populate initial/default values in the MIB Panel schema.
    /// Return an empty list if no walk data is available.
    /// </summary>
    Task<List<SnmpRecord>> LoadWalkDataAsync(DeviceProfile device);
}
