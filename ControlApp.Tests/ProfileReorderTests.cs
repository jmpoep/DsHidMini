using Nefarius.DsHidMini.ControlApp.Models.DshmConfigManager;

using Xunit;

namespace Nefarius.DsHidMini.ControlApp.Tests;

public class ProfileReorderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dshm-tests-" + Guid.NewGuid().ToString("N"));

    public ProfileReorderTests()
    {
        Directory.CreateDirectory(UserDir);
        Directory.CreateDirectory(DriverDir);
        ProfileData.DefaultProfile.Settings.ResetToDefault();
    }

    private string UserDir => Path.Combine(_root, "ControlApp");
    private string DriverDir => Path.Combine(_root, "DsHidMini");

    public void Dispose()
    {
        ProfileData.DefaultProfile.Settings.ResetToDefault();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void MoveUserProfile_ReordersDisplayList_WithDefaultPinned()
    {
        DshmConfigManager manager = CreateManager();
        ProfileData first = manager.CreateProfile("First");
        ProfileData second = manager.CreateProfile("Second");
        ProfileData third = manager.CreateProfile("Third");

        Assert.True(manager.MoveUserProfile(third, 0));

        List<ProfileData> list = manager.GetListOfProfilesWithDefault();
        Assert.Same(ProfileData.DefaultProfile, list[0]);
        Assert.Equal(third.ProfileGuid, list[1].ProfileGuid);
        Assert.Equal(first.ProfileGuid, list[2].ProfileGuid);
        Assert.Equal(second.ProfileGuid, list[3].ProfileGuid);
    }

    [Fact]
    public void MoveUserProfile_PersistsOrder_AcrossSaveAndReload()
    {
        DshmConfigLocations locations = new(UserDir, DriverDir);
        DshmConfigManager manager = new(locations);
        ProfileData first = manager.CreateProfile("First");
        ProfileData second = manager.CreateProfile("Second");
        ProfileData third = manager.CreateProfile("Third");
        Assert.True(manager.MoveUserProfile(third, 0));
        manager.SaveChanges();

        DshmConfigManager reloaded = new(locations);
        List<ProfileData> list = reloaded.GetListOfProfilesWithDefault();
        Assert.Same(ProfileData.DefaultProfile, list[0]);
        Assert.Equal(third.ProfileGuid, list[1].ProfileGuid);
        Assert.Equal("Third", list[1].ProfileName);
        Assert.Equal(first.ProfileGuid, list[2].ProfileGuid);
        Assert.Equal(second.ProfileGuid, list[3].ProfileGuid);
    }

    [Fact]
    public void MoveUserProfile_NoOps_ForDefaultUnknownAndUnchangedIndex()
    {
        DshmConfigManager manager = CreateManager();
        ProfileData first = manager.CreateProfile("First");
        ProfileData second = manager.CreateProfile("Second");
        List<Guid> original = manager.GetListOfProfilesWithDefault().Select(p => p.ProfileGuid).ToList();

        Assert.False(manager.MoveUserProfile(ProfileData.DefaultProfile, 0));
        Assert.False(manager.MoveUserProfile(new ProfileData { ProfileName = "Missing" }, 0));
        Assert.False(manager.MoveUserProfile(first, 0));
        Assert.False(manager.MoveUserProfile(second, -1));
        Assert.False(manager.MoveUserProfile(second, 99));

        Assert.Equal(original, manager.GetListOfProfilesWithDefault().Select(p => p.ProfileGuid));
    }

    private DshmConfigManager CreateManager() =>
        new(new DshmConfigLocations(UserDir, DriverDir));
}
