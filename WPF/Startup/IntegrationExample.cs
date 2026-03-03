// ═══════════════════════════════════════════════════════════════════
// Integration Example — How to wire the MIB Panel into your real system
// ═══════════════════════════════════════════════════════════════════
//
// This file shows the COMPLETE wiring needed to integrate the MIB Panel
// into your WPF application. You need to:
//
//   1. Implement IDeviceStore  (your device DB / config layer)
//   2. Implement ISimulatorService (your SNMP agent / simulator manager)
//   3. Call IntegrationWiring.Start() during app startup
//   4. Call IntegrationWiring.Stop() during app shutdown
//
// That's it. The Angular client connects automatically.
// ═══════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SNMPSimMgr.Hubs;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.Startup;

/// <summary>
/// One-time wiring that connects your real system to the MIB Panel.
/// Call <see cref="Start"/> during app startup and <see cref="Stop"/> on exit.
/// </summary>
public static class IntegrationWiring
{
    private static SignalRService? _signalR;
    private static OidWatchService? _oidWatch;
    private static PeriodicWalkService? _periodicWalk;
    private static IDeviceStore? _store;

    /// <summary>
    /// Wire everything and start the SignalR server.
    /// </summary>
    /// <param name="deviceStore">Your implementation of IDeviceStore.</param>
    /// <param name="simulatorService">Your implementation of ISimulatorService.</param>
    /// <param name="signalRPort">Port for the SignalR server (default: 5050).</param>
    public static void Start(IDeviceStore deviceStore, ISimulatorService simulatorService, int signalRPort = 5050)
    {
        // ── Create internal services ──
        var recorder = new SnmpRecorderService();
        var mibStore = new MibStore();
        var exportService = new MibPanelExportService(mibStore, deviceStore);
        _oidWatch = new OidWatchService();
        _periodicWalk = new PeriodicWalkService(recorder);
        _store = deviceStore;

        // ── Wire the Hub ──
        SnmpHub.Recorder = recorder;
        SnmpHub.ExportService = exportService;
        SnmpHub.Store = deviceStore;
        SnmpHub.Simulator = simulatorService;
        SnmpHub.MibStoreRef = mibStore;
        SnmpHub.OidWatch = _oidWatch;

        // ── Start SignalR ──
        _signalR = new SignalRService();
        _signalR.Start(signalRPort);

        System.Diagnostics.Debug.WriteLine($"[MibPanel] SignalR server running on port {signalRPort}");
    }

    /// <summary>
    /// Stop the SignalR server. Call during app shutdown.
    /// </summary>
    public static void Stop()
    {
        _periodicWalk?.Stop();
        _periodicWalk = null;
        _signalR?.Dispose();
        _signalR = null;
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper: broadcast events from your system → Angular client
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Call this when SNMP traffic occurs (GET/SET) to update the Angular live view.
    /// </summary>
    public static void NotifyTraffic(string deviceName, string operation, string oid, string value, string sourceIp)
    {
        if (_signalR?.IsRunning != true) return;
        SnmpHub.BroadcastTraffic(deviceName, operation, oid, value, sourceIp);

        // Track OID changes for automation
        if (operation is "SET" or "GET" && _oidWatch != null)
        {
            var previousValue = _oidWatch.NotifyChange(oid, value);
            if (previousValue != value)
            {
                // You can resolve deviceId from your system here
                SnmpHub.BroadcastOidChanged("", deviceName, oid, value, previousValue, sourceIp);
            }
        }
    }

    /// <summary>
    /// Call this when a device simulator starts or stops.
    /// </summary>
    public static void NotifyDeviceStatus(string deviceId, string deviceName, string status)
    {
        if (_signalR?.IsRunning != true) return;
        SnmpHub.BroadcastDeviceStatus(deviceId, deviceName, status);
    }

    /// <summary>
    /// Call this when OID values change (e.g., after a SET or periodic update).
    /// </summary>
    public static void NotifyOidChanged(string deviceId, string deviceName, string oid, string newValue, string previousValue, string source)
    {
        if (_signalR?.IsRunning != true) return;
        SnmpHub.BroadcastOidChanged(deviceId, deviceName, oid, newValue, previousValue, source);
    }

    /// <summary>
    /// Call this after your periodic WALK completes to update the Angular panel
    /// and trigger OID watch callbacks for automation.
    /// Sends one batch broadcast (BroadcastMibUpdate) + per-OID change detection.
    /// </summary>
    /// <param name="deviceId">The device ID.</param>
    /// <param name="deviceName">The device display name.</param>
    /// <param name="walkResults">Dictionary of OID → Value from the WALK results.</param>
    public static void NotifyWalkResults(string deviceId, string deviceName, Dictionary<string, string> walkResults)
    {
        if (_signalR?.IsRunning != true) return;

        // Step A: Batch broadcast — updates all panel fields in one SignalR call
        SnmpHub.BroadcastMibUpdate(deviceId, walkResults);

        // Step B: OID watch — detect changes + fire registered callbacks + broadcast per-OID
        if (_oidWatch != null)
        {
            foreach (var kvp in walkResults)
            {
                var previousValue = _oidWatch.NotifyChange(kvp.Key, kvp.Value);
                if (previousValue != kvp.Value)
                    SnmpHub.BroadcastOidChanged(deviceId, deviceName, kvp.Key, kvp.Value, previousValue, "walk");
            }
        }
    }
    // ═══════════════════════════════════════════════════════════════
    // Periodic WALK — auto-poll a device and push updates to Angular
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Start periodically WALKing a device and broadcasting results to Angular.
    /// Automatically triggered when Angular selects a device (RequestSchema).
    /// Switching devices cancels the previous walk before starting the new one.
    /// </summary>
    /// <param name="deviceId">The device ID to WALK.</param>
    /// <param name="intervalSeconds">Seconds between WALK cycles (default 10).</param>
    public static async Task StartPeriodicWalk(string deviceId, int intervalSeconds = 10)
    {
        if (_periodicWalk == null || _store == null) return;

        var profiles = await _store.LoadProfilesAsync();
        var device = profiles.FirstOrDefault(d => d.Id == deviceId);
        if (device == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MibPanel] StartPeriodicWalk: device {deviceId} not found");
            return;
        }

        _periodicWalk.Start(device, intervalSeconds);
    }

    /// <summary>
    /// Stop the current periodic walk. Safe to call even if not running.
    /// </summary>
    public static void StopPeriodicWalk()
    {
        _periodicWalk?.Stop();
    }

    // ═══════════════════════════════════════════════════════════════
    // IDD SET — handle non-SNMP field changes from Angular
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Call after an IDD SET completes (hardware confirmed the change).
    /// Tracks the change in OidWatch, broadcasts traffic + onOidChanged + onMibUpdated to Angular.
    /// </summary>
    /// <param name="deviceId">The device ID.</param>
    /// <param name="fieldId">The IDD field identifier (e.g., "idd.net.ip").</param>
    /// <param name="value">The new value.</param>
    public static void NotifyIddSet(string deviceId, string fieldId, string value)
    {
        if (_signalR?.IsRunning != true) return;

        // Broadcast traffic event (Angular "traffic" panel)
        SnmpHub.BroadcastTraffic("IDD", "SET", fieldId, value, "localhost");

        // OidWatch: track change + fire registered callbacks (WPF automation)
        if (_oidWatch != null)
        {
            var previousValue = _oidWatch.NotifyChange(fieldId, value);
            if (previousValue != value)
            {
                SnmpHub.BroadcastOidChanged(deviceId, "IDD", fieldId, value, previousValue, "idd-set");
            }
        }

        // Batch update: broadcast so the panel reflects the change immediately
        SnmpHub.BroadcastMibUpdate(deviceId, new Dictionary<string, string> { [fieldId] = value });
    }
}

// ═══════════════════════════════════════════════════════════════════
// Example implementations — REPLACE these with your real system code
// ═══════════════════════════════════════════════════════════════════

/*
// ── Example IDeviceStore ──────────────────────────────────────────
public class MyDeviceStore : IDeviceStore
{
    public Task<List<DeviceProfile>> LoadProfilesAsync()
    {
        // Load from your database, config file, or service:
        var devices = new List<DeviceProfile>
        {
            new DeviceProfile
            {
                Id = "device-001",
                Name = "My Router",
                IpAddress = "192.168.1.1",
                Port = 161,
                Community = "public",
                Version = SnmpVersionOption.V2c,
                MibFilePaths = new List<string>
                {
                    @"C:\MIBs\RFC1213-MIB.txt",
                    @"C:\MIBs\IF-MIB.txt"
                }
            }
        };
        return Task.FromResult(devices);
    }

    public Task<List<SnmpRecord>> LoadWalkDataAsync(DeviceProfile device)
    {
        // Return cached SNMP walk data, or empty list if none:
        return Task.FromResult(new List<SnmpRecord>());
    }
}

// ── Example ISimulatorService ─────────────────────────────────────
public class MySimulatorService : ISimulatorService
{
    private readonly List<ActiveSimulator> _active = new();

    public IReadOnlyList<ActiveSimulator> ActiveSimulators => _active;

    public void RaiseIddSet(string deviceId, string fieldId, string value)
    {
        // Handle IDD (non-SNMP) SET commands from the Angular client.
        // Forward to your hardware/device communication layer:
        Debug.WriteLine($"IDD SET: device={deviceId}, field={fieldId}, value={value}");
    }

    // Call these from your simulator management layer:
    public void AddSimulator(string deviceId, string deviceName, int port)
    {
        _active.Add(new ActiveSimulator { DeviceId = deviceId, DeviceName = deviceName, Port = port });
        IntegrationWiring.NotifyDeviceStatus(deviceId, deviceName, "Running");
    }

    public void RemoveSimulator(string deviceId)
    {
        var sim = _active.FirstOrDefault(s => s.DeviceId == deviceId);
        if (sim != null)
        {
            _active.Remove(sim);
            IntegrationWiring.NotifyDeviceStatus(deviceId, sim.DeviceName, "Stopped");
        }
    }
}

// ── Usage in your App.xaml.cs or Main() ───────────────────────────
//
//   var deviceStore = new MyDeviceStore();
//   var simulatorService = new MySimulatorService();
//   IntegrationWiring.Start(deviceStore, simulatorService, signalRPort: 5050);
//
//   // ... your app runs ...
//
//   // On shutdown:
//   IntegrationWiring.Stop();
//
*/
