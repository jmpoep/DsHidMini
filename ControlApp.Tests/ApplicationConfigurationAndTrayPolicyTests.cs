using System.Windows;

using Nefarius.DsHidMini.ControlApp.Models;

using Newtonsoft.Json;

using Xunit;

namespace Nefarius.DsHidMini.ControlApp.Tests;

public class ApplicationConfigurationAndTrayPolicyTests
{
    [Fact]
    public void ApplicationConfiguration_JsonRoundTrip_PreservesMinimizeToTray()
    {
        ApplicationConfiguration original = new()
        {
            MinimizeToTray = true,
            IsLoggingEnabled = true,
            IsUpdateCheckEnabled = false
        };

        string json = JsonConvert.SerializeObject(original);
        ApplicationConfiguration? loaded = JsonConvert.DeserializeObject<ApplicationConfiguration>(json);

        Assert.NotNull(loaded);
        Assert.True(loaded.MinimizeToTray);
        Assert.True(loaded.IsLoggingEnabled);
        Assert.False(loaded.IsUpdateCheckEnabled);
    }

    [Fact]
    public void ApplicationConfiguration_Default_MinimizeToTrayIsOff()
    {
        ApplicationConfiguration config = new();

        Assert.False(config.MinimizeToTray);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldHideInsteadOfClose_MatchesPolicy(bool minimizeToTray, bool isExiting, bool expected)
    {
        Assert.Equal(expected, TrayWindowPolicy.ShouldHideInsteadOfClose(minimizeToTray, isExiting));
    }

    [Theory]
    [InlineData(true, WindowState.Minimized, true)]
    [InlineData(true, WindowState.Normal, false)]
    [InlineData(true, WindowState.Maximized, false)]
    [InlineData(false, WindowState.Minimized, false)]
    public void ShouldHideOnMinimize_MatchesPolicy(bool minimizeToTray, WindowState newState, bool expected)
    {
        Assert.Equal(expected, TrayWindowPolicy.ShouldHideOnMinimize(minimizeToTray, newState));
    }

    [Fact]
    public void RestartAsAdminFlow_ReleasesOwnershipBeforeLaunchThenShutsDown()
    {
        List<string> steps = new();

        RestartAsAdminFlow.Run(
            () => steps.Add("release"),
            () => steps.Add("launch"),
            () => steps.Add("shutdown"));

        Assert.Equal(new[] { "release", "launch", "shutdown" }, steps);
    }

    [Fact]
    public void SingleInstanceLifetime_ReleasedParentAllowsReplacementToBecomePrimary()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string mutexName = "Nefarius.DsHidMini.ControlApp.Tests.Mutex." + suffix;
        string eventName = "Nefarius.DsHidMini.ControlApp.Tests.Event." + suffix;

        using SingleInstanceLifetime parent = new(mutexName, eventName);
        Assert.True(parent.IsPrimary);

        using (SingleInstanceLifetime blocked = new(mutexName, eventName))
        {
            Assert.False(blocked.IsPrimary);
        }

        parent.ReleaseOwnership();

        using SingleInstanceLifetime replacement = new(mutexName, eventName);
        Assert.True(replacement.IsPrimary);
    }

    [Fact]
    public void SingleInstanceLifetime_SecondarySetSurvivesSecondaryDispose()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string mutexName = "Nefarius.DsHidMini.ControlApp.Tests.Mutex." + suffix;
        string eventName = "Nefarius.DsHidMini.ControlApp.Tests.Event." + suffix;

        using SingleInstanceLifetime primary = new(mutexName, eventName);
        Assert.True(primary.IsPrimary);

        using (SingleInstanceLifetime secondary = new(mutexName, eventName))
        {
            Assert.False(secondary.IsPrimary);
            secondary.ShowWindowEvent.Set();
        }

        Assert.True(primary.ShowWindowEvent.WaitOne(TimeSpan.FromSeconds(1)));
    }
}
