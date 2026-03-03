using System;
using System.Windows;
using SNMPSimMgr.Services;
using SNMPSimMgr.Startup;

namespace SNMPSimMgr;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var deviceStore = new DeviceProfileStore();
        var simulator = new SampleSimulatorService();

        try
        {
            IntegrationWiring.Start(deviceStore, simulator, signalRPort: 5050);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"SignalR failed to start:\n\n{ex.Message}\n\nTip: run as Administrator or:\nnetsh http add urlacl url=http://+:5050/ user=Everyone",
                "SignalR", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IntegrationWiring.Stop();
        base.OnExit(e);
    }
}
