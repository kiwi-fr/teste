using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class CentralUnitBusRateLimitTests
{
    [Fact]
    public async Task QueryAsyncDistinguishesTypesSharingTheSameKey()
    {
        var bus = new CentralUnitBus();

        int stringFactoryCalls = 0;
        int intFactoryCalls = 0;

        string stringValue = await bus.QueryAsync("Central.Tests.SharedKey", ct =>
        {
            Interlocked.Increment(ref stringFactoryCalls);
            return Task.FromResult("text");
        }, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);

        int intValue = await bus.QueryAsync("Central.Tests.SharedKey", ct =>
        {
            Interlocked.Increment(ref intFactoryCalls);
            return Task.FromResult(42);
        }, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("text", stringValue);
        Assert.Equal(42, intValue);
        Assert.Equal(1, stringFactoryCalls);
        Assert.Equal(1, intFactoryCalls);
    }

    [Fact]
    public async Task SendAsyncDropsRequestsBeyondTheRouteCapacity()
    {
        var bus = new CentralUnitBus();
        int handled = 0;
        bus.RegisterRoute<string>("Central.Tests.RateLimited", (_, _) =>
        {
            Interlocked.Increment(ref handled);
            return Task.CompletedTask;
        });
        bus.ConfigureRouteRate("Central.Tests.RateLimited", 2, 0);

        await bus.SendAsync("Central.Tests.RateLimited", "one", TestContext.Current.CancellationToken);
        await bus.SendAsync("Central.Tests.RateLimited", "two", TestContext.Current.CancellationToken);
        await bus.SendAsync("Central.Tests.RateLimited", "three", TestContext.Current.CancellationToken);
        await bus.SendAsync("Central.Tests.RateLimited", "four", TestContext.Current.CancellationToken);

        Assert.Equal(2, handled);
        Assert.Equal(2, bus.RoutedRequests);
        Assert.Equal(2, bus.DroppedRequests);
    }

    [Fact]
    public async Task SendAsyncRoutesUnlimitedRequestsWhenNoBucketIsConfigured()
    {
        var bus = new CentralUnitBus();
        int handled = 0;
        bus.RegisterRoute<string>("Central.Tests.Unlimited", (_, _) =>
        {
            Interlocked.Increment(ref handled);
            return Task.CompletedTask;
        });

        for (int i = 0; i < 10; i++)
        {
            await bus.SendAsync("Central.Tests.Unlimited", "payload", TestContext.Current.CancellationToken);
        }

        Assert.Equal(10, handled);
        Assert.Equal(10, bus.RoutedRequests);
        Assert.Equal(0, bus.DroppedRequests);
    }

    [Fact]
    public async Task SendAsyncRefillsTokensOverTime()
    {
        var bus = new CentralUnitBus();
        int handled = 0;
        bus.RegisterRoute<string>("Central.Tests.Refills", (_, _) =>
        {
            Interlocked.Increment(ref handled);
            return Task.CompletedTask;
        });
        bus.ConfigureRouteRate("Central.Tests.Refills", 1, 0.5);

        await bus.SendAsync("Central.Tests.Refills", "first", TestContext.Current.CancellationToken);
        await bus.SendAsync("Central.Tests.Refills", "second", TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await bus.SendAsync("Central.Tests.Refills", "third", TestContext.Current.CancellationToken);

        Assert.Equal(1, handled);
        Assert.Equal(2, bus.DroppedRequests);

        await Task.Delay(2100, TestContext.Current.CancellationToken);
        await bus.SendAsync("Central.Tests.Refills", "fourth", TestContext.Current.CancellationToken);

        Assert.Equal(2, handled);
        Assert.Equal(2, bus.DroppedRequests);
    }

    [Fact]
    public void SendAsyncThrowsForAnUnknownRoute()
    {
        var bus = new CentralUnitBus();
        Assert.Throws<KeyNotFoundException>(() => { _ = bus.SendAsync("Central.Tests.MissingRoute", "payload", TestContext.Current.CancellationToken); });
    }
}
