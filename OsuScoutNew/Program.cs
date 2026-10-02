using System;
using Avalonia;
using Velopack;

namespace OsuScoutNew
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Velopack setup must run before any UI starts
            VelopackApp.Build().Run();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Also used by Avalonia's design-time tools.
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
