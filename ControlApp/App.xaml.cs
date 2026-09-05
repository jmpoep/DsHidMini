using System.IO;
using System.Windows.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Nefarius.DsHidMini.ControlApp.Models;
using Nefarius.DsHidMini.ControlApp.Models.DshmConfigManager;
using Nefarius.DsHidMini.ControlApp.Models.Util.Web;
using Nefarius.DsHidMini.ControlApp.Services;
using Nefarius.DsHidMini.ControlApp.ViewModels.Pages;
using Nefarius.DsHidMini.ControlApp.ViewModels.Windows;
using Nefarius.DsHidMini.ControlApp.Views.Pages;
using Nefarius.DsHidMini.ControlApp.Views.Windows;
using Nefarius.Utilities.DeviceManagement.PnP;

using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace Nefarius.DsHidMini.ControlApp;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App
{
    private const string SingleInstanceMutexName = "Nefarius.DsHidMini.ControlApp.SingleInstance";
    private const string ShowWindowEventName = "Nefarius.DsHidMini.ControlApp.ShowWindow";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showWindowEvent;
    private static CancellationTokenSource? _showWindowListenCts;
    private static bool _hostStarted;

    /// <summary>
    ///     True once a real shutdown has been requested (tray Exit, Restart as Admin, or Windows session end).
    /// </summary>
    public static bool IsExiting { get; private set; }

    /// <summary>
    ///     Exit the process even when minimize-to-tray is enabled.
    /// </summary>
    public static void RequestExit()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private static readonly IHost AppHost = Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(c =>
        {
            c.SetBasePath(Path.GetDirectoryName(Environment.ProcessPath!)!);
        })
        .ConfigureServices((context, services) =>
        {
            services.AddHostedService<ApplicationHostService>();

            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddNavigationViewPageProvider();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();

            services.AddSingleton<DeviceNotificationListener>();
            services.AddSingleton<AppSnackbarMessagesService>();

            services.AddSingleton<DshmDevMan>();
            services.AddSingleton<DshmConfigManager>();

            services.AddSingleton<DevicesPage>();
            services.AddSingleton<DevicesViewModel>();
            services.AddSingleton<ProfilesPage>();
            services.AddSingleton<ProfilesViewModel>();
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<Main>();

            services.AddSingleton<AddressValidator>();

            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#endif
                .WriteTo.File(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "DsHidMini\\Log\\ControlAppLog.txt"))
                .CreateLogger();

            services.AddSerilog(Log.Logger);

            services.AddHttpClient("Buildbot", client =>
            {
                client.BaseAddress = new Uri("https://buildbot.nefarius.at/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(context.HostingEnvironment.ApplicationName);
            });

            services.AddHttpClient("Docs", client =>
            {
                client.BaseAddress = new Uri("https://docs.nefarius.at/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(context.HostingEnvironment.ApplicationName);
            });
        }).Build();

    /// <summary>
    ///     Occurs when the application is loading.
    /// </summary>
    private void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

        if (!createdNew)
        {
            _showWindowEvent.Set();
            Shutdown();
            return;
        }

        Log.Logger.Information("App startup");
        AppHost.Start();
        _hostStarted = true;
        StartListeningForActivation();
    }

    /// <summary>
    ///     Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        Log.Logger.Information("App exiting");
        _showWindowListenCts?.Cancel();
        _showWindowEvent?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (second instance exiting after signaling the first).
            }

            _singleInstanceMutex.Dispose();
        }

        if (_hostStarted)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
    }

    /// <summary>
    ///     Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0

        Log.Logger.Fatal(e.Exception, "Unhandled exception");
    }

    private static void StartListeningForActivation()
    {
        _showWindowListenCts = new CancellationTokenSource();
        CancellationToken token = _showWindowListenCts.Token;
        _ = Task.Factory.StartNew(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_showWindowEvent!.WaitOne(TimeSpan.FromMilliseconds(500)))
                    {
                        Current.Dispatcher.Invoke(ActivateMainWindow, DispatcherPriority.Normal);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal static void ActivateMainWindow()
    {
        if (AppHost.Services.GetService(typeof(MainWindow)) is MainWindow window)
        {
            window.RestoreFromTray();
        }
    }
}