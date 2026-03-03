using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SNMPSimMgr.Models;
using SNMPSimMgr.Startup;

namespace SNMPSimMgr.Services;

/// <summary>
/// Periodically WALKs a device and broadcasts updated values to Angular via SignalR.
/// Start/Stop are safe to call from any thread. Switching devices automatically
/// cancels the previous walk loop before starting the new one.
/// </summary>
public class PeriodicWalkService
{
    private readonly SnmpRecorderService _recorder;
    private CancellationTokenSource? _cts;
    private int _frameCount;

    public PeriodicWalkService(SnmpRecorderService recorder)
    {
        _recorder = recorder;
    }

    public bool IsRunning { get; private set; }
    public string? ActiveDeviceId { get; private set; }

    /// <summary>
    /// Start periodic WALK on the given device. Cancels any previous walk first.
    /// </summary>
    /// <param name="device">The device to WALK.</param>
    /// <param name="intervalSeconds">Seconds between WALK cycles (default 10).</param>
    public void Start(DeviceProfile device, int intervalSeconds = 10)
    {
        // Stop previous walk if running (e.g. user switched devices)
        Stop();

        _cts = new CancellationTokenSource();
        _frameCount = 0;
        ActiveDeviceId = device.Id;
        IsRunning = true;

        var ct = _cts.Token;

        _ = Task.Run(async () =>
        {
            Debug.WriteLine($"[PeriodicWalk] Started for {device.Name} ({device.IpAddress}:{device.Port}), interval={intervalSeconds}s");

            while (!ct.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();

                try
                {
                    // Resolve to simulator endpoint if one is active, otherwise walk the real device
                    var target = SnmpRecorderService.ResolveTarget(device);

                    var results = await _recorder.WalkDeviceAsync(target, ct);

                    if (ct.IsCancellationRequested) break;

                    _frameCount++;
                    var dict = results.ToDictionary(r => r.Oid, r => r.Value);

                    Debug.WriteLine($"[PeriodicWalk] Frame #{_frameCount} captured — {dict.Count} OIDs from {device.Name}");

                    // Broadcast to Angular + trigger OID watch callbacks
                    IntegrationWiring.NotifyWalkResults(device.Id, device.Name, dict);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PeriodicWalk] Error during walk: {ex.Message}");
                }

                // Wait for the remainder of the interval
                var elapsed = sw.Elapsed;
                var delay = TimeSpan.FromSeconds(intervalSeconds) - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }

            Debug.WriteLine($"[PeriodicWalk] Stopped for {device.Name}");
            IsRunning = false;
            ActiveDeviceId = null;
        }, ct);
    }

    /// <summary>
    /// Stop the current periodic walk. Safe to call even if not running.
    /// </summary>
    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        IsRunning = false;
        ActiveDeviceId = null;
    }
}
