using System.Windows;

namespace IptvRecorder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Apply(Store.LoadSettings().Language);
    }
}
