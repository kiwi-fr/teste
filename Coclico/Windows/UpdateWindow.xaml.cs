using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Coclico.Services;
using Wpf.Ui.Controls;

namespace Coclico.Windows;

public partial class UpdateWindow : FluentWindow
{
    private static readonly Regex MarkdownLinkPattern = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
    private static readonly Regex MarkdownHeadingPattern = new(@"^#{1,6}\s*", RegexOptions.Compiled);

    private readonly GitHubRelease _release;
    private readonly UpdateManager _updateManager;
    private readonly SettingsService _settings;
    private bool _downloading;

    internal UpdateWindow(GitHubRelease release, string currentVersion)
    {
        InitializeComponent();
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _settings = ServiceContainer.GetRequired<SettingsService>();
        _updateManager = ServiceContainer.GetRequired<UpdateManager>();

        VersionLine.Text = $"v{currentVersion}  →  {_release.TagName}";
        NotesText.Text = CleanReleaseNotes(_release.Body);
    }

    private static string CleanReleaseNotes(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Pas de notes de version pour cette mise à jour.";
        }

        var lines = new List<string>();
        string[] trimmedLines = body.Replace("\r\n", "\n").Split('\n').Select(rawLine => rawLine.Trim()).ToArray();
        for (int i = 0; i < trimmedLines.Length; i++)
        {
            string line = trimmedLines[i];
            if (line.Length == 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0)
                {
                    lines.Add(string.Empty);
                }

                continue;
            }

            if (line.StartsWith('<') || line.StartsWith("![", StringComparison.Ordinal))
            {
                continue;
            }

            line = MarkdownLinkPattern.Replace(line, "$1");
            line = line.Replace("**", string.Empty).Replace("`", string.Empty);
            line = MarkdownHeadingPattern.Replace(line, string.Empty).Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count == 0
            ? "Pas de notes de version pour cette mise à jour."
            : string.Join(Environment.NewLine, lines);
    }

    private void BtnInstall_Click(object sender, RoutedEventArgs e)
        => AsyncEventSafe.Run(() => BtnInstall_ClickAsync(sender, e), "UpdateWindow.BtnInstall_Click");

    private async Task BtnInstall_ClickAsync(object sender, RoutedEventArgs e)
    {
        if (_downloading)
        {
            return;
        }

        GitHubAsset? asset = UpdateManager.PickSetupAsset(_release);
        if (asset == null)
        {
            StatusText.Text = Loc.Get("Update_NoInstaller");
            return;
        }

        _downloading = true;
        BtnInstall.IsEnabled = false;
        BtnLater.IsEnabled = false;
        BtnSkip.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Maximum = Math.Max(asset.Size, 1);
        DownloadProgress.Value = 0;
        StatusText.Text = Loc.Get("Update_Downloading");

        string savePath = Path.Join(Path.GetTempPath(), asset.Name);
        var progress = new Progress<long>(bytes =>
        {
            DownloadProgress.Value = bytes;
            StatusText.Text = Loc.Format("Update_ProgressMb", bytes / 1048576.0, asset.Size / 1048576.0);
        });

        try
        {
            bool downloaded = await _updateManager.DownloadReleaseAsync(_release, asset.Name, savePath, progress).ConfigureAwait(false);
            if (!downloaded)
            {
                StatusText.Text = Loc.Get("Update_DownloadFailed");
                ResetUi();
                return;
            }

            bool launched = _updateManager.LaunchInstaller(savePath, _release.TagName);
            if (!launched)
            {
                StatusText.Text = Loc.Get("Update_LaunchFailed");
                ResetUi();
                return;
            }

            StatusText.Text = Loc.Get("Update_Launched");
            await Task.Delay(1200).ConfigureAwait(false);
            ExitForUpdate();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "UpdateWindow.Install");
            StatusText.Text = Loc.Get("Update_Error");
            ResetUi();
        }
    }

    private void ResetUi()
    {
        _downloading = false;
        BtnInstall.IsEnabled = true;
        BtnLater.IsEnabled = true;
        BtnSkip.IsEnabled = true;
        DownloadProgress.Visibility = Visibility.Collapsed;
    }

    private void BtnLater_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Settings.SkippedUpdateVersion = _release.TagName;
            _settings.Save();
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            LoggingService.LogException(ex, "UpdateWindow.Skip");
        }

        Close();
    }

    private static void ExitForUpdate()
    {
        try
        {
            ServiceContainer.GetRequired<SettingsService>().Settings.MinimizeToTray = false;
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException) { _ = ex; }

        Application.Current.Shutdown();
        Environment.Exit(0);
    }

}
