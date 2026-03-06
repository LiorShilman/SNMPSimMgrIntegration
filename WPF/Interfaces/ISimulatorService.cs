using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Interfaces
{
    /// <summary>
    /// Provides information about active SNMP simulators/agents and handles
    /// IDD (non-SNMP) SET commands. Implement this interface in your real system
    /// to connect the MIB Panel to your simulator/agent management layer.
    /// </summary>
    public interface ISimulatorService
    {
        /// <summary>
        /// Currently running simulators/agents.
        /// Each entry maps a device to its local SNMP agent port.
        /// The Hub uses this to route SET/GET requests to the correct agent.
        /// </summary>
        IReadOnlyList<ActiveSimulator> ActiveSimulators { get; }

        /// <summary>
        /// Dispatch an IDD (non-SNMP) SET command to the real system.
        /// Called when the Angular client sends an IDD field change.
        /// </summary>
        void RaiseIddSet(string deviceId, string fieldId, string value);
    }
}
