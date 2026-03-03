using System;
using Microsoft.Owin.Hosting;

namespace SNMPSimMgr.Services;

public class SignalRService : IDisposable
{
    private IDisposable? _host;

    public event Action<string>? LogMessage;

    public bool IsRunning => _host != null;
    public int Port { get; private set; }

    public void Start(int port = 5050)
    {
        if (IsRunning) return;

        Port = port;

        // Try binding to all interfaces first, then fall back to localhost
        try
        {
            var url = $"http://+:{port}";
            _host = WebApp.Start<SignalRStartup>(url);
            Log($"SignalR server started on {url}");
        }
        catch
        {
            try
            {
                var url = $"http://localhost:{port}";
                _host = WebApp.Start<SignalRStartup>(url);
                Log($"SignalR server started on {url} (localhost only — run as admin for network access)");
            }
            catch (Exception ex)
            {
                Log($"SignalR server failed to start: {ex.Message}");
                throw;
            }
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _host?.Dispose();
        _host = null;
        Log("SignalR server stopped.");
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
