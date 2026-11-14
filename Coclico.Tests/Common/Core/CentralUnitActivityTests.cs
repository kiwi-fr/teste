using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitActivityTests
{
    [Fact]
    public void InitialLevelIsStartingUp()
    {
        var activity = new CentralUnitActivity();

        Assert.Equal(CentralActivityLevel.StartingUp, activity.Level);
        Assert.False(activity.IsAiInferenceActive);
        Assert.Equal(TimeSpan.FromSeconds(4), activity.AdaptInterval(TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void StartedWithoutWindowOrTrayRampsTowardBackgroundInterval()
    {
        var activity = new CentralUnitActivity();

        activity.MarkStarted();

        Assert.Equal(CentralActivityLevel.Background, activity.Level);

        TimeSpan first = activity.AdaptInterval(TimeSpan.FromSeconds(4));
        Assert.True(first > TimeSpan.FromSeconds(4));
        Assert.True(first < TimeSpan.FromSeconds(12));

        TimeSpan current = first;
        for (int i = 0; i < 200 && current < TimeSpan.FromSeconds(12); i++)
        {
            current = activity.AdaptInterval(TimeSpan.FromSeconds(4));
        }

        Assert.Equal(TimeSpan.FromSeconds(12), current);
    }

    [Fact]
    public void VisibleWindowForcesActiveWithBaseInterval()
    {
        var activity = new CentralUnitActivity();

        activity.MarkStarted();
        activity.SetWindowVisible(true);

        Assert.Equal(CentralActivityLevel.Active, activity.Level);
        Assert.Equal(TimeSpan.FromSeconds(4), activity.AdaptInterval(TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void HiddenWindowWithTrayRampsToTenTimesIntervalThenReturnsProgressively()
    {
        var activity = new CentralUnitActivity();

        activity.MarkStarted();
        activity.SetWindowVisible(false);
        activity.SetTrayMinimized(true);

        Assert.Equal(CentralActivityLevel.Idle, activity.Level);

        TimeSpan current = TimeSpan.FromSeconds(4);
        for (int i = 0; i < 200 && current < TimeSpan.FromSeconds(40); i++)
        {
            current = activity.AdaptInterval(TimeSpan.FromSeconds(4));
        }

        Assert.Equal(TimeSpan.FromSeconds(40), current);

        activity.SetWindowVisible(true);

        Assert.Equal(CentralActivityLevel.Active, activity.Level);

        TimeSpan relaxed = activity.AdaptInterval(TimeSpan.FromSeconds(4));
        Assert.True(relaxed > TimeSpan.FromSeconds(4));
        Assert.True(relaxed < TimeSpan.FromSeconds(40));

        current = relaxed;
        for (int i = 0; i < 20 && current > TimeSpan.FromSeconds(4); i++)
        {
            current = activity.AdaptInterval(TimeSpan.FromSeconds(4));
        }

        Assert.Equal(TimeSpan.FromSeconds(4), current);
    }

    [Fact]
    public void InferenceForcesActiveEvenWhenHiddenToTray()
    {
        var activity = new CentralUnitActivity();

        activity.MarkStarted();
        activity.SetWindowVisible(false);
        activity.SetTrayMinimized(true);
        activity.SetAiInference(true);

        Assert.Equal(CentralActivityLevel.Active, activity.Level);
        Assert.True(activity.IsAiInferenceActive);

        activity.SetAiInference(false);

        Assert.Equal(CentralActivityLevel.Idle, activity.Level);
        Assert.False(activity.IsAiInferenceActive);
    }

    [Fact]
    public void ExternalPressureTakesPrecedenceAndSuspendsIntervals()
    {
        var activity = new CentralUnitActivity();

        activity.MarkStarted();
        activity.SetWindowVisible(true);
        activity.SetExternalPressure(true);

        Assert.Equal(CentralActivityLevel.ExternalPressure, activity.Level);
        Assert.Equal(Timeout.InfiniteTimeSpan, activity.AdaptInterval(TimeSpan.FromSeconds(4)));

        activity.SetExternalPressure(false);

        Assert.Equal(CentralActivityLevel.Active, activity.Level);
    }

    [Fact]
    public void LevelChangedFiresOnlyOnActualChanges()
    {
        var activity = new CentralUnitActivity();
        var observed = new List<CentralActivityLevel>();
        activity.LevelChanged += level => observed.Add(level);

        activity.MarkStarted();
        activity.SetWindowVisible(true);
        activity.SetWindowVisible(true);
        activity.SetTrayMinimized(true);

        Assert.Equal(new[] { CentralActivityLevel.Background, CentralActivityLevel.Active }, observed);
    }
}
