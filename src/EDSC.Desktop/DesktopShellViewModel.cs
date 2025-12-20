using EDSC.Services;
using EDSC.ViewModels;
using System.Diagnostics;

namespace EDSC.Desktop
{
    /// <summary>
    /// Desktop container view model that exposes connection and controls views.
    /// </summary>
    public class DesktopShellViewModel
    {
        public ConnectionViewModel Connection { get; }
        public MainViewModel Controls { get; }

        public DesktopShellViewModel(ConnectionViewModel connectionViewModel, IConfigurationService configService, int port)
        {
            Connection = connectionViewModel;
            Controls = new MainViewModel(new HttpCommandClient(), configService);

            Debug.WriteLine("[DesktopShellVM] Initializing local controls");
            _ = Controls.InitializeAsync("127.0.0.1", port);
        }
    }
}
