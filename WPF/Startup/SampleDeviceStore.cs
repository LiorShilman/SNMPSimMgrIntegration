using System.Collections.Generic;
using System.Threading.Tasks;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Startup;

/// <summary>
/// Sample IDeviceStore for testing. Replace with your real implementation.
/// </summary>
public class SampleDeviceStore : IDeviceStore
{
    public Task<List<DeviceProfile>> LoadProfilesAsync()
    {
        return Task.FromResult(new List<DeviceProfile>());
    }

    public Task<List<SnmpRecord>> LoadWalkDataAsync(DeviceProfile device)
    {
        return Task.FromResult(new List<SnmpRecord>());
    }
}
