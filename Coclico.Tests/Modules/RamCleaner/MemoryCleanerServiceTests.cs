using System.Globalization;
using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class MemoryCleanerServiceTests
{
    [Theory]
    [InlineData(-5000)]
    [InlineData(-1)]
    [InlineData(0)]
    public void FormatBytesNegativeOrNullReturnsZeroBytes(long bytes)
    {
        Assert.Equal("0 B", MemoryCleanerService.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1023)]
    public void FormatBytesBelowOneKilobyteReturnsRawBytes(long bytes)
    {
        Assert.Equal($"{bytes} B", MemoryCleanerService.FormatBytes(bytes));
    }

    [Fact]
    public void FormatBytesKilobyteTierUsesKbUnit()
    {
        using var _ = CulturePin.Invariant();

        Assert.Equal("1.0 KB", MemoryCleanerService.FormatBytes(1024));
        Assert.Equal("1.5 KB", MemoryCleanerService.FormatBytes(1536));
        Assert.Equal("512.0 KB", MemoryCleanerService.FormatBytes(512 * 1024));
    }

    [Fact]
    public void FormatBytesMegabyteTierUsesMbUnit()
    {
        using var _ = CulturePin.Invariant();

        Assert.Equal("1.0 MB", MemoryCleanerService.FormatBytes(1024L * 1024));
        Assert.Equal("1.5 MB", MemoryCleanerService.FormatBytes((long)(1024L * 1024 * 1.5)));
    }

    [Fact]
    public void FormatBytesGigabyteTierUsesGbUnitWithTwoDecimals()
    {
        using var _ = CulturePin.Invariant();

        Assert.Equal("1.00 GB", MemoryCleanerService.FormatBytes(1024L * 1024 * 1024));
        Assert.Equal("0.50 GB", MemoryCleanerService.FormatBytes(1024L * 1024 * 1024 / 2));
    }

    [Fact]
    public void GetRamInfoReportsConsistentPhysicalMemory()
    {
        MemoryCleanerService.RamInfo info = MemoryCleanerService.GetRamInfo();

        Assert.True(info.TotalPhysBytes > 0);
        Assert.True(info.AvailPhysBytes > 0);
        Assert.True(info.AvailPhysBytes <= info.TotalPhysBytes);
        Assert.Equal(info.TotalPhysBytes - info.AvailPhysBytes, info.UsedPhysBytes);
        Assert.InRange(info.PhysUsedPercent, 0, 100);
        Assert.InRange(info.VirtUsedPercent, 0, 100);
        Assert.True(info.TotalPageBytes >= info.TotalPhysBytes);
    }

    [Fact]
    public void GetTopProcessesByMemoryRespectsCountAndSortsDescending()
    {
        List<MemoryCleanerService.ProcessMemInfo> top = MemoryCleanerService.GetTopProcessesByMemory(8);

        Assert.True(top.Count > 0);
        Assert.True(top.Count <= 8);
        for (int i = 1; i < top.Count; i++)
        {
            Assert.True(top[i - 1].WorkingSetMb >= top[i].WorkingSetMb);
        }

        foreach (MemoryCleanerService.ProcessMemInfo p in top)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.True(p.Pid > 0);
        }
    }

    [Fact]
    public void GetTopProcessesByMemoryWithCountOneReturnsSingleProcess()
    {
        List<MemoryCleanerService.ProcessMemInfo> top = MemoryCleanerService.GetTopProcessesByMemory(1);

        Assert.True(top.Count <= 1);
        if (top.Count == 1)
        {
            Assert.False(string.IsNullOrWhiteSpace(top[0].Name));
        }
    }

    private sealed class CulturePin : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

        private CulturePin()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }

        public static CulturePin Invariant()
        {
            return new CulturePin();
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
        }
    }
}
