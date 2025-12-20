using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Diagnostics;

namespace EDSC.Views
{
    /// <summary>
    /// Code-behind for ConnectionView
    /// </summary>
    public partial class ConnectionView : UserControl
    {
        public ConnectionView()
        {
            Debug.WriteLine("[ConnectionView] Entry: Constructor");

            InitializeComponent();

            Debug.WriteLine("[ConnectionView] Exit: Constructor");
        }

        private void InitializeComponent()
        {
            Debug.WriteLine("[ConnectionView] Entry: InitializeComponent");

            AvaloniaXamlLoader.Load(this);

            Debug.WriteLine("[ConnectionView] Exit: InitializeComponent");
        }
    }
}
