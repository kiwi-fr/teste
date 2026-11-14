using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class LaunchModesTests
{
    [Theory]
    [InlineData("Normal")]
    [InlineData("Minimized")]
    [InlineData("Tray")]
    [InlineData("Maximized")]
    [InlineData("tray")]
    [InlineData("MAXIMIZED")]
    public void IsValidAcceptsDefinedModesCaseInsensitive(string value)
    {
        Assert.True(LaunchModes.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("tr a y")]
    public void IsValidRejectsUnknownValues(string? value)
    {
        Assert.False(LaunchModes.IsValid(value));
    }

    [Fact]
    public void EnumValuesAreAllUsableAsSettings()
    {
        foreach (LaunchMode mode in Enum.GetValues<LaunchMode>())
        {
            Assert.True(LaunchModes.IsValid(mode.ToString()));
        }
    }
}
