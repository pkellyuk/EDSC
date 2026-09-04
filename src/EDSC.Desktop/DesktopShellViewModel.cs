using EDSC.Desktop.Services;
using EDSC.Desktop.ViewModels;
using EDSC.Services;
using EDSC.ViewModels;
using System.Diagnostics;

namespace EDSC.Desktop
{
    /// <summary>
    /// Desktop container view model that exposes the tracking view and the button editor.
    /// </summary>
    public class DesktopShellViewModel
    {
        public ConnectionViewModel Connection { get; }
        public ButtonEditorViewModel ButtonEditor { get; }

        public DesktopShellViewModel(ConnectionViewModel connectionViewModel, IConfigurationService configService)
        {
            Connection = connectionViewModel;
            ButtonEditor = new ButtonEditorViewModel(configService, new EliteBindingsService());

            Debug.WriteLine("[DesktopShellVM] Loading button editor");
            _ = ButtonEditor.LoadAsync();
        }
    }
}
