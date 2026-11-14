using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitBusTests
{
    [Fact]
    public async Task QueryAsyncCachesTheResultForTheTtlDuration()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;

        string first = await bus.QueryAsync("Central.Tests.Gpu", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult("value");
        }, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);

        string second = await bus.QueryAsync("Central.Tests.Gpu", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult("other");
        }, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, bus.DroppedRequests);
    }

    [Fact]
    public async Task ExpiredCacheTriggersANewFactoryCall()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;
        TimeSpan ttl = TimeSpan.FromMilliseconds(50);

        _ = await bus.QueryAsync("Central.Tests.Expired", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(1);
        }, ttl, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(150, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await bus.QueryAsync("Central.Tests.Expired", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(2);
        }, ttl, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task LowPriorityDoublesCacheLifetimeWhileCriticalHalvesIt()
    {
        var bus = new CentralUnitBus();
        int lowCalls = 0;
        int criticalCalls = 0;
        TimeSpan ttl = TimeSpan.FromSeconds(1);

        _ = await bus.QueryAsync("Central.Tests.Low", ct =>
        {
            Interlocked.Increment(ref lowCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Low, TestContext.Current.CancellationToken);

        await Task.Delay(600, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await bus.QueryAsync("Central.Tests.Low", ct =>
        {
            Interlocked.Increment(ref lowCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Low, TestContext.Current.CancellationToken);

        Assert.Equal(1, lowCalls);

        _ = await bus.QueryAsync("Central.Tests.Critical", ct =>
        {
            Interlocked.Increment(ref criticalCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Critical, TestContext.Current.CancellationToken);

        await Task.Delay(700, TestContext.Current.CancellationToken).ConfigureAwait(true);

        _ = await bus.QueryAsync("Central.Tests.Critical", ct =>
        {
            Interlocked.Increment(ref criticalCalls);
            return Task.FromResult(1);
        }, ttl, CentralRequestPriority.Critical, TestContext.Current.CancellationToken);

        Assert.Equal(2, criticalCalls);
    }

    [Fact]
    public async Task ConcurrentQueriesShareASingleFactoryExecution()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> first = bus.QueryAsync("Central.Tests.Shared", async ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            await release.Task.ConfigureAwait(false);
            return "shared";
        }, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);

        Task<string> second = bus.QueryAsync("Central.Tests.Shared", async ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            await release.Task.ConfigureAwait(false);
            return "shared";
        }, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(1, factoryCalls);

        release.SetResult();

        Assert.Equal("shared", await first.ConfigureAwait(true));
        Assert.Equal("shared", await second.ConfigureAwait(true));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task InvalidateForcesANewFactoryCall()
    {
        var bus = new CentralUnitBus();
        int factoryCalls = 0;
        TimeSpan ttl = TimeSpan.FromMinutes(1);

        _ = await bus.QueryAsync("Central.Tests.Invalidated", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(1);
        }, ttl, cancellationToken: TestContext.Current.CancellationToken);

        bus.Invalidate("Central.Tests.Invalidated");

        _ = await bus.QueryAsync("Central.Tests.Invalidated", ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(2);
        }, ttl, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task SendAsyncRoutesTypedCommandsToRegisteredHandlers()
    {
        var bus = new CentralUnitBus();
        string? routedCommand = null;

        bus.RegisterRoute<string>("Central.Tests.Route", (command, _) =>
        {
            routedCommand = command;
            return Task.CompletedTask;
        });

        await bus.SendAsync("Central.Tests.Route", "command", TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("command", routedCommand);
        Assert.Equal(1, bus.RoutedRequests);
    }

    [Fact]
    public async Task SendAsyncOnUnknownRouteThrows()
    {
        var bus = new CentralUnitBus();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => bus.SendAsync("Central.Tests.Unknown", "command", TestContext.Current.CancellationToken)).ConfigureAwait(true);
    }

    [Fact]
    public async Task CacheEvictsLeastRecentlyUsedEntriesBeyondCapacity()
    {
        var bus = new CentralUnitBus(maxCacheEntries: 4);
        int factoryCalls = 0;

        Task<int> Query(string key) => bus.QueryAsync(key, ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(1);
        }, TimeSpan.FromMinutes(1));

        await Query("Central.Tests.A").ConfigureAwait(true);
        await Query("Central.Tests.B").ConfigureAwait(true);
        await Query("Central.Tests.C").ConfigureAwait(true);
        await Query("Central.Tests.D").ConfigureAwait(true);
        _ = await Query("Central.Tests.A").ConfigureAwait(true);
        await Query("Central.Tests.E").ConfigureAwait(true);

        Assert.Equal(5, factoryCalls);
        Assert.Equal(1, bus.EvictedEntries);

        await Query("Central.Tests.B").ConfigureAwait(true);

        Assert.Equal(6, factoryCalls);
        Assert.Equal(2, bus.EvictedEntries);

        _ = await Query("Central.Tests.A").ConfigureAwait(true);

        Assert.Equal(6, factoryCalls);
    }
}
