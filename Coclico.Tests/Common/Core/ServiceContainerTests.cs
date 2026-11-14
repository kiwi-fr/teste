using Coclico.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coclico.Tests;

[Collection("ServiceContainerSerial")]
public sealed class ServiceContainerTests
{
    private sealed class Marker;

    [Fact]
    public void BuildThenGetRequiredReturnsRegisteredSingleton()
    {
        try
        {
            ServiceContainer.Build(services => services.AddSingleton<Marker>());

            Marker first = ServiceContainer.GetRequired<Marker>();
            Marker second = ServiceContainer.GetRequired<Marker>();

            Assert.Same(first, second);
            Assert.Same(first, ServiceContainer.GetOptional<Marker>());
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }

    [Fact]
    public void BuildThenGetRequiredReturnsNewInstancesForTransients()
    {
        try
        {
            ServiceContainer.Build(services => services.AddTransient<Marker>());

            Marker first = ServiceContainer.GetRequired<Marker>();
            Marker second = ServiceContainer.GetRequired<Marker>();

            Assert.NotSame(first, second);
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }

    [Fact]
    public void GetOptionalReturnsNullForUnregisteredService()
    {
        try
        {
            ServiceContainer.Build(services => { });

            Assert.Null(ServiceContainer.GetOptional<Marker>());
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }

    [Fact]
    public void ShutdownReleasesContainerAndGetOptionalFallsBackToNull()
    {
        ServiceContainer.Build(services => services.AddSingleton<Marker>());
        _ = ServiceContainer.GetRequired<Marker>();
        ServiceContainer.Shutdown();

        Assert.False(ServiceContainer.GetOptional<Marker>() is not null);
    }

    [Fact]
    public void ProviderAfterShutdownWithoutRebuildThrows()
    {
        ServiceContainer.Build(services => services.AddSingleton<Marker>());
        ServiceContainer.Shutdown();

        Assert.Throws<InvalidOperationException>(() => ServiceContainer.Provider);
    }
}
