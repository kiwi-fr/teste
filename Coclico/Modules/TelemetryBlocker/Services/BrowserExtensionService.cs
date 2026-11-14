using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Coclico.Modules.TelemetryBlocker.Services;

internal enum BrowserKind
{
    Chrome,
    Edge,
    Firefox,
    Brave,
    Zen
}
internal sealed class BrowserExtensionService
{
    public static string SourceExtensionDir => Path.Combine(AppContext.BaseDirectory, "extension");

    public static string DeployedExtensionDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "CoclicoAdBlock");

    public string DeployExtension()
    {
        if (!Directory.Exists(SourceExtensionDir))
        {
            throw new DirectoryNotFoundException("Dossier extension introuvable : " + SourceExtensionDir);
        }

        CopyDirectory(SourceExtensionDir, DeployedExtensionDir);
        return DeployedExtensionDir;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            string target = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(target) || new FileInfo(file).LastWriteTimeUtc > new FileInfo(target).LastWriteTimeUtc)
            {
                File.Copy(file, target, overwrite: true);
            }
        }

        foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }

    public static string? FindBrowserExe(BrowserKind browser)
    {
        string[] candidates = browser switch
        {
            BrowserKind.Chrome =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            ],
            BrowserKind.Edge =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            ],
            BrowserKind.Firefox =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"),
            ],
            BrowserKind.Brave =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            ],
            BrowserKind.Zen =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zen Browser", "zen.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Zen Browser", "zen.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zen Browser", "zen.exe"),
            ],
            _ => []
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        try
        {
            string regPath = browser switch
            {
                BrowserKind.Chrome => @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                BrowserKind.Edge => @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe",
                BrowserKind.Firefox => @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe",
                BrowserKind.Brave => @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\brave.exe",
                BrowserKind.Zen => @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\zen.exe",
                _ => ""
            };
            if (!string.IsNullOrEmpty(regPath))
            {
                string? exe = Registry.GetValue(regPath, null, null) as string;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe;
            }
        }
        catch { }

        return null;
    }

    public static (bool Opened, string Detail) OpenExtensionsPage(BrowserKind browser, string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return (false, "Navigateur introuvable.");
        }

        string url = browser switch
        {
            BrowserKind.Firefox => "about:debugging#/runtime/this-firefox",
            BrowserKind.Zen => "about:debugging#/runtime/this-firefox",
            BrowserKind.Edge => "edge://extensions",
            BrowserKind.Brave => "brave://extensions",
            _ => "chrome://extensions"
        };

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "\"" + url + "\"",
                UseShellExecute = true
            });
            return (p != null, url);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
