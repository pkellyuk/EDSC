using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using EDSC.Services.Discovery;
using EDSC.ViewModels;
using EDSC.Views;
using System;
using System.Diagnostics;

namespace EDSC.Android
{
    /// <summary>
    /// Android-specific application class with mobile functionality
    /// </summary>
    public class AndroidApp : App
    {
        private IDiscoveryService? _discoveryService;
        private ConnectionViewModel? _connectionViewModel;

        public override void OnFrameworkInitializationCompleted()
        {
            Debug.WriteLine("[AndroidApp] Entry: OnFrameworkInitializationCompleted");

            base.OnFrameworkInitializationCompleted();

            if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                try
                {
                    // Initialize discovery service (mobile version)
                    _discoveryService = new UdpDiscoveryServiceAndroid();
                    Debug.WriteLine("[AndroidApp] Discovery service initialized");

                    // Initialize connection view model
                    _connectionViewModel = new ConnectionViewModel(_discoveryService);
                    Debug.WriteLine("[AndroidApp] Connection view model initialized");

                    // Create and set connection view as initial view
                    var connectionView = new ConnectionView
                    {
                        DataContext = _connectionViewModel
                    };

                    singleView.MainView = connectionView;
                    Debug.WriteLine("[AndroidApp] Connection view set as main view");

                    // TODO: Optional auto-discover on startup
                    // _ = _connectionViewModel.DiscoverServersAsync();

                    Debug.WriteLine("[AndroidApp] Android application initialized successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AndroidApp] Error during initialization: {ex.Message}");
                    Debug.WriteLine($"[AndroidApp] Stack trace: {ex.StackTrace}");

                    // Show error view
                    singleView.MainView = new TextBlock
                    {
                        Text = $"Initialization error: {ex.Message}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                }
            }

            Debug.WriteLine("[AndroidApp] Exit: OnFrameworkInitializationCompleted");
        }
    }
}
