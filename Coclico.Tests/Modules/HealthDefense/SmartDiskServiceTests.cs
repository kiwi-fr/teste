using Coclico.Services;
using Xunit;

namespace Coclico.Tests.Modules.HealthDefense;

/// <summary>
/// La garantie contractuelle de SmartDiskService est de ne JAMAIS lever d'exception,
/// même sur une machine sans racine WMI S.M.A.R.T. (contrôleurs RAID/NVMe, accès refusé) :
/// c'est exactement ce que ces tests vérifient.
/// </summary>
public sealed class SmartDiskServiceTests
{
    [Fact]
    public void GetDiskStatus_NeverThrows()
    {
        Exception? exception = Record.Exception(() => _ = SmartDiskService.GetDiskStatus());

        Assert.Null(exception);
    }

    [Fact]
    public void GetDiskStatus_ReturnsNonNullList()
    {
        List<SmartDiskStatus> status = SmartDiskService.GetDiskStatus();

        Assert.NotNull(status);
    }
}
