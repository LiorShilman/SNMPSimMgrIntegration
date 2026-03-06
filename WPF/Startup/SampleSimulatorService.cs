using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SNMPSimMgr.Devices;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Startup
{
    /// <summary>
    /// Sample ISimulatorService — routes IDD SET commands to the correct device handler.
    /// In your real system, replace this with your actual hardware communication layer.
    /// </summary>
    public class SampleSimulatorService : ISimulatorService
    {
        private readonly List<ActiveSimulator>  _active = new List<ActiveSimulator>();

        public IReadOnlyList<ActiveSimulator> ActiveSimulators => _active;

        public void RaiseIddSet(string deviceId, string fieldId, string value)
        {
            // Route to the correct IDD device handler based on deviceId.
            // This is the equivalent of routing SNMP SET to the correct device port.
            switch (deviceId)
            {
                case SampleIddDevice.DeviceId:
                    SampleIddDevice.HandleSet(fieldId, value);
                    break;

                // ── Add your own IDD devices here ──
                // case MyCustomDevice.DeviceId:
                //     MyCustomDevice.HandleSet(fieldId, value);
                //     break;

                default:
                    System.Diagnostics.Debug.WriteLine(
                        $"[Simulator] Unknown IDD device: {deviceId}, field={fieldId}, value={value}");
                    // Still broadcast back so Angular gets feedback
                    IntegrationWiring.NotifyIddSet(deviceId, fieldId, value);
                    break;
            }
        }
    }
}
