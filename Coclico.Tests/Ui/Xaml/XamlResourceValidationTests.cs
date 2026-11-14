using System.Text.Json;
using Coclico.Modules.TelemetryBlocker.Services;
using Xunit;

namespace Coclico.Tests;

public sealed class TelemetryBlockServiceTests
{
    private static readonly string[] ExpectedCategories = ["Registry", "Services", "ScheduledTasks", "Hosts", "AdsHosts", "DnsLockdown"];

    [Fact]
    public void BuildPlanRecommendedContainsCoreTelemetryActions()
    {
        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended, null);

        Assert.Contains(plan, a => a.Kind == "service" && a.Target == "DiagTrack");
        Assert.Contains(plan, a => a.Kind == "registry" && a.Target.Contains("DataCollection", StringComparison.Ordinal) && a.Value == "0");
        Assert.Contains(plan, a => a.Kind == "task" && a.Target.Contains("Customer Experience Improvement Program", StringComparison.Ordinal));
        Assert.Contains(plan, a => a.Kind == "hosts");
        Assert.Contains(plan, a => a.Target == "dmwappushservice");
    }

    [Fact]
    public void BuildPlanRecommendedBlocksEverythingSetsAllowTelemetryToZero()
    {

        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended, null);

        Assert.Contains(plan, a => a.Kind == "registry" && a.Target.Contains("DataCollection", StringComparison.Ordinal) && a.Value == "0");
        Assert.Contains(plan, a => a.Kind == "service" && a.Target == "dmwappushservice");
        Assert.Contains(plan, a => a.Kind == "task" && a.Target.Contains("DmClient", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPlanRespectsCategoryFilter()
    {
        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hosts" });

        Assert.Single(plan);
        Assert.Equal("hosts", plan[0].Kind);
    }

    [Fact]
    public void BuildPlanIdsAreUnique()
    {
        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended, null);
        var ids = plan.Select(a => a.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BackupSerializationRoundTrips()
    {
        var backup = new TelemetryBackup
        {
            CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            HostsPath = @"C:\Windows\System32\drivers\etc\hosts",
            HostsContent = "127.0.0.1 localhost\n",
            Registry = { new RegistrySnapshot { Path = @"HKLM\SOFTWARE\X", ValueName = "V", Existed = true, KindCode = 4, Value = "1" } },
            Services = { new ServiceSnapshot { Name = "DiagTrack", Existed = true, StartMode = "Auto", State = "Running" } },
            Tasks = { new TaskSnapshot { Path = @"\Microsoft\Windows\X", Existed = true, Enabled = true } }
        };

        string json = JsonSerializer.Serialize(backup);
        var loaded = JsonSerializer.Deserialize<TelemetryBackup>(json);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Registry);
        Assert.Equal("DiagTrack", loaded.Services[0].Name);
        Assert.True(loaded.Tasks[0].Enabled);
        Assert.Equal(backup.HostsContent, loaded.HostsContent);
    }

    [Fact]
    public void CategoriesContainsExpectedSix()
    {
        Assert.Equal(
            ExpectedCategories,
            TelemetryBlockService.Categories);
    }

    [Fact]
    public void BuildPlanDnsLockdownCategoryAddsFirewallLockdownAction()
    {
        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DnsLockdown" });

        Assert.Single(plan);
        Assert.Equal("dns-lockdown", plan[0].Kind);
    }

    [Fact]
    public void GetAdHostsIncludesEmbeddedAndDohDomains()
    {
        var hosts = TelemetryBlockService.GetAdHosts();

        Assert.Contains("doubleclick.net", hosts);
        Assert.Contains("dns.google", hosts);
        Assert.Contains("cloudflare-dns.com", hosts);
    }

    [Fact]
    public void BuildPlanAdsHostsCategoryAddsSystemWideHostsAction()
    {
        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AdsHosts" });

        Assert.Single(plan);
        Assert.Equal("hosts-ads", plan[0].Kind);
        Assert.Equal("hosts:ads", plan[0].Id);
        Assert.Equal(TelemetryBlockService.HostsPath, plan[0].Target);
    }

    [Fact]
    public void BuildPlanDefaultCategoriesIncludesAdsHostsBlock()
    {
        var plan = TelemetryBlockService.BuildPlan(TelemetryProfile.Recommended, null);

        Assert.Contains(plan, a => a.Kind == "hosts");
        Assert.Contains(plan, a => a.Kind == "hosts-ads");
    }
}
