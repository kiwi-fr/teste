using System.Windows;
using System.Windows.Media;
using Coclico.Services;
using Coclico.Windows;
namespace Coclico;

public partial class MainWindow
{
    private void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
        => AsyncEventSafe.Run(() => UpdateCheckButton_ClickAsync(sender, e), "MainWindow.UpdateCheckButton_Click");

    private async Task UpdateCheckButton_ClickAsync(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is GitHubRelease pendingRelease)
        {
            OpenUpdateWindow(pendingRelease);
            return;
        }

        try
        {
            ToastService.ShowInfo(Loc.Get("Update_Checking"));
            var updateCheckService = ServiceContainer.GetRequired<UpdateCheckService>();

            GitHubRelease? update = await updateCheckService.CheckForUpdatesAsync().ConfigureAwait(false);

            if (update != null)
            {
                _pendingUpdate = update;
                MarkUpdateButton(update.TagName);
                OpenUpdateWindow(update);
            }
            else
            {
                ToastService.ShowInfo(Loc.Get("MainWindow_LatestVersion"));
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.UpdateCheck");
            ToastService.ShowError(Loc.Get("MainWindow_UpdateCheckError"));
        }
    }

    private void OnUpdateAvailable(object? sender, UpdateAvailableEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnUpdateAvailable(sender, e));
            return;
        }

        try
        {
            string? skipped = ServiceContainer.GetOptional<SettingsService>()?.Settings.SkippedUpdateVersion;
            if (UpdateManager.IsSkipped(e.Release.TagName, skipped))
            {
                return;
            }

            _pendingUpdate = e.Release;
            MarkUpdateButton(e.Release.TagName);
            ToastService.Show(Loc.Format("MainWindow_UpdateAvailableToast", e.Release.TagName));
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.OnUpdateAvailable");
        }
    }

    private void MarkUpdateButton(string tagName)
    {
        try
        {
            UpdateCheckButton.ToolTip = $"{Loc.Get("Main_CheckUpdatesTip")} {tagName}";
            if (UpdateCheckButton.Content is Wpf.Ui.Controls.SymbolIcon icon)
            {
                icon.Foreground = FindResource("ArchitecturalYellowBrush") as Brush ?? icon.Foreground;
            }
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }
    }

    private void OpenUpdateWindow(GitHubRelease release)
    {
        try
        {
            var window = new UpdateWindow(release, UpdateCheckService.GetCurrentVersion()) { Owner = this };
            _ = window.ShowDialog();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "MainWindow.OpenUpdateWindow");
            ToastService.ShowError(Loc.Get("Update_DialogError"));
        }
    }
}
