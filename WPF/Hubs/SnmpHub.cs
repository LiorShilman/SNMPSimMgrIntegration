using System.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Newtonsoft.Json;
using SNMPSimMgr.Interfaces;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;
using SNMPSimMgr.Startup;

namespace SNMPSimMgr.Hubs
{
    [HubName("snmpHub")]
    public class SnmpHub : Hub
    {
        // Static service references — set once during App startup.
        // SignalR 2.0 creates a new Hub instance per request; statics are the
        // standard pattern when there is no DI container.
        public static SnmpRecorderService Recorder { get; set; }
        public static MibPanelExportService ExportService { get; set; }
        public static IDeviceStore Store { get; set; }
        public static ISimulatorService Simulator { get; set; }
        public static MibStore MibStoreRef { get; set; }
        public static OidWatchService OidWatch { get; set; }

        // ── Schema cache ─────────────────────────────────────────────────
        // Avoids re-parsing MIBs and rebuilding the schema on every device switch.
        // Key = deviceId, Value = (cacheKey based on MIB file paths, cached schema).
        private static readonly Dictionary<string, (string cacheKey, MibPanelSchema schema)>  SchemaCache = new Dictionary<string, (string cacheKey, MibPanelSchema schema)>();

        // For deserializing pre-built schema JSON files (camelCase from MIB Browser export)
        private static readonly System.Text.Json.JsonSerializerOptions  SchemaJsonOptions = new System.Text.Json.JsonSerializerOptions() {
            PropertyNameCaseInsensitive = true
        };

        private static string BuildCacheKey(DeviceProfile device)
        {
            // SchemaPath takes priority — cache key is the file path itself
            if (!string.IsNullOrEmpty(device.SchemaPath))
                return "schema:" + device.SchemaPath;

            // Sorted MIB file paths → deterministic key. If user adds/removes MIBs, key changes.
            var paths = device.MibFilePaths.OrderBy(p => p) ?? Enumerable.Empty<string>();
            return string.Join("|", paths);
        }

        /// <summary>Invalidate cached schema for a device (call when MIBs change).</summary>
        public static void InvalidateSchemaCache(string deviceId)
        {
            lock (SchemaCache) { SchemaCache.Remove(deviceId); }
        }

        // ── Server methods (called by Angular) ──────────────────────────

        /// <summary>
        /// Send an SNMP SET to a device.
        /// Routes to simulator if one is active, otherwise sends directly to the real hardware.
        /// </summary>
        public async Task<SetResult> SendSet(string deviceId, string oid, string value, string valueType)
        {
            try
            {
                if (Store == null || Recorder == null)
                    return new SetResult { Success = false, Message = "Services not initialized" };

                var profiles = await Store.LoadProfilesAsync();
                var device = profiles.FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                    return new SetResult { Success = false, Message = $"Device {deviceId} not found" };

                // ResolveTarget: if simulator is active → route to it, otherwise → real hardware
                var target = SnmpRecorderService.ResolveTarget(device);

                var success = await Recorder.SetAsync(target, oid, value, valueType);
                return new SetResult
                {
                    Success = success,
                    Message = success ? "SET acknowledged" : "SET failed"
                };
            }
            catch (Exception ex)
            {
                return new SetResult { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Send multiple SNMP SETs in batch.
        /// Routes to simulator if active, otherwise sends directly to real hardware.
        /// </summary>
        public async Task<BulkSetResult> SendBulkSet(string deviceId, List<BulkSetItem> items)
        {
            var result = new BulkSetResult() { Total = items?.Count ?? 0 };
            try
            {
                if (Store == null || Recorder == null)
                {
                    result.Results.Add(new BulkSetItemResult { Oid = "", Success = false, Message = "Services not initialized" });
                    return result;
                }

                var profiles = await Store.LoadProfilesAsync();
                var device = profiles.FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                {
                    result.Results.Add(new BulkSetItemResult { Oid = "", Success = false, Message = $"Device {deviceId} not found" });
                    return result;
                }

                // ResolveTarget: if simulator is active → route to it, otherwise → real hardware
                var target = SnmpRecorderService.ResolveTarget(device);

                foreach (var item in items ?? new List<BulkSetItem>())
                {
                    try
                    {
                        var success = await Recorder.SetAsync(target, item.Oid, item.Value, item.ValueType);
                        result.Results.Add(new BulkSetItemResult
                        {
                            Oid = item.Oid,
                            Success = success,
                            Message = success ? "OK" : "SET failed"
                        });
                        if (success) result.Succeeded++;
                        else result.Failed++;
                    }
                    catch (Exception ex)
                    {
                        result.Results.Add(new BulkSetItemResult
                        {
                            Oid = item.Oid,
                            Success = false,
                            Message = ex.Message
                        });
                        result.Failed++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Results.Add(new BulkSetItemResult { Oid = "", Success = false, Message = ex.Message });
            }
            return result;
        }

        /// <summary>Build and return a full MIB panel schema for a device.</summary>
        public async Task<object> RequestSchema(string deviceId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestSchema called for: {deviceId}");

                if (Store == null || ExportService == null || MibStoreRef == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] Service null — Store={Store != null}, Export={ExportService != null}, MibStore={MibStoreRef != null}");
                    return null;
                }

                var profiles = await Store.LoadProfilesAsync();
                var device = profiles.FirstOrDefault(d => d.Id == deviceId);
                if (device == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] Device not found: {deviceId}. Known: {string.Join(", ", profiles.Select(p => p.Id))}");
                    return null;
                }

                // Check schema cache — skip MIB reload + schema build if MIBs haven't changed
                var cacheKey = BuildCacheKey(device);
                lock (SchemaCache)
                {
                    if (SchemaCache.TryGetValue(deviceId, out var cached) && cached.cacheKey == cacheKey)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema cache HIT for {device.Name} ({cached.schema.TotalFields} fields)");
                        return cached.schema;
                    }
                }

                // Pre-built schema JSON — load directly, skip MIB parsing / IDD build
                if (!string.IsNullOrEmpty(device.SchemaPath) && File.Exists(device.SchemaPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] Loading pre-built schema from: {device.SchemaPath}");
                    var json = await Task.Run(() => File.ReadAllText(device.SchemaPath));
                    var fileSchema = System.Text.Json.JsonSerializer.Deserialize<MibPanelSchema>(json, SchemaJsonOptions);
                    if (fileSchema != null)
                    {
                        lock (SchemaCache) { SchemaCache[deviceId] = (cacheKey, fileSchema); }
                        System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema loaded from file: {fileSchema.TotalFields} fields, {fileSchema.Modules.Count} modules");
                        // Start periodic WALK for SNMP devices (not IDD)
                        if (!device.IsIddDevice)
                            _ = Task.Run(() => IntegrationWiring.StartPeriodicWalk(deviceId));
                        return fileSchema;
                    }
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] Failed to deserialize schema from: {device.SchemaPath}");
                }

                // IDD device — build schema from IDD field definitions (no SNMP)
                if (device.IsIddDevice)
                {
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] IDD device: {device.Name}, building IDD schema...");
                    var iddSchema = IddPanelBuilderService.BuildFromIdd(device.Name, device.IpAddress, device.IddFields);
                    lock (SchemaCache) { SchemaCache[deviceId] = (cacheKey, iddSchema); }
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] IDD schema built: {iddSchema.TotalFields} fields, {iddSchema.Modules.Count} modules");
                    // No periodic WALK for IDD devices — they don't speak SNMP
                    return iddSchema;
                }

                System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema cache MISS for {device.Name}, loading MIBs...");

                // Skip UI updates (ObservableCollection) — avoids Dispatcher deadlocks
                await MibStoreRef.LoadForDeviceAsync(device, updateUI: false);

                System.Diagnostics.Debug.WriteLine("[SnmpHub] Building schema...");
                var schema = await ExportService.BuildSchemaAsync(device);
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema built: {schema?.TotalFields} fields, {schema?.Modules.Count} modules");

                // Store in cache
                if (schema != null)
                    lock (SchemaCache) { SchemaCache[deviceId] = (cacheKey, schema); }

                // Auto-start periodic WALK — pushes live values to Angular every N seconds
                _ = Task.Run(() => IntegrationWiring.StartPeriodicWalk(deviceId));

                return schema;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestSchema ERROR: {ex}");
                return null;
            }
        }

        /// <summary>Batch GET all scalar OIDs and return updated values.</summary>
        public async Task<Dictionary<string, string>> RequestRefresh(string deviceId)
        {
            var result = new Dictionary<string, string>();
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh called for: {deviceId}");

                if (Store == null || Recorder == null || ExportService == null || MibStoreRef == null)
                {
                    System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: services null");
                    return result;
                }

                var profiles = await Store.LoadProfilesAsync();
                var device = profiles.FirstOrDefault(d => d.Id == deviceId);
                if (device == null) { System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: device not found"); return result; }

                // ResolveTarget: if simulator is active → route to it, otherwise → real hardware
                var target = SnmpRecorderService.ResolveTarget(device);

                // Use cached schema for OID collection — avoid re-parsing MIBs
                MibPanelSchema schema = null;
                var cacheKey = BuildCacheKey(device);
                bool cacheHit = false;
                lock (SchemaCache)
                {
                    if (SchemaCache.TryGetValue(deviceId, out var cached) && cached.cacheKey == cacheKey)
                    {
                        schema = cached.schema;
                        cacheHit = true;
                    }
                }

                if (!cacheHit)
                {
                    System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: schema cache MISS, rebuilding...");

                    // Skip UI updates (ObservableCollection) — avoids Dispatcher deadlocks
                    await MibStoreRef.LoadForDeviceAsync(device, updateUI: false);

                    schema = await ExportService.BuildSchemaAsync(device);
                    if (schema != null)
                        lock (SchemaCache) { SchemaCache[deviceId] = (cacheKey, schema); }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: schema cache HIT, skipping MIB reload");
                }

                if (schema == null) return result;

                // Collect all OIDs: scalars + table cells
                var allOids = new List<string>();

                // Scalar OIDs (append .0 for instance)
                allOids.AddRange(schema.Modules
                    .SelectMany(m => m.Scalars)
                    .Select(f => f.Oid.EndsWith(".0") ? f.Oid : f.Oid + ".0"));

                // Table cell OIDs: column.oid + "." + row.index
                foreach (var module in schema.Modules)
                foreach (var table in module.Tables)
                foreach (var row in table.Rows)
                foreach (var col in table.Columns)
                    allOids.Add(col.Oid + "." + row.Index);

                System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh: fetching {allOids.Count} OIDs from {target.IpAddress}:{target.Port}");

                // Batch GET in groups of 20 — tolerate per-batch failures
                for (int i = 0; i < allOids.Count; i += 20)
                {
                    try
                    {
                        var batch = allOids.Skip(i).Take(20);
                        var records = await Recorder.GetMultipleAsync(target, batch);
                        foreach (var r in records)
                            result[r.Oid] = r.Value;
                    }
                    catch (Exception batchEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SnmpHub] Batch GET failed (offset {i}): {batchEx.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh: got {result.Count} values");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh ERROR: {ex}");
            }
            return result;
        }

        /// <summary>Send an IDD SET (non-SNMP) to WPF for handling.</summary>
        public Task<SetResult> SendIddSet(string deviceId, string fieldId, string value)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] SendIddSet: device={deviceId}, field={fieldId}, value={value}");

                if (Simulator == null)
                    return Task.FromResult(new SetResult { Success = false, Message = "Simulator service not initialized" });

                // Fire event — WPF handles the actual IDD communication
                Simulator.RaiseIddSet(deviceId, fieldId, value);

                return Task.FromResult(new SetResult
                {
                    Success = true,
                    Message = "IDD SET dispatched"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new SetResult { Success = false, Message = ex.Message });
            }
        }

        /// <summary>List all known devices with their simulator status.</summary>
        public async Task<List<DeviceInfo>> GetDevices()
        {
            if (Store == null) return new List<DeviceInfo>();

            var profiles = await Store.LoadProfilesAsync();
            return profiles.Select(d =>
            {
                var sim = Simulator.ActiveSimulators.FirstOrDefault(s => s.DeviceId == d.Id);
                return new DeviceInfo
                {
                    Id = d.Id,
                    Name = d.Name,
                    IpAddress = d.IpAddress,
                    Port = d.Port,
                    IsSimulating = sim != null,
                    SimulatorPort = sim?.Port ?? 0
                };
            }).ToList();
        }

        // ── Connection lifecycle ────────────────────────────────────────

        public override Task OnConnected()
        {
            System.Diagnostics.Debug.WriteLine($"[SignalR] Client connected: {Context.ConnectionId}");
            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            System.Diagnostics.Debug.WriteLine($"[SignalR] Client disconnected: {Context.ConnectionId}");
            return base.OnDisconnected(stopCalled);
        }

        // ── Static broadcast helpers (called from WPF event handlers) ──

        public static void BroadcastTraffic(string deviceName, string op, string oid, string value, string sourceIp)
        {
            System.Diagnostics.Debug.WriteLine($"[SignalR] BroadcastTraffic: {op} {oid} = '{value}' (device={deviceName}, src={sourceIp})");
            var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
            context.Clients.All.onTrafficReceived(new
            {
                deviceName,
                operation = op,
                oid,
                value,
                sourceIp,
                timestamp = DateTime.UtcNow
            });
        }

        public static void BroadcastDeviceStatus(string deviceId, string deviceName, string status)
        {
            var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
            context.Clients.All.onDeviceStatusChanged(new
            {
                deviceId,
                deviceName,
                status,
                timestamp = DateTime.UtcNow
            });
        }

        public static void BroadcastMibUpdate(string deviceId, Dictionary<string, string> updatedValues)
        {
            var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
            context.Clients.All.onMibUpdated(new
            {
                deviceId,
                values = updatedValues,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Broadcast a targeted OID change event to Angular clients.
        /// Includes previous value, source, and resolved field name for client-side automation.
        /// </summary>
        public static void BroadcastOidChanged(string deviceId, string deviceName, string oid, string newValue, string previousValue, string source)
        {
            // Resolve field name from OID watch mappings, fallback to OID itself (IDD fields are already names)
            var fieldName = OidWatch.ResolveOidToName(oid) ?? oid;

            var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
            context.Clients.All.onOidChanged(new
            {
                deviceId,
                deviceName,
                oid,
                fieldName,
                newValue,
                previousValue,
                source,
                timestamp = DateTime.UtcNow
            });
        }
    }

    // ── DTOs ────────────────────────────────────────────────────────

    public class SetResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }

    public class DeviceInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("ipAddress")]
        public string IpAddress { get; set; } = string.Empty;

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("isSimulating")]
        public bool IsSimulating { get; set; }

        [JsonProperty("simulatorPort")]
        public int SimulatorPort { get; set; }
    }

    // ── Bulk SET DTOs ────────────────────────────────────────────

    public class BulkSetItem
    {
        [JsonProperty("oid")]
        public string Oid { get; set; } = string.Empty;

        [JsonProperty("value")]
        public string Value { get; set; } = string.Empty;

        [JsonProperty("valueType")]
        public string ValueType { get; set; } = "OctetString";
    }

    public class BulkSetResult
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("succeeded")]
        public int Succeeded { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("results")]
        public List<BulkSetItemResult>  Results { get; set; } = new List<BulkSetItemResult>();
    }

    public class BulkSetItemResult
    {
        [JsonProperty("oid")]
        public string Oid { get; set; } = string.Empty;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }
}
