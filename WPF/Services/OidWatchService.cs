using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    /// <summary>
    /// Subscribe to specific OID changes and get notified when values update.
    /// Supports exact OID matches, prefix (subtree) watches, and name-based watches.
    /// Thread-safe — callbacks are dispatched on the WPF UI thread.
    /// Tracks previous values so clients can compare old vs new.
    /// </summary>
    public class OidWatchService
    {
        private readonly ConcurrentDictionary<string, List<Action<string, string, string>>> _exactWatches = new ConcurrentDictionary<string, List<Action<string, string, string>>>();
        private readonly ConcurrentDictionary<string, List<Action<string, string, string>>> _prefixWatches = new ConcurrentDictionary<string, List<Action<string, string, string>>>();
        private readonly ConcurrentDictionary<string, List<Action<string, string, string>>> _nameWatches = new ConcurrentDictionary<string, List<Action<string, string, string>>>();
        private readonly ConcurrentDictionary<string, string>  _lastValues = new ConcurrentDictionary<string, string>();

        // Bidirectional name ↔ OID mapping (case-insensitive names)
        private readonly ConcurrentDictionary<string, string> _nameToOid = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string>  _oidToName = new ConcurrentDictionary<string, string>();

        // ── Name ↔ OID Mapping ──

        /// <summary>Register a single name ↔ OID mapping.</summary>
        public void RegisterMapping(string name, string oid)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(oid)) return;
            _nameToOid[name] = oid;
            _oidToName[oid] = name;
            // Also map collapsed scalar: "x.y.z" → "x.y.z.0"
            if (oid.EndsWith(".0"))
                _oidToName[oid.Substring(0, oid.Length - 2)] = name;
        }

        /// <summary>
        /// Register all field names from a MibPanelSchema.
        /// Call this after export, schema load, or device registration.
        /// </summary>
        public void RegisterSchema(MibPanelSchema schema)
        {
            if (schema?.Modules == null) return;
            foreach (var module in schema.Modules)
            {
                foreach (var field in module.Scalars)
                    RegisterMapping(field.Name, field.Oid);
                foreach (var table in module.Tables)
                    foreach (var col in table.Columns)
                        RegisterMapping(col.Name, col.Oid);
            }
            System.Diagnostics.Debug.WriteLine($"[OidWatch] Registered {_nameToOid.Count} name↔OID mappings from schema '{schema.DeviceName}'");
        }

        /// <summary>Resolve a field name to OID. Returns null if not found.</summary>
        public string ResolveNameToOid(string name) =>
            _nameToOid.TryGetValue(name, out var oid) ? oid : null;

        /// <summary>Resolve an OID to field name. Returns null if not found.</summary>
        public string ResolveOidToName(string oid) =>
            _oidToName.TryGetValue(oid, out var name) ? name : null;

        // ── Watch Registration ──

        /// <summary>
        /// Register a callback for an exact OID match.
        /// Callback receives (oid, newValue, previousValue).
        /// </summary>
        public void Watch(string oid, Action<string, string, string> callback)
        {
            _exactWatches.AddOrUpdate(oid,
                _ => new List<Action<string, string, string>> { callback },
                (_, list) => { list.Add(callback); return list; });
        }

        /// <summary>
        /// Register a callback for all OIDs under a prefix (subtree watch).
        /// Callback receives (oid, newValue, previousValue).
        /// </summary>
        public void WatchPrefix(string oidPrefix, Action<string, string, string> callback)
        {
            _prefixWatches.AddOrUpdate(oidPrefix,
                _ => new List<Action<string, string, string>> { callback },
                (_, list) => { list.Add(callback); return list; });
        }

        /// <summary>
        /// Register a callback by field name (e.g., "sysName", "temperature").
        /// Works for both SNMP fields (resolved via schema mapping) and IDD fields
        /// (where the OID IS the name). Case-insensitive.
        /// Callback receives (oid, newValue, previousValue).
        /// </summary>
        public void WatchByName(string fieldName, Action<string, string, string> callback)
        {
            _nameWatches.AddOrUpdate(fieldName.ToLowerInvariant(),
                _ => new List<Action<string, string, string>> { callback },
                (_, list) => { list.Add(callback); return list; });
        }

        /// <summary>Remove all watches for an exact OID.</summary>
        public void Unwatch(string oid) => _exactWatches.TryRemove(oid, out _);

        /// <summary>Remove all prefix watches for an OID prefix.</summary>
        public void UnwatchPrefix(string oidPrefix) => _prefixWatches.TryRemove(oidPrefix, out _);

        /// <summary>Remove all name-based watches for a field name.</summary>
        public void UnwatchByName(string fieldName) => _nameWatches.TryRemove(fieldName.ToLowerInvariant(), out _);

        /// <summary>Remove all watches and mappings.</summary>
        public void Clear()
        {
            _exactWatches.Clear();
            _prefixWatches.Clear();
            _nameWatches.Clear();
        }

        // ── Notification ──

        /// <summary>
        /// Called when an OID value changes. Tracks previous value, fires matching callbacks
        /// (exact OID, prefix, and name-based), and returns the previous value.
        /// </summary>
        public string NotifyChange(string oid, string newValue)
        {
            var previousValue = _lastValues.TryGetValue(oid, out var prev) ? prev : "";
            _lastValues[oid] = newValue;

            // Skip if value hasn't actually changed
            if (previousValue == newValue)
                return previousValue;

            // Exact match
            if (_exactWatches.TryGetValue(oid, out var exactCallbacks))
            {
                foreach (var cb in exactCallbacks.ToList())
                    DispatchSafe(cb, oid, newValue, previousValue);
            }

            // Collapsed scalar: "x.y.z.0" → also check "x.y.z"
            if (oid.EndsWith(".0"))
            {
                var parentOid = oid.Substring(0, oid.Length - 2);
                if (_exactWatches.TryGetValue(parentOid, out var parentCallbacks))
                {
                    foreach (var cb in parentCallbacks.ToList())
                        DispatchSafe(cb, oid, newValue, previousValue);
                }
            }

            // Prefix matches
            foreach (var kvp in _prefixWatches)
            {
                if (oid.StartsWith(kvp.Key))
                {
                    foreach (var cb in kvp.Value.ToList())
                        DispatchSafe(cb, oid, newValue, previousValue);
                }
            }

            // Name-based matches — resolve OID → name, also check if OID itself IS a name (IDD)
            FireNameWatches(oid, oid, newValue, previousValue);
            if (_oidToName.TryGetValue(oid, out var resolvedName))
                FireNameWatches(resolvedName, oid, newValue, previousValue);

            return previousValue;
        }

        private void FireNameWatches(string name, string oid, string newValue, string previousValue)
        {
            if (_nameWatches.TryGetValue(name.ToLowerInvariant(), out var callbacks))
            {
                foreach (var cb in callbacks.ToList())
                    DispatchSafe(cb, oid, newValue, previousValue);
            }
        }

        private static void DispatchSafe(Action<string, string, string> callback, string oid, string newValue, string previousValue)
        {
            try
            {
                if (System.Windows.Application.Current.Dispatcher.CheckAccess() == true)
                    callback(oid, newValue, previousValue);
                else
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => callback(oid, newValue, previousValue)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OidWatch] Callback error for {oid}: {ex.Message}");
            }
        }
    }
}
