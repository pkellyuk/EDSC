using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Diagnostics;

namespace EDSC
{
    /// <summary>
    /// Main application class - shared between Desktop and Android
    /// </summary>
    public partial class App : Application
    {
        public override void Initialize()
        {
            Debug.WriteLine("[App] Entry: Initialize");

            AvaloniaXamlLoader.Load(this);

            Debug.WriteLine("[App] Exit: Initialize");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Debug.WriteLine("[App] Entry: OnFrameworkInitializationCompleted");

            // Platform-specific initialization happens in Desktop/Android projects
            // This base implementation just ensures framework is ready

            base.OnFrameworkInitializationCompleted();

            Debug.WriteLine("[App] Exit: OnFrameworkInitializationCompleted");
        }
    }
}
