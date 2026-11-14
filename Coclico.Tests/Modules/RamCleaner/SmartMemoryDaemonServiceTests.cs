using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class SmartMemoryDaemonServiceTests : IDisposable
{
    private readonly SmartMemoryDaemonService _daemon = new(new SettingsService());

    public void Dispose()
    {
        _daemon.Dispose();
    }

    [Theory]
    [InlineData(null, AutoCleanMode.Interval)]
    [InlineData("", AutoCleanMode.Interval)]
    [InlineData("Interval", AutoCleanMode.Interval)]
    [InlineData("ThresholdPercent", AutoCleanMode.ThresholdPercent)]
    [InlineData("Threshold", AutoCleanMode.ThresholdPercent)]
    [InlineData("Hybrid", AutoCleanMode.Hybrid)]
    [InlineData("n'importe quoi", AutoCleanMode.Interval)]
    public void ParseModeSettingMapsLegacyAndCanonicalValues(string? input, AutoCleanMode expected)
    {
        Assert.Equal(expected, SmartMemoryDaemonService.ParseModeSetting(input));
    }

    [Theory]
    [InlineData(AutoCleanMode.ThresholdPercent, "ThresholdPercent")]
    [InlineData(AutoCleanMode.Hybrid, "Hybrid")]
    [InlineData(AutoCleanMode.Interval, "Interval")]
    public void ModeSettingToStringRoundTripsWithParse(AutoCleanMode mode, string expected)
    {
        Assert.Equal(expected, SmartMemoryDaemonService.ModeSettingToString(mode));
        Assert.Equal(
            mode,
            SmartMemoryDaemonService.ParseModeSetting(SmartMemoryDaemonService.ModeSettingToString(mode)));
    }

    [Fact]
    public void ConstructorWithDefaultSettingsReadsDefaults()
    {
        Assert.False(_daemon.IsEnabled);
        Assert.Equal(AutoCleanMode.Interval, _daemon.Mode);
        Assert.Equal(15, _daemon.Value);
        Assert.True(_daemon.ProtectForeground);
        Assert.Equal(MemoryCleanerService.CleanProfile.Smart, _daemon.Profile);
    }

    [Fact]
    public void TimeUntilNextCleanIsNullWhenDaemonDisabled()
    {
        Assert.Null(_daemon.TimeUntilNextClean);
    }

    [Fact]
    public void StatusMessageHasInitialValue()
    {
        Assert.False(string.IsNullOrWhiteSpace(_daemon.StatusMessage));
    }

    [Fact]
    public void SessionCountersStartAtZero()
    {
        Assert.Equal(0, _daemon.TotalSessionFreedBytes);
        Assert.Equal(0, _daemon.LastFreedBytes);
        Assert.Null(_daemon.LastCleanUtc);
    }
}
