using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Discord4KHelper.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                SelfTest.Run();
                Shutdown(0);
            }
            catch (Exception error)
            {
                var log = Environment.GetEnvironmentVariable("SELF_TEST_LOG");
                if (!string.IsNullOrEmpty(log)) File.WriteAllText(log, error.ToString());
                Shutdown(1);
            }
            return;
        }

        new MainWindow().Show();
    }
}
