using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Diagnostics;

namespace EDSC.Views
{
    /// <summary>
    /// Code-behind for MainView
    /// </summary>
    public partial class MainView : UserControl
    {
        public MainView()
        {
            Debug.WriteLine("[MainView] Entry: Constructor");

            InitializeComponent();

            Debug.WriteLine("[MainView] Exit: Constructor");
        }

        private void InitializeComponent()
        {
            Debug.WriteLine("[MainView] Entry: InitializeComponent");

            AvaloniaXamlLoader.Load(this);

            Debug.WriteLine("[MainView] Exit: InitializeComponent");
        }
    }
}
