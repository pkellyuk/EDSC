using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Diagnostics;

namespace EDSC
{
    /// <summary>
    /// Main application class for shared Avalonia resources
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

            // Platform-specific initialization happens in the Desktop project.

            base.OnFrameworkInitializationCompleted();

            Debug.WriteLine("[App] Exit: OnFrameworkInitializationCompleted");
        }
    }
}
