using System.Reflection;

using Nefarius.DsHidMini.ControlApp.Models;
using Nefarius.DsHidMini.ControlApp.Models.DshmConfigManager;
using Nefarius.DsHidMini.ControlApp.Services;

using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace Nefarius.DsHidMini.ControlApp.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly AppSnackbarMessagesService _appSnackbarMessagesService;
    private readonly DshmConfigManager _dshmConfigManager;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

    private bool _isInitialized;

    public SettingsViewModel(
        DshmConfigManager dshmConfigManager,
        BthPS3StatusService bthPs3,
        AppSnackbarMessagesService appSnackbarMessagesService)
    {
        _dshmConfigManager = dshmConfigManager;
        BthPs3 = bthPs3;
        _appSnackbarMessagesService = appSnackbarMessagesService;
    }

    public BthPS3StatusService BthPs3 { get; }

    /// <summary>
    ///     When enabled (default), the driver requests a self restart on a HID mode mismatch instead of requiring a
    ///     manual reconnect / second replug (see issue #374).
    /// </summary>
    public bool AutoRestartOnHidModeMismatch
    {
        get => _dshmConfigManager.AutoRestartOnHidModeMismatch;
        set
        {
            if (_dshmConfigManager.AutoRestartOnHidModeMismatch == value)
            {
                return;
            }

            _dshmConfigManager.AutoRestartOnHidModeMismatch = value;
            if (!_dshmConfigManager.SaveChangesAndUpdateDsHidMiniConfigFile())
            {
                Log.Logger.Error("Failed to persist AutoRestartOnHidModeMismatch.");
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     When enabled, Minimize and Close hide ControlApp to the notification area instead of exiting
    ///     (see issue #80).
    /// </summary>
    public bool MinimizeToTray
    {
        get => ApplicationConfiguration.Instance.MinimizeToTray;
        set
        {
            if (ApplicationConfiguration.Instance.MinimizeToTray == value)
            {
                return;
            }

            ApplicationConfiguration.Instance.MinimizeToTray = value;
            try
            {
                ApplicationConfiguration.Instance.Save();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Failed to persist MinimizeToTray.");
            }

            OnPropertyChanged();
        }
    }

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
        {
            InitializeViewModel();
        }

        BthPs3.Refresh();

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    private void InitializeViewModel()
    {
        CurrentTheme = ApplicationThemeManager.GetAppTheme();
        AppVersion = $"Dshm_ControlApp_WpfUi - {GetAssemblyVersion()}";

        _isInitialized = true;
    }

    private string GetAssemblyVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString()
               ?? string.Empty;
    }

    [RelayCommand]
    private void OnChangeTheme(string parameter)
    {
        switch (parameter)
        {
            case "theme_light":
                if (CurrentTheme == ApplicationTheme.Light)
                {
                    break;
                }

                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                CurrentTheme = ApplicationTheme.Light;

                break;

            default:
                if (CurrentTheme == ApplicationTheme.Dark)
                {
                    break;
                }

                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                CurrentTheme = ApplicationTheme.Dark;

                break;
        }
    }

    [RelayCommand]
    private void RefreshBthPs3()
    {
        BthPs3.Refresh();
    }

    [RelayCommand]
    private void RectifyBthPs3Settings()
    {
        if (BthPs3.TryRectifySettings())
        {
            _appSnackbarMessagesService.ShowBthPS3SettingsRectifiedMessage();
        }
        else
        {
            _appSnackbarMessagesService.ShowBthPS3SettingsRectifyFailedMessage();
        }
    }
}
