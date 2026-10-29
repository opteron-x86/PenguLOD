using AnvilLOD.Plugins.Mo2;
using Xunit;

namespace AnvilLOD.Core.Tests;

public class Mo2ProfileChoiceTests
{
    private static readonly string[] Profiles = ["default profile!", "texture-test", "TEST"];

    [Fact]
    public void UnderMo2_TheProfileMo2HasSelectedWinsOverTheRememberedOne() =>
        Assert.Equal("texture-test", Mo2ProfileChoice.Pick(Profiles, "default profile!", "texture-test", underMo2: true));

    [Fact]
    public void OutsideMo2_TheRememberedProfileWins() =>
        Assert.Equal("default profile!", Mo2ProfileChoice.Pick(Profiles, "default profile!", "texture-test", underMo2: false));

    [Fact]
    public void NamesMatchWithoutRegardToCase() =>
        Assert.Equal("TEST", Mo2ProfileChoice.Pick(Profiles, null, "test", underMo2: true));

    [Fact]
    public void UnderMo2_AMissingMo2ProfileFallsBackToTheRememberedOne() =>
        Assert.Equal("default profile!", Mo2ProfileChoice.Pick(Profiles, "default profile!", "gone", underMo2: true));

    [Fact]
    public void NothingKnown_TheFirstProfile() =>
        Assert.Equal("default profile!", Mo2ProfileChoice.Pick(Profiles, null, null, underMo2: false));

    [Fact]
    public void NoProfiles_Null() =>
        Assert.Null(Mo2ProfileChoice.Pick([], "a", "b", underMo2: true));
}
