using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitFlagStoreTests : IDisposable
{
    private readonly CentralUnitFlagStore _store = new();

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task WaitAsyncCompletesImmediatelyWhenFlagIsAlreadySet()
    {
        _store.Set(CentralFlags.NetworkStatsDue);

        Assert.True(await _store.WaitAsync(CentralFlags.NetworkStatsDue, TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task WaitAsyncCompletesWhenFlagIsSetLater()
    {
        Task<bool> wait = _store.WaitAsync(CentralFlags.GuardTickDue, TestContext.Current.CancellationToken);

        Assert.False(wait.IsCompleted);

        _store.Set(CentralFlags.GuardTickDue);

        Assert.True(await wait.ConfigureAwait(true));
    }

    [Fact]
    public async Task WaitAsyncReturnsFalseWhenFlagIsClearedWhileWaiting()
    {
        Task<bool> wait = _store.WaitAsync(CentralFlags.GuardTickDue, TestContext.Current.CancellationToken);

        _store.Clear(CentralFlags.GuardTickDue);

        Assert.False(await wait.ConfigureAwait(true));
    }

    [Fact]
    public async Task WaitAsyncHonorsCancellation()
    {
        using var cts = new CancellationTokenSource();

        Task<bool> wait = _store.WaitAsync(CentralFlags.StartupCompleted, cts.Token);

        await cts.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait).ConfigureAwait(true);
    }

    [Fact]
    public async Task PendingFlagIsConsumedByASingleWaiter()
    {
        _store.Set(CentralFlags.RamDaemonCycleDue);
        Assert.True(await _store.WaitAsync(CentralFlags.RamDaemonCycleDue, TestContext.Current.CancellationToken)).ConfigureAwait(true);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.WaitAsync(CentralFlags.RamDaemonCycleDue, timeoutCts.Token));
    }

    [Fact]
    public async Task SetWakesEveryWaiterOfTheSameFlag()
    {
        Task<bool> first = _store.WaitAsync(CentralFlags.DashboardRefreshDue, TestContext.Current.CancellationToken);
        Task<bool> second = _store.WaitAsync(CentralFlags.DashboardRefreshDue, TestContext.Current.CancellationToken);

        _store.Set(CentralFlags.DashboardRefreshDue);

        Assert.True(await first.ConfigureAwait(true));
        Assert.True(await second.ConfigureAwait(true));
    }

    [Fact]
    public async Task ClearRemovesAPendingFlagBeforeAnyWaiterConsumesIt()
    {
        _store.Set(CentralFlags.PressureSample);
        _store.Clear(CentralFlags.PressureSample);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.WaitAsync(CentralFlags.PressureSample, timeoutCts.Token));
    }

    [Fact]
    public void SetIncrementsWakeups()
    {
        long wakeupsBefore = _store.Wakeups;

        _store.Set(CentralFlags.NetworkStatsDue);
        _store.Set(CentralFlags.GuardTickDue);

        Assert.Equal(wakeupsBefore + 2, _store.Wakeups);
    }

    [Fact]
    public void ScheduleAndCancelTrackScheduledCount()
    {
        _store.Schedule(CentralFlags.AiModelUnload, DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(1, _store.ScheduledCount);

        _store.CancelSchedule(CentralFlags.AiModelUnload);

        Assert.Equal(0, _store.ScheduledCount);
    }

    [Fact]
    public async Task SchedulerFiresDueSchedules()
    {
        _store.StartScheduler();
        _store.Schedule(CentralFlags.PressureSample, DateTime.UtcNow.AddMilliseconds(50));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.True(await _store.WaitAsync(CentralFlags.PressureSample, timeoutCts.Token)).ConfigureAwait(true);
        Assert.Equal(0, _store.ScheduledCount);
    }

    [Fact]
    public async Task PostponeNonCriticalDelaysOnlyNonCriticalSchedules()
    {
        _store.SetCriticalFlags(CentralFlags.StartupCompleted);
        _store.StartScheduler();
        _store.Schedule(CentralFlags.StartupCompleted, DateTime.UtcNow.AddMilliseconds(50));
        _store.Schedule(CentralFlags.NetworkStatsDue, DateTime.UtcNow.AddMilliseconds(50));
        _store.PostponeNonCritical(TimeSpan.FromMinutes(30));

        using var criticalCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(await _store.WaitAsync(CentralFlags.StartupCompleted, criticalCts.Token)).ConfigureAwait(true);

        using var postponedCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.WaitAsync(CentralFlags.NetworkStatsDue, postponedCts.Token));
    }

    [Fact]
    public async Task SchedulerFiresTheEarliestScheduleFirst()
    {
        _store.StartScheduler();
        _store.Schedule(CentralFlags.DashboardRefreshDue, DateTime.UtcNow.AddSeconds(2));
        _store.Schedule(CentralFlags.GuardTickDue, DateTime.UtcNow.AddMilliseconds(100));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        bool fired = await _store.WaitAsync(CentralFlags.GuardTickDue, timeoutCts.Token).ConfigureAwait(true);

        Assert.True(fired);
        Assert.Equal(1, _store.ScheduledCount);
    }
}
