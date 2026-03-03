using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SNMPSimMgr.Services;

/// <summary>
/// Subscribe to specific OID changes and get notified when values update.
/// Supports exact OID matches and prefix (subtree) watches.
/// Thread-safe — callbacks are dispatched on the WPF UI thread.
/// Tracks previous values so clients can compare old vs new.
/// </summary>
public class OidWatchService
{
    private readonly ConcurrentDictionary<string, List<Action<string, string, string>>> _exactWatches = new();
    private readonly ConcurrentDictionary<string, List<Action<string, string, string>>> _prefixWatches = new();
    private readonly ConcurrentDictionary<string, string> _lastValues = new();

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

    /// <summary>Remove all watches for an exact OID.</summary>
    public void Unwatch(string oid) => _exactWatches.TryRemove(oid, out _);

    /// <summary>Remove all prefix watches for an OID prefix.</summary>
    public void UnwatchPrefix(string oidPrefix) => _prefixWatches.TryRemove(oidPrefix, out _);

    /// <summary>Remove all watches.</summary>
    public void Clear()
    {
        _exactWatches.Clear();
        _prefixWatches.Clear();
    }

    /// <summary>
    /// Called when an OID value changes. Tracks previous value, fires matching callbacks,
    /// and returns the previous value for the caller to use.
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

        return previousValue;
    }

    private static void DispatchSafe(Action<string, string, string> callback, string oid, string newValue, string previousValue)
    {
        try
        {
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
                callback(oid, newValue, previousValue);
            else
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => callback(oid, newValue, previousValue));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OidWatch] Callback error for {oid}: {ex.Message}");
        }
    }
}
