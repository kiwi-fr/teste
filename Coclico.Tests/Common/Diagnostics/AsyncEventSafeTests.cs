using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class AsyncEventSafeTests
{
    [Fact]
    public async Task RunExecutesTheHandlerAsynchronously()
    {
        using var executed = new SemaphoreSlim(0, 1);

        AsyncEventSafe.Run(() =>
        {
            executed.Release();
            return Task.CompletedTask;
        }, "ut-handler");

        bool ran = await executed.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(ran);
    }

    [Fact]
    public async Task RunSwallowsExceptionsFromHandler()
    {
        using var reached = new SemaphoreSlim(0, 1);

        AsyncEventSafe.Run(() =>
        {
            reached.Release();
            throw new InvalidOperationException("erreur volontaire (test)");
        }, "ut-throwing-handler");

        bool ran = await reached.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(ran);

        await Task.Delay(100, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunSwallowsOperationCanceledException()
    {
        using var reached = new SemaphoreSlim(0, 1);

        AsyncEventSafe.Run(() =>
        {
            reached.Release();
            throw new OperationCanceledException();
        }, "ut-cancelled-handler");

        bool ran = await reached.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(ran);
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void RunNullHandlerDoesNothing()
    {
        AsyncEventSafe.Run(handler: null!, operationName: "ut-null-handler");
    }

    [Fact]
    public async Task RunMultipleConcurrentHandlersAllExecute()
    {
        int counter = 0;
        using var allDone = new SemaphoreSlim(0, 5);

        for (int i = 0; i < 5; i++)
        {
            AsyncEventSafe.Run(() =>
            {
                Interlocked.Increment(ref counter);
                allDone.Release();
                return Task.CompletedTask;
            }, "ut-concurrent");
        }

        for (int i = 0; i < 5; i++)
        {
            _ = await allDone.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        Assert.Equal(5, counter);
    }
}
