using System.ComponentModel;
using System.Windows.Controls;

using Nefarius.DsHidMini.ControlApp.Models;
using Nefarius.DsHidMini.ControlApp.ViewModels.Windows;

using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Nefarius.DsHidMini.ControlApp.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    private readonly DshmDevMan _dshmDevMan;

    public MainWindow(
        MainWindowViewModel viewModel,
        DshmDevMan dshmDevMan, //
        INavigationService navigationService,
        IServiceProvider serviceProvider,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService
    )
    {
        ViewModel = viewModel;
        DataContext = this;

        _dshmDevMan = dshmDevMan;

        SystemThemeWatcher.Watch(this);

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        InitializeComponent();

        navigationService.SetNavigationControl(RootNavigation);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        ViewModel.OpenFromTrayRequested += (_, _) => RestoreFromTray();
        ViewModel.AppConfig.MinimizeToTrayChanged += OnMinimizeToTrayChanged;

        if (AppNotifyIcon.Menu is ContextMenu menu)
        {
            menu.DataContext = this;
        }
    }

    public MainWindowViewModel ViewModel { get; }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _dshmDevMan.StartListeningForDshmDevices();
        ApplyMinimizeToTraySetting();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (TrayWindowPolicy.ShouldHideOnMinimize(ViewModel.AppConfig.MinimizeToTray, WindowState))
        {
            HideToTray();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (TrayWindowPolicy.ShouldHideInsteadOfClose(ViewModel.AppConfig.MinimizeToTray, App.IsExiting))
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        ViewModel.AppConfig.MinimizeToTrayChanged -= OnMinimizeToTrayChanged;
        if (AppNotifyIcon.IsRegistered)
        {
            AppNotifyIcon.Unregister();
        }

        _dshmDevMan.StopListeningForDshmDevices();
        base.OnClosing(e);
    }

    private void AppNotifyIcon_OnLeftClick(object sender, RoutedEventArgs e)
    {
        RestoreFromTray();
    }

    private void OnMinimizeToTrayChanged(object? sender, EventArgs e)
    {
        ApplyMinimizeToTraySetting();
    }

    private void ApplyMinimizeToTraySetting()
    {
        if (ViewModel.AppConfig.MinimizeToTray)
        {
            if (!AppNotifyIcon.IsRegistered)
            {
                AppNotifyIcon.Register();
            }

            return;
        }

        if (!IsVisible)
        {
            RestoreFromTray();
        }

        if (AppNotifyIcon.IsRegistered)
        {
            AppNotifyIcon.Unregister();
        }
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }


    #region INavigationWindow methods

    public INavigationView GetNavigation()
    {
        return RootNavigation;
    }

    public bool Navigate(Type pageType)
    {
        return RootNavigation.Navigate(pageType);
    }

    public void SetPageService(INavigationViewPageProvider pageService)
    {
        RootNavigation.SetPageProviderService(pageService);
    }

    public void ShowWindow()
    {
        Show();
    }

    public void CloseWindow()
    {
        Close();
    }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }

    #endregion INavigationWindow methods
}
