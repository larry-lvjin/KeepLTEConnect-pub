using System.Windows;
using KeepLTEConnect.Protocol;

namespace KeepLTEConnect;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SimFrame.SelfTest();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Protocol self-test failed:\n{ex.Message}\n\nThe SIM frame implementation does not match the spec. " +
                "The app will exit to avoid sending malformed frames to the SIM Simulator.",
                "KeepLTEConnect — fatal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
    }
}
