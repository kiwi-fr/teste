using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Coclico.InstallerAot.Services;

public static class ShortcutService
{
    public static Task CreateShortcutsAsync(string installDir, string exeName)
    {
        var exePath = Path.Join(installDir, exeName);
        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var startMenuDir = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs", "Coclico");
        Directory.CreateDirectory(startMenuDir);

        var script =
            "$s = New-Object -ComObject WScript.Shell; " +
            $"$s.CreateShortcut('{Path.Join(desktopDir, "Coclico.lnk")}').TargetPath = '{exePath}'; " +
            $"$s.CreateShortcut('{Path.Join(startMenuDir, "Coclico.lnk")}').TargetPath = '{exePath}';";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossible de lancer PowerShell pour les raccourcis.");
        return process.WaitForExitAsync();
    }
}
