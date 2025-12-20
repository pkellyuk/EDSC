using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using EDSC.Services.Discovery;
using EDSC.ViewModels;
using System;
using System.Diagnostics;

namespace EDSC.Android
{
    /// <summary>
    /// Android main activity - entry point for the Android app
    /// </summary>
    [Activity(
        Label = "EDSC",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<AndroidApp>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            Debug.WriteLine("[MainActivity] Entry: CustomizeAppBuilder");

            var customBuilder = base.CustomizeAppBuilder(builder)
                .WithInterFont()
                .LogToTrace();

            Debug.WriteLine("[MainActivity] Exit: CustomizeAppBuilder");

            return customBuilder;
        }
    }
}
