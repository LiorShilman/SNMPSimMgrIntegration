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

using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SNMPSimMgr.Hubs;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.Startup
{
    /// <summary>
    /// One-time wiring that connects your real system to the MIB Panel.
    /// Call <see cref="Start"/> during app startup and <see cref="Stop"/> on exit.
    /// </summary>
    public static class IntegrationWiring
    {
        private static SignalRService _signalR;
        private static OidWatchService _oidWatch;
        private static PeriodicWalkService _periodicWalk;
        private static IDeviceStore _store;

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

            // ── Register schemas for name-based watches ──
            // Load all device schemas so WatchByName can resolve field names → OIDs
            Task.Run(async () =>
            {
                try
                {
                    var profiles = await deviceStore.LoadProfilesAsync();
                    foreach (var device in profiles)
                    {
                        if (!string.IsNullOrEmpty(device.SchemaPath) && System.IO.File.Exists(device.SchemaPath))
                        {
                            var json = System.IO.File.ReadAllText(device.SchemaPath);
                            var schema = System.Text.Json.JsonSerializer.Deserialize<MibPanelSchema>(json,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (schema != null)
                                _oidWatch.RegisterSchema(schema);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MibPanel] Schema registration error: {ex.Message}");
                }
            });

            // ── WatchByName examples — real automation logic ──
            // These fire whenever any device's field changes, matched by name.

            // Example 1: Log sysName changes
            _oidWatch.WatchByName("sysName", (oid, newValue, previousValue) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Automation] sysName changed: '{previousValue}' → '{newValue}'");
            });

            // Example 2: Temperature threshold → auto-SET alarm ON/OFF
            _oidWatch.WatchByName("temperature", (oid, newValue, previousValue) =>
            {
                if (int.TryParse(newValue, out var temp) && temp > 80)
                {
                    System.Diagnostics.Debug.WriteLine($"[Automation] ALERT: temperature={temp}°C — triggering alarm");
                    NotifyIddSet("device-001", "alarm-indicator", "ON");
                }
                else if (int.TryParse(newValue, out var tempNormal) && tempNormal <= 60)
                {
                    System.Diagnostics.Debug.WriteLine($"[Automation] Temperature normal: {tempNormal}°C — clearing alarm");
                    NotifyIddSet("device-001", "alarm-indicator", "OFF");
                }
            });

            // Example 3: Interface status change → log UP/DOWN
            _oidWatch.WatchByName("ifOperStatus", (oid, newValue, previousValue) =>
            {
                var status = newValue == "1" ? "UP" : "DOWN";
                System.Diagnostics.Debug.WriteLine($"[Automation] Interface {oid}: {previousValue} → {status}");
            });

            System.Diagnostics.Debug.WriteLine($"[MibPanel] SignalR server running on port {signalRPort} — 3 WatchByName automations registered");
        }

        /// <summary>
        /// Access the OidWatchService to register additional name-based watches.
        /// Use after Start() has been called.
        /// </summary>
        public static OidWatchService OidWatch => _oidWatch;

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
            if ((operation == "SET" || operation == "GET") && _oidWatch != null)
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

        /// <summary>
        /// Call when your hardware sends multiple IDD field updates at once.
        /// More efficient than calling NotifyIddSet() in a loop — sends one batch broadcast.
        /// Each field still gets individual OidWatch change detection + callbacks.
        /// </summary>
        /// <param name="deviceId">The device ID (e.g., SampleIddDevice.DeviceId).</param>
        /// <param name="fieldValues">Dictionary of fieldId → value.</param>
        public static void NotifyIddBatch(string deviceId, Dictionary<string, string> fieldValues)
        {
            if (_signalR?.IsRunning != true) return;

            // Step A: Batch broadcast — updates all panel fields in one SignalR call
            SnmpHub.BroadcastMibUpdate(deviceId, fieldValues);

            // Step B: Per-field change detection + automation callbacks
            if (_oidWatch != null)
            {
                foreach (var kvp in fieldValues)
                {
                    var previousValue = _oidWatch.NotifyChange(kvp.Key, kvp.Value);
                    if (previousValue != kvp.Value)
                    {
                        SnmpHub.BroadcastOidChanged(deviceId, "IDD", kvp.Key, kvp.Value, previousValue, "hardware");
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // USAGE EXAMPLES — How to push IDD field updates from WPF to Angular
    // ═══════════════════════════════════════════════════════════════════
    //
    // ┌───────────────────────────────────────────────────────────────────┐
    // │  FLOW: Hardware → WPF → Angular                                  │
    // │                                                                   │
    // │  1. Your hardware sends a status update (TCP, serial, Modbus...) │
    // │  2. WPF receives it and calls UpdateField / NotifyIddSet         │
    // │  3. SignalR broadcasts to Angular                                 │
    // │  4. Angular panel updates the field value in real-time            │
    // │                                                                   │
    // │  The field ID (e.g., "idd.info.temperature") is used as the      │
    // │  matching key — Angular matches it by field.name in the schema.  │
    // └───────────────────────────────────────────────────────────────────┘
    //
    // ── Example 1: Single field update ────────────────────────────────
    //
    //   // Your hardware callback / event handler:
    //   void OnHardwareTemperatureChanged(double temp)
    //   {
    //       // Push to Angular — updates the "Temperature" field in the panel
    //       SampleIddDevice.UpdateField("idd.info.temperature", temp.ToString());
    //   }
    //
    // ── Example 2: Batch update (multiple fields at once) ─────────────
    //
    //   // Your periodic hardware poll:
    //   void OnHardwarePollComplete(HardwareStatus status)
    //   {
    //       SampleIddDevice.UpdateFields(new Dictionary<string, string>
    //       {
    //           ["idd.info.temperature"] = status.Temperature.ToString(),
    //           ["idd.info.uptime"]      = status.UptimeSeconds.ToString(),
    //           ["idd.info.hwStatus"]    = status.IsHealthy ? "1" : "0",
    //           ["idd.ctrl.txPower"]     = status.TxPower.ToString(),
    //       });
    //   }
    //
    // ── Example 3: Direct call without device class ──────────────────
    //
    //   // If you don't have a device class, call IntegrationWiring directly:
    //   IntegrationWiring.NotifyIddSet("my-device-id", "fieldName", "newValue");
    //
    //   // Or batch:
    //   IntegrationWiring.NotifyIddBatch("my-device-id", new Dictionary<string, string>
    //   {
    //       ["status"]  = "online",
    //       ["signal"]  = "-42",
    //       ["clients"] = "7",
    //   });
    //
    // ── Example 4: Timer-based periodic updates ──────────────────────
    //
    //   // In your App startup, after IntegrationWiring.Start():
    //   var timer = new System.Windows.Threading.DispatcherTimer
    //   {
    //       Interval = TimeSpan.FromSeconds(5)
    //   };
    //   timer.Tick += (s, e) =>
    //   {
    //       // Read from hardware and push to Angular
    //       var temp = ReadTemperatureSensor();
    //       SampleIddDevice.UpdateField("idd.info.temperature", temp.ToString());
    //   };
    //   timer.Start();
    //
    // ── Example 5: Combine with WatchByName for automation ────────────
    //
    //   // Push hardware update → triggers WatchByName callback → auto-SET another field
    //   //
    //   // Step 1: Register automation
    //   IntegrationWiring.OidWatch.WatchByName("Temperature", (oid, newVal, prevVal) =>
    //   {
    //       if (int.TryParse(newVal, out var t) && t > 80)
    //           SampleIddDevice.UpdateField("idd.info.hwStatus", "0");  // Fault
    //   });
    //   //
    //   // Step 2: Hardware reports temperature change
    //   SampleIddDevice.UpdateField("idd.info.temperature", "85");
    //   //
    //   // Result: Angular sees temperature=85 AND hwStatus=Fault (both update)
    //
    // ═══════════════════════════════════════════════════════════════════
}
