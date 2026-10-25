using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coclico.Modules.UpdateCenter.Models;
using Coclico.Modules.UpdateCenter.Services;
using Coclico.Modules.UpdateCenter.ViewModels;
using Xunit;

namespace Coclico.Tests.Modules.UpdateCenter;

public sealed class UpdateCenterViewModelTests
{
    private sealed class StubScanService : UpdateScanService
    {
        public IReadOnlyList<UpdateItem> Result { get; set; } = Array.Empty<UpdateItem>();

        public StubScanService() : base(null!, null!, null!) { }

        public override Task<UpdateScanResult> ScanAllAsync(CancellationToken ct)
            => Task.FromResult(new UpdateScanResult
            {
                Items = Result,
                ScannedAt = DateTimeOffset.UtcNow,
                SourceErrors = new Dictionary<string, string>()
            });
    }

    private sealed class StubWingetService : WingetUpdateService
    {
        public StubWingetService() : base(new SecureProcessRunner()) { }

        public override Task<bool> UpgradeAsync(string packageId, CancellationToken ct)
            => Task.FromResult(true);
    }

    private sealed class StubRestorePointService : RestorePointService
    {
        public override bool Create(string description) => true;
    }

    private static UpdateItem AppItem(string id, UpdateItemStatus status = UpdateItemStatus.Available) => new()
    {
        Id = id,
        Name = id,
        Kind = UpdateKind.Application,
        Source = UpdateSource.Winget,
        Status = status,
        CurrentVersion = "1.0",
        AvailableVersion = "2.0"
    };

    private static UpdateCenterViewModel CreateViewModel(StubScanService scanService)
        => new(scanService, new StubWingetService(), new StubRestorePointService());

    [Fact]
    public async Task CheckAll_FillsSections_AndBecomesReady()
    {
        var scanService = new StubScanService
        {
            Result = new[] { AppItem("App1"), AppItem("App2", UpdateItemStatus.UnknownVersion) }
        };

        var viewModel = CreateViewModel(scanService);
        viewModel.CheckAll();

        await WaitUntilAsync(() => viewModel.State == UpdateCenterState.Ready);

        Assert.Equal(UpdateCenterState.Ready, viewModel.State);
        Assert.Equal(2, viewModel.Applications.Items.Count);
        Assert.NotNull(viewModel.LastScan);
    }

    [Fact]
    public async Task Selection_OnlySelectableItemsCanBeChecked()
    {
        var scanService = new StubScanService
        {
            Result = new[]
            {
                AppItem("App1"),
                UpdateItem.UpToDate("App2", "App2", UpdateKind.Application, UpdateSource.Winget)
            }
        };

        var viewModel = CreateViewModel(scanService);
        viewModel.CheckAll();
        await WaitUntilAsync(() => viewModel.State == UpdateCenterState.Ready);

        var upToDate = viewModel.Applications.Items.Single(i => i.Id == "App2");
        upToDate.IsSelected = true;

        Assert.False(upToDate.IsSelected);
        Assert.Empty(viewModel.SelectedItems);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
            await Task.Delay(20);

        Assert.True(condition());
    }
}