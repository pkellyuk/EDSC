using Avalonia;
using System;
using System.Diagnostics;

namespace EDSC.Desktop
{
    /// <summary>
    /// Desktop application entry point
    /// </summary>
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            Debug.WriteLine("[Program] Entry: Main");

            try
            {
                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Program] Fatal error: {ex.Message}");
                throw;
            }

            Debug.WriteLine("[Program] Exit: Main");
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            Debug.WriteLine("[Program] Entry: BuildAvaloniaApp");

            var builder = AppBuilder.Configure<DesktopApp>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

            Debug.WriteLine("[Program] Exit: BuildAvaloniaApp");

            return builder;
        }
    }
}
